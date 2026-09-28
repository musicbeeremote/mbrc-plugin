# Troubleshooting and bug reports

## Capture the problem

The plugin can record a problem while it happens and save everything a bug report
needs into one file.

1. In MusicBee, open `Preferences > Plugins > MusicBee Remote > Configure`.
2. In the **Diagnostics** group, press **Start capture**.
3. Reproduce the problem.
4. Press **Stop and save**.

A file named `mbrc-diagnostics-<date>-<time>.zip` is written to your Desktop and
Explorer opens with it selected. Attach it to a
[bug report](https://github.com/musicbeeremote/mbrc-plugin/issues/new?template=bug.yml).

While a capture runs the plugin logs in much more detail than normal, including
the messages it exchanges with your phone. That is the point: at the normal
setting those messages are not recorded at all, so a log taken after the fact
usually cannot show what went wrong.

A capture stops on its own after 30 minutes, and the log level goes back to
whatever you had it set to. **Cancel** ends it without saving anything. If
MusicBee restarts while a capture is running, the capture carries on - which is
how a problem that only happens at startup gets recorded.

## What is in the file

| Entry | What it is |
| --- | --- |
| `report.json` | Versions (plugin, core, MusicBee, Windows, .NET), your settings, the addresses the plugin is listening on, cache health, recently blocked connections, and where the update flow stands |
| `capture.log` | The plugin's log for the capture window only, not your whole history |
| `logs/` | Only present when the capture overlapped another log file - because the log rolled partway through, or because MusicBee restarted during it. Log files older than the capture are not included: they predate the problem, and they are the one part of a bundle you could not read before sending it |

**What is removed.** Any username and password in a configured proxy is replaced
with `<redacted>`, and the list of specifically-allowed client addresses is left
out. `report.json` lists what was removed under `redacted_keys`.

**What is kept, deliberately.** Your music folder paths, this PC's local network
addresses, its port, and the addresses of devices that tried to connect. Those are
readable on purpose: the bugs that most need a diagnostics file are the ones about
paths, scanning and who is allowed to connect, and stripping them would make the
file useless for exactly the reports it exists to serve.

Nothing is uploaded anywhere. The plugin writes the file to your Desktop and
stops; what happens to it next is entirely your decision. Open it and look before
you attach it to a public issue.

## Where the logs live

Press **Open log folder** in the Configure window's Advanced group, or go to the
storage folder for your install:

| Install | Storage folder |
|---|---|
| Installer | `%APPDATA%\MusicBee\mb_remote\` |
| Portable | `<MusicBee folder>\AppData\mb_remote\` |
| Microsoft Store | `%LOCALAPPDATA%\Packages\<MusicBee package>\LocalCache\Roaming\MusicBee\mb_remote\` |

On a Store install the log lines show the unredirected `%APPDATA%` path, because
Windows redirects it inside the package. That folder does not exist in Explorer;
use the `Packages` path above.

- `mbrc-core.log` - the plugin's main log, capped at 10 MB. C# lines go here too
- `mbrc-core.1.log.gz` .. `mbrc-core.3.log.gz` - the three previous rolled logs
- `mbrc-bootstrap.log` - the earliest startup lines, before the main log opens
- `initialization_error.log` - only present if the plugin failed to start
- `mbrc-helper.log` - the update helper's log (falls back to `%TEMP%` if the
  helper cannot reach the storage folder)
- `core_settings.json` - your settings

## Log levels

The **Log level** dropdown in the Advanced group sets what is recorded normally:

- **Normal** - the default. No message-by-message detail.
- **Debug** - adds the messages exchanged with clients, with long lists sampled.
- **Trace** - everything, including per-item timings. Verbose enough to roll the
  log file in minutes on a large library.

You do not need to change this to file a report - a capture raises it for you and
puts it back. Leave it on Normal unless someone asks otherwise.

### Setting it without the panel

If MusicBee crashes before you can open the Configure window, close MusicBee, open
`core_settings.json` in the storage folder above, and set `log_level`:

```json
{
  "log_level": "debug"
}
```

The values are `"info"` (Normal), `"debug"` and `"trace"`, in lower case. Change
only that key (or add it, keeping the commas between entries) and start MusicBee;
the core reads it at startup. The file must stay valid JSON: if the core cannot
parse it, it ignores the whole file and starts with default settings. Set it back
to `"info"` when you are done.

## Common problems

**The app cannot find the PC.** Check `report.json`'s `listening.addresses`
against what the phone is on: they need to be the same network. A VPN on either
device is the usual culprit. If the addresses look right, the Windows Firewall
rule may be missing - re-save the settings with "Update firewall rule" ticked.

**Sync starts and stops partway.** Capture it. The log will show whether the
connection dropped or the library scan stalled, and those have different fixes.

**Covers are missing.** Check `caches.covers_cached` in `report.json`. If it is 0
or far below your album count, press **Rebuild covers** in the Cache group and
watch MusicBee's status bar - a first build on a large library takes a while.
