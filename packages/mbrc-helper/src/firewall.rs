//! Windows Firewall rule management for the listening port.
//!
//! Off Windows nothing here is reachable from `main`, so every item reads as
//! dead code and the `mod` declaration carries an `allow`. It still compiles and
//! its unit tests still run on Linux, which is the point: the create-or-update
//! logic is platform-independent.
//!
//! Ported from the retired C# `firewall-utility` with two deliberate departures.
//! [`RULE_NAME`] is not one of them: it stays byte-identical, or every existing
//! user gets a stale rule plus a duplicate.
//!
//! **The rule is written even when the firewall is disabled**, where the C#
//! returned early. The user has already approved a UAC prompt by then, and a
//! rule that was never written goes missing the day the firewall comes back on.
//! An inert rule costs nothing.
//!
//! **The enabled state is read per active profile**, not through the XP-era
//! `INetFwMgr.LocalPolicy.CurrentProfile` that flattens multiple profiles. It
//! gates nothing any more, so a failure to read it must not stop the rule being
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
    /// A rule with this name existed; it was restored to an inbound allow rule and pointed at the port.
    Updated,
}

impl fmt::Display for Outcome {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Outcome::Created => f.write_str("rule created"),
            Outcome::Updated => f.write_str("rule already present; restored and port updated"),
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

/// `NET_FW_PROFILE2_PUBLIC`, kept here so the check stays testable off Windows.
pub const PUBLIC_PROFILE: i32 = 0x4;

/// What [`FirewallPolicy::rule_state`] reads off one rule.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RuleState {
    pub enabled: bool,
    /// The rule allows traffic, and inbound traffic: what our rule is for.
    pub allows_inbound: bool,
    /// The rule's `LocalPorts`: `*`, or a comma list of ports and `a-b` ranges.
    pub local_ports: String,
    /// The `NET_FW_PROFILE2_*` profiles the rule applies to.
    pub profiles: i32,
}

/// The slice of the firewall API this port needs, behind a trait so the
/// create-or-update decision can be tested without a live COM apartment. The
/// real implementation is `com::ComPolicy`; the tests use a fake.
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

    /// Rewrites an existing rule's local port list.
    fn set_local_ports(&self, rule: &Self::Rule, ports: &str) -> Result<()>;

    /// Makes an existing rule what [`FirewallPolicy::add_rule`] creates: on,
    /// allowing inbound traffic, for every profile.
    fn restore_rule(&self, rule: &Self::Rule) -> Result<()>;

    /// Adds a new inbound allow rule for `ports`.
    fn add_rule(&self, name: &str, ports: &str) -> Result<()>;

    /// The profiles active now, as a `NET_FW_PROFILE2_*` bit mask.
    fn active_profiles(&self) -> Result<i32>;

    /// What `rule` does: on or off, allow or block, its ports and profiles.
    fn rule_state(&self, rule: &Self::Rule) -> Result<RuleState>;
}

/// Where our rule stands for the port, as the setup wizard's firewall step reports it.
///
/// Declared worst to best, so of several rules with our name the best one wins.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, serde::Serialize)]
#[serde(rename_all = "snake_case")]
pub enum RuleStatus {
    Missing,
    /// Every rule with our name is turned off.
    Disabled,
    /// It blocks, or applies to outgoing traffic, instead of letting phones in.
    Blocking,
    /// It lets traffic in, but not on the port.
    OtherPort,
    /// It opens the port, but not on the network profile that is active now.
    OtherProfile,
    Open,
}

/// On, off, or could not be read.
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "snake_case")]
pub enum Tristate {
    On,
    Off,
    Unknown,
}

impl From<Option<bool>> for Tristate {
    fn from(value: Option<bool>) -> Self {
        match value {
            Some(true) => Tristate::On,
            Some(false) => Tristate::Off,
            None => Tristate::Unknown,
        }
    }
}

/// What [`check_rule`] found, printed by `firewall --check` as one JSON object.
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize)]
pub struct Check {
    pub rule: RuleStatus,
    /// Whether the firewall is on for any active profile.
    pub firewall: Tristate,
    /// Whether the Public profile is among the active ones.
    pub public_network: Tristate,
}

/// Reads whether our rule lets `port` in, without changing anything.
///
/// Read-only, so it runs without elevation. Only rules named `name` count, and
/// one whose state cannot be read is skipped, as [`FirewallPolicy::rules`]
/// skips unreadable names.
///
/// # Errors
/// The rule collection could not be read.
pub fn check_rule<P: FirewallPolicy>(policy: &P, name: &str, port: u16) -> Result<Check> {
    // No active profile at all is no answer: a running firewall always has one.
    let active = policy.active_profiles().ok().filter(|mask| *mask != 0);
    let mut rule = RuleStatus::Missing;
    for (rule_name, handle) in policy.rules()? {
        if rule_name != name {
            continue;
        }
        if let Ok(state) = policy.rule_state(&handle) {
            rule = rule.max(status_of(&state, port, active));
        }
    }

    Ok(Check {
        rule,
        firewall: policy.is_enabled().ok().into(),
        public_network: active.map(|mask| mask & PUBLIC_PROFILE != 0).into(),
    })
}

