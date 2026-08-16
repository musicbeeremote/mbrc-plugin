# Release manifest and update verification

How a MusicBee Remote release proves it came from this project, and what the
plugin checks before it applies one.

## The short version

Every release publishes a `manifest.json` listing the SHA512 of each artifact and
of each file inside the zip, plus a `manifest.json.minisig` signature over it. The
plugin has the release public keys compiled in, so it verifies updates offline
with no user action and nothing extra to install.

A `.sha512` sidecar proves only that a download was not corrupted: whoever can
serve a bad zip can serve a matching hash file. The signature is what proves
origin.

## Manifest schema

Schema version 1. A manifest declaring any other `schema` is rejected outright
rather than best-effort parsed: an updater that guesses at a format it does not
know is an updater that installs the wrong bytes.

```json
{
  "schema": 1,
  "channel": "stable",
  "version": "1.5.0",
  "released_at": "2026-07-29T12:00:00Z",
  "abi_version": 1,
  "min_musicbee_build": 6500,
  "notes_url": "https://github.com/musicbeeremote/mbrc-plugin/releases/tag/v1.5.0",
  "artifacts": {
    "zip":       { "name": "musicbee_remote_1.5.0.zip", "size": 1234567, "sha512": "..." },
    "installer": { "name": "musicbee_remote_1.5.0.exe", "size": 2345678, "sha512": "..." }
  },
  "files": [
    { "path": "mb_remote.dll",        "sha512": "..." },
    { "path": "mbrc_core.dll",        "sha512": "..." },
    { "path": "mbrc-helper.exe",      "sha512": "..." }
  ]
}
```

`abi_version` and `min_musicbee_build` are generated from `MBRC_ABI_VERSION` and
the NSIS script rather than restated, so the manifest cannot drift from what the
plugin and installer actually enforce.

### Why `files` matters

`files` is the extraction allowlist, not a description of the zip:

- The updater writes **only** the entries listed here. Anything else in the
  archive is ignored, so a tampered zip cannot smuggle in an extra DLL.
- Every path is validated as a bare filename at parse time. `..`, path
  separators, and drive letters are all refused. This is the zip-slip guard, and
  it is enforced in the parser so no caller can forget it.
- It pins the shim and core to each other. A new `mb_remote.dll` can never be
  paired with an old `mbrc_core.dll`, which would violate the FFI ABI.

`LICENSE` and `README.txt` ride along in the zip but are not listed, because they
are not installed.

## Checking

The core asks GitHub for the channel's release, verifies the manifest's
signature, and only then compares versions. Nothing is decided on unverified
bytes and nothing is downloaded on the strength of a version number.

| | stable | testing |
|---|---|---|
| Endpoint | `releases/latest` | `releases?per_page=10` |
| Follows | released versions only | releases **and** pre-releases |
| Manifest `channel` may be | `stable` | `stable` or `testing` |

`releases/latest` is GitHub's own definition of the newest release that is
neither a draft nor a pre-release, so a pre-release cannot reach a stable user by
GitHub's rule rather than by ours. There is no "latest including pre-releases"
endpoint, so `testing` lists instead and takes the newest entry that is published
and carries a manifest - skipping a draft, or a tag whose assets are still
uploading, rather than stalling behind it.

A `testing` release is cut the same way a stable one is - packaged with build
provenance attested, its manifest signed by the release job, published with
`--prerelease`. The signature is not optional: the updater refuses a manifest it
cannot verify, so an unsigned pre-release is invisible to the channel meant to
find it. The scheduled nightly workflow is the exception, and only because it
uploads a 7-day workflow artifact rather than publishing anything.

**`testing` is a superset of `stable`, not a fork of it.** It accepts a stable
manifest as well as a testing one, so someone testing `1.6.0-rc.1` is offered
`1.6.0` when it ships instead of being stranded on pre-releases. The version
ordering already does the right thing: prerelease suffixes are kept, and semver
puts `1.6.0-rc.1` below `1.6.0` and above `1.5.0`.

The version the check compares against is the running plugin's, reported over FFI
as a four-component .NET string (`1.5.0.0`). Only major.minor.patch has ever been
meaningful here, so it is normalized to three parts before it reaches `semver`,
which cannot parse four.

**Automatic checking is opt-in.** `update_check_enabled` defaults to *off*: a
check is an unprompted outbound request to github.com, so making one is the
user's decision. When it is on, the core runs exactly one check per session, a
minute after networking starts - late enough that the library reconcile and the
cover-cache build have the machine to themselves first. The panel's "Check now"
button forces a check regardless of the setting and regardless of the interval;
never being able to ask would be the other way to get this wrong.

