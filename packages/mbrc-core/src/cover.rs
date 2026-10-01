//! Cover image handling in the core: resizing, cache identities (SHA1), and the
//! on-disk album cover cache.
//!
//! The C# host hands over raw MusicBee artwork and the core owns sizing +
//! caching. The wire contract is unchanged -
//! `nowplayingcover`/`libraryalbumcover` still reply `{status, cover, ...}`
//! with base64 JPEG; only *where* the resize/cache happens moves.

use std::fmt::Write as _;
use std::io::Cursor;

use base64::Engine;
use sha1::{Digest, Sha1};

pub mod store;

/// The album-cover cache thumbnail size.
///
/// Raised from the C# `DefaultCacheSize` of 150, which every grid in the web
/// app draws larger than: 97% of a real library's covers were being capped at
/// 150 from a bigger source, so the detail was discarded and then upscaled
/// back. Measured at ~2x the bytes for a cache of a few thousand albums.
pub const CACHE_SIZE: u32 = 250;
/// The now-playing cover size.
///
/// Raised from the C# `DefaultResizeSize` of 600: the web app can open the art
/// at window size, where 600 is an upscale. Rendered per request from the
/// original, so it costs bytes on a track change and nothing on disk.
pub const NOW_PLAYING_SIZE: u32 = 900;
/// JPEG re-encode quality for cached/served covers (C# `DefaultJpegQuality`).
const JPEG_QUALITY: u8 = 80;

/// Decode guards against a "decompression bomb" - a tiny payload whose header
/// claims enormous dimensions, which would otherwise allocate w*h*channels bytes
/// and exhaust memory. Artwork can arrive over the wire (base64 from a client),
/// so the decode is untrusted. Real album art is far under these; the resize
/// target is only a few hundred pixels.
const MAX_DECODE_DIM: u32 = 12_000;
/// The most one full-size decode may allocate. A JPEG is decoded at reduced
/// scale and never comes near it; this bounds the formats that cannot be.
const MAX_DECODE_ALLOC: u64 = 64 * 1024 * 1024;

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
///
/// Byte-matches C# `HashingUtilities.CoverIdentifier`. The joined string always
/// contains the separating space, so it is never empty (never the 40-zero hash)
/// even when both parts are empty. Note: uses Unicode `to_lowercase`, which can
/// differ from C# `ToLowerInvariant` for a few non-ASCII cases; parity holds
/// for ASCII (the overwhelmingly common case) and existing keys re-resolve.
pub fn cover_identifier(artist: &str, album: &str) -> String {
    sha1_hex_str(&format!(
        "{} {}",
        artist.to_lowercase(),
        album.to_lowercase()
    ))
}

/// A resized cover, and what it was made from.
#[derive(Debug, Clone)]
pub struct Resized {
    /// The resized JPEG: the cache file and the hash input.
    pub jpeg: Vec<u8>,
    /// The source image's width and height.
    pub source: (u32, u32),
    /// Whether the JPEG was decoded at reduced scale rather than full size.
    pub reduced: bool,
}

/// Resizes raw artwork to fit within `max_w` x `max_h`, preserving aspect and
/// never upscaling (mirrors C# `CalculateScaledSize`), re-encoding as JPEG.
///
/// A JPEG is decoded at the smallest DCT scale (1/1 to 1/8) that still covers
/// the target, so a 3000 px cover bound for 250 px never exists at full size.
/// Anything else, or a JPEG the scaled decoder cannot read, takes the full
/// decode, which first checks that its memory can be had at all.
///
/// # Errors
/// The bytes do not decode as a supported image, exceed the decode limits, need
/// more memory than is available, or fail to re-encode as JPEG.
pub fn resize_cover(raw: &[u8], max_w: u32, max_h: u32) -> Result<Resized, String> {
    if let Some(jpeg) = decode_jpeg_reduced(raw, max_w, max_h) {
        let target = scaled_size(jpeg.source.0, jpeg.source.1, max_w, max_h);
        return Ok(Resized {
            jpeg: encode_resized(jpeg.rgb, jpeg.decoded, target)?,
            source: jpeg.source,
            reduced: jpeg.decoded != jpeg.source,
        });
    }

    let img = decode_limited(raw)?;
    let source = (img.width(), img.height());
    let target = scaled_size(source.0, source.1, max_w, max_h);
    // Flatten to RGB8 once; JPEG has no alpha, and GDI+ flattened too.
    let rgb = img.into_rgb8().into_raw();
    Ok(Resized {
        jpeg: encode_resized(rgb, source, target)?,
        source,
        reduced: false,
    })
}

