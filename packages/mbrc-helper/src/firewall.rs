//! Windows Firewall rule management for the listening port.
//!
//! Ported from the retired C# `firewall-utility`, with two deliberate
//! departures from it.
//!
//! [`RULE_NAME`] is *not* one of them: it is byte-identical to the name the C#
//! utility used, because changing it would leave every existing user with a
//! stale rule plus a duplicate.
//!
//! **The rule is written even when the firewall is disabled.** The C# returned
//! early in that case. That made sense when nothing had been spent to get there,
//! but the plugin launches this helper with `runas`, so by the time the check
//! runs the user has already been shown a UAC prompt and approved it - and then
//! got nothing for it. Worse, a rule that was never written is missing the day
//! the firewall is turned back on, and the failure looks like a plugin bug. A
//! rule added while the firewall is off is inert, persists, and applies the
//! moment it is enabled, so writing it unconditionally costs nothing.
//!
//! **The enabled state is read per active profile**, where the C# read
//! `INetFwMgr.LocalPolicy.CurrentProfile.FirewallEnabled`, the XP-era API that
//! flattens the multi-profile case. Since the answer no longer gates anything it
//! is purely diagnostic, and a failure to read it must not stop the rule being
//! written - hence [`Report::firewall_active`] being an `Option`.

use std::fmt;

/// Name of the inbound rule. Must stay byte-identical to the name the C#
/// `firewall-utility` wrote, or upgrades leave a stale duplicate behind.
pub const RULE_NAME: &str = "MusicBee Remote: Listening Port";

/// What [`ensure_rule`] did to the rule itself.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Outcome {
    /// No rule with this name existed, so one was added.
    Created,
    /// A rule with this name existed and its port list was rewritten.
    Updated,
}

impl fmt::Display for Outcome {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Outcome::Created => f.write_str("rule created"),
            Outcome::Updated => f.write_str("rule already present; port updated"),
        }
    }
}

/// The result of [`ensure_rule`]: what happened to the rule, plus the firewall
/// state observed on the way through.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Report {
    pub outcome: Outcome,
    /// Whether the firewall was on for any active profile. `None` means the
    /// query failed, which is reported but never blocks the rule being written.
    pub firewall_active: Option<bool>,
}

impl fmt::Display for Report {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.outcome)?;
        match self.firewall_active {
            Some(true) => Ok(()),
            Some(false) => {
                f.write_str(" (the firewall is currently off; the rule applies when it is enabled)")
            }
            None => f.write_str(" (could not read the firewall's enabled state)"),
        }
    }
}

/// Failure modes worth distinguishing to the caller. `AccessDenied` is separate
/// because it is the one the user can act on: re-run elevated.
#[derive(Debug)]
pub enum Error {
    /// The process is not elevated, or is otherwise refused by the firewall.
    AccessDenied,
    /// Any other COM failure, carrying the underlying description.
    Com(String),
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Error::AccessDenied => {
                f.write_str("access denied; the firewall rule requires administrative rights")
            }
            Error::Com(msg) => write!(f, "firewall COM call failed: {msg}"),
        }
    }
}

impl std::error::Error for Error {}

pub type Result<T> = std::result::Result<T, Error>;

/// The slice of the firewall API this port needs, behind a trait so the
/// create-or-update decision can be tested without a live COM apartment. The
/// real implementation is [`com::ComPolicy`]; the tests use a fake.
pub trait FirewallPolicy {
    /// An opaque handle to an existing rule, as yielded by [`Self::rules`].
    type Rule;

    /// Whether the firewall is on for any currently active profile.
    fn is_enabled(&self) -> Result<bool>;

    /// Every rule, paired with its name.
    ///
    /// The C# used `Rules.OfType<INetFwRule>().FirstOrDefault(...)`; over COM
    /// this is an `IEnumVARIANT` walk, so the collection is materialised here
    /// rather than searched lazily. Rule counts are in the hundreds, so the
    /// difference does not matter.
    fn rules(&self) -> Result<Vec<(String, Self::Rule)>>;

    /// Rewrite an existing rule's local port list.
    fn set_local_ports(&self, rule: &Self::Rule, ports: &str) -> Result<()>;

    /// Add a new inbound allow rule for `ports`.
    fn add_rule(&self, name: &str, ports: &str) -> Result<()>;
}

/// Create the rule, or point the existing one at `port`.
///
/// Runs regardless of whether the firewall is enabled; see the module docs for
/// why. Idempotent by design: running it twice with the same port is a no-op the
/// second time apart from rewriting an identical port list.
pub fn ensure_rule<P: FirewallPolicy>(policy: &P, name: &str, port: u16) -> Result<Report> {
    // Diagnostic only, and explicitly not a gate: a firewall whose state cannot
    // be read is still a firewall that needs the rule.
    let firewall_active = policy.is_enabled().ok();

    let ports = port.to_string();
    // Match on the exact name, as the C# `x.Name == ruleName` did. Firewall
    // rule names are case-preserving and users can create their own, so this
    // stays an ordinal comparison rather than a case-insensitive one.
    let existing = policy.rules()?.into_iter().find(|(n, _)| n == name);

    let outcome = match existing {
        Some((_, rule)) => {
            policy.set_local_ports(&rule, &ports)?;
            Outcome::Updated
        }
        None => {
            policy.add_rule(name, &ports)?;
            Outcome::Created
        }
    };

    Ok(Report {
        outcome,
        firewall_active,
    })
}

