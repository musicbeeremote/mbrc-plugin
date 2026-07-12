//! Cover image handling in the core: resizing, cache identities (SHA1), and the
//! on-disk album cover cache. The C# host hands over raw MusicBee artwork and
//! the core owns sizing + caching. The wire contract is unchanged -
//! `nowplayingcover`/`libraryalbumcover` still reply `{status, cover, ...}` with
//! base64 JPEG; only *where* the resize/cache happens moves.

use std::fmt::Write as _;
use std::io::Cursor;

use base64::Engine;
use sha1::{Digest, Sha1};

pub mod store;

/// The album-cover cache thumbnail size (C# `DefaultCacheSize`).
pub const CACHE_SIZE: u32 = 150;
/// The now-playing cover size (C# `DefaultResizeSize`).
pub const NOW_PLAYING_SIZE: u32 = 600;
/// JPEG re-encode quality for cached/served covers (C# `DefaultJpegQuality`).
const JPEG_QUALITY: u8 = 80;

/// Decode guards against a "decompression bomb" - a tiny payload whose header
/// claims enormous dimensions, which would otherwise allocate w*h*channels bytes
/// and exhaust memory. Artwork can arrive over the wire (base64 from a client),
/// so the decode is untrusted. Real album art is far under these; the resize
/// target is only a few hundred pixels.
const MAX_DECODE_DIM: u32 = 12_000;
const MAX_DECODE_ALLOC: u64 = 256 * 1024 * 1024;

/// The SHA1 of empty input: 40 zeros (matches C# `HashingUtilities.EmptyHash`).
pub const EMPTY_SHA1: &str = "0000000000000000000000000000000000000000";

/// Standard base64 (no padding config change) - matches what the plugin sends.
fn b64() -> base64::engine::general_purpose::GeneralPurpose {
    base64::engine::general_purpose::STANDARD
}

/// Lowercase hex of a byte slice.
fn hex_lower(bytes: &[u8]) -> String {
    let mut s = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        let _ = write!(s, "{b:02x}");
    }
    s
}

/// SHA1 of raw bytes as lowercase hex. Empty input -> 40 zeros. Byte-matches C#
/// `HashingUtilities.Sha1Hash(byte[])` (used for the content hash / etag).
pub fn sha1_hex(data: &[u8]) -> String {
    if data.is_empty() {
        return EMPTY_SHA1.to_string();
    }
    let mut hasher = Sha1::new();
    hasher.update(data);
    hex_lower(&hasher.finalize())
}

/// SHA1 of a UTF-8 string as lowercase hex. Empty input -> 40 zeros. Byte-matches
/// C# `HashingUtilities.Sha1Hash(string)`.
pub fn sha1_hex_str(value: &str) -> String {
    if value.is_empty() {
        return EMPTY_SHA1.to_string();
    }
    sha1_hex(value.as_bytes())
}

/// The album cache key: `SHA1("{artist.lower} {album.lower}")`, lowercase hex.
/// Byte-matches C# `HashingUtilities.CoverIdentifier`. The joined string always
/// contains the separating space, so it is never empty (never the 40-zero hash)
/// even when both parts are empty. Note: uses Unicode `to_lowercase`, which can
/// differ from C# `ToLowerInvariant` for a few non-ASCII cases; parity holds for
/// ASCII (the overwhelmingly common case) and existing keys re-resolve.
pub fn cover_identifier(artist: &str, album: &str) -> String {
    sha1_hex_str(&format!(
        "{} {}",
        artist.to_lowercase(),
        album.to_lowercase()
    ))
}

/// Resize raw image bytes to fit within `max_w` x `max_h`, preserving aspect and
/// never upscaling (mirrors C# `CalculateScaledSize`), re-encoding as JPEG.
/// Decode an image with allocation + dimension limits enforced, so an untrusted
/// payload can't trigger a huge allocation (a decompression bomb: a tiny file
/// whose header claims enormous dimensions). Dimensions over the cap are rejected
/// against the header, before the pixel buffer is allocated.
fn decode_limited(raw: &[u8]) -> Result<image::DynamicImage, String> {
    let mut reader = image::ImageReader::new(Cursor::new(raw))
        .with_guessed_format()
        .map_err(|e| format!("image format: {e}"))?;
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(MAX_DECODE_DIM);
    limits.max_image_height = Some(MAX_DECODE_DIM);
    limits.max_alloc = Some(MAX_DECODE_ALLOC);
    reader.limits(limits);
    reader.decode().map_err(|e| format!("image decode: {e}"))
}