/// What one rule does for `port` on the `active` profiles; unreadable profiles do not count against it.
fn status_of(state: &RuleState, port: u16, active: Option<i32>) -> RuleStatus {
    if !state.enabled {
        RuleStatus::Disabled
    } else if !state.allows_inbound {
        RuleStatus::Blocking
    } else if !ports_cover(&state.local_ports, port) {
        RuleStatus::OtherPort
    } else if active.is_some_and(|mask| state.profiles & mask == 0) {
        RuleStatus::OtherProfile
    } else {
        RuleStatus::Open
    }
}

/// Whether a `LocalPorts` value opens `port`. Keywords such as `RPC` open nothing here.
pub fn ports_cover(local_ports: &str, port: u16) -> bool {
    local_ports.split(',').map(str::trim).any(|part| {
        if part == "*" {
            return true;
        }
        match part.split_once('-') {
            Some((low, high)) => match (low.trim().parse::<u16>(), high.trim().parse::<u16>()) {
                (Ok(low), Ok(high)) => (low..=high).contains(&port),
                _ => false,
            },
            None => part.parse::<u16>() == Ok(port),
        }
    })
}

/// Creates the rule, or restores the existing one and points it at `port`.
///
/// Runs regardless of whether the firewall is enabled; see the module docs for
/// why. Idempotent by design: running it twice with the same port is a no-op the
/// second time apart from rewriting an identical port list.
///
/// # Errors
/// COM could not bind the firewall policy, or the rule could not be created
/// or updated.
pub fn ensure_rule<P: FirewallPolicy>(policy: &P, name: &str, port: u16) -> Result<Report> {
    // Diagnostic only, and explicitly not a gate: a firewall whose state cannot
    // be read is still a firewall that needs the rule.
    let firewall_active = policy.is_enabled().ok();

    let ports = port.to_string();
    // Ordinal, as the C# `x.Name == ruleName` was: rule names are
    // case-preserving and users can create their own.
    let existing = policy.rules()?.into_iter().find(|(n, _)| n == name);

    let outcome = match existing {
        Some((_, rule)) => {
            policy.restore_rule(&rule)?;
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
        /// Per rule name, overriding the default of enabled on `3000`.
        states: Vec<(String, RuleState)>,
        profiles: i32,
        fail_profiles: bool,
        added: RefCell<Vec<(String, String)>>,
        updated: RefCell<Vec<(String, String)>>,
        restored: RefCell<Vec<String>>,
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

        fn restore_rule(&self, rule: &Self::Rule) -> Result<()> {
            self.restored.borrow_mut().push(rule.clone());
            Ok(())
        }

        fn add_rule(&self, name: &str, ports: &str) -> Result<()> {
            self.added
                .borrow_mut()
                .push((name.to_string(), ports.to_string()));
            Ok(())
        }

        fn active_profiles(&self) -> Result<i32> {
            if self.fail_profiles {
                return Err(Error::Com("profile query failed".into()));
            }
            Ok(self.profiles)
        }

        fn rule_state(&self, rule: &Self::Rule) -> Result<RuleState> {
            Ok(self
                .states
                .iter()
                .find(|(name, _)| name == rule)
                .map(|(_, state)| state.clone())
                .unwrap_or_else(|| state(true, "3000")))
        }
    }

    const ALL_PROFILES: i32 = 0x7fff_ffff;
    const PRIVATE_PROFILE: i32 = 0x2;

    fn state(enabled: bool, local_ports: &str) -> RuleState {
        RuleState {
            enabled,
            allows_inbound: true,
            local_ports: local_ports.into(),
            profiles: ALL_PROFILES,
        }
    }

    fn check(policy: &FakePolicy) -> Check {
        check_rule(policy, RULE_NAME, 3000).unwrap()
    }

    #[test]
    fn check_finds_an_enabled_rule_on_the_port_open() {
        let policy = FakePolicy {
            enabled: true,
            rules: vec!["Other".into(), RULE_NAME.into()],
            ..Default::default()
        };

        assert_eq!(check(&policy).rule, RuleStatus::Open);
        assert_eq!(check(&policy).firewall, Tristate::On);
    }

    #[test]
    fn check_reports_a_missing_rule() {
        let policy = FakePolicy {
            rules: vec!["Other".into()],
            ..Default::default()
        };

        assert_eq!(check(&policy).rule, RuleStatus::Missing);
    }

    #[test]
    fn a_disabled_rule_is_reported_as_disabled() {
        let policy = FakePolicy {
            rules: vec![RULE_NAME.into()],
            states: vec![(RULE_NAME.into(), state(false, "3000"))],
            ..Default::default()
        };

        assert_eq!(check(&policy).rule, RuleStatus::Disabled);
    }

    #[test]
    fn a_rule_on_another_port_is_reported_as_such() {
        let policy = FakePolicy {
            rules: vec![RULE_NAME.into()],
            states: vec![(RULE_NAME.into(), state(true, "3001"))],
            ..Default::default()
        };

        assert_eq!(check(&policy).rule, RuleStatus::OtherPort);
    }

    #[test]
    fn of_two_rules_with_our_name_the_open_one_wins() {
        let policy = FakePolicy {
            rules: vec![RULE_NAME.into(), RULE_NAME.into()],
            states: vec![(RULE_NAME.into(), state(true, "3000"))],
            ..Default::default()
        };

        assert_eq!(check(&policy).rule, RuleStatus::Open);
    }

    #[test]
    fn check_never_writes() {
        let policy = FakePolicy {
            rules: vec!["Other".into()],
            ..Default::default()
        };

        check(&policy);
        assert!(policy.added.borrow().is_empty());
        assert!(policy.updated.borrow().is_empty());
    }

    #[test]
    fn the_public_network_is_read_from_the_active_profiles() {
        let public = FakePolicy {
            profiles: 0x2 | PUBLIC_PROFILE,
            ..Default::default()
        };
        let private = FakePolicy {
            profiles: 0x2,
            ..Default::default()
        };
        let unreadable = FakePolicy {
            fail_profiles: true,
            ..Default::default()
        };

        assert_eq!(check(&public).public_network, Tristate::On);
        assert_eq!(check(&private).public_network, Tristate::Off);
        assert_eq!(check(&unreadable).public_network, Tristate::Unknown);
    }

    /// The core parses this same file into its `FirewallStatus`, so a rename on either side fails a test.
    const CORE_CONTRACT: &str = include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../tests/golden/firewall-check.json"
    ));

    #[test]
    fn check_serializes_as_the_core_reads_it() {
        let report = Check {
            rule: RuleStatus::OtherProfile,
            firewall: Tristate::Unknown,
            public_network: Tristate::On,
        };

        assert_eq!(
            serde_json::to_string(&report).unwrap(),
            CORE_CONTRACT.trim()
        );
    }

    #[test]
    fn ports_cover_reads_lists_ranges_and_any() {
        assert!(ports_cover("3000", 3000));
        assert!(ports_cover("80, 3000 ,8080", 3000));
        assert!(ports_cover("2990-3010", 3000));
        assert!(ports_cover("*", 3000));
        assert!(!ports_cover("3001", 3000));
        assert!(!ports_cover("3001-3010", 3000));
        assert!(!ports_cover("RPC", 3000));
        assert!(!ports_cover("", 3000));
    }

    #[test]
    fn writes_the_rule_even_when_the_firewall_is_disabled() {
        // The UAC prompt is already paid for by the time this runs, and a rule
        // never written goes missing the day the firewall is switched back on.
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
    fn an_existing_rule_is_restored() {
        let policy = FakePolicy {
            rules: vec![RULE_NAME.to_string()],
            states: vec![(RULE_NAME.into(), state(false, "3000"))],
            ..Default::default()
        };

        ensure_rule(&policy, RULE_NAME, 3000).unwrap();
        assert_eq!(
            policy.restored.borrow().as_slice(),
            &[RULE_NAME.to_string()]
        );
    }

    #[test]
    fn a_rule_that_blocks_or_faces_outward_is_not_open() {
        let blocking = RuleState {
            allows_inbound: false,
            ..state(true, "3000")
        };

        assert_eq!(status_of(&blocking, 3000, None), RuleStatus::Blocking);
    }

    #[test]
    fn a_rule_for_another_profile_is_not_open_on_this_one() {
        let private_only = RuleState {
            profiles: PRIVATE_PROFILE,
            ..state(true, "3000")
        };

        assert_eq!(
            status_of(&private_only, 3000, Some(PUBLIC_PROFILE)),
            RuleStatus::OtherProfile
        );
        assert_eq!(
            status_of(&private_only, 3000, Some(PRIVATE_PROFILE)),
            RuleStatus::Open
        );
        assert_eq!(
            status_of(&private_only, 3000, None),
            RuleStatus::Open,
            "unreadable profiles do not count"
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
