//! Live COM implementation of [`FirewallPolicy`] over `INetFwPolicy2`.
//!
//! This is the half that cannot be exercised without a real COM apartment, so
//! it is kept as thin as possible: every decision lives in
//! [`super::ensure_rule`], which is tested against a fake.

use super::{Error, FirewallPolicy, Result};

use windows::Win32::Foundation::{E_ACCESSDENIED, VARIANT_TRUE};
use windows::Win32::NetworkManagement::WindowsFirewall::{
    INetFwPolicy2, INetFwRule, NET_FW_ACTION_ALLOW, NET_FW_IP_PROTOCOL_TCP, NET_FW_PROFILE_TYPE2,
    NET_FW_PROFILE2_DOMAIN, NET_FW_PROFILE2_PRIVATE, NET_FW_PROFILE2_PUBLIC, NET_FW_RULE_DIR_IN,
    NetFwPolicy2, NetFwRule,
};
use windows::Win32::System::Com::{
    CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx, IDispatch,
};
use windows::Win32::System::Ole::IEnumVARIANT;
use windows::Win32::System::Variant::VARIANT;
use windows::core::{BSTR, Interface};

/// Maps a COM failure onto the two cases the caller distinguishes.
fn map_err(e: windows::core::Error) -> Error {
    // The firewall service returns E_ACCESSDENIED for an unelevated caller, and
    // the Win32-wrapped form of ERROR_ACCESS_DENIED in some configurations.
    const ACCESS_DENIED_WIN32: windows::core::HRESULT =
        windows::core::HRESULT(0x8007_0005_u32 as i32);
    if e.code() == E_ACCESSDENIED || e.code() == ACCESS_DENIED_WIN32 {
        Error::AccessDenied
    } else {
        Error::Com(format!("{e} ({:#010x})", e.code().0))
    }
}

/// A live `INetFwPolicy2`.
pub struct ComPolicy {
    policy: INetFwPolicy2,
}

impl ComPolicy {
    /// Initialises COM for this thread and binds the firewall policy object.
    ///
    /// # Errors
    /// COM could not create the firewall policy object.
    pub fn new() -> Result<Self> {
        // SAFETY: this is the documented way to bind the policy object, and both
        // results are checked below.
        unsafe {
            // Either S_FALSE or RPC_E_CHANGED_MODE, and neither stops the calls
            // below working in this single-purpose process.
            let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);

            let policy: INetFwPolicy2 =
                CoCreateInstance(&NetFwPolicy2, None, CLSCTX_INPROC_SERVER).map_err(map_err)?;
            Ok(Self { policy })
        }
    }
}

// No Drop calling CoUninitialize: the interface pointer would have to be
// released first, and this process exits immediately after the single
// operation, so the OS reclaims the apartment either way.

impl FirewallPolicy for ComPolicy {
    type Rule = INetFwRule;

    /// True when the firewall is on for *any* currently active profile.
    ///
    /// The retired C# asked `INetFwMgr.LocalPolicy.CurrentProfile`, which
    /// reports a single profile and so answered the wrong question on a machine
    /// with several active at once. Each active profile is queried here, and any
    /// one of them being enabled means the rule is needed.
    fn is_enabled(&self) -> Result<bool> {
        unsafe {
            // SAFETY: COM is initialised for this thread by `ComPolicy::new`, and every
            // interface pointer here came from a checked call.
            let active = self.policy.CurrentProfileTypes().map_err(map_err)?;

            for profile in [
                NET_FW_PROFILE2_DOMAIN,
                NET_FW_PROFILE2_PRIVATE,
                NET_FW_PROFILE2_PUBLIC,
            ] {
                if active & profile.0 == 0 {
                    continue;
                }
                if self
                    .policy
                    .get_FirewallEnabled(NET_FW_PROFILE_TYPE2(profile.0))
                    .map_err(map_err)?
                    .as_bool()
                {
                    return Ok(true);
                }
            }

            Ok(false)
        }
    }

    /// Walks the rule collection through `IEnumVARIANT`.
    ///
    /// The C# one-liner `Rules.OfType<INetFwRule>()` has no equivalent here: the
    /// collection is exposed as an automation enumerator, so each `VARIANT` has
    /// to be unwrapped to an `IDispatch` and queried for `INetFwRule`. Entries
    /// that are not rules, and rules whose name cannot be read, are skipped
    /// rather than failing the whole walk: a single malformed rule elsewhere in
    /// the user's firewall must not stop us finding ours.
    fn rules(&self) -> Result<Vec<(String, Self::Rule)>> {
        unsafe {
            // SAFETY: COM is initialised for this thread by `ComPolicy::new`, and every
            // interface pointer here came from a checked call.
            let collection = self.policy.Rules().map_err(map_err)?;
            let enumerator: IEnumVARIANT = collection
                ._NewEnum()
                .map_err(map_err)?
                .cast()
                .map_err(map_err)?;

            let mut found = Vec::new();
            loop {
                let mut item = [VARIANT::default()];
                let mut fetched = 0u32;

                // Next returns S_FALSE when it runs dry, which is not an error.
                enumerator
                    .Next(&mut item, &mut fetched)
                    .ok()
                    .map_err(map_err)?;
                if fetched == 0 {
                    break;
                }

                let Ok(dispatch) = IDispatch::try_from(&item[0]) else {
                    continue;
                };
                let Ok(rule) = dispatch.cast::<INetFwRule>() else {
                    continue;
                };
                let Ok(name) = rule.Name() else {
                    continue;
                };

                found.push((name.to_string(), rule));
            }

            Ok(found)
        }
    }

    fn set_local_ports(&self, rule: &Self::Rule, ports: &str) -> Result<()> {
        // SAFETY: COM is initialised for this thread by `ComPolicy::new`, and every
        // interface pointer here came from a checked call.
        unsafe { rule.SetLocalPorts(&BSTR::from(ports)).map_err(map_err) }
    }

    fn add_rule(&self, name: &str, ports: &str) -> Result<()> {
        unsafe {
            // SAFETY: COM is initialised for this thread by `ComPolicy::new`, and every
            // interface pointer here came from a checked call.
            let rule: INetFwRule =
                CoCreateInstance(&NetFwRule, None, CLSCTX_INPROC_SERVER).map_err(map_err)?;

            // Field-for-field the same rule the C# utility created.
            rule.SetAction(NET_FW_ACTION_ALLOW).map_err(map_err)?;
            rule.SetName(&BSTR::from(name)).map_err(map_err)?;
            rule.SetDirection(NET_FW_RULE_DIR_IN).map_err(map_err)?;
            rule.SetEnabled(VARIANT_TRUE).map_err(map_err)?;
            rule.SetProtocol(NET_FW_IP_PROTOCOL_TCP.0)
                .map_err(map_err)?;
            rule.SetLocalPorts(&BSTR::from(ports)).map_err(map_err)?;
            rule.SetInterfaceTypes(&BSTR::from("All"))
                .map_err(map_err)?;

            self.policy
                .Rules()
                .map_err(map_err)?
                .Add(&rule)
                .map_err(map_err)
        }
    }
}
