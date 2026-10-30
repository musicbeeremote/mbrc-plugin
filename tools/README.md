# Tools

Developer tooling that lives alongside the MBRC plugin but is not part of `MBRC.sln`.

## api-debugger

A cross-platform desktop app (Tauri 2 + Vue 3 + TypeScript + Tailwind) for
exercising and debugging the MusicBee Remote wire protocol: connect directly to
a running plugin, tee/capture live traffic through a proxy, save and compare
sessions, and discover plugin instances on the network. Replaces the former C#
Avalonia debugger.

See [`api-debugger/README.md`](api-debugger/README.md) for setup and usage.

## render-ui.ps1

Renders every settings page and setup-wizard step to PNG without MusicBee, at
100% and 150% by default, into `build\ui-renders`. It runs the explicit
`UiRenderTests` in the C# suite, which also fail when a page scrolls. Build the
solution first; live values read as unavailable because the core never starts.

```powershell
.\build-msbuild.ps1 -Configuration Release
.\tools\render-ui.ps1
```