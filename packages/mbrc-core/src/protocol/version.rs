//! The negotiated legacy protocol version and its formatter selection.
//!
//! This enum covers the versions the legacy `protocol` handshake negotiates.
//! V6 is not one of them: it is routed by its first frame's shape into
//! [`session_v6`](crate::server::session_v6) and carries its own envelope, so it
//! never reaches a [`WireCodec`]. What the port as a whole speaks is
//! [`ADVERTISED_PROTOCOLS`]; what this enum accepts is [`SUPPORTED_VERSIONS`].

use crate::wire::{V4_CODEC, WireCodec};

/// A protocol version the core can format wire frames for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ProtocolVersion {
    /// The maintained legacy V4 surface (Android + iOS 1.4.1 clients).
    V4,
    /// The maintained legacy V5 surface: byte-identical to V4 on the wire, plus
    /// the single iOS `nowplayingcurrentposition` c2s alias. Same codec as V4.
    V5,
    // V6 is reserved: add the variant + a `wire::v6` formatter and map it below.
}

/// Every version the legacy `protocol` handshake accepts, low to high.
///
/// The test below pins it to what [`ProtocolVersion::from_negotiated`] accepts,
/// so the two cannot drift. This is the handshake's accept-list, not a claim
/// about the port - to state what the port speaks, use [`ADVERTISED_PROTOCOLS`].
pub const SUPPORTED_VERSIONS: &[u8] = &[4, 5];

/// Every protocol a client may find on the command port, low to high.
///
/// Exists so anything that has to *state* what is reachable - the mDNS TXT
/// record, the diagnostics report - reads it from here instead of carrying a
/// list that goes stale when a protocol is added. It is the legacy accept-list
/// plus V6, which the port serves through a different door entirely.
pub const ADVERTISED_PROTOCOLS: &[u8] = &[4, 5, mbrc_wire::v6::PROTOCOL_VERSION as u8];

impl ProtocolVersion {
    /// Maps a negotiated handshake version number to a formatter version, or
    /// `None` if unsupported (the handshake already rejects pre-V4).
    ///
    /// Callers pass the negotiated version capped at `MAX_PROTOCOL`, mirroring
    /// the handshake reply: a client told MAX must be dispatched as MAX, or the
    /// MAX-gated commands it then sends would be silently dropped.
    pub fn from_negotiated(version: u8) -> Option<Self> {
        match version {
            4 => Some(Self::V4),
            5 => Some(Self::V5),
            _ => None,
        }
    }

    /// The wire codec for this version. V5 shares the V4 codec: it adds one c2s
    /// trigger, not a new wire shape.
    pub fn codec(self) -> &'static dyn WireCodec {
        match self {
            Self::V4 | Self::V5 => &V4_CODEC,
        }
    }

    /// Whether this version accepts the `nowplayingcurrentposition` c2s alias
    /// (V5+ only; it never fires in a V4 session).
    pub fn accepts_current_position(self) -> bool {
        matches!(self, Self::V5)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Advertising V6 is the point of the list being separate: a client that
    /// only speaks V6 must be able to tell, before connecting, that this port
    /// will answer it.
    #[test]
    fn every_protocol_on_the_port_is_advertised() {
        assert!(
            ADVERTISED_PROTOCOLS.contains(&(mbrc_wire::v6::PROTOCOL_VERSION as u8)),
            "V6 is served on the command port but not advertised"
        );
        for &version in SUPPORTED_VERSIONS {
            assert!(
                ADVERTISED_PROTOCOLS.contains(&version),
                "legacy version {version} is accepted but not advertised"
            );
        }
    }

    /// The legacy list stays the handshake's accept-list. V6 must never leak
    /// into it: `from_negotiated` would then be asked for a codec V6 has not
    /// got, and the legacy handshake would start claiming to speak it.
    #[test]
    fn the_legacy_list_excludes_v6() {
        assert!(!SUPPORTED_VERSIONS.contains(&(mbrc_wire::v6::PROTOCOL_VERSION as u8)));
        assert!(ProtocolVersion::from_negotiated(mbrc_wire::v6::PROTOCOL_VERSION as u8).is_none());
    }

    #[test]
    fn the_advertised_versions_are_the_accepted_ones() {
        // The list exists to be published (mDNS TXT). If it ever disagrees with
        // what the handshake accepts, clients are told something untrue.
        for &version in SUPPORTED_VERSIONS {
            assert!(
                ProtocolVersion::from_negotiated(version).is_some(),
                "version {version} is advertised but not accepted"
            );
        }
        for version in 0u8..=10 {
            if ProtocolVersion::from_negotiated(version).is_some() {
                assert!(
                    SUPPORTED_VERSIONS.contains(&version),
                    "version {version} is accepted but not advertised"
                );
            }
        }
    }
}