Checks are rate-limited by an interval (24 hours by default) and by an `ETag`:
the release document is requested with `If-None-Match`, and a `304` ends the
check without downloading or verifying anything. GitHub allows 60 unauthenticated
requests an hour per IP, which a daily check never approaches. A failed check
backs off - 15 minutes, doubling to a cap - so a machine that is offline retries
sooner than the interval but does not retry on every tick.

What the check has done - the last check time, the cached `ETag`, a version the
user skipped - is written to `update_state.json`, not to `core_settings.json`.
Those are not preferences, and keeping them out of the settings file means a save
from the Configure panel cannot silently un-skip a release.

## Fetching

Requests go out through WinHTTP, not a Rust HTTP stack. Two of its properties are
ones an updater cannot add after the fact:

- **The system proxy, PAC and WPAD included.** `HTTP_PROXY`-style environment
  variables are not how a managed Windows desktop is configured, and on one where
  the proxy is the only route out, an updater that ignores it fails silently and
  invisibly. A `proxy_override` setting exists for the networks auto-detection
  still gets wrong.
- **The OS root store**, which Windows Update keeps current. A compiled-in root
  snapshot ages, and this component's whole job is to still work years after its
  build date.

It also keeps the i686 target free of NASM and CMake, which rustls' crypto
providers need.

TLS is pinned to 1.2 and 1.3 explicitly. Windows still negotiates TLS 1.0 by
default in places, GitHub refuses it, and the machines running those defaults are
exactly the ones least likely to be updated by hand. Plain-HTTP URLs are refused
before a socket is opened, and an HTTPS-to-HTTP redirect is refused by policy.

The cost is one file of `unsafe` FFI (`winhttp.rs`) that cannot run on a non-Windows
host, which is why the [`HttpClient`] seam is where it is: everything that decides
anything sits above it and is tested against a stub. What only a real run can prove
- TLS against GitHub, cross-host redirects to the asset CDN, `ETag` round-tripping
- is covered by `#[ignore]`d tests in that file, run with `--ignored` on Windows.

## Staging

