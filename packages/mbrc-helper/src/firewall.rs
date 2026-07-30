//! Windows Firewall rule management for the listening port.
//!
//! Ported from the retired C# `firewall-utility`. Two things are load-bearing
//! for compatibility with installs that already ran the old utility:
//!
//! * [`RULE_NAME`] is byte-identical to the name the C# utility used. Changing
//!   it would leave every existing user with a stale rule plus a duplicate.
//! * A disabled firewall is a silent no-op, not an error. The old utility
//!   returned early in that case and callers depend on it being non-fatal.
//!
//! One deliberate behaviour change: the C# read
//! `INetFwMgr.LocalPolicy.CurrentProfile.FirewallEnabled`, the XP-era API that
//! flattens the multi-profile case. This uses
//! `INetFwPolicy2::get_FirewallEnabled(CurrentProfileTypes())`, which is correct
//! when domain, private and public profiles are active simultaneously.

use std::fmt;

/// Name of the inbound rule. Must stay byte-identical to the name the C#
/// `firewall-utility` wrote, or upgrades leave a stale duplicate behind.
pub const RULE_NAME: &str = "MusicBee Remote: Listening Port";

/// What [`ensure_rule`] actually did, so the caller can report it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Outcome {
    /// The firewall is off for the current profiles; nothing to do.
    FirewallDisabled,
    /// No rule with this name existed, so one was added.
    Created,
    /// A rule with this name existed and its port list was rewritten.
    Updated,
}

impl fmt::Display for Outcome {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Outcome::FirewallDisabled => f.write_str("firewall is disabled; no rule needed"),
            Outcome::Created => f.write_str("rule created"),
            Outcome::Updated => f.write_str("rule already present; port updated"),
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
/// Idempotent by design: running it twice with the same port is a no-op the
/// second time apart from rewriting an identical port list.
pub fn ensure_rule<P: FirewallPolicy>(policy: &P, name: &str, port: u16) -> Result<Outcome> {
    if !policy.is_enabled()? {
        return Ok(Outcome::FirewallDisabled);
    }

    let ports = port.to_string();
    // Match on the exact name, as the C# `x.Name == ruleName` did. Firewall
    // rule names are case-preserving and users can create their own, so this
    // stays an ordinal comparison rather than a case-insensitive one.
    let existing = policy.rules()?.into_iter().find(|(n, _)| n == name);

    match existing {
        Some((_, rule)) => {
            policy.set_local_ports(&rule, &ports)?;
            Ok(Outcome::Updated)
        }
        None => {
            policy.add_rule(name, &ports)?;
            Ok(Outcome::Created)
        }
    }
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
    }

    impl FirewallPolicy for FakePolicy {
        type Rule = String;

        fn is_enabled(&self) -> Result<bool> {
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
    fn disabled_firewall_is_a_silent_no_op() {
        let policy = FakePolicy {
            enabled: false,
            // A rule is present, to prove the disabled check short-circuits
            // before enumeration rather than after it.
            rules: vec![RULE_NAME.to_string()],
            ..Default::default()
        };

        assert_eq!(
            ensure_rule(&policy, RULE_NAME, 3000).unwrap(),
            Outcome::FirewallDisabled
        );
        assert!(policy.added.borrow().is_empty());
        assert!(policy.updated.borrow().is_empty());
    }

    #[test]
    fn creates_the_rule_when_absent() {
        let policy = FakePolicy {
            enabled: true,
            rules: vec!["Some Other Rule".into(), "MusicBee".into()],
            ..Default::default()
        };

        assert_eq!(
            ensure_rule(&policy, RULE_NAME, 3000).unwrap(),
            Outcome::Created
        );
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

        assert_eq!(
            ensure_rule(&policy, RULE_NAME, 3001).unwrap(),
            Outcome::Updated
        );
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
            ensure_rule(&policy, RULE_NAME, 3000).unwrap(),
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
                ensure_rule(&policy, RULE_NAME, 3000).unwrap(),
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
