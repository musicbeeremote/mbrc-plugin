//! Shared test scaffolding: the stub that stands in for WinHTTP.
//!
//! Each integration test binary compiles this module separately and uses part of
//! it, so anything the *other* binary needs looks unused from here.
#![allow(dead_code)]

use std::cell::RefCell;
use std::collections::HashMap;

use mbrc_release::{HttpClient, HttpResponse, Result, TrustedKey, UpdateError};

/// The committed test key, lifted from `tests/keys/test.pub`. The release keys
/// never sign anything a test can produce, so every test that drives a check
/// end to end passes this as the trust list.
pub const TEST_KEYS: &[TrustedKey] = &[TrustedKey {
    name: "test",
    base64: "RWT+ztjSHP1aBowOy75aVsw0jf2Vn6MMbzuTIAPRaN5EWVPjPU9fjwAj",
}];

/// An [`HttpClient`] serving canned bodies, counting requests, and able to
/// answer 304.
///
/// Interior mutability because [`HttpClient::get`] takes `&self` - the real
/// client has no reason to need `&mut`, and the stub should not distort the
/// trait to suit itself.
#[derive(Default)]
pub struct StubHttp {
    routes: RefCell<HashMap<String, Vec<u8>>>,
    requests: RefCell<Vec<String>>,
    not_modified: RefCell<bool>,
}

impl StubHttp {
    /// The `ETag` the stub returns for every body it serves.
    pub const ETAG: &'static str = "\"stub-etag\"";

    pub fn serve(&self, url: &str, body: &[u8]) {
        self.routes
            .borrow_mut()
            .insert(url.to_owned(), body.to_vec());
    }

    /// Answers every conditional request with 304 from now on.
    pub fn reply_not_modified(&self) {
        *self.not_modified.borrow_mut() = true;
    }

    pub fn request_count(&self) -> usize {
        self.requests.borrow().len()
    }

    pub fn requested(&self, url: &str) -> bool {
        self.requests.borrow().iter().any(|r| r == url)
    }

    /// Serves a GitHub release document at `api_base` whose assets point at the
    /// manifest, the signature, and the zip for `version`.
    pub fn serve_release(&self, api_base: &str, endpoint: &str, version: &str) {
        let document = format!(
            r#"{{
              "tag_name": "v{version}",
              "assets": [
                {{ "name": "manifest.json", "browser_download_url": "https://assets.test/manifest.json" }},
                {{ "name": "manifest.json.minisig", "browser_download_url": "https://assets.test/manifest.json.minisig" }},
                {{ "name": "musicbee_remote_{version}.zip", "browser_download_url": "https://assets.test/musicbee_remote_{version}.zip" }}
              ]
            }}"#
        );
        self.serve(&format!("{api_base}/{endpoint}"), document.as_bytes());
    }

    /// The testing channel's endpoint answers with a *list*, newest first. Each
    /// entry is `(version, draft, has_manifest)`, so a test can put a draft or an
    /// asset-less tag in front of the release that should actually be picked.
    pub fn serve_release_list(
        &self,
        api_base: &str,
        endpoint: &str,
        entries: &[(&str, bool, bool)],
    ) {
        let documents: Vec<String> = entries
            .iter()
            .map(|(version, draft, has_manifest)| {
                let assets = if *has_manifest {
                    format!(
                        r#"{{ "name": "manifest.json", "browser_download_url": "https://assets.test/manifest.json" }},
                           {{ "name": "manifest.json.minisig", "browser_download_url": "https://assets.test/manifest.json.minisig" }},
                           {{ "name": "musicbee_remote_{version}.zip", "browser_download_url": "https://assets.test/musicbee_remote_{version}.zip" }}"#
                    )
                } else {
                    String::new()
                };
                format!(
                    r#"{{ "tag_name": "v{version}", "draft": {draft}, "assets": [{assets}] }}"#
                )
            })
            .collect();
        self.serve(
            &format!("{api_base}/{endpoint}"),
            format!("[{}]", documents.join(",")).as_bytes(),
        );
    }
}

impl HttpClient for StubHttp {
    fn get(&self, url: &str, etag: Option<&str>) -> Result<HttpResponse> {
        self.requests.borrow_mut().push(url.to_owned());

        if etag.is_some() && *self.not_modified.borrow() {
            return Ok(HttpResponse {
                status: 304,
                etag: None,
                body: Vec::new(),
            });
        }

        match self.routes.borrow().get(url) {
            Some(body) => Ok(HttpResponse {
                status: 200,
                etag: Some(Self::ETAG.to_owned()),
                body: body.clone(),
            }),
            None => Ok(HttpResponse {
                status: 404,
                etag: None,
                body: Vec::new(),
            }),
        }
    }
}

/// A client that fails at the transport layer, the way a machine with no route
/// out does.
pub struct OfflineHttp;

impl HttpClient for OfflineHttp {
    fn get(&self, _url: &str, _etag: Option<&str>) -> Result<HttpResponse> {
        Err(UpdateError::Network("the network is unreachable".into()))
    }
}
