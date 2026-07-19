//! V6 system ops: server / plugin metadata.
//!
//! `system_info` returns the **real** plugin build version (not the V4-pinned
//! `1.4.1.0` legacy value - that pin exists only so shipped V4 clients keep their
//! behavior) plus the protocol version.

use serde_json::{json, Value};

use mbrc_wire::v6;

use super::{internal, OpResult};
use crate::providers::Providers;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &["system_info"];

/// Dispatch a `system_*` op. `None` if `op` is not in this domain.
pub fn dispatch(op: &str, _data: &Value, p: &dyn Providers) -> Option<OpResult> {
    Some(match op {
        "system_info" => info(p),
        _ => return None,
    })
}

fn info(p: &dyn Providers) -> OpResult {
    Ok(json!({
        "plugin_version": p.plugin_version().map_err(internal)?,
        "protocol_version": v6::PROTOCOL_VERSION,
    }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::MockProviders;

    #[test]
    fn system_info_reports_real_version_and_protocol() {
        let m = MockProviders {
            plugin_version: "1.5.0.0".into(),
            ..Default::default()
        };
        let out = dispatch("system_info", &json!({}), &m).unwrap().unwrap();
        // Unlike the V4 `pluginversion` (pinned to 1.4.1.0), V6 reports the build.
        assert_eq!(out["plugin_version"], "1.5.0.0");
        assert_eq!(out["protocol_version"], 6);
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m).is_none());
    }
}
