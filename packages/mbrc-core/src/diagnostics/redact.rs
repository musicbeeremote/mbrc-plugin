//! The diagnostics bundle's redaction policy.
//!
//! Deliberately separate from [`crate::logging::redact_frame`]. That one is
//! built for wire frames: it elides base64 blobs (cover/image/art/lyrics) so the
//! log stays readable, and does nothing about credentials because a frame has
//! none. A bundle is different - it carries the user's whole settings file, and
//! they are about to attach it to a public issue.
//!
//! The policy is narrow on purpose: **strip secrets, keep the rest readable.**
//! Credentials in `proxy_override` are masked and `allowed_addresses` is
//! dropped; file paths, LAN addresses, hostname and port stay exactly as they
//! are. Redacting those too would be safer to paste and would also make the
//! bug classes that most need this (library paths, cover scanning, who is
//! allowed to connect) undiagnosable from the bundle alone.

use serde_json::{Map, Value};

/// What replaces a masked credential. Keeping the shape visible tells the
/// maintainer a proxy with auth is configured, which is itself a clue.
const MASK: &str = "<redacted>";

/// Settings keys dropped from the bundle entirely.
const DROPPED_KEYS: &[&str] = &["allowed_addresses"];

/// Applies the bundle policy to a serialized [`crate::config::Config`].
///
/// Takes and returns JSON rather than a `Config` so the policy lives in one
/// place regardless of who is serializing: today that is `report.json`'s
/// `settings` block, the bundle's only copy of the user's settings.
pub fn settings(mut value: Value) -> Value {
    let Some(object) = value.as_object_mut() else {
        return value;
    };
    for key in DROPPED_KEYS {
        object.remove(*key);
    }
    if let Some(Value::String(proxy)) = object.get("proxy_override") {
        let masked = mask_url_credentials(proxy);
        object.insert("proxy_override".to_owned(), Value::String(masked));
    }
    note_dropped(object);
    value
}

/// Records what the policy removed, so a reader of the bundle is never left
/// wondering whether a missing key means "unset" or "withheld".
fn note_dropped(object: &mut Map<String, Value>) {
    let dropped: Vec<Value> = DROPPED_KEYS
        .iter()
        .map(|k| Value::String((*k).to_owned()))
        .collect();
    object.insert("redacted_keys".to_owned(), Value::Array(dropped));
}

/// Mask the userinfo in a `scheme://user:pass@host:port` URL, leaving the rest
/// intact. Anything without userinfo is returned untouched, so the common case
/// (`http://proxy.corp:8080`) stays fully readable.
///
/// Deliberately string-level rather than URL-parsed: a value the user typed by
/// hand may not parse, and a proxy setting that fails to parse must still not
/// leak its password.
fn mask_url_credentials(url: &str) -> String {
    // Userinfo ends at the last '@' before the first '/' of the path, so a path
    // containing '@' cannot pull the split past the authority.
    let authority_end = url
        .find("://")
        .map(|i| i + 3)
        .and_then(|start| url[start..].find('/').map(|i| start + i))
        .unwrap_or(url.len());
    let Some(at) = url[..authority_end].rfind('@') else {
        return url.to_owned();
    };
    let start = url.find("://").map(|i| i + 3).unwrap_or(0);
    if at < start {
        return url.to_owned();
    }
    format!("{}{MASK}{}", &url[..start], &url[at..])
}

#[cfg(test)]
mod tests {
    use super::*;

    fn settings_json() -> Value {
        serde_json::json!({
            "port": 3000,
            "allowed_addresses": ["192.168.1.50", "192.168.1.51"],
            "proxy_override": "http://alice:hunter2@proxy.corp:8080",
            "storage_path": r"C:\Users\someone\AppData\MusicBee\mb_remote",
        })
    }

    #[test]
    fn drops_allowed_addresses_and_says_so() {
        let out = settings(settings_json());
        let object = out.as_object().expect("settings stay an object");
        assert!(!object.contains_key("allowed_addresses"));
        assert_eq!(
            object["redacted_keys"],
            serde_json::json!(["allowed_addresses"])
        );
    }

    #[test]
    fn masks_proxy_credentials_but_keeps_the_host() {
        let out = settings(settings_json());
        let proxy = out["proxy_override"]
            .as_str()
            .expect("proxy stays a string");
        assert!(!proxy.contains("hunter2"), "password survived: {proxy}");
        assert!(!proxy.contains("alice"), "username survived: {proxy}");
        assert_eq!(proxy, "http://<redacted>@proxy.corp:8080");
    }

    #[test]
    fn keeps_paths_and_port_readable() {
        // The whole point of this policy: a library-path bug must stay
        // diagnosable from the bundle.
        let out = settings(settings_json());
        assert_eq!(out["port"], 3000);
        assert!(
            out["storage_path"]
                .as_str()
                .expect("path stays a string")
                .contains("mb_remote")
        );
    }

    #[test]
    fn leaves_a_credential_free_proxy_alone() {
        assert_eq!(
            mask_url_credentials("http://proxy.corp:8080"),
            "http://proxy.corp:8080"
        );
        assert_eq!(mask_url_credentials(""), "");
    }

    #[test]
    fn an_at_sign_in_the_path_does_not_move_the_split() {
        // rfind('@') over the whole string would mask the authority away here.
        assert_eq!(
            mask_url_credentials("http://proxy.corp:8080/pac@v2.dat"),
            "http://proxy.corp:8080/pac@v2.dat"
        );
    }

    #[test]
    fn a_non_object_payload_is_returned_unchanged() {
        assert_eq!(settings(Value::Null), Value::Null);
    }
}
