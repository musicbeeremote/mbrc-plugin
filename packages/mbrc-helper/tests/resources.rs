//! The built exe carries its version resource and application manifest.

#![cfg(windows)]

/// `needle` encoded the way a version resource stores its strings.
fn utf16(needle: &str) -> Vec<u8> {
    needle.encode_utf16().flat_map(u16::to_le_bytes).collect()
}

fn contains(haystack: &[u8], needle: &[u8]) -> bool {
    haystack.windows(needle.len()).any(|w| w == needle)
}

fn helper_image() -> Vec<u8> {
    std::fs::read(env!("CARGO_BIN_EXE_mbrc-helper")).expect("read the built helper")
}

#[test]
fn the_helper_describes_itself_and_its_version() {
    let image = helper_image();
    for value in [
        "MusicBee Remote Helper",
        "MusicBee Remote",
        "mbrc-helper.exe",
        env!("MBRC_VERSION"),
    ] {
        assert!(
            contains(&image, &utf16(value)),
            "no {value:?} in the version resource"
        );
    }
}

#[test]
fn the_helper_runs_as_invoker() {
    let image = helper_image();
    assert!(
        contains(&image, br#"level="asInvoker""#),
        "no application manifest"
    );
}
