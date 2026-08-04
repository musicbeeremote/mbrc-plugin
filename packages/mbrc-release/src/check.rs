//! Deciding whether there is an update worth offering.
//!
//! The order matters and is the same every time: fetch the release document,
//! verify the manifest's signature, *then* look at the version. Nothing decides
//! anything on unverified bytes, and nothing is downloaded on the strength of a
//! version number alone.
//!
//! Everything here is above the [`HttpClient`] seam, so `tests/check_tests.rs`
//! drives the whole thing against a stub on any host.

use serde::Deserialize;
use time::OffsetDateTime;

use crate::{
    error::{Result, UpdateError},
    http::HttpClient,
    manifest::{Channel, Manifest},
    state::UpdateState,
    verify::{verify_manifest_with, TrustedKey, TRUSTED_KEYS},
    version,
};

/// The repository releases are published to.
pub const DEFAULT_REPO: &str = "musicbeeremote/mbrc-plugin";

/// Asset names every release carries, written by the packaging workflow.
pub const MANIFEST_ASSET: &str = "manifest.json";
pub const SIGNATURE_ASSET: &str = "manifest.json.minisig";

/// The rolling tag the nightly channel tracks (#148).
const NIGHTLY_TAG: &str = "nightly";

/// What the caller wants checked. Assembled from `Config` by the core; kept as a
/// parameter object so the check itself stays a pure function of its inputs.
#[derive(Debug, Clone)]
pub struct CheckOptions<'a> {
    /// The running plugin's version, as reported over FFI. Four-component .NET
    /// strings are fine - [`version::parse`] normalizes them.
    pub current_version: &'a str,
    pub channel: Channel,
    pub interval_hours: u64,
    /// `owner/name`; a parameter so tests never touch the real repository.
    pub repo: &'a str,
    /// The user pressed "Check now": ignore the interval, but nothing else.
    pub force: bool,
    /// The keys a manifest may be signed with. Defaults to the compiled-in
    /// release keys; explicit so the trust list is a visible input rather than an
    /// ambient global, matching [`crate::verify::verify_signature_with`].
    pub keys: &'static [TrustedKey],
}

impl<'a> CheckOptions<'a> {
    pub fn new(current_version: &'a str, channel: Channel) -> Self {
        Self {
            current_version,
            channel,
            interval_hours: 24,
            repo: DEFAULT_REPO,
            force: false,
            keys: TRUSTED_KEYS,
        }
    }

    /// The GitHub API endpoint for the channel. Stable follows `releases/latest`,
    /// which GitHub resolves to the newest non-prerelease; nightly is a fixed
    /// rolling tag, which cannot be reached that way.
    pub fn endpoint(&self) -> String {
        let repo = self.repo;
        match self.channel {
            Channel::Stable => format!("https://api.github.com/repos/{repo}/releases/latest"),
            Channel::Nightly => {
                format!("https://api.github.com/repos/{repo}/releases/tags/{NIGHTLY_TAG}")
            }
        }
    }
}

/// A verified update the user can be offered.
#[derive(Debug, Clone)]
pub struct AvailableUpdate {
    pub manifest: Manifest,
    /// The manifest exactly as it was signed. Kept byte-for-byte rather than
    /// re-serialized from `manifest`, because a re-serialized copy is a different
    /// document as far as the signature is concerned.
    pub manifest_bytes: Vec<u8>,
    /// The detached signature, carried through to staging so the helper can
    /// re-verify at apply time from the staged copy rather than trusting us.
    pub signature: String,
    /// Download URL of the zip named by `manifest.artifacts.zip`.
    pub zip_url: String,
    /// Which trusted key verified the manifest, for the log line.
    pub key_name: &'static str,
}

/// Which update a check produced, or why it produced none.
#[derive(Debug, Clone)]
pub enum CheckOutcome {
    /// The user turned update checks off. Produced by the caller that owns the
    /// setting, not by [`check`] itself.
    Disabled,
    /// The interval has not elapsed and the caller did not force a check.
    NotDue,
    /// The release document is unchanged since the cached `ETag`. Nothing was
    /// downloaded and nothing re-verified.
    NotModified,
    /// The published release is not newer than what is running.
    UpToDate {
        latest: String,
    },
    /// Newer, but the user asked to skip exactly this version.
    Skipped {
        version: String,
    },
    Available(Box<AvailableUpdate>),
}

/// Runs a check, updating `state` with the outcome. The caller persists `state`
/// (including after an error, so a failing check backs off instead of retrying on
/// every tick) and decides what to do with the result.
///
/// Note what is deliberately *not* gated here: the manifest's `abi_version` and
/// `min_musicbee_build`. A bundle ships `mb_remote.dll` and `mbrc_core.dll`
/// together, so its ABI is internally consistent by construction, and refusing an
/// update because it bumped the ABI would block exactly the updates that need to
/// ship. The MusicBee build gate belongs where the MusicBee version is known,
/// which is the panel (#152), not here.
pub fn check(
    client: &dyn HttpClient,
    options: &CheckOptions<'_>,
    state: &mut UpdateState,
    now: OffsetDateTime,
) -> Result<CheckOutcome> {
    if !options.force && !state.is_due(now, options.interval_hours) {
        return Ok(CheckOutcome::NotDue);
    }

    match run(client, options, state, now) {
        Ok(outcome) => {
            state.clear_failures();
            Ok(outcome)
        }
        Err(e) => {
            state.record_failure(now);
            Err(e)
        }
    }
}

fn run(
    client: &dyn HttpClient,
    options: &CheckOptions<'_>,
    state: &mut UpdateState,
    now: OffsetDateTime,
) -> Result<CheckOutcome> {
    let current = version::parse(options.current_version)?;

    let endpoint = options.endpoint();
    let response = client.get(&endpoint, state.etag.as_deref())?;
    if response.is_not_modified() {
        state.record_check(now, None);
        return Ok(CheckOutcome::NotModified);
    }
    let etag = response.etag.clone();
    let release: Release = serde_json::from_slice(&response.into_body(&endpoint)?)
        .map_err(|e| UpdateError::Parse(format!("release document: {e}")))?;

    // Signature first: the manifest's claims are worth nothing until it has been
    // shown to come from a release key.
    let manifest_url = release.asset_url(MANIFEST_ASSET)?.to_owned();
    let signature_url = release.asset_url(SIGNATURE_ASSET)?.to_owned();
    let manifest_bytes = client.get(&manifest_url, None)?.into_body(&manifest_url)?;
    let signature = String::from_utf8(
        client
            .get(&signature_url, None)?
            .into_body(&signature_url)?,
    )
    .map_err(|e| UpdateError::MalformedSignature(e.to_string()))?;
    let (manifest, key_name) = verify_manifest_with(&manifest_bytes, &signature, options.keys)?;

    // A manifest from the wrong channel means the tag and its assets disagree.
    // Nightly and stable carry different expectations, so this is refused rather
    // than quietly accepted.
    if manifest.channel != options.channel {
        return Err(UpdateError::Invalid(format!(
            "{endpoint} carries a {:?} manifest but the {:?} channel was checked",
            manifest.channel, options.channel
        )));
    }

    let zip_url = release.asset_url(&manifest.artifacts.zip.name)?.to_owned();
    let latest = version::parse(&manifest.version)?;

    // Only reaching here counts as a check; the interval starts now whatever the
    // verdict below turns out to be.
    state.record_check(now, etag);

    if latest <= current {
        return Ok(CheckOutcome::UpToDate {
            latest: manifest.version,
        });
    }
    if state.is_skipped(&manifest.version) {
        return Ok(CheckOutcome::Skipped {
            version: manifest.version,
        });
    }

    Ok(CheckOutcome::Available(Box::new(AvailableUpdate {
        manifest,
        manifest_bytes,
        signature,
        zip_url,
        key_name,
    })))
}

/// The subset of GitHub's release document the updater reads. Unknown fields are
/// ignored: this is somebody else's schema and it grows.
#[derive(Debug, Deserialize)]
struct Release {
    #[serde(default)]
    assets: Vec<Asset>,
}

#[derive(Debug, Deserialize)]
struct Asset {
    name: String,
    browser_download_url: String,
}

impl Release {
    fn asset_url(&self, name: &str) -> Result<&str> {
        self.assets
            .iter()
            .find(|a| a.name == name)
            .map(|a| a.browser_download_url.as_str())
            .ok_or_else(|| UpdateError::Invalid(format!("the release has no asset named {name:?}")))
    }
}
