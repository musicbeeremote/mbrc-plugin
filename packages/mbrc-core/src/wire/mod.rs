//! The wire codec seam: renders the strictly-typed canonical core model into
//! the client-facing JSON shapes for a negotiated protocol version, and parses
//! client input strings back into canonical enums.
//!
//! All V4 spellings/quirks (stringified volume, enum spellings, the input value
//! mappings) live in the version codec, not in the model - so a future V6 codec
//! is a parallel `impl WireCodec` over the same canonical data, selected by
//! [`crate::protocol::version::ProtocolVersion`], with zero handler edits.

pub mod v4;

use serde_json::Value;

use crate::protocol::messages::{
    Cover, LastfmStatus, Lyrics, NowPlayingListTrack, OutputDevices, Page, PlayState, PlayerState,
    QueueType, RepeatMode, ShuffleMode, TrackDetails, TrackInfo,
};
use crate::protocol::Platform;

/// Remove synchronized-lyrics (LRC) tags from a lyrics blob. Legacy clients
/// (V4, V5) cannot render them, so their codecs strip; V6+ receives the LRC
/// untouched.
///
/// The shipped C# regex (`\[\d:\d{2}.\d{3}\] `) only caught single-digit
/// minutes, exactly-3-digit fractions, and required a trailing space - so
/// real-world tags (`[01:23.45]`, `[00:12]`, no trailing space, several per
/// line) slipped through. This matches the full LRC grammar:
///   - time tags: `[mm:ss]`, `[mm:ss.xx]`, `[mm:ss.xxx]`, `[m:ss]`, ... (1-3
///     minute digits, 2 second digits, optional `.`/`:` + 1-3 fraction digits);
///   - metadata tags: `[ti:...]`, `[ar:...]`, `[al:...]`, `[offset:...]`, etc.
///     (a known key before the colon, so lyric text like `[Verse: 1]` is kept).
///
/// One space immediately after a stripped tag is also consumed.
pub fn strip_lrc(input: &str) -> String {
    let bytes = input.as_bytes();
    let mut out = String::with_capacity(input.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'[' {
            if let Some(rel) = bytes[i + 1..].iter().position(|&b| b == b']') {
                let content = &input[i + 1..i + 1 + rel];
                if is_lrc_tag(content) {
                    i += rel + 2; // past the closing ']'
                    if i < bytes.len() && bytes[i] == b' ' {
                        i += 1; // consume one trailing space
                    }
                    continue;
                }
            }
        }
        // Copy one whole UTF-8 char so multibyte lyrics stay intact.
        let ch = input[i..].chars().next().expect("valid char boundary");
        out.push(ch);
        i += ch.len_utf8();
    }
    out
}

/// Wire-safe lyrics text for legacy codecs (V4/V5): trim, normalize the doubled
/// CRLF paragraph breaks the shipped plugin produced, drop NULs, and XML-escape
/// (the old `SecurityElement.Escape`). LRC stripping is separate (`strip_lrc`),
/// applied before this. V6+ does not use this (JSON-native, no XML escaping).
pub fn sanitize_lyrics(raw: &str) -> String {
    let mut s = raw.trim().to_string();
    if s.contains("\r\r\n\r\r\n") {
        s = s
            .replace("\r\r\n\r\r\n", " \r\n ")
            .replace("\r\r\n", " \n ");
    }
    s = s.replace('\0', " ");
    xml_escape(&s)
}

