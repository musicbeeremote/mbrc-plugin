# Git hooks

Committed here rather than left in `.git/hooks`, which is per-clone, unversioned
and invisible to review. Git is pointed at this directory instead:

```powershell
.\build.ps1 -SetupHooks      # sets core.hooksPath to tools/hooks
```

One command per clone. Until it is run, no hooks fire.

| Hook | Runs | Cost |
| --- | --- | --- |
| `commit-msg` | Conventional Commits, matching `@commitlint/config-conventional` | instant |
| `pre-commit` | `cargo fmt --check`, root workspace **and** `tools/api-debugger/src-tauri` | ~2s |
| `pre-push` | working tree vs pushed range, clippy (`-D warnings`), the test suites, generated-FFI-bindings drift, `dotnet format --verify-no-changes` | ~2-4 min |

The split is the point. A pre-commit hook that takes a minute gets bypassed, and
a bypassed hook is worse than none because it still looks like a safety net. So
the per-commit one only does formatting, and everything slow waits for a push.

`tools/api-debugger/src-tauri` is checked separately because it is its own cargo
workspace - the root `cargo fmt --all` does not reach it, while CI does.

## Commit messages

`commit-msg` implements `@commitlint/config-conventional` in shell rather than
pulling in Node. Errors fail the commit; warnings only print:

| | rules |
| --- | --- |
| error | `type-enum`, `type-case`, `type-empty`, `scope-case`, `subject-empty`, `subject-full-stop`, `subject-case`, `header-max-length` (100), `header-trim`, `body-max-line-length` (100) |
| warning | `body-leading-blank` |

Two deliberate differences from upstream: `scope-enum` is left open (as
config-conventional also leaves it, and this repo already uses fifteen-odd
scopes), and `subject-case` is implemented as the same equality test commitlint
uses rather than by parsing English.

That equality test has one inherited sharp edge worth knowing: a capitalised
opening token followed by all-lowercase text counts as sentence-case. So
`fix: V4 frames must stay byte-identical` is rejected, while
`fix: MusicBee closes without reopening` and
`feat(update): WinHTTP client for the update check` are not. commitlint agrees;
write `v4 frames ...` or reword.

The same file runs in CI over every commit in a pull request
([`commits.yml`](../../.github/workflows/commits.yml)), because merges here are
`--ff-only` and a locally skipped hook would otherwise put a malformed subject
into permanent history. Checked against the last 60 commits: 59 pass, and the
one rejection is a genuine 275-character body line.

## Skipping

```powershell
$env:MBRC_SKIP_HOOKS = 1
```

Preferred over `--no-verify`: it is explicit, it covers all three hooks, and it
does not build the habit of passing a flag that disables every hook forever.
Note CI does not honour it, so a skipped `commit-msg` is still caught in the
pull request.

## What these do not catch

They run on **your** platform. CI also builds on ubuntu, and a path literal or a
`cfg` that behaves differently there will pass here and fail in CI - that has
already happened once. These hooks shorten the loop on the failures that are
reproducible locally; they are not a reason to stop reading CI.

They also test your **working tree**, not the commits being pushed. Normally the
two are identical; when they are not, a green hook can wave through a commit that
does not compile - a fix left uncommitted in the tree while the commit that
needed it was amended and pushed, and the hook dutifully tested the fixed tree.

`pre-push` therefore refuses when a file with uncommitted changes is **also**
touched by the commits being pushed, and only then: unrelated work in progress is
normal and gets a one-line note instead. Commit or stash the overlap, or use
`MBRC_SKIP_HOOKS=1`.