/// Returns the resized JPEG bytes (used for the content hash + on-disk file).
///
/// # Errors
/// As [`resize_cover`].
pub fn resize_to_jpeg(raw: &[u8], max_w: u32, max_h: u32) -> Result<Vec<u8>, String> {
    resize_cover(raw, max_w, max_h).map(|resized| resized.jpeg)
}

/// A JPEG decoded at reduced scale.
struct ReducedJpeg {
    /// RGB8 pixels at `decoded` size.
    rgb: Vec<u8>,
    decoded: (u32, u32),
    /// The full size the JPEG declares.
    source: (u32, u32),
}

/// Decodes a JPEG at the smallest scale that still covers the target, as RGB8.
///
/// `None` when the bytes are not a JPEG this decoder handles (arithmetic coding,
/// CMYK, 16-bit), which sends the caller to the full decode instead.
fn decode_jpeg_reduced(raw: &[u8], max_w: u32, max_h: u32) -> Option<ReducedJpeg> {
    if !raw.starts_with(&[0xFF, 0xD8]) {
        return None;
    }
    let mut decoder = jpeg_decoder::Decoder::new(Cursor::new(raw));
    decoder.read_info().ok()?;
    let info = decoder.info()?;
    let source = (u32::from(info.width), u32::from(info.height));
    if source.0 > MAX_DECODE_DIM || source.1 > MAX_DECODE_DIM {
        return None;
    }
    let (tw, th) = scaled_size(source.0, source.1, max_w, max_h);
    let (sw, sh) = decoder
        .scale(u16::try_from(tw).ok()?, u16::try_from(th).ok()?)
        .ok()?;
    let pixels = decoder.decode().ok()?;
    let decoded = (u32::from(sw), u32::from(sh));
    let rgb = match decoder.info()?.pixel_format {
        jpeg_decoder::PixelFormat::RGB24 => pixels,
        jpeg_decoder::PixelFormat::L8 => pixels.iter().flat_map(|&l| [l, l, l]).collect(),
        _ => return None,
    };
    (rgb.len() == decoded.0 as usize * decoded.1 as usize * 3).then_some(ReducedJpeg {
        rgb,
        decoded,
        source,
    })
}

/// Decodes an image at full size with allocation + dimension limits enforced.
///
/// The limits stop an untrusted payload whose header claims enormous dimensions
/// (a decompression bomb) before its pixel buffer is allocated. Within them, the
/// memory is reserved fallibly first, so a 32-bit process short of address space
/// gets an error back rather than an allocation failure, which aborts.
fn decode_limited(raw: &[u8]) -> Result<image::DynamicImage, String> {
    let reader = || {
        let mut reader = image::ImageReader::new(Cursor::new(raw))
            .with_guessed_format()
            .map_err(|e| format!("image format: {e}"))?;
        let mut limits = image::Limits::default();
        limits.max_image_width = Some(MAX_DECODE_DIM);
        limits.max_image_height = Some(MAX_DECODE_DIM);
        limits.max_alloc = Some(MAX_DECODE_ALLOC);
        reader.limits(limits);
        Ok::<_, String>(reader)
    };
    let (w, h) = reader()?
        .into_dimensions()
        .map_err(|e| format!("image header: {e}"))?;
    ensure_room(full_decode_bytes(w, h))?;
    reader()?.decode().map_err(|e| format!("image decode: {e}"))
}

