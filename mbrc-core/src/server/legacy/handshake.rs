use crate::protocol::constants;
use crate::protocol::messages::{ProtocolHandshake, SocketMessage};

/// Minimum protocol version the legacy server will accept. Protocol 4
/// is the stable contract shipped since Android 1.4.0 and is the only
/// version still in the field. Pre-v4 handshakes are rejected with a
/// `notallowed` frame and the connection is closed.
///
/// The v4 branch is permanent legacy compatibility — all its quirks
/// (concatenated keys, stringified `playervolume`, iOS `\'` escapes)
/// are preserved as-is to keep shipping clients working. A clean-slate
/// `v5` will be introduced as a separate handshake path with strict
/// schemas and no quirks.
pub const MIN_SUPPORTED_PROTOCOL: i32 = 4;

/// Client platform type.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClientPlatform {
    Android,
    #[allow(dead_code)]
    Ios,
    Unknown,
}

/// Handshake state machine.
/// Packet 0 must be "player", packet 1 must be "protocol", then authenticated.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum HandshakeState {
    AwaitingPlayer,
    AwaitingProtocol,
    Authenticated,
}

/// Per-connection client state.
pub struct ClientState {
    pub connection_id: String,
    pub client_id: Option<String>,
    pub protocol_version: i32,
    pub broadcasts_enabled: bool,
    pub platform: ClientPlatform,
    pub handshake: HandshakeState,
}

impl ClientState {
    pub fn new(connection_id: String) -> Self {
        Self {
            connection_id,
            client_id: None,
            protocol_version: 2,
            broadcasts_enabled: true,
            platform: ClientPlatform::Unknown,
            handshake: HandshakeState::AwaitingPlayer,
        }
    }

    pub fn is_authenticated(&self) -> bool {
        self.handshake == HandshakeState::Authenticated
    }

    pub fn short_id(&self) -> &str {
        if self.connection_id.len() > 6 {
            &self.connection_id[..6]
        } else {
            &self.connection_id
        }
    }
}

/// Result of processing a handshake message.
pub enum HandshakeResult {
    /// Send this response and advance state.
    Response(SocketMessage),
    /// Send this response (e.g., `notallowed`) and then close the
    /// connection — used to reject unsupported protocol versions
    /// with a human-readable diagnostic instead of silently dropping.
    ResponseAndDisconnect(SocketMessage),
    /// Invalid handshake — disconnect the client.
    Disconnect,
    /// Already authenticated — this is a normal command, not handled here.
    NotHandshake,
}

/// Process a message during the handshake phase.
pub fn process_handshake(client: &mut ClientState, msg: &SocketMessage) -> HandshakeResult {
    match &client.handshake {
        HandshakeState::AwaitingPlayer => {
            if msg.context != constants::PLAYER {
                return HandshakeResult::Disconnect;
            }

            // Parse platform from data
            if let Some(platform_str) = msg.data.as_str() {
                client.platform = match platform_str.to_lowercase().as_str() {
                    "android" => ClientPlatform::Android,
                    "ios" => ClientPlatform::Ios,
                    _ => ClientPlatform::Unknown,
                };
            }

            client.handshake = HandshakeState::AwaitingProtocol;

            // Respond with player name
            HandshakeResult::Response(SocketMessage::new(
                constants::PLAYER,
                constants::PLAYER_NAME,
            ))
        }

        HandshakeState::AwaitingProtocol => {
            if msg.context != constants::PROTOCOL {
                return HandshakeResult::Disconnect;
            }

            // Parse protocol version — supports both object and simple formats
            if let Ok(handshake) = serde_json::from_value::<ProtocolHandshake>(msg.data.clone()) {
                // Object format: {"protocol_version": 4, "no_broadcast": false, "client_id": "..."}
                client.protocol_version = handshake.protocol_version;
                client.broadcasts_enabled = !handshake.no_broadcast;
                client.client_id = handshake.client_id;
            } else if let Some(v) = msg.data.as_i64() {
                // Simple integer: 4
                client.protocol_version = v as i32;
            } else if let Some(s) = msg.data.as_str() {
                // Simple string: "4"
                if let Ok(v) = s.parse::<i32>() {
                    client.protocol_version = v;
                }
            }

            // Reject pre-v4 clients explicitly. Tell them why so the
            // user can see "update your client" rather than "connection
            // silently dropped", then disconnect.
            if client.protocol_version < MIN_SUPPORTED_PROTOCOL {
                return HandshakeResult::ResponseAndDisconnect(SocketMessage::new(
                    constants::NOT_ALLOWED,
                    format!(
                        "Protocol version {} is not supported. Minimum: {}. Please update your MBRC client.",
                        client.protocol_version, MIN_SUPPORTED_PROTOCOL
                    ),
                ));
            }

            client.handshake = HandshakeState::Authenticated;

            // Respond with server protocol version
            HandshakeResult::Response(SocketMessage::new(
                constants::PROTOCOL,
                constants::PROTOCOL_VERSION,
            ))
        }

        HandshakeState::Authenticated => HandshakeResult::NotHandshake,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn advance_to_protocol(client: &mut ClientState) {
        let player = SocketMessage::new(constants::PLAYER, "Android");
        let _ = process_handshake(client, &player);
        assert_eq!(client.handshake, HandshakeState::AwaitingProtocol);
    }

    #[test]
    fn rejects_protocol_v2_with_notallowed() {
        let mut client = ClientState::new("test".into());
        advance_to_protocol(&mut client);

        let msg = SocketMessage::new(constants::PROTOCOL, 2);
        match process_handshake(&mut client, &msg) {
            HandshakeResult::ResponseAndDisconnect(resp) => {
                assert_eq!(resp.context, constants::NOT_ALLOWED);
                let data = resp.data.as_str().expect("data is a string");
                assert!(data.contains("Protocol version 2"));
                assert!(data.contains("Minimum: 4"));
            }
            other => panic!("expected ResponseAndDisconnect, got {:?}", std::mem::discriminant(&other)),
        }
        // Client is NOT authenticated after rejection.
        assert!(!client.is_authenticated());
    }

    #[test]
    fn rejects_protocol_v3_via_object_form() {
        let mut client = ClientState::new("test".into());
        advance_to_protocol(&mut client);

        let msg = SocketMessage::new(
            constants::PROTOCOL,
            json!({"protocol_version": 3, "no_broadcast": false}),
        );
        assert!(matches!(
            process_handshake(&mut client, &msg),
            HandshakeResult::ResponseAndDisconnect(_)
        ));
    }

    #[test]
    fn accepts_protocol_v4() {
        let mut client = ClientState::new("test".into());
        advance_to_protocol(&mut client);

        let msg = SocketMessage::new(
            constants::PROTOCOL,
            json!({"protocol_version": 4, "no_broadcast": false}),
        );
        match process_handshake(&mut client, &msg) {
            HandshakeResult::Response(resp) => {
                assert_eq!(resp.context, constants::PROTOCOL);
            }
            _ => panic!("expected Response"),
        }
        assert!(client.is_authenticated());
        assert_eq!(client.protocol_version, 4);
    }

    #[test]
    fn accepts_v4_via_legacy_bare_int() {
        // iOS sometimes sends `{"context":"protocol","data":4}` instead
        // of the object form. Must still be accepted.
        let mut client = ClientState::new("test".into());
        advance_to_protocol(&mut client);

        let msg = SocketMessage::new(constants::PROTOCOL, 4);
        assert!(matches!(
            process_handshake(&mut client, &msg),
            HandshakeResult::Response(_)
        ));
        assert!(client.is_authenticated());
    }
}