/// Returns the resized JPEG bytes (used for the content hash + on-disk file).
///
/// Pipeline: decode with `image` (any supported format) -> flatten to RGB8 (JPEG
/// has no alpha; GDI+ flattened too) -> downscale with SIMD `fast_image_resize`
/// (the measured bottleneck; several times faster than `image`'s scalar resize)
/// -> JPEG-encode. Bilinear convolution: at a 150px thumbnail it is visually
/// indistinguishable from bicubic/Lanczos while being the cheapest filter.
pub fn resize_to_jpeg(raw: &[u8], max_w: u32, max_h: u32) -> Result<Vec<u8>, String> {
    let img = decode_limited(raw)?;
    let (w, h) = (img.width(), img.height());
    let (tw, th) = scaled_size(w, h, max_w, max_h);

    // Flatten to RGB8 once; both the no-resample and resample paths encode from it.
    let rgb = img.into_rgb8().into_raw();

    let pixels = if (tw, th) == (w, h) {
        // Identical target = no resample (avoids a needless re-filter of small art).
        rgb
    } else {
        use fast_image_resize::images::Image;
        use fast_image_resize::{FilterType, PixelType, ResizeAlg, ResizeOptions, Resizer};

        let src = Image::from_vec_u8(w, h, rgb, PixelType::U8x3)
            .map_err(|e| format!("resize source: {e}"))?;
        let mut dst = Image::new(tw, th, PixelType::U8x3);
        Resizer::new()
            .resize(
                &src,
                &mut dst,
                &ResizeOptions::new().resize_alg(ResizeAlg::Convolution(FilterType::Bilinear)),
            )
            .map_err(|e| format!("resize: {e}"))?;
        dst.into_vec()
    };

    // Quality 80 to match the shipped C# (`DefaultJpegQuality = 80`); the crate
    // default is 75. Encoder scales differ (GDI+ vs image crate) so bytes never
    // matched C# anyway, but 80 keeps the visual fidelity the plugin always had.
    let mut out = Vec::new();
    image::codecs::jpeg::JpegEncoder::new_with_quality(&mut Cursor::new(&mut out), JPEG_QUALITY)
        .encode(&pixels, tw, th, image::ExtendedColorType::Rgb8)
        .map_err(|e| format!("jpeg encode: {e}"))?;
    Ok(out)
}

/// Standard-base64 encode raw bytes (e.g. a cached JPEG for the wire `cover`).
pub fn to_base64(bytes: &[u8]) -> String {
    b64().encode(bytes)
}

/// Decode standard base64 (trimmed) to raw bytes. `None` on malformed input -
/// used to turn the host's base64 artwork back into image bytes for resizing.
pub fn from_base64(input_b64: &str) -> Option<Vec<u8>> {
    b64().decode(input_b64.trim()).ok()
}

/// Resize a base64 image to fit within `max_w` x `max_h`, re-encoding as JPEG.
/// Returns the base64 of the resized JPEG.
pub fn resize_base64_jpeg(input_b64: &str, max_w: u32, max_h: u32) -> Result<String, String> {
    let raw = b64()
        .decode(input_b64.trim())
        .map_err(|e| format!("base64 decode: {e}"))?;
    Ok(b64().encode(resize_to_jpeg(&raw, max_w, max_h)?))
}

/// Aspect-preserving, no-upscale target size. Port of the shipped C#
/// `CalculateScaledSize`: scale = min over each axis of `max/dim` (or 1 when the
/// source is already smaller than the box).
fn scaled_size(w: u32, h: u32, max_w: u32, max_h: u32) -> (u32, u32) {
    let sx = if w < max_w {
        1.0
    } else {
        max_w as f32 / w as f32
    };
    let sy = if h < max_h {
        1.0
    } else {
        max_h as f32 / h as f32
    };
    let s = sx.min(sy);
    (((w as f32) * s) as u32, ((h as f32) * s) as u32)
}

/// A synthetic JPEG of the given size, as raw bytes. Shared by the cover unit
/// tests and the `store` submodule tests.
#[cfg(test)]
pub(crate) fn test_jpeg_bytes(w: u32, h: u32) -> Vec<u8> {
    let img = image::RgbImage::from_fn(w, h, |x, y| {
        image::Rgb([(x % 256) as u8, (y % 256) as u8, 128])
    });
    let mut buf = Vec::new();
    image::DynamicImage::ImageRgb8(img)
        .write_to(&mut Cursor::new(&mut buf), image::ImageFormat::Jpeg)
        .unwrap();
    buf
}

