# Git hooks

Committed here rather than left in `.git/hooks`, which is per-clone, unversioned
and invisible to review. Git is pointed at this directory instead:

```powershell
.\build.ps1 -SetupHooks      # sets core.hooksPath to tools/hooks
```

One command per clone. Until it is run, no hooks fire.

| Hook | Runs | Cost |
| --- | --- | --- |
| `pre-commit` | `cargo fmt --check`, root workspace **and** `tools/api-debugger/src-tauri` | ~2s |
| `pre-push` | clippy (`-D warnings`), the test suites, generated-FFI-bindings drift, `dotnet format --verify-no-changes` | ~2-4 min |

The split is the point. A pre-commit hook that takes a minute gets bypassed, and
a bypassed hook is worse than none because it still looks like a safety net. So
the per-commit one only does formatting, and everything slow waits for a push.

`tools/api-debugger/src-tauri` is checked separately because it is its own cargo
workspace - the root `cargo fmt --all` does not reach it, while CI does.

## Skipping

```powershell
$env:MBRC_SKIP_HOOKS = 1
```

Preferred over `--no-verify`: it is explicit, it covers both hooks, and it does
not build the habit of passing a flag that disables every hook forever.

## What these do not catch

They run on **your** platform. CI also builds on ubuntu, and a path literal or a
`cfg` that behaves differently there will pass here and fail in CI - that has
already happened once. These hooks shorten the loop on the failures that are
reproducible locally; they are not a reason to stop reading CI.