A verified update is downloaded and unpacked to
`%APPDATA%\MusicBee\mb_remote\updates\<version>\`, which the NSIS uninstaller
already removes wholesale. The zip is verified against the manifest's hash before
it is opened, and every file is verified after extraction; `pending.json` is
written last, so its presence means the whole bundle checked out.

Staging runs unelevated but everything it writes is later read by a process
running as administrator, so the boundaries are part of the design:

- **One directory, derived rather than supplied.** No caller passes a destination
  in. `<version>` and every filename must be a bare filename before it becomes a
  path segment, so nothing out of the manifest or the archive can name a
  directory of its own choosing.
- **Nothing is followed.** The staging directories are refused if they exist as
  symlinks or junctions. Otherwise anyone able to create a reparse point in
  `%APPDATA%` could aim the unelevated write, and the elevated copy that follows
  it, somewhere neither of us chose.
- **The whole archive is refused over one bad name.** An entry that is not a bare
  filename fails the bundle rather than being skipped: CI produces a flat zip, so
  anything else means the bytes are not what the manifest describes. Entries that
  are safe but unlisted (`LICENSE`, `README.txt`) are simply never extracted.
- **`pending.json` contains no paths.** It names a version. The helper derives the
  storage directory itself and re-checks that version is a bare filename before
  joining it, so a tampered marker can name a directory that does not exist but
  cannot name one outside the staging root.
- **Staging-time verification is not apply-time verification.** The staged files
  sit somewhere an unelevated process can write, so the signed `manifest.json` and
  its `.minisig` are staged alongside them and the helper re-verifies from those
  before copying anything into the plugins directory.

## Applying

`mbrc-helper update --pid <n> --staged <dir> --target <dir> --relaunch <exe>`,
run elevated. In order: re-verify, wait, back up, swap, roll back if needed.

**Re-verification comes first and is not a formality.** The staged bundle sits
where any user process can write, so nothing in it is trusted because the core
put it there. The manifest is checked against the compiled-in release keys and
every listed file re-hashed before a byte is copied. The files are read into
memory, verified there, and *that buffer* is what gets written - re-reading from
disk after verifying would leave a window to swap the file in between.

**Only then does it wait for MusicBee to exit** (120s). A mapped DLL cannot be
replaced, and discovering that half way through the swap is the situation the
rollback exists for; there is no reason to walk into it deliberately.

**Paths are checked separately from contents.** The signature says *what* may be
written, and the path rules say *where*: absolute only, no UNC, no reparse points,
canonicalized, and `--staged` and `--target` may not contain one another. Every
filename written must appear in the verified manifest, so a file sitting in the
staged directory that the manifest does not name is never read and never copied.

`--staged` is taken from argv rather than derived, which is a deliberate departure
from "an elevated process should not accept paths from an unelevated one".
Elevation can run the helper as a *different* administrator account, so
`%APPDATA%` inside it is not the user's `%APPDATA%`, and a derived path would name
the wrong profile or nothing at all. The path is an input; the signature is the
trust.

Replaced files go to `<storage>/backup/<version>/` - "what installing that version
replaced" - and one generation is kept. A failed write restores them. A restore
that cannot put back a file whose installed copy is already identical to the
backup is not a failure: that file was never replaced.

**The restore writes from memory, not from that directory.** The backup lives
under the user's profile, so an unelevated process can rewrite it while the
elevated apply is running; reading it back would turn that write into an elevated
one. The bytes read during the backup are what get restored, for the same reason
the payload is verified in memory rather than re-read from disk. The on-disk copy
remains, for a manual recovery.

Exit codes are the contract with the panel:

| | |
|---|---|
| 0 | applied |
| 2 | arguments refused; nothing touched |
| 5 | the staged bundle did not verify; nothing touched |
| 6 | MusicBee did not exit in time; nothing touched |
| 7 | failed part way, previous files restored; the install is intact |
| 8 | failed part way *and* the restore failed; the user must reinstall |

MusicBee is relaunched **through Explorer**, not as a child of the helper. A child
would inherit elevation and run MusicBee as administrator for the rest of the
session, writing its settings and cache as the wrong user.

### Getting there: `mbrc_apply_staged_update()`

The panel calls one FFI export that takes **no arguments**. The storage directory
comes from the initialized core, the plugins directory is where `mbrc_core.dll`
was loaded from (asked of the loader, so it is true by construction), MusicBee is
the current process, and the pid is our own. A caller that could name the
directory to overwrite would be a caller worth attacking; there is nothing to
pass, so there is nothing to tamper with.

Before launching, the core verifies the **staged** helper against the staged
signed manifest. That copy is what runs, never the installed one: a release
replaces `mbrc-helper.exe` too, and a running image cannot overwrite itself. It
runs elevated out of a user-writable directory, so checking it before *execution*
- earlier than the check it then performs on the DLLs - is the boundary.

The file is opened **denying write and delete sharing**, verified through that
handle, and the handle is held open across `ShellExecuteExW`. Verifying by path
and then launching by path would leave a window in which any process running as
the user could replace the file after it verified and have its own binary run as
administrator, on the prompt the user was expecting. Execution still works:
Windows counts `FILE_EXECUTE` as read access when it checks sharing.

A staged bundle that verifies but is **not newer than the running plugin is
refused** (`NotAnUpgrade`). Every release is public and signed, so a signature
proves a bundle is ours, not that it is the right one to install - without this,
anyone able to write to the staging directory could roll the plugin back to an
older release, signature and all, and undo whatever the newer one fixed.

Elevation is requested with `ShellExecuteExW` and the `runas` verb, chosen by
probing whether the plugins directory is writable (by writing, not by reading an
ACL). The prompt therefore appears while the user is still looking at the button
they pressed. Declining it comes back as `ERROR_CANCELLED` and is reported as a
normal outcome with the staged download intact - the concrete gain over letting
the helper self-elevate, where the prompt would arrive after MusicBee had already
exited and there would be no UI left to report into.

## The panel

The Configure dialog's Updates group is the whole user-facing surface: a status
line, one action button, and a "Check for updates automatically" checkbox.

The core owns the state machine (`updates::service`) and the panel renders it.
Check, download and skip are fire-and-forget host commands that start a
background thread, so nothing the user presses blocks MusicBee's UI thread on a
network request; the status comes back through the `UpdateStatus` host query and
an `UpdateStatusChanged` push event.

| `state` | the button offers |
|---|---|
| `unknown` / `up_to_date` / `skipped` / `disabled` / `error` | Check now |
| `available` | Download |
| `download_failed` | Retry download |
| `downloading` / `checking` | nothing (disabled) |
| `staged` | Install and restart |

A failed *check* renders as "No update could be found", not as the underlying
diagnostic: an unreachable host, a release without a manifest, and a signature
that did not verify all leave the user with the same next move, and the core has
already written the real reason to `mbrc-core.log`. A failed *download* is a
separate state precisely so it does not say that - the update is known, named,
and worth retrying.

Three rules that are not obvious from the table:

- **One job at a time.** A check and a download both talk to github.com and both
  write the same status. A second request while one runs is refused, not queued.
- **A check with no news changes nothing.** A `304` (the release document is
  unchanged since the cached `ETag`) and a not-due check are not answers, so they
  neither rewrite the status nor discard the verified update behind an offer.
  Without that, pressing "Check now" twice would retract the Download button the
  first press produced - the second request is a `304` almost every time.
- **A staged bundle outlives a later check.** If a check cannot reach GitHub at
  all, the status stays `staged`, with the failure carried alongside as a
  message. Losing the restart button would strand a download the user has already
  approved.
- **The download is never named by the host.** `DownloadUpdate` fetches the
  update the core's own verified check produced, or is refused. There is no
  parameter to point it somewhere else.

`min_musicbee_build` is enforced here rather than in the check, because the core
cannot see MusicBee's version and the panel can: it reads the build from
`MusicBee.exe`'s file version - the same number the NSIS installer gates on - and
greys out the download when the release needs a newer one.

The dialog itself is fixed-size, and six groups plus the footer do not fit a 720p
screen - nor a 1080p one at 150% scaling, which is the same thing in logical
pixels. Its height is therefore clamped to the screen's work area and the group
column scrolls, with the Save/Close footer docked outside the scrolling region so
it can never end up off-screen.

After `mbrc_apply_staged_update()` returns `Launched`, the panel closes MusicBee
by posting `WM_CLOSE` to its main window, the same message its title bar sends. A
MusicBee configured to minimize on close will not exit; the helper then times out
after two minutes having touched nothing (exit 6), and the staged update simply
waits for the next real exit.

## Signing

minisign (ed25519). Authenticode was rejected on cost, and GPG on the verifier
side: checking one signature would mean pulling a full OpenPGP stack into a
32-bit plugin core. minisign needs neither ASN.1 nor a keyring, and
`minisign-verify` is a zero-dependency crate.

Releases are signed in CI by a dedicated Linux job gated on the `release`
environment. That job is the only place the private key exists, and it does
nothing but read one JSON file and emit a signature: the packaging job runs
Chocolatey and NSIS, and third-party installers have no business sharing a
runner with a release signing key.

Before signing, the job parses the generated manifest with the same Rust code the
plugin uses, so a drift between the generator and the parser fails the release
rather than shipping something no installed plugin can read. After signing, it
re-verifies against the committed public keys, which catches a mismatched secret
before users do.

## Keys and rotation

Public keys live in [`packages/mbrc-release/keys/`](../packages/mbrc-release/keys/)
and are compiled into the verifier by `build.rs`. **Any** of them is accepted.

More than one key ships because rotation only works if the replacement was
already trusted by installs in the field. A key generated after a compromise is
useless: shipped plugins have never heard of it. So:

- `release-1` is active. Its private half is in the `release` GitHub environment.
- `release-2` and `release-3` are cold spares whose private halves have never
  been on a networked machine, and must stay that way.

To rotate, move the next key's private half into the `release` environment and
re-point the signing step. No plugin change is needed.

Per-key revocation is deliberately not modelled. It would only ever protect
installs new enough to have received the revocation, which are the installs least
at risk.

Losing the active key means rotating to a spare. Losing all three means every
install in the field is permanently unupgradable, because no key they trust could
sign a manifest again.

## Verifying a release yourself

Optional. The plugin already does this automatically.

**With the GitHub CLI**, which most people will find easiest since it is
distributed through winget and GitHub's own installer:

```
gh attestation verify musicbee_remote_1.5.0.zip --repo musicbeeremote/mbrc-plugin
```

**With minisign**, which works fully offline:

```
minisign -Vm manifest.json -P <public key from packages/mbrc-release/keys/release-1.pub>
```

then confirm the `sha512` values in the manifest match your downloads.

A caveat worth stating plainly: minisign's own Windows binaries are not
Authenticode signed, and the `.minisig` files next to them are circular for a
first-time verifier. There is no clean bootstrap for a Windows user with no
developer tools, which is precisely why the plugin ships with the key built in
rather than relying on anyone running these commands.

## Two verification layers

They serve different audiences and neither replaces the other:

| | minisign | build provenance attestation |
|---|---|---|
| Audience | the plugin, offline | humans and CI |
| Trust root | key compiled into `mbrc_core.dll` | Sigstore + GitHub OIDC |
| Needs network | no | yes |
| Proves | signed by the release key | built by this repo's workflow |

The attestation is the stronger statement about *where* a build came from, but it
needs network access and Sigstore roots, which makes it a poor in-process gate.
