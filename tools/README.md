# Tools

Developer tooling that lives alongside the MBRC plugin but is not part of `MBRC.sln`.

## api-debugger

A cross-platform desktop app (Tauri 2 + Vue 3 + TypeScript + Tailwind) for
exercising and debugging the MusicBee Remote wire protocol: connect directly to
a running plugin, tee/capture live traffic through a proxy, save and compare
sessions, and discover plugin instances on the network. Replaces the former C#
Avalonia debugger.

See [`api-debugger/README.md`](api-debugger/README.md) for setup and usage.