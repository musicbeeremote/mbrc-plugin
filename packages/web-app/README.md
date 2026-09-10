# MusicBee Remote web app

The browser client. Built to `dist/` and embedded in `mbrc_core.dll`, served by
the plugin on its own listening port (default 3000).

## Development

```bash
pnpm install
pnpm dev                                  # against MusicBee on this machine
MBRC_TARGET=http://192.168.1.20:3000 pnpm dev   # against another machine's
```

`pnpm dev` proxies `/api` and `/ws` to a running MusicBee, so the app talks to a
real library while Vite serves the UI with hot reload. The dev server binds every
interface, so a phone on the same network can load it: a phone layout that only
a desktop ever sees is one that ships broken.

MusicBee must be running with the plugin's **Web remote** setting on (it is on by
default). If pairing is enforced, generate a code from the plugin's settings
panel.

## Layout

Two layouts rather than one that stretches. Below 768px it is a phone: one pane
at a time behind a bottom tab bar, with a compact transport strip so playback is
reachable from every tab. At 768px and up it is a tablet or desktop: a nav rail,
the browse pane, and now playing all on screen at once, so changing what is
playing never hides what you were browsing.

## Protocol

The app is an ordinary V6 client (`docs/protocol-v6.md`): the same handshake, ops
and events the native clients use, over a WebSocket. `POST /api/v6/{op}` is the
fallback when the socket cannot open. See `src/api/client.ts`.
