//! The update check, driven end to end against a stub HTTP client.
//!
//! These run on any host: the only Windows-bound part of the updater is the one
//! implementation of `HttpClient`, which is exactly why the trait is there.
//!
//! The manifest is the same golden fixture the verification tests use, signed
//! with the committed test key. Its version (1.5.0) is fixed, so the cases below
//! vary the *running* version instead - which is the input that actually varies
//! in the field.

#![allow(clippy::unwrap_used)]

mod support;

use mbrc_release::{Channel, CheckOptions, CheckOutcome, UpdateError, UpdateState, check};
use support::{OfflineHttp, StubHttp, TEST_KEYS};
use time::{OffsetDateTime, format_description::well_known::Rfc3339};

const MANIFEST: &str = include_str!("fixtures/manifest.json");
const SIGNATURE: &str = include_str!("fixtures/manifest.json.minisig");
const RELEASE_VERSION: &str = "1.5.0";
const API: &str = "https://api.github.com/repos/test/repo";
const NOW: &str = "2026-08-04T10:00:00Z";

fn at(raw: &str) -> OffsetDateTime {
    OffsetDateTime::parse(raw, &Rfc3339).unwrap()
}

/// A stub serving the golden release on the stable channel.
fn stable_release() -> StubHttp {
    let stub = StubHttp::default();
    stub.serve_release(API, "releases/latest", RELEASE_VERSION);
    stub.serve("https://assets.test/manifest.json", MANIFEST.as_bytes());
    stub.serve(
        "https://assets.test/manifest.json.minisig",
        SIGNATURE.as_bytes(),
    );
    stub.serve(
        "https://assets.test/musicbee_remote_1.5.0.zip",
        b"zip bytes",
    );
    stub
}

fn options(current: &str) -> CheckOptions<'_> {
    CheckOptions {
        repo: "test/repo",
        keys: TEST_KEYS,
        ..CheckOptions::new(current, Channel::Stable)
    }
}

/// The same, following the testing channel (pre-releases included).
fn testing(current: &str) -> CheckOptions<'_> {
    CheckOptions {
        repo: "test/repo",
        keys: TEST_KEYS,
        ..CheckOptions::new(current, Channel::Testing)
    }
}

fn forced(current: &str) -> CheckOptions<'_> {
    CheckOptions {
        force: true,
        ..options(current)
    }
}

#[test]
fn a_newer_release_is_offered() {
    let stub = stable_release();
    let mut state = UpdateState::default();

    // The running plugin reports four components; the manifest carries three.
    let outcome = check(&stub, &options("1.4.0.0"), &mut state, at(NOW)).unwrap();

    let CheckOutcome::Available(update) = outcome else {
        panic!("expected an update, got {outcome:?}");
    };
    assert_eq!(update.manifest.version, RELEASE_VERSION);
    assert_eq!(update.key_name, "test");
    assert_eq!(
        update.zip_url,
        "https://assets.test/musicbee_remote_1.5.0.zip"
    );
    // The signed bytes are kept verbatim: a re-serialized manifest is a
    // different document as far as the signature is concerned.
    assert_eq!(update.manifest_bytes, MANIFEST.as_bytes());
    assert_eq!(update.signature, SIGNATURE);
    // Checking does not download the payload.
    assert!(!stub.requested("https://assets.test/musicbee_remote_1.5.0.zip"));
    assert_eq!(state.last_check.as_deref(), Some(NOW));
    assert_eq!(state.consecutive_failures, 0);
}

#[test]
fn the_same_or_an_older_release_is_not() {
    for current in ["1.5.0.0", "1.5.0", "1.6.0.0"] {
        let stub = stable_release();
        let mut state = UpdateState::default();
        let outcome = check(&stub, &options(current), &mut state, at(NOW)).unwrap();
        assert!(
            matches!(&outcome, CheckOutcome::UpToDate { latest } if latest == RELEASE_VERSION),
            "{current} against {RELEASE_VERSION} should be up to date, got {outcome:?}"
        );
    }
}

#[test]
fn a_nightly_is_never_offered_an_older_stable() {
    let stub = stable_release();
    let mut state = UpdateState::default();

    // Running a nightly of 1.6.0 while stable is 1.5.0: a downgrade, refused.
    let outcome = check(
        &stub,
        &options("1.6.0-nightly.20260804"),
        &mut state,
        at(NOW),
    )
    .unwrap();
    assert!(
        matches!(outcome, CheckOutcome::UpToDate { .. }),
        "{outcome:?}"
    );

    // Running a nightly of 1.5.0 while stable is 1.5.0: the finished release is
    // newer than the prerelease of it, so that one is offered.
    let stub = stable_release();
    let mut state = UpdateState::default();
    let outcome = check(
        &stub,
        &options("1.5.0-nightly.20260701"),
        &mut state,
        at(NOW),
    )
    .unwrap();
    assert!(matches!(outcome, CheckOutcome::Available(_)), "{outcome:?}");
}

#[test]
fn each_channel_asks_the_right_endpoint() {
    // Stable takes GitHub's own "latest", which excludes pre-releases by its
    // rule. Testing must list, as there is no combined endpoint.
    assert_eq!(
        options("1.5.0").endpoint(),
        "https://api.github.com/repos/test/repo/releases/latest"
    );
    assert_eq!(
        testing("1.5.0").endpoint(),
        "https://api.github.com/repos/test/repo/releases?per_page=10"
    );
}

#[test]
fn the_channels_accept_what_they_should() {
    // Stable takes only stable. Testing is a superset, so a tester on a
    // pre-release is offered the final release rather than being stranded.
    assert!(Channel::Stable.accepted_by(Channel::Stable));
    assert!(!Channel::Testing.accepted_by(Channel::Stable));
    assert!(Channel::Stable.accepted_by(Channel::Testing));
    assert!(Channel::Testing.accepted_by(Channel::Testing));
}

#[test]
fn testing_takes_the_newest_listed_release() {
    // The list comes back newest first; the stable-channel manifest behind it is
    // accepted, because testing follows both kinds.
    let stub = StubHttp::default();
    stub.serve_release_list(
        API,
        "releases?per_page=10",
        &[(RELEASE_VERSION, false, true), ("1.4.0", false, true)],
    );
    stub.serve("https://assets.test/manifest.json", MANIFEST.as_bytes());
    stub.serve(
        "https://assets.test/manifest.json.minisig",
        SIGNATURE.as_bytes(),
    );
    stub.serve(
        "https://assets.test/musicbee_remote_1.5.0.zip",
        b"zip bytes",
    );

    let mut state = UpdateState::default();
    let outcome = check(&stub, &testing("1.4.0.0"), &mut state, at(NOW)).unwrap();
    assert!(matches!(outcome, CheckOutcome::Available(_)), "{outcome:?}");
}

#[test]
fn testing_skips_drafts_and_releases_without_a_manifest() {
    // A draft is unpublished, and a tag whose assets are missing (or still
    // uploading) must not stall the channel behind it.
    let stub = StubHttp::default();
    stub.serve_release_list(
        API,
        "releases?per_page=10",
        &[
            ("1.6.0", true, true),   // draft
            ("1.5.1", false, false), // published, no manifest attached
            (RELEASE_VERSION, false, true),
        ],
    );
    stub.serve("https://assets.test/manifest.json", MANIFEST.as_bytes());
    stub.serve(
        "https://assets.test/manifest.json.minisig",
        SIGNATURE.as_bytes(),
    );
    stub.serve(
        "https://assets.test/musicbee_remote_1.5.0.zip",
        b"zip bytes",
    );

    let mut state = UpdateState::default();
    let outcome = check(&stub, &testing("1.4.0.0"), &mut state, at(NOW)).unwrap();
    assert!(matches!(outcome, CheckOutcome::Available(_)), "{outcome:?}");
}

#[test]
fn testing_reports_a_list_with_nothing_usable() {
    let stub = StubHttp::default();
    stub.serve_release_list(API, "releases?per_page=10", &[("1.6.0", true, true)]);

    let mut state = UpdateState::default();
    let error = check(&stub, &testing("1.4.0.0"), &mut state, at(NOW)).unwrap_err();
    assert!(
        matches!(&error, UpdateError::Invalid(m) if m.contains("no release carrying a manifest")),
        "{error:?}"
    );
}

#[test]
fn a_tampered_manifest_never_reaches_the_version_comparison() {
    let stub = stable_release();
    stub.serve(
        "https://assets.test/manifest.json",
        MANIFEST.replace("1.5.0", "9.9.9").as_bytes(),
    );

    let mut state = UpdateState::default();
    let error = check(&stub, &options("1.4.0.0"), &mut state, at(NOW)).unwrap_err();
    assert!(
        matches!(error, UpdateError::UntrustedSignature),
        "{error:?}"
    );
    assert!(!stub.requested("https://assets.test/musicbee_remote_9.9.9.zip"));
    // A failed check still counts, so the next one backs off.
    assert_eq!(state.consecutive_failures, 1);
}

