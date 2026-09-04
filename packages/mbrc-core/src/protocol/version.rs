//! The negotiated wire protocol version and its formatter selection.
//!
//! Only V4 is spoken today. The enum is the pre-wired seam for V6+: adding a
//! variant here plus a `wire` formatter is the entire change - handlers select
//! their formatter via `ProtocolVersion::formatter()` and never name a version.

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

/// Every handshake version the core accepts, low to high.
///
/// Exists so anything that has to *state* what is supported - the mDNS TXT
/// record, and whatever else advertises later - reads it from here instead of
/// carrying its own list that quietly goes stale when a version is added. The
/// test below pins it to what [`ProtocolVersion::from_negotiated`] will accept.
pub const SUPPORTED_VERSIONS: &[u8] = &[4, 5];

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
