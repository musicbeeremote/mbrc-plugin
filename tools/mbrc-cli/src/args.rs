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
