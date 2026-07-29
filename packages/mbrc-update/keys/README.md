# Release signing keys (public halves)

Every `*.pub` in this directory is compiled into the verifier by `build.rs` and is
trusted unconditionally: a manifest signed by **any** of them verifies.

## Why there is more than one

Rotation only works if the replacement key was already trusted by installs in the
field. A key generated *after* a compromise is useless, because shipped plugins have
never heard of it. So the spares ship from day one:

- `release-1` is the active key. Its private half lives in the `release` GitHub
  environment as `MINISIGN_SECRET_KEY` / `MINISIGN_PASSWORD`.
- `release-2` and `release-3` are cold spares. Their private halves have never been
  on a networked machine and must stay that way, otherwise they are no safer than the
  key they would replace.

To rotate, move the next key's private half into the `release` environment and
re-point the signing step. No plugin change is needed: every shipped build already
trusts it.

## Losing keys

Losing the active key means rotating to a spare. Losing **all** of them means every
install in the field is permanently unupgradable, because no key they trust can sign
a manifest again. Back up all three private halves and their passwords offline.

## Adding a key

Drop the `.pub` file in this directory. `build.rs` globs `*.pub`, sorted by filename,
so nothing else needs editing. Private keys must never be committed; `.gitignore`
carries a `mbrc-release-*.key` guard, but that guard is narrow by design (the test
keypair under `tests/keys/` *is* committed) and is not a substitute for care.