/// The most a full decode of a `w` x `h` image holds at once: the decoded pixels
/// (up to four 8-bit channels) plus the RGB8 copy the encoder is fed.
fn full_decode_bytes(w: u32, h: u32) -> usize {
    (w as usize)
        .saturating_mul(h as usize)
        .saturating_mul(4 + 3)
}

/// Checks that `bytes` can be allocated, without keeping them.
///
/// # Errors
/// The allocation would fail; the cover is then reported too large rather than
/// taking the process down.
fn ensure_room(bytes: usize) -> Result<(), String> {
    Vec::<u8>::new()
        .try_reserve_exact(bytes)
        .map_err(|_| format!("too large to decode: needs {} MiB", bytes / (1024 * 1024)))
}

/// Scales RGB8 `pixels` of size `from` to `to`, then JPEG-encodes them.
///
/// Bilinear convolution: at a thumbnail size it is visually indistinguishable
/// from bicubic or Lanczos while being the cheapest filter. Identical sizes skip
/// the resample, so small art is not re-filtered for nothing.
fn encode_resized(pixels: Vec<u8>, from: (u32, u32), to: (u32, u32)) -> Result<Vec<u8>, String> {
    let pixels = if from == to {
        pixels
    } else {
        use fast_image_resize::images::Image;
        use fast_image_resize::{FilterType, PixelType, ResizeAlg, ResizeOptions, Resizer};

        let src = Image::from_vec_u8(from.0, from.1, pixels, PixelType::U8x3)
            .map_err(|e| format!("resize source: {e}"))?;
        let mut dst = Image::new(to.0, to.1, PixelType::U8x3);
        Resizer::new()
            .resize(
                &src,
                &mut dst,
                &ResizeOptions::new().resize_alg(ResizeAlg::Convolution(FilterType::Bilinear)),
            )
            .map_err(|e| format!("resize: {e}"))?;
        dst.into_vec()
    };

    // Quality 80 matches the shipped C# `DefaultJpegQuality`; the crate
    // default is 75. Encoders differ, so bytes never matched anyway.
    let mut out = Vec::new();
    image::codecs::jpeg::JpegEncoder::new_with_quality(&mut Cursor::new(&mut out), JPEG_QUALITY)
        .encode(&pixels, to.0, to.1, image::ExtendedColorType::Rgb8)
        .map_err(|e| format!("jpeg encode: {e}"))?;
    Ok(out)
}

/// Standard-base64 encode raw bytes (e.g. a cached JPEG for the wire `cover`).
pub fn to_base64(bytes: &[u8]) -> String {
    b64().encode(bytes)
}

/// Decodes standard base64 (trimmed) to raw bytes. `None` on malformed input -
/// used to turn the host's base64 artwork back into image bytes for resizing.
pub fn from_base64(input_b64: &str) -> Option<Vec<u8>> {
    b64().decode(input_b64.trim()).ok()
}

