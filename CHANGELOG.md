Change Log
---------
# Versions

## 1.5.0 - 2026/08/26

The plugin's core has been rewritten in Rust. It ships as `mbrc_core.dll`
alongside the existing `mb_remote.dll`, plus a small `mbrc-helper.exe`; the C#
side is now a thin shim over MusicBee's API. **Nothing on the wire changed**:
the V4 protocol is byte-identical to 1.4.1, field order included, so every
shipped Android and iOS client keeps working untouched.

### Added
- In-plugin updates. The plugin can check for a new release, verify it against a
  signed manifest, download it, and install it on restart. Checking is opt-in and
  off by default - a check is an unprompted request to github.com - but the
  Configure panel's "Check now" always works.
- A `testing` update channel that follows pre-releases, for trying a build before
  it ships.
- mDNS / DNS-SD advertisement (`_mbrc._tcp`), alongside the existing custom
  discovery, so standard tooling and the platform browsers (`NsdManager`,
  `NWBrowser`) can find a MusicBee host.
- A rebuilt Configure dialog: the addresses clients can reach the plugin on, cache
  status with per-cache rebuild buttons, recently blocked connections, a log-level
  selector, and the update controls.
- `mbrc-helper.exe`, an elevated helper that adds the Windows firewall rule and
  installs updates. It replaces the old firewall utility.
- A diagnostics capture in the Configure panel. Start it, reproduce a problem,
  stop it, and the plugin saves one zip to the Desktop with a detailed log of
  just that window plus the versions, settings and cache health a bug report
  needs. It raises the log level only for the duration, stops itself after 30
  minutes, and survives a MusicBee restart so a startup problem can be caught.
  Proxy credentials are masked and the allowed-address list is left out; nothing
  is uploaded anywhere. See `docs/troubleshooting.md`.

### Changed
- Library browsing is served from an on-disk cache and paged, instead of
  materialising the whole library per request. Large libraries page in constant
  memory, and the cache survives restarts when the library has not changed.
- Album covers are cached, resized and served by the core, and refresh when tags,
  artwork or files change rather than only on a manual invalidation.
- Settings moved to `core_settings.json`, owned by the core. The Configure panel
  edits them; there is no second copy.
- Logging goes to `mbrc-core.log` with size-based rotation, and redacts what
  should not be in a log file while keeping it readable.

### Fixed
- Library sync could intermittently fail to finish on both clients. The connection
  layer's keepalive and reaping now match what the clients expect: broadcast
  subscribers are pinged, handshaked sockets are never idle-reaped, and OS-level
  TCP keepalive detects dead half-open connections.
- Playback status no longer reports as stopped in situations where the plugin had
  already recovered.
- A MusicBee too old for the plugin now says so, in the plugin list and in a log
  file, instead of loading and silently doing nothing.
- The installer's uninstaller removed nothing. It looked for the plugin files one
  directory deeper than they are, so every delete missed, and it still reported
  that the plugin had been removed.
- Removing the plugin inside MusicBee used to leave `mbrc_core.dll` and
  `mbrc-helper.exe` in the Plugins folder, because MusicBee only knows about the
  assembly it loaded. The plugin now unloads the native core and deletes it, the
  helper and its own text files itself, takes its entry out of the Tools menu,
  and clears its stored data - all without closing MusicBee (#192).
- Removing the plugin while the cover cache was building froze MusicBee until the
  build finished. Teardown now tells the build to stop first and it gives up
  between albums, keeping what it had already built for next time.
- Debug logs and diagnostics captures showed nothing the plugin pushed on its
  own. Events sent to connected clients and the keepalive pings never reached
  the log, so a capture of a push problem looked identical to a capture of
  nothing happening. Both are logged now, with the number of clients each event
  reached (#188).
- The archive's `LICENSE` and `README.txt` overwrote MusicBee's own `readme.txt`
  in the Plugins folder, and earlier versions left both behind on every update.
  They ship as `MBRC_LICENSE.txt` and `MBRC_README.txt` now.
- Single-file albums split by a `.cue` sheet showed in the now-playing list as a
  row of "Unknown Artist" per track. Every such track reports the same container
  file, so reading tags by file returned nothing for all of them; the list now
  reads by list position and shows the real per-track titles. Browsing the
  library still shows one entry per cue album rather than its tracks - MusicBee's
  plugin interface offers no way to read them (#87).

### Upgrading from a 1.5.0 beta
`1.5.0-beta.1` and `1.5.0-beta.2` report themselves as plain `1.5.0` because of a
version-stamping bug in how they were built. They will never be offered an update,
this release included, since nothing looks newer than what they claim to be. If you
are on one of those two, install this release by hand once; updates work normally
afterwards. `beta.3` and `beta.4` are unaffected.

## 1.4.1 - 2021/06/12
### Changed
- Introduces state persistence for the cover caching mechanism to improve performance.

### Added
- Adds a button in the control panel to allow for easy cache invalidation.

## 1.4.0
### Changed
- Fixes status displaying as stopped when range filtering is active.
- Adds pagination to the radio station api
- Adds support for different behavior on different client platforms (Android/iOS)
- Fixes repeat one functionality.
- Fixes issue with lyrics initialization on direct request.
- Fixes off by one now playing play on Android clients
- Adds Album Artist info to `nowplayinglist` and `libraryalbumtracks` commands.

### Added
- Adds support for requesting list of Album Artists instead of Artists.
- Adds support for shuffle/non-shuffle play all command. 
- Adds support for Album covers.

## 1.3.0-ios
### Changed
- Adds disk number to `libraryalbumtracks`.

### Added
- Introduces tag manipulation command.

## 1.2.1-ios
### Changed
- Introduces ordering into the now playing list and a limit of 100 entries.

## 1.2.0-ios
### Changed
- Allows the reset of a track's rating by sending an empty string.

### Added
- Introduces support for playing track details.

## 1.1.0
- Adds a check to avoid a case where invalid characters in the tags would result in a sync failure.
- Adds a proper socket checker to update the status.
- Fixes an issue with the rating when using specific locales (like German)
- Fixes an issue with the favorite state not updating properly when changing tracks
- Adds protocol support for switching audio outputs
- Adds protocol support for getting Radio Stations

## 1.0.0
- Adds new API for playlist retrieval.
- Adds new API for now playing that works with pagination.
- Removed settings for now playing. The old call is now hard limited to 5000 (will be deprecated).
- Adds debug checkbox on the Plugin settings options menu.
- Adds settings button to easily open the log.
- Makes the discovery listen to all available interfaces.
- Adds new paginated API to enable library browsing.
- Fixes an issue where the last.fm love status was the opposite of the expected.
