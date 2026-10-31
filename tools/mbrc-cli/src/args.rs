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

/// The `--host` and `--port` the plugin listens on, defaulting to the local one.
///
/// `None` once it has told the user `--port` is not a usable port.
pub fn endpoint(args: &[String]) -> Option<(String, u16)> {
    let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
    match flag_value(args, "--port")
        .as_deref()
        .unwrap_or("3000")
        .parse()
    {
        Ok(port) if port > 0 => Some((host, port)),
        _ => {
            eprintln!("--port must be a number from 1 to {}", u16::MAX);
            None
        }
    }
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
    fn endpoint_defaults_to_the_local_plugin() {
        assert_eq!(endpoint(&[]), Some(("127.0.0.1".to_string(), 3000)));
        let a = v(&["--host", "10.0.0.1", "--port", "3001"]);
        assert_eq!(endpoint(&a), Some(("10.0.0.1".to_string(), 3001)));
        assert_eq!(endpoint(&v(&["--port", "x"])), None);
        assert_eq!(endpoint(&v(&["--port", "0"])), None);
        assert_eq!(endpoint(&v(&["--port", "70000"])), None);
    }

    #[test]
    fn detects_bool_flags() {
        let a = v(&["--no-broadcast"]);
        assert!(has_flag(&a, "--no-broadcast"));
        assert!(!has_flag(&a, "--verbose"));
    }
}
