use std::net::SocketAddr;
use std::sync::Arc;

use tokio::net::TcpStream;
use tokio::time::{interval, Duration};
use tracing::{info, warn};
use uuid::Uuid;

use crate::protocol::constants;
use crate::protocol::messages::SocketMessage;
use crate::state::AppState;

use super::codec::{MessageReader, MessageWriter};
use super::commands;
use super::handshake::{process_handshake, ClientPlatform, ClientState, HandshakeResult};

const PING_INTERVAL: Duration = Duration::from_secs(30);

/// Handle a single legacy TCP client connection.
pub async fn handle_connection(stream: TcpStream, peer: SocketAddr, state: Arc<AppState>) {
    let connection_id = Uuid::new_v4().to_string();
    let mut client = ClientState::new(connection_id.clone());

    let (read_half, write_half) = stream.into_split();
    let mut reader = MessageReader::new(read_half);
    let mut writer = MessageWriter::new(write_half);

    let mut event_rx = state.event_tx().subscribe();
    let mut ping_timer = interval(PING_INTERVAL);
    // Consume the first tick immediately (interval fires immediately on creation)
    ping_timer.tick().await;

    info!(
        peer = %peer,
        connection_id = client.short_id(),
        "Legacy TCP connection handler started"
    );

    loop {
        tokio::select! {
            // Read from client. Only authenticated iOS v4 connections opt
            // into the `\'` sanitizer — pre-auth frames are tightly
            // controlled by the handshake FSM.
            msg_opt = reader.read_message(
                client.is_authenticated()
                    && client.platform == ClientPlatform::Ios
                    && client.protocol_version == 4,
            ) => {
                match msg_opt {
                    Some(msg) => {
                        if !handle_message(&mut client, &msg, &mut writer, &state).await {
                            break;
                        }
                    }
                    None => {
                        // Client disconnected or read error
                        info!(
                            connection_id = client.short_id(),
                            "Legacy TCP client disconnected"
                        );
                        break;
                    }
                }
            }

            // Receive broadcast events (only when authenticated and broadcasts enabled)
            event = event_rx.recv(), if client.is_authenticated() && client.broadcasts_enabled => {
                match event {
                    Ok(broadcast) => {
                        for msg in &broadcast.messages {
                            if let Err(e) = writer.write_message(msg).await {
                                warn!(
                                    connection_id = client.short_id(),
                                    error = %e,
                                    "Failed to send broadcast, disconnecting"
                                );
                                return;
                            }
                        }
                    }
                    Err(tokio::sync::broadcast::error::RecvError::Lagged(n)) => {
                        warn!(
                            connection_id = client.short_id(),
                            skipped = n,
                            "Broadcast receiver lagged, skipped events"
                        );
                    }
                    Err(tokio::sync::broadcast::error::RecvError::Closed) => {
                        info!(
                            connection_id = client.short_id(),
                            "Broadcast channel closed, disconnecting"
                        );
                        break;
                    }
                }
            }

            // Ping keepalive (only when authenticated)
            _ = ping_timer.tick(), if client.is_authenticated() => {
                let ping = SocketMessage::empty(constants::PING);
                if let Err(e) = writer.write_message(&ping).await {
                    warn!(
                        connection_id = client.short_id(),
                        error = %e,
                        "Failed to send ping, disconnecting"
                    );
                    break;
                }
            }
        }
    }

    info!(
        peer = %peer,
        connection_id = client.short_id(),
        "Legacy TCP connection handler exiting"
    );
}

/// Handle a single incoming message. Returns `false` if the connection should close.
async fn handle_message(
    client: &mut ClientState,
    msg: &SocketMessage,
    writer: &mut MessageWriter,
    state: &Arc<AppState>,
) -> bool {
    // During handshake, delegate to the FSM
    if !client.is_authenticated() {
        match process_handshake(client, msg) {
            HandshakeResult::Response(response) => {
                if let Err(e) = writer.write_message(&response).await {
                    warn!(
                        connection_id = client.short_id(),
                        error = %e,
                        "Failed to send handshake response"
                    );
                    return false;
                }

                if client.is_authenticated() {
                    info!(
                        connection_id = client.short_id(),
                        protocol_version = client.protocol_version,
                        platform = ?client.platform,
                        "Client authenticated"
                    );
                }
                return true;
            }
            HandshakeResult::ResponseAndDisconnect(response) => {
                // Best-effort send the rejection message so the client
                // can show the user a reason; then close regardless of
                // whether the write succeeded.
                let _ = writer.write_message(&response).await;
                warn!(
                    connection_id = client.short_id(),
                    protocol_version = client.protocol_version,
                    "Rejecting unsupported protocol version"
                );
                return false;
            }
            HandshakeResult::Disconnect => {
                warn!(
                    connection_id = client.short_id(),
                    context = msg.context.as_str(),
                    "Invalid handshake message, disconnecting"
                );
                return false;
            }
            HandshakeResult::NotHandshake => {
                // Shouldn't happen since we checked is_authenticated above
            }
        }
    }

    // Authenticated — handle fast inline commands first
    match msg.context.as_str() {
        constants::PING => {
            let pong = SocketMessage::empty(constants::PONG);
            if let Err(e) = writer.write_message(&pong).await {
                warn!(
                    connection_id = client.short_id(),
                    error = %e,
                    "Failed to send pong"
                );
                return false;
            }
        }
        constants::PONG => {
            // Client responded to our ping — nothing to do
        }
        constants::VERIFY_CONNECTION => {
            // C# `ProtocolHandler.cs:160` uses `string.Empty`, not a bool.
            // Match the wire byte-for-byte so parity fixtures pass.
            let response = SocketMessage::new(constants::VERIFY_CONNECTION, "");
            if let Err(e) = writer.write_message(&response).await {
                warn!(
                    connection_id = client.short_id(),
                    error = %e,
                    "Failed to send verifyconnection response"
                );
                return false;
            }
        }
        _ => {
            // Dispatch to command handlers
            let responses =
                commands::dispatch_command(msg.context.as_str(), &msg.data, state, client.platform)
                    .await;
            for response in &responses {
                if let Err(e) = writer.write_message(response).await {
                    warn!(
                        connection_id = client.short_id(),
                        error = %e,
                        "Failed to send command response"
                    );
                    return false;
                }
            }
        }
    }

    true
}