#[test]
fn a_release_missing_its_manifest_is_an_error_not_an_update() {
    let stub = StubHttp::default();
    stub.serve(
        &format!("{API}/releases/latest"),
        br#"{"tag_name":"v1.5.0","assets":[]}"#,
    );
    let mut state = UpdateState::default();
    let error = check(&stub, &options("1.4.0.0"), &mut state, at(NOW)).unwrap_err();
    assert!(
        matches!(&error, UpdateError::Invalid(m) if m.contains("manifest.json")),
        "{error:?}"
    );
}

#[test]
fn a_304_costs_one_request_and_no_verification() {
    let stub = stable_release();
    let mut state = UpdateState::default();
    check(&stub, &options("1.4.0.0"), &mut state, at(NOW)).unwrap();
    let after_first = stub.request_count();
    assert_eq!(state.etag.as_deref(), Some(StubHttp::ETAG));

    stub.reply_not_modified();
    let outcome = check(
        &stub,
        &options("1.4.0.0"),
        &mut state,
        at("2026-08-06T10:00:00Z"),
    )
    .unwrap();

    assert!(matches!(outcome, CheckOutcome::NotModified), "{outcome:?}");
    // The release document, and nothing else: no manifest, no signature.
    assert_eq!(stub.request_count(), after_first + 1);
    assert_eq!(state.etag.as_deref(), Some(StubHttp::ETAG));
    assert_eq!(state.last_check.as_deref(), Some("2026-08-06T10:00:00Z"));
}

#[test]
fn the_interval_suppresses_a_second_check_but_check_now_goes_through() {
    let stub = stable_release();
    let mut state = UpdateState::default();
    check(&stub, &options("1.4.0.0"), &mut state, at(NOW)).unwrap();
    let after_first = stub.request_count();

    let outcome = check(
        &stub,
        &options("1.4.0.0"),
        &mut state,
        at("2026-08-04T20:00:00Z"),
    )
    .unwrap();
    assert!(matches!(outcome, CheckOutcome::NotDue), "{outcome:?}");
    assert_eq!(stub.request_count(), after_first, "nothing was requested");

    let outcome = check(
        &stub,
        &forced("1.4.0.0"),
        &mut state,
        at("2026-08-04T20:00:00Z"),
    )
    .unwrap();
    assert!(matches!(outcome, CheckOutcome::Available(_)), "{outcome:?}");
}

#[test]
fn a_skipped_version_is_not_offered() {
    let stub = stable_release();
    let mut state = UpdateState {
        skipped_version: Some(RELEASE_VERSION.into()),
        ..UpdateState::default()
    };

    let outcome = check(&stub, &options("1.4.0.0"), &mut state, at(NOW)).unwrap();
    assert!(
        matches!(&outcome, CheckOutcome::Skipped { version } if version == RELEASE_VERSION),
        "{outcome:?}"
    );

    // Skipping says "not this one", so a different version is unaffected.
    let mut state = UpdateState {
        skipped_version: Some("1.4.9".into()),
        ..UpdateState::default()
    };
    let outcome = check(&stub, &forced("1.4.0.0"), &mut state, at(NOW)).unwrap();
    assert!(matches!(outcome, CheckOutcome::Available(_)), "{outcome:?}");
}

#[test]
fn a_failing_check_backs_off_instead_of_retrying_on_every_tick() {
    let mut state = UpdateState::default();
    assert!(check(&OfflineHttp, &options("1.4.0.0"), &mut state, at(NOW)).is_err());
    assert_eq!(state.consecutive_failures, 1);

    // Sooner than the 24 hour interval, but not immediately.
    let outcome = check(
        &OfflineHttp,
        &options("1.4.0.0"),
        &mut state,
        at("2026-08-04T10:05:00Z"),
    )
    .unwrap();
    assert!(matches!(outcome, CheckOutcome::NotDue), "{outcome:?}");

    assert!(
        check(
            &OfflineHttp,
            &options("1.4.0.0"),
            &mut state,
            at("2026-08-04T10:20:00Z")
        )
        .is_err()
    );
    assert_eq!(state.consecutive_failures, 2);

    // A check that works clears the streak.
    let stub = stable_release();
    check(
        &stub,
        &forced("1.4.0.0"),
        &mut state,
        at("2026-08-04T11:00:00Z"),
    )
    .unwrap();
    assert_eq!(state.consecutive_failures, 0);
}

#[test]
fn an_unreadable_running_version_is_an_error() {
    let stub = stable_release();
    let mut state = UpdateState::default();
    let error = check(&stub, &options(""), &mut state, at(NOW)).unwrap_err();
    assert!(matches!(error, UpdateError::Version(_)), "{error:?}");
    // Nothing was fetched: the check gave up before it went near the network.
    assert_eq!(stub.request_count(), 0);
}