#[cfg(test)]
mod tests {
    use super::*;

    fn make_jpeg_base64(w: u32, h: u32) -> String {
        b64().encode(test_jpeg_bytes(w, h))
    }

    fn dims(b64s: &str) -> (u32, u32) {
        let raw = b64().decode(b64s).unwrap();
        let img = image::load_from_memory(&raw).unwrap();
        (img.width(), img.height())
    }

    #[test]
    fn scaled_size_matches_csharp() {
        assert_eq!(scaled_size(1200, 800, 600, 600), (600, 400)); // fit width
        assert_eq!(scaled_size(200, 150, 600, 600), (200, 150)); // no upscale
        assert_eq!(scaled_size(600, 600, 600, 600), (600, 600)); // exact
        assert_eq!(scaled_size(1000, 2000, 150, 150), (75, 150)); // tall, fit height
    }

    #[test]
    fn resizes_large_image_down_preserving_aspect() {
        let input = make_jpeg_base64(1200, 800);
        let out = resize_base64_jpeg(&input, 600, 600).unwrap();
        assert_eq!(dims(&out), (600, 400));
    }

    #[test]
    fn does_not_upscale_small_image() {
        let input = make_jpeg_base64(200, 150);
        let out = resize_base64_jpeg(&input, 600, 600).unwrap();
        assert_eq!(dims(&out), (200, 150));
    }

    #[test]
    fn rejects_non_base64_and_non_image() {
        assert!(resize_base64_jpeg("not base64 !!!", 600, 600).is_err());
        let not_an_image = b64().encode(b"hello world, definitely not an image");
        assert!(resize_base64_jpeg(&not_an_image, 600, 600).is_err());
    }

    #[test]
    fn rejects_oversized_dimensions_decompression_bomb() {
        // An image wider than the decode cap is rejected against its header, not
        // decoded. Kept a real, tiny (~few KB PNG) 1px-tall image so that if the
        // limit were NOT applied the assert just fails - it never OOMs the test.
        let big = image::DynamicImage::ImageRgb8(image::RgbImage::from_pixel(
            MAX_DECODE_DIM + 1000,
            1,
            image::Rgb([1, 2, 3]),
        ));
        let mut bytes = Vec::new();
        big.write_to(&mut Cursor::new(&mut bytes), image::ImageFormat::Png)
            .unwrap();
        assert!(
            resize_to_jpeg(&bytes, 600, 600).is_err(),
            "an over-cap image must be rejected by the decode limits"
        );

        // A normal image is unaffected by the guard.
        assert!(resize_to_jpeg(&test_jpeg_bytes(1200, 800), 600, 600).is_ok());
    }

    // Golden hashes computed with `sha1sum` (same algorithm as C#
    // HashingUtilities), so these are an independent oracle - existing
    // state.json keys written by the C# cache must resolve to the same values.
    #[test]
    fn sha1_matches_known_vectors() {
        assert_eq!(
            sha1_hex(b"hello"),
            "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d"
        );
        // Empty input -> 40 zeros (C# EmptyHash), NOT the real SHA1 of "".
        assert_eq!(sha1_hex(&[]), EMPTY_SHA1);
        assert_eq!(sha1_hex_str(""), EMPTY_SHA1);
    }

    #[test]
    fn cover_identifier_matches_csharp() {
        // SHA1("the beatles abbey road"), lowercased+joined with a space.
        assert_eq!(
            cover_identifier("The Beatles", "Abbey Road"),
            "7dc1498fc3b3956b5cca9585582d1158cc410293"
        );
        // Both parts empty -> SHA1(" ") (the join always keeps the space), so it
        // is NOT the empty 40-zero hash.
        assert_eq!(
            cover_identifier("", ""),
            "b858cb282617fb0956d960215c8e84d1ccf909c6"
        );
    }

    #[test]
    fn resize_to_jpeg_is_hashable_and_smaller() {
        let raw = b64().decode(make_jpeg_base64(1200, 800)).unwrap();
        let out = resize_to_jpeg(&raw, CACHE_SIZE, CACHE_SIZE).unwrap();
        let img = image::load_from_memory(&out).unwrap();
        assert_eq!((img.width(), img.height()), (150, 100));
        // A stable, non-empty content hash (the on-disk filename / etag).
        assert_eq!(sha1_hex(&out).len(), 40);
    }
}
