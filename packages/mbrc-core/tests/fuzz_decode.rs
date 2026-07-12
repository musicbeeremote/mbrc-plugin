//! Property-based fuzzing of mbrc-core's untrusted decode paths: album artwork
//! (base64 + image decode + SIMD resize + JPEG encode) and MessagePack settings
//! decode. These eat bytes that ultimately come from a client or the host, so
//! the requirement is: never panic - always degrade to an `Err`/`None`.
//!
//! cargo-fuzz would be the coverage-guided tool but needs nightly; the toolchain
//! is pinned to stable, so these run as proptest cases under `cargo test` + CI.

use mbrc_core::config::Config;
use mbrc_core::cover::{from_base64, resize_base64_jpeg, resize_to_jpeg};
use mbrc_core::wire::{sanitize_lyrics, strip_lrc};
use proptest::prelude::*;

proptest! {
    // Decoding + resizing arbitrary bytes as artwork must never panic; a
    // non-image just returns Err. Dimensions are bounded so a decodable image in
    // the corpus can't trigger an enormous allocation.
    #[test]
    fn resize_to_jpeg_never_panics(
        raw in prop::collection::vec(any::<u8>(), 0..4096),
        w in 1u32..512,
        h in 1u32..512,
    ) {
        let _ = resize_to_jpeg(&raw, w, h);
    }

    // base64 decode of arbitrary text never panics (returns None on garbage).
    #[test]
    fn from_base64_never_panics(s in "(?s).*") {
        let _ = from_base64(&s);
    }

    // The combined base64 -> decode -> resize path never panics on arbitrary text.
    #[test]
    fn resize_base64_never_panics(s in "(?s).*", w in 1u32..512, h in 1u32..512) {
        let _ = resize_base64_jpeg(&s, w, h);
    }

    // MessagePack settings decode never panics on arbitrary bytes: malformed
    // msgpack is an `Err`, not a crash - the settings write path relies on this.
    #[test]
    fn settings_msgpack_decode_never_panics(bytes in prop::collection::vec(any::<u8>(), 0..2048)) {
        let _ = rmp_serde::from_slice::<Config>(&bytes);
    }

    // Lyrics text processing (LRC timing strip + V4 XML-safe sanitization) runs
    // on arbitrary lyrics text from MusicBee tags/files and must never panic.
    // Strip-then-sanitize is the real pipeline; both are also tested standalone.
    #[test]
    fn lyrics_processing_never_panics(s in "(?s).*") {
        let stripped = strip_lrc(&s);
        let _ = sanitize_lyrics(&stripped);
        let _ = sanitize_lyrics(&s);
        let _ = strip_lrc(&s);
    }
}
