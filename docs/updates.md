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
    { "path": "firewall-utility.exe", "sha512": "..." }
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

Public keys live in [`packages/mbrc-update/keys/`](../packages/mbrc-update/keys/)
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
minisign -Vm manifest.json -P <public key from packages/mbrc-update/keys/release-1.pub>
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
