//! The one seam between the update logic and the network.
//!
//! The production client is WinHTTP (system proxy including PAC/WPAD, OS root
//! certificates, no new build tooling for the i686 target - see `docs/updates.md`),
//! which cannot be exercised from a non-Windows host. Everything interesting -
//! version comparison, ETag caching, interval and skip-version suppression,
//! signature verification, staging - sits above this trait and is tested against
//! a stub, so exactly one thin implementation is Windows-bound.

use crate::error::{Result, UpdateError};

/// What a `GET` produced. Only the pieces the updater acts on: the rest of an
/// HTTP response is not this crate's business.
#[derive(Debug, Clone)]
pub struct HttpResponse {
    pub status: u16,
    /// The response `ETag`, stored and sent back as `If-None-Match` so a repeat
    /// check that changes nothing costs one 304 instead of a download.
    pub etag: Option<String>,
    pub body: Vec<u8>,
}

/// Not modified since the caller's `ETag`.
pub const STATUS_NOT_MODIFIED: u16 = 304;

impl HttpResponse {
    pub fn is_not_modified(&self) -> bool {
        self.status == STATUS_NOT_MODIFIED
    }

    pub fn is_success(&self) -> bool {
        (200..300).contains(&self.status)
    }

    /// The body, or an error for any status that is not a success.
    ///
    /// A 304 is an error here too: it is meaningful only where the caller asked
    /// for it, and those call sites check [`is_not_modified`](Self::is_not_modified)
    /// first. Reaching this with one means a conditional request was made
    /// somewhere that cannot handle the conditional answer.
    pub fn into_body(self, url: &str) -> Result<Vec<u8>> {
        if self.is_success() {
            Ok(self.body)
        } else {
            Err(UpdateError::Http {
                status: self.status,
                url: url.to_owned(),
            })
        }
    }
}

/// A blocking HTTP GET.
///
/// Deliberately the whole interface. The updater only ever reads: it fetches a
/// release document, a manifest, a signature, and a zip. Anything richer would be
/// surface that the WinHTTP implementation has to carry for no caller.
pub trait HttpClient {
    /// Issues `GET url`. When `etag` is set it is sent as `If-None-Match`, and a
    /// `304` is a normal, non-error outcome the caller is expected to handle.
    fn get(&self, url: &str, etag: Option<&str>) -> Result<HttpResponse>;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn into_body_rejects_failure_statuses() {
        let response = HttpResponse {
            status: 404,
            etag: None,
            body: b"nope".to_vec(),
        };
        assert!(matches!(
            response.into_body("https://example.invalid/x"),
            Err(UpdateError::Http { status: 404, .. })
        ));
    }

    #[test]
    fn into_body_rejects_an_unexpected_304() {
        let response = HttpResponse {
            status: STATUS_NOT_MODIFIED,
            etag: None,
            body: Vec::new(),
        };
        assert!(response.into_body("https://example.invalid/x").is_err());
    }
}
