use serde::{Deserialize, Serialize};

use crate::ffi::types::NotificationType;

/// Incoming/outgoing socket message matching C# SocketMessage format.
/// `{"context":"command_name","data":"value_or_object"}`
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SocketMessage {
    pub context: String,
    #[serde(default)]
    pub data: serde_json::Value,
}

impl SocketMessage {
    pub fn new(context: &str, data: impl Into<serde_json::Value>) -> Self {
        Self {
            context: context.to_owned(),
            data: data.into(),
        }
    }

    pub fn empty(context: &str) -> Self {
        Self {
            context: context.to_owned(),
            data: serde_json::Value::String(String::new()),
        }
    }
}

/// Event broadcast to all connected legacy TCP clients.
#[derive(Debug, Clone)]
pub struct BroadcastEvent {
    /// The original notification type (used for filtering in future phases).
    #[allow(dead_code)]
    pub notification: NotificationType,
    /// Pre-serialized JSON payload for the broadcast.
    /// Each entry is (context, data) for a specific event.
    pub messages: Vec<SocketMessage>,
}

impl BroadcastEvent {
    pub fn single(notification: NotificationType, context: &str, data: serde_json::Value) -> Self {
        Self {
            notification,
            messages: vec![SocketMessage::new(context, data)],
        }
    }

    pub fn multi(notification: NotificationType, messages: Vec<SocketMessage>) -> Self {
        Self {
            notification,
            messages,
        }
    }
}

/// Protocol handshake data sent by the client in the "protocol" message.
/// Supports the object format: `{"protocol_version": 4, "no_broadcast": false, "client_id": "..."}`
#[derive(Debug, Deserialize)]
pub struct ProtocolHandshake {
    #[serde(default = "default_protocol_version")]
    pub protocol_version: i32,
    #[serde(default)]
    pub no_broadcast: bool,
    #[serde(default)]
    pub client_id: Option<String>,
}

fn default_protocol_version() -> i32 {
    2
}