#[cfg(windows)]
pub mod com;

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;

    /// Records what `ensure_rule` asked for, so the tests can assert on the
    /// decision rather than on firewall state.
    #[derive(Default)]
    struct FakePolicy {
        enabled: bool,
        rules: Vec<String>,
        added: RefCell<Vec<(String, String)>>,
        updated: RefCell<Vec<(String, String)>>,
        fail_enumeration: bool,
        fail_enabled_query: bool,
    }

    impl FirewallPolicy for FakePolicy {
        type Rule = String;

        fn is_enabled(&self) -> Result<bool> {
            if self.fail_enabled_query {
                return Err(Error::Com("enabled query failed".into()));
            }
            Ok(self.enabled)
        }

        fn rules(&self) -> Result<Vec<(String, Self::Rule)>> {
            if self.fail_enumeration {
                return Err(Error::Com("enumeration failed".into()));
            }
            Ok(self.rules.iter().cloned().map(|n| (n.clone(), n)).collect())
        }

        fn set_local_ports(&self, rule: &Self::Rule, ports: &str) -> Result<()> {
            self.updated
                .borrow_mut()
                .push((rule.clone(), ports.to_string()));
            Ok(())
        }

        fn add_rule(&self, name: &str, ports: &str) -> Result<()> {
            self.added
                .borrow_mut()
                .push((name.to_string(), ports.to_string()));
            Ok(())
        }
    }

    #[test]
    fn writes_the_rule_even_when_the_firewall_is_disabled() {
        // The old C# returned early here. It must not: the UAC prompt has
        // already been paid for by the time this runs, and a rule that was never
        // written is missing the day the firewall is switched back on.
        let policy = FakePolicy {
            enabled: false,
            ..Default::default()
        };

        let report = ensure_rule(&policy, RULE_NAME, 3000).unwrap();
        assert_eq!(report.outcome, Outcome::Created);
        assert_eq!(report.firewall_active, Some(false));
        assert_eq!(
            policy.added.borrow().as_slice(),
            &[(RULE_NAME.to_string(), "3000".to_string())]
        );
    }

    #[test]
    fn a_failed_enabled_query_does_not_block_the_write() {
        // The state is diagnostic, so failing to read it must not cost the user
        // their rule.
        let policy = FakePolicy {
            fail_enabled_query: true,
            ..Default::default()
        };

        let report = ensure_rule(&policy, RULE_NAME, 3000).unwrap();
        assert_eq!(report.outcome, Outcome::Created);
        assert_eq!(report.firewall_active, None);
        assert_eq!(policy.added.borrow().len(), 1);
    }

    #[test]
    fn creates_the_rule_when_absent() {
        let policy = FakePolicy {
            enabled: true,
            rules: vec!["Some Other Rule".into(), "MusicBee".into()],
            ..Default::default()
        };

        let report = ensure_rule(&policy, RULE_NAME, 3000).unwrap();
        assert_eq!(report.outcome, Outcome::Created);
        assert_eq!(report.firewall_active, Some(true));
        assert_eq!(
            policy.added.borrow().as_slice(),
            &[(RULE_NAME.to_string(), "3000".to_string())]
        );
        assert!(policy.updated.borrow().is_empty());
    }

    #[test]
    fn updates_the_port_when_the_rule_exists() {
        let policy = FakePolicy {
            enabled: true,
            rules: vec!["Some Other Rule".into(), RULE_NAME.to_string()],
            ..Default::default()
        };

        let report = ensure_rule(&policy, RULE_NAME, 3001).unwrap();
        assert_eq!(report.outcome, Outcome::Updated);
        assert!(policy.added.borrow().is_empty());
        assert_eq!(
            policy.updated.borrow().as_slice(),
            &[(RULE_NAME.to_string(), "3001".to_string())]
        );
    }

    #[test]
    fn rule_lookup_is_an_exact_name_match() {
        // Names that merely contain or case-fold to the rule name must not be
        // mistaken for it, or the helper would rewrite a user's own rule.
        let policy = FakePolicy {
            enabled: true,
            rules: vec![
                "musicbee remote: listening port".into(),
                "MusicBee Remote: Listening Port (old)".into(),
                " MusicBee Remote: Listening Port".into(),
            ],
            ..Default::default()
        };

        assert_eq!(
            ensure_rule(&policy, RULE_NAME, 3000).unwrap().outcome,
            Outcome::Created
        );
    }

    #[test]
    fn is_idempotent_across_runs() {
        let policy = FakePolicy {
            enabled: true,
            rules: vec![RULE_NAME.to_string()],
            ..Default::default()
        };

        for _ in 0..3 {
            assert_eq!(
                ensure_rule(&policy, RULE_NAME, 3000).unwrap().outcome,
                Outcome::Updated
            );
        }
        assert!(policy.added.borrow().is_empty());
        assert_eq!(policy.updated.borrow().len(), 3);
    }

    #[test]
    fn enumeration_failure_propagates() {
        let policy = FakePolicy {
            enabled: true,
            fail_enumeration: true,
            ..Default::default()
        };

        assert!(matches!(
            ensure_rule(&policy, RULE_NAME, 3000),
            Err(Error::Com(_))
        ));
    }
}
