//! The negotiated wire protocol version and its formatter selection.
//!
//! Only V4 is spoken today. The enum is the pre-wired seam for V6+: adding a
//! variant here plus a `wire` formatter is the entire change - handlers select
//! their formatter via `ProtocolVersion::formatter()` and never name a version.

use crate::wire::{WireCodec, V4_CODEC};

/// A protocol version the core can format wire frames for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ProtocolVersion {
    /// The maintained legacy V4 surface (Android + iOS 1.4.1 clients).
    V4,
    // V6 is reserved: add the variant + a `wire::v6` formatter and map it below.
}

impl ProtocolVersion {
    /// Map a negotiated handshake version number to a formatter version, or
    /// `None` if unsupported (the handshake already rejects pre-V4).
    pub fn from_negotiated(version: u8) -> Option<Self> {
        match version {
            4 => Some(Self::V4),
            _ => None,
        }
    }

    /// The wire codec for this version.
    pub fn codec(self) -> &'static dyn WireCodec {
        match self {
            Self::V4 => &V4_CODEC,
        }
    }
}
