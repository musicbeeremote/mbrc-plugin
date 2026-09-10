//! The embedded Vue bundle.
//!
//! The assets live inside `mbrc_core.dll` rather than in a folder beside it, so
//! deployment stays the two DLLs it already is and there is no third artifact for
//! the updater, the installer or the uninstall sweep to track.

use axum::http::{StatusCode, Uri, header};
use axum::response::{IntoResponse, Response};
use rust_embed::Embed;

#[derive(Embed)]
#[folder = "../web-app/dist"]
struct Assets;

/// Vite fingerprints every asset filename, so a hit can be cached indefinitely.
const IMMUTABLE: &str = "public, max-age=31536000, immutable";

/// Everything that is not fingerprinted must be revalidated: the entry document,
/// whose stale copy points at asset names a new build has removed, and the
/// service worker and manifest, whose names are equally fixed and whose stale
/// copies would outlive several releases.
const NO_CACHE: &str = "no-cache";

/// Where Vite puts the files whose names carry their content hash.
const FINGERPRINTED: &str = "assets/";

pub async fn serve(uri: Uri) -> Response {
    let path = uri.path().trim_start_matches('/');
    let path = if path.is_empty() { "index.html" } else { path };

    match Assets::get(path) {
        Some(file) => {
            let mime = mime_for(path);
            let cache = cache_for(path);
            (
                [(header::CONTENT_TYPE, mime), (header::CACHE_CONTROL, cache)],
                file.data,
            )
                .into_response()
        }
        // Unknown paths fall back to the entry document so the SPA's own router
        // owns them; a missing bundle is the only real 404 here.
        None => match Assets::get("index.html") {
            Some(index) => (
                [
                    (header::CONTENT_TYPE, "text/html; charset=utf-8"),
                    (header::CACHE_CONTROL, NO_CACHE),
                ],
                index.data,
            )
                .into_response(),
            None => (StatusCode::NOT_FOUND, "web app not built").into_response(),
        },
    }
}

/// How long a served file may be kept.
///
/// Only Vite's fingerprinted names are safe to keep forever. Everything else -
/// the entry document, the service worker, the manifest, the icons - has a
/// fixed name, so a cached copy would outlive the build it came from.
fn cache_for(path: &str) -> &'static str {
    if path.starts_with(FINGERPRINTED) {
        IMMUTABLE
    } else {
        NO_CACHE
    }
}

fn mime_for(path: &str) -> &'static str {
    match path.rsplit('.').next().unwrap_or("") {
        "html" => "text/html; charset=utf-8",
        "js" => "text/javascript; charset=utf-8",
        "css" => "text/css; charset=utf-8",
        "json" => "application/json",
        "webmanifest" => "application/manifest+json",
        "svg" => "image/svg+xml",
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "ico" => "image/x-icon",
        "woff2" => "font/woff2",
        _ => "application/octet-stream",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A file whose name is fixed must be revalidated. Kept for a year, a stale
    /// service worker would go on serving a build nothing else belongs to.
    #[test]
    fn only_fingerprinted_names_are_kept_forever() {
        assert_eq!(cache_for("assets/index-D1Vco_Zz.js"), IMMUTABLE);

        for fixed in [
            "index.html",
            "sw.js",
            "manifest.webmanifest",
            "icon-192.png",
        ] {
            assert_eq!(cache_for(fixed), NO_CACHE, "{fixed} must be revalidated");
        }
    }
}
