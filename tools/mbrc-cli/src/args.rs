//! Tiny flag parsing for the CLI - deliberately dependency-free (no clap) so the
//! shared-crate workspace builds with only already-cached registry crates.

/// Value of `--flag value`, or `None` if the flag is absent or has no value.
pub fn flag_value(args: &[String], flag: &str) -> Option<String> {
    let pos = args.iter().position(|a| a == flag)?;
    args.get(pos + 1).cloned()
}

/// Whether a boolean `--flag` is present.
pub fn has_flag(args: &[String], flag: &str) -> bool {
    args.iter().any(|a| a == flag)
}

/// A per-run V6 `client_id` for a dev client that does not persist its token.
///
/// The server issues a token to the first handshake for an id and refuses every
/// later one that arrives without it, so a fixed id would work once and be
/// locked out after. Each run really is a new installation, so it says so.
pub fn run_client_id(tag: &str) -> String {
    let nanos = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or_default();
    format!("{tag}-{}-{nanos}", std::process::id())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn v(items: &[&str]) -> Vec<String> {
        items.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn reads_flag_value() {
        let a = v(&["--host", "10.0.0.1", "--port", "3000"]);
        assert_eq!(flag_value(&a, "--host").as_deref(), Some("10.0.0.1"));
        assert_eq!(flag_value(&a, "--port").as_deref(), Some("3000"));
        assert_eq!(flag_value(&a, "--missing"), None);
    }

    #[test]
    fn flag_without_value_is_none() {
        let a = v(&["--host"]);
        assert_eq!(flag_value(&a, "--host"), None);
    }

    #[test]
    fn detects_bool_flags() {
        let a = v(&["--no-broadcast"]);
        assert!(has_flag(&a, "--no-broadcast"));
        assert!(!has_flag(&a, "--verbose"));
    }
}