/// Escape the five XML entities, matching .NET `SecurityElement.Escape`
/// (ampersand first so the others are not double-escaped).
fn xml_escape(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

/// True if the text between `[` and `]` is an LRC time tag or a standard
/// metadata tag.
fn is_lrc_tag(content: &str) -> bool {
    is_lrc_time_tag(content) || is_lrc_metadata_tag(content)
}

/// `mm:ss`, `mm:ss.xx`, `mm:ss.xxx`, `mm:ss:xx` (1-3 min digits, 2 sec digits,
/// optional `.`/`:` fraction of 1-3 digits).
fn is_lrc_time_tag(content: &str) -> bool {
    let Some(colon) = content.find(':') else {
        return false;
    };
    let bytes = content.as_bytes();
    let (min, rest) = (&bytes[..colon], &bytes[colon + 1..]);
    if min.is_empty() || min.len() > 3 || !min.iter().all(u8::is_ascii_digit) {
        return false;
    }
    if rest.len() < 2 || !rest[0].is_ascii_digit() || !rest[1].is_ascii_digit() {
        return false;
    }
    match &rest[2..] {
        [] => true,
        [sep, frac @ ..] if (*sep == b'.' || *sep == b':') => {
            !frac.is_empty() && frac.len() <= 3 && frac.iter().all(u8::is_ascii_digit)
        }
        _ => false,
    }
}

/// `[ti:..]`, `[ar:..]`, etc. - a known LRC metadata key before the colon.
/// Allowlisted so ordinary bracketed lyric text is not stripped.
fn is_lrc_metadata_tag(content: &str) -> bool {
    const KEYS: &[&str] = &[
        "ti", "ar", "al", "au", "by", "re", "ve", "offset", "length", "la", "lang", "tool", "id",
    ];
    match content.find(':') {
        Some(colon) => KEYS.contains(&content[..colon].to_ascii_lowercase().as_str()),
        None => false,
    }
}

/// Translates between the canonical core model and a protocol version's wire
/// form (both directions). V4 is [`v4::V4Codec`]; a V6 impl is the additive
/// extension point.
pub trait WireCodec: Send + Sync {
    // --- Output: canonical -> wire ---
    /// The composite `playerstatus` object.
    fn player_status(&self, state: &PlayerState) -> Value;
    /// The `playeroutput` object (active device + device list).
    fn output_devices(&self, devices: &OutputDevices) -> Value;
    /// The standalone `playerstate` value.
    fn play_state(&self, state: PlayState) -> Value;
    /// The standalone `playershuffle` value.
    fn shuffle(&self, mode: ShuffleMode) -> Value;
    /// The standalone `playerrepeat` value.
    fn repeat(&self, mode: RepeatMode) -> Value;
    /// The standalone `nowplayinglfmrating` value.
    fn lfm_status(&self, status: LastfmStatus) -> Value;
    /// The `nowplayinglyrics` object. Legacy codecs (V4/V5) strip LRC timing and
    /// apply the wire-safe text formatting; V6+ passes synchronized lyrics through.
    fn lyrics(&self, lyrics: &Lyrics) -> Value;
    /// The `nowplayingcover` *broadcast* payload. V4 sends a bare `{status:1}`
    /// "cover ready" marker (no inline data) so the client re-requests the image
    /// on the `nowplayingcover` request path - it ignores inline data on a
    /// broadcast. The on-request reply (full `{status,cover}`) is the `Cover` DTO.
    fn cover_notification(&self, cover: &Cover) -> Value;
    /// The `nowplayinglist` page. The item shape is platform-dependent within
    /// V4: iOS carries `album`/`album_artist` (even empty); Android omits them.
    /// The canonical `NowPlayingListTrack` holds every field regardless (V6+
    /// will surface album/album_artist on Android too - see issues #329/#196).
    fn now_playing_list(&self, page: &Page<NowPlayingListTrack>, platform: Platform) -> Value;
    /// The `nowplayingtrack` object. V4 fills empty display fields (`artist` ->
    /// `Unknown Artist`, `album` -> `Unknown Album`, `title` -> the file name) -
    /// a wire-presentation quirk that used to live in the C# host.
    fn track_info(&self, info: &TrackInfo) -> Value;
    /// The `nowplayingdetails` object. V4 fills an empty `albumArtist` with
    /// `Unknown Artist` (same host-side quirk).
    fn track_details(&self, details: &TrackDetails) -> Value;

    // --- Input: wire -> canonical ---
    /// Parse a `playerrepeat` set value (`None` for an unrecognized/query value;
    /// the `"toggle"` action is handled by the handler, not here).
    fn parse_repeat(&self, value: &str) -> Option<RepeatMode>;
    /// Parse a `nowplayinglfmrating` set value (`"love"`/`"ban"`; `"toggle"` is
    /// handled by the handler).
    fn parse_lfm(&self, value: &str) -> Option<LastfmStatus>;
    /// Parse a `nowplayingqueue` placement (defaults to `Next`, matching C#).
    fn parse_queue_type(&self, value: &str) -> QueueType;
}

pub use v4::V4_CODEC;

#[cfg(test)]
mod tests {
    use super::{sanitize_lyrics, strip_lrc};

    #[test]
    fn strip_lrc_handles_all_tag_shapes() {
        // Variations the old single-digit/3-fraction/trailing-space regex missed.
        assert_eq!(strip_lrc("[00:12.34] hello"), "hello"); // 2-digit min+frac, trailing space
        assert_eq!(strip_lrc("[00:12.34]hello"), "hello"); // no trailing space
        assert_eq!(strip_lrc("[0:12.345] hi"), "hi"); // old-style
        assert_eq!(strip_lrc("[01:02]line"), "line"); // no fraction
        assert_eq!(strip_lrc("[00:12.34][00:15.67]dup"), "dup"); // multiple per line
        assert_eq!(strip_lrc("[123:45.678]x"), "x"); // 3-digit minutes
    }

    #[test]
    fn strip_lrc_handles_metadata_and_keeps_plain_brackets() {
        assert_eq!(strip_lrc("[ti:Song][ar:Artist]words"), "words");
        assert_eq!(strip_lrc("[offset:250] text"), "text");
        // Ordinary bracketed lyric text is NOT a tag - keep it verbatim.
        assert_eq!(strip_lrc("[Chorus]"), "[Chorus]");
        assert_eq!(strip_lrc("[Verse: 1] sing"), "[Verse: 1] sing");
        // Multibyte content survives.
        assert_eq!(strip_lrc("[00:01.00] café"), "café");
    }

    #[test]
    fn sanitize_lyrics_escapes_xml_and_normalizes() {
        assert_eq!(sanitize_lyrics("a < b & c"), "a &lt; b &amp; c");
        assert_eq!(sanitize_lyrics("  trimmed  "), "trimmed");
        assert_eq!(sanitize_lyrics("x\0y"), "x y");
    }
}