/// Resizes a base64 image to fit within `max_w` x `max_h`, re-encoding as JPEG.
/// Returns the base64 of the resized JPEG.
///
/// # Errors
/// The input is not valid base64, or the decoded bytes fail
/// [`resize_to_jpeg`].
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
        // A real but tiny 1px-tall image, so an unapplied limit fails the
        // assertion instead of OOMing the test.
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

    /// Golden hashes computed with `sha1sum` (same algorithm as C#
    /// HashingUtilities), so these are an independent oracle - existing
    /// state.json keys written by the C# cache must resolve to the same values.
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

    /// A smooth gradient, so a reduced and a full decode can be compared pixel
    /// for pixel; the test pattern above is too busy for any two resizes to agree.
    fn gradient_jpeg(w: u32, h: u32, grey: bool) -> Vec<u8> {
        let img = image::RgbImage::from_fn(w, h, |x, y| {
            let r = (x * 255 / w) as u8;
            let g = (y * 255 / h) as u8;
            if grey {
                image::Rgb([r, r, r])
            } else {
                image::Rgb([r, g, 128])
            }
        });
        let img = if grey {
            image::DynamicImage::ImageLuma8(image::DynamicImage::ImageRgb8(img).into_luma8())
        } else {
            image::DynamicImage::ImageRgb8(img)
        };
        let mut buf = Vec::new();
        img.write_to(&mut Cursor::new(&mut buf), image::ImageFormat::Jpeg)
            .unwrap();
        buf
    }

    /// The same resize the full-size path produces, for comparison.
    fn full_decode_resize(raw: &[u8], max: u32) -> image::RgbImage {
        let img = image::load_from_memory(raw).unwrap();
        let (w, h) = (img.width(), img.height());
        let target = scaled_size(w, h, max, max);
        let jpeg = encode_resized(img.into_rgb8().into_raw(), (w, h), target).unwrap();
        image::load_from_memory(&jpeg).unwrap().into_rgb8()
    }

    fn mean_abs_diff(a: &image::RgbImage, b: &image::RgbImage) -> f64 {
        assert_eq!(a.dimensions(), b.dimensions());
        let total: u64 = a
            .as_raw()
            .iter()
            .zip(b.as_raw())
            .map(|(x, y)| u64::from(x.abs_diff(*y)))
            .sum();
        total as f64 / a.as_raw().len() as f64
    }

    #[test]
    fn a_large_jpeg_is_decoded_at_reduced_scale_and_looks_the_same() {
        let raw = gradient_jpeg(2000, 1600, false);
        let resized = resize_cover(&raw, CACHE_SIZE, CACHE_SIZE).unwrap();
        assert!(
            resized.reduced,
            "a 2000 px source for 250 px decodes at 1/4 or 1/8"
        );
        assert_eq!(resized.source, (2000, 1600));
        let out = image::load_from_memory(&resized.jpeg).unwrap().into_rgb8();
        assert_eq!(out.dimensions(), (250, 200));
        let diff = mean_abs_diff(&out, &full_decode_resize(&raw, CACHE_SIZE));
        assert!(diff < 4.0, "mean difference per channel {diff}");
    }

    #[test]
    fn a_grayscale_jpeg_is_decoded_at_reduced_scale_too() {
        let resized =
            resize_cover(&gradient_jpeg(1600, 1600, true), CACHE_SIZE, CACHE_SIZE).unwrap();
        assert!(resized.reduced);
        let out = image::load_from_memory(&resized.jpeg).unwrap().into_rgb8();
        assert_eq!(out.dimensions(), (250, 250));
    }

    #[test]
    fn a_jpeg_already_small_enough_is_not_reduced() {
        let resized = resize_cover(&test_jpeg_bytes(200, 150), CACHE_SIZE, CACHE_SIZE).unwrap();
        assert!(!resized.reduced);
        assert_eq!(resized.source, (200, 150));
    }

    #[test]
    fn a_png_takes_the_full_decode() {
        let img = image::RgbImage::from_pixel(800, 600, image::Rgb([10, 20, 30]));
        let mut png = Vec::new();
        image::DynamicImage::ImageRgb8(img)
            .write_to(&mut Cursor::new(&mut png), image::ImageFormat::Png)
            .unwrap();
        let resized = resize_cover(&png, CACHE_SIZE, CACHE_SIZE).unwrap();
        assert!(!resized.reduced);
        assert_eq!(resized.source, (800, 600));
    }

    #[test]
    fn memory_that_cannot_be_had_is_an_error_not_an_abort() {
        let error = ensure_room(usize::MAX).unwrap_err();
        assert!(error.starts_with("too large to decode"), "{error}");
        assert!(ensure_room(1024).is_ok());
        assert_eq!(full_decode_bytes(u32::MAX, u32::MAX), usize::MAX);
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
        // The long side takes the cap and the shape is kept, whatever the cap
        // is set to - a size written into the assertion outlives the constant.
        assert_eq!(img.width(), CACHE_SIZE);
        assert_eq!(img.height(), CACHE_SIZE * 800 / 1200);
        // A stable, non-empty content hash (the on-disk filename / etag).
        assert_eq!(sha1_hex(&out).len(), 40);
    }
}
