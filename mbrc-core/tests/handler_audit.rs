//! Coverage guard: every legacy V4 protocol command-style context
//! must have a dispatch arm in `server/legacy/commands.rs`.
//!
//! A new V4 context appearing in `protocol/constants.rs` (or being
//! added to the expected list below) without a matching `constants::X
//! =>` arm in `commands.rs` fails this test, so missing handlers like
//! `nowplayingqueue` can't slip through unnoticed — golden-trace replay
//! is permissive about silent handlers (timeouts are non-fatal), and
//! the only signal otherwise is a runtime `INFO` log.

use std::collections::HashSet;
use std::path::Path;

/// Every legacy V4 wire context the dispatcher MUST handle. Excludes
/// pure broadcast contexts (e.g. `nowplayinglistchanged`) that the
/// server emits but never receives, and constants used as response
/// frames only (e.g. `pong`, `error`, `notallowed`).
const EXPECTED_COMMAND_CONTEXTS: &[&str] = &[
    "playerplaypause",
    "playerplay",
    "playerpause",
    "playerstop",
    "playernext",
    "playerprevious",
    "playervolume",
    "playerstate",
    "playerstatus",
    "playermute",
    "playershuffle",
    "playerrepeat",
    "scrobbler",
    "playerautodj",
    "nowplayingrating",
    "nowplayinglfmrating",
    "verifyconnection",
    "nowplayingtrack",
    "nowplayingcover",
    "nowplayinglyrics",
    "nowplayingposition",
    "init",
    "ping",
    "playlistplay",
    "libraryplayall",
    "playeroutputswitch",
    "nowplayinglistplay",
    "nowplayinglistremove",
    "libraryqueuegenre",
    "libraryqueueartist",
    "libraryqueuealbum",
    "libraryqueuetrack",
    "nowplayinglistmove",
    "nowplayinglistsearch",
    "nowplayingtagchange",
    "nowplayingqueue",
    "playeroutput",
    "nowplayingdetails",
    "librarycovercachebuildstatus",
    "playlistlist",
    "nowplayinglist",
    "radiostations",
    "browsegenres",
    "browseartists",
    "browsealbums",
    "browsetracks",
    "libraryartistalbums",
    "librarygenreartists",
    "libraryalbumtracks",
    "libraryalbumcover",
    "librarysearchartist",
    "librarysearchalbum",
    "librarysearchgenre",
    "librarysearchtitle",
    "pluginversion",
];

#[test]
fn every_legacy_command_has_dispatch_arm() {
    let path = Path::new("src/server/legacy/commands.rs");
    let source = std::fs::read_to_string(path)
        .unwrap_or_else(|e| panic!("read {}: {}", path.display(), e));

    // Map each context constant name → its wire string by reading
    // protocol/constants.rs. We grep for `pub const NAME: &str = "value";`.
    let constants_src = std::fs::read_to_string("src/protocol/constants.rs")
        .expect("read protocol/constants.rs");
    let mut name_to_value: std::collections::HashMap<String, String> =
        std::collections::HashMap::new();
    for line in constants_src.lines() {
        let line = line.trim();
        if !line.starts_with("pub const ") {
            continue;
        }
        if let Some(rest) = line.strip_prefix("pub const ") {
            if let Some(colon) = rest.find(':') {
                let name = rest[..colon].trim().to_string();
                if let Some(eq) = rest.find('=') {
                    let value_part = rest[eq + 1..].trim();
                    if value_part.starts_with('"') {
                        if let Some(end) = value_part[1..].find('"') {
                            let value = &value_part[1..1 + end];
                            name_to_value.insert(name, value.to_string());
                        }
                    }
                }
            }
        }
    }

    // Find every `constants::IDENT` token in commands.rs and resolve
    // to its wire string. This catches ident-renames and missed arms.
    let mut handled_values: HashSet<String> = HashSet::new();
    for token in source.split(|c: char| !(c.is_alphanumeric() || c == '_' || c == ':')) {
        if let Some(ident) = token.strip_prefix("constants::") {
            if let Some(value) = name_to_value.get(ident) {
                handled_values.insert(value.clone());
            }
        }
    }

    let expected: HashSet<&str> = EXPECTED_COMMAND_CONTEXTS.iter().copied().collect();
    let missing: Vec<&str> = expected
        .iter()
        .filter(|ctx| !handled_values.contains(**ctx))
        .copied()
        .collect();

    assert!(
        missing.is_empty(),
        "legacy V4 command(s) without a dispatch arm in server/legacy/commands.rs: {:?}\n\
         Either add a handler or, if the command is intentionally unsupported, \
         remove it from EXPECTED_COMMAND_CONTEXTS in tests/dispatch_coverage.rs.",
        missing
    );
}
