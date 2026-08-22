//! Direct connection(s) to a MusicBee Remote plugin instance.
//!
//! The wire protocol (CRLF-JSON framing, `{context,data}` messages, and the
//! `player` -> `protocol` / `ping` -> `pong` handshake automation) lives in the
//! shared `mbrc-wire` crate. This module is the Tauri glue: it owns the sockets,
//! surfaces every frame to the UI, and keys connections by a `slot` string
//! ("primary" / "secondary") so the UI can hold two independent sockets - the
//! secondary mirrors the Android client's separate `no_broadcast` data-fetch
//! connection. Events are emitted on slot-scoped channels:
//! `mbrc://message/<slot>` and `mbrc://state/<slot>`.

use std::collections::HashMap;
use std::sync::Mutex;

use mbrc_wire::{frame_line, parse_context, v6, ClientHandshake, FrameAccumulator};
use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, State};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::sync::mpsc;
use tokio::task::JoinHandle;

/// Base event channel names; the actual channel is suffixed with `/<slot>`.
pub const EVENT_MESSAGE: &str = "mbrc://message";
pub const EVENT_STATE: &str = "mbrc://state";

fn message_channel(slot: &str) -> String {
    format!("{EVENT_MESSAGE}/{slot}")
}
fn state_channel(slot: &str) -> String {
    format!("{EVENT_STATE}/{slot}")
}

/// `context` of a wire line, with the UI's "unknown" fallback for junk frames.
fn context_of(line: &str) -> String {
    parse_context(line).unwrap_or_else(|| "unknown".to_string())
}

#[derive(Debug, Deserialize)]
pub struct ConnectOptions {
    pub host: String,
    pub port: u16,
    #[serde(default = "default_client_type")]
    pub client_type: String,
    #[serde(default = "default_protocol_version")]
    pub protocol_version: u8,
    #[serde(default)]
    pub no_broadcast: bool,
    /// Required per-install id for a V6 connection; ignored by legacy. Defaults to a
    /// fixed dev id when the UI does not supply one.
    #[serde(default)]
    pub client_id: Option<String>,
}

fn default_client_type() -> String {
    "Android".to_string()
}
fn default_protocol_version() -> u8 {
    4
}

/// A single wire message surfaced to the UI log. Legacy frames carry a `context`;
/// V6 frames additionally carry the parsed envelope fields so the UI can render
/// them as typed rows.
#[derive(Debug, Clone, Serialize)]
pub struct WireMessage {
    /// "sent" | "received" | "info" | "error"
    pub direction: String,
    pub context: String,
    pub raw: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<u64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub kind: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub op: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub event: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error_code: Option<String>,
}

/// Build a UI log message from a wire line, auto-detecting protocol by shape: a
/// frame carrying `kind` is a V6 envelope (parse out `id`/`op`/`event`/error);
/// anything else is a legacy `{context,data}` frame.
fn wire_message(direction: &str, line: &str) -> WireMessage {
    let parsed = serde_json::from_str::<serde_json::Value>(line).ok();
    let is_v6 = parsed
        .as_ref()
        .and_then(|v| v.get("kind"))
        .and_then(|k| k.as_str())
        .is_some();
    if let (true, Some(v)) = (is_v6, parsed.as_ref()) {
        let str_field = |k: &str| v.get(k).and_then(|x| x.as_str()).map(String::from);
        let kind = str_field("kind");
        let op = str_field("op");
        let event = str_field("event");
        let error_code = v
            .get("error")
            .and_then(|e| e.get("code"))
            .and_then(|c| c.as_str())
            .map(String::from);
        // Display label: op (request) / event (broadcast) / else the kind.
        let context = op
            .clone()
            .or_else(|| event.clone())
            .or_else(|| kind.clone())
            .unwrap_or_else(|| "v6".to_string());
        WireMessage {
            direction: direction.to_string(),
            context,
            raw: line.to_string(),
            id: v.get("id").and_then(|i| i.as_u64()),
            kind,
            op,
            event,
            error_code,
        }
    } else {
        WireMessage {
            direction: direction.to_string(),
            context: context_of(line),
            raw: line.to_string(),
            id: None,
            kind: None,
            op: None,
            event: None,
            error_code: None,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct StateEvent {
    pub connected: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub detail: Option<String>,
}

struct Connection {
    outbound: mpsc::UnboundedSender<String>,
    reader: JoinHandle<()>,
    writer: JoinHandle<()>,
}

#[derive(Default)]
pub struct ConnectionState {
    slots: Mutex<HashMap<String, Connection>>,
}

impl ConnectionState {
    /// Install (or clear) the connection for a slot, tearing down any previous.
    fn replace(&self, slot: &str, conn: Option<Connection>) {
        let mut guard = self.slots.lock().unwrap();
        if let Some(old) = guard.remove(slot) {
            old.reader.abort();
            old.writer.abort();
        }
        if let Some(c) = conn {
            guard.insert(slot.to_string(), c);
        }
    }

    fn sender(&self, slot: &str) -> Option<mpsc::UnboundedSender<String>> {
        self.slots
            .lock()
            .unwrap()
            .get(slot)
            .map(|c| c.outbound.clone())
    }
}

/// Send a line and mirror it into the slot's UI log as a "sent" message.
fn emit_send(app: &AppHandle, slot: &str, tx: &mpsc::UnboundedSender<String>, line: String) {
    let _ = app.emit(&message_channel(slot), wire_message("sent", &line));
    let _ = tx.send(line);
}

#[tauri::command]
pub async fn connect(
    app: AppHandle,
    state: State<'_, ConnectionState>,
    slot: String,
    options: ConnectOptions,
) -> Result<(), String> {
    let stream = TcpStream::connect((options.host.as_str(), options.port))
        .await
        .map_err(|e| format!("connect failed: {e}"))?;
    let (mut read_half, mut write_half) = stream.into_split();

    let (tx, mut rx) = mpsc::unbounded_channel::<String>();

    // V6 (envelope, newline framing, handshake op) vs legacy (CRLF, {context,data},
    // player/protocol automation). Chosen once per connection by protocol_version.
    let is_v6 = options.protocol_version == v6::PROTOCOL_VERSION as u8;

    // Writer task: frame each outbound line with the protocol's terminator (`\n`
    // for V6, `\r\n` for legacy).
    let writer = tokio::spawn(async move {
        while let Some(line) = rx.recv().await {
            let framed = if is_v6 {
                v6::frame_line(&line)
            } else {
                frame_line(&line)
            };
            if write_half.write_all(framed.as_bytes()).await.is_err() {
                break;
            }
            let _ = write_half.flush().await;
        }
    });

    // Legacy drives handshake/keepalive automation from the shared crate; V6 sends
    // its handshake once and does no auto-replies (no keepalive in the spine).
    let mut handshake = if is_v6 {
        None
    } else {
        Some(ClientHandshake::new(
            options.client_type.clone(),
            options.protocol_version,
            options.no_broadcast,
        ))
    };
    let initial = if is_v6 {
        let client_id = options
            .client_id
            .clone()
            .unwrap_or_else(|| "api-debugger-dev".to_string());
        let ct = v6::ClientType::parse(&options.client_type.to_lowercase())
            .unwrap_or(v6::ClientType::Desktop);
        v6::handshake_request(&client_id, ct, options.no_broadcast)
    } else {
        handshake.as_ref().unwrap().initial()
    };

    // Reader task: split on terminator, (legacy) automate handshake, surface each
    // frame with its protocol-appropriate fields.
    let app_reader = app.clone();
    let tx_reader = tx.clone();
    let slot_reader = slot.clone();
    let reader = tokio::spawn(async move {
        let mut buf = vec![0u8; 8192];
        let mut acc = FrameAccumulator::default();

        loop {
            match read_half.read(&mut buf).await {
                Ok(0) => break, // peer closed
                Ok(n) => {
                    acc.push_bytes(&buf[..n]);
                    while let Some(line) = acc.next_frame() {
                        if line.trim().is_empty() {
                            continue;
                        }

                        // Legacy handshake + keepalive automation (player ->
                        // protocol, ping -> pong). V6 has no auto-replies.
                        if let Some(hs) = handshake.as_mut() {
                            let context = context_of(&line);
                            if let Some(reply) = hs.on_incoming(&context) {
                                emit_send(&app_reader, &slot_reader, &tx_reader, reply);
                            }
                        }

                        let _ = app_reader.emit(
                            &message_channel(&slot_reader),
                            wire_message("received", &line),
                        );
                    }
                }
                Err(_) => break,
            }
        }

        let _ = app_reader.emit(
            &state_channel(&slot_reader),
            StateEvent {
                connected: false,
                detail: Some("connection closed".into()),
            },
        );
    });

    // Kick off the handshake with the initial player message.
    emit_send(&app, &slot, &tx, initial);

    state.replace(
        &slot,
        Some(Connection {
            outbound: tx,
            reader,
            writer,
        }),
    );
    let _ = app.emit(
        &state_channel(&slot),
        StateEvent {
            connected: true,
            detail: None,
        },
    );
    Ok(())
}

#[tauri::command]
pub fn send_command(
    app: AppHandle,
    state: State<'_, ConnectionState>,
    slot: String,
    json: String,
) -> Result<(), String> {
    let tx = state
        .sender(&slot)
        .ok_or_else(|| "not connected".to_string())?;
    emit_send(&app, &slot, &tx, json);
    Ok(())
}

#[tauri::command]
pub fn disconnect(app: AppHandle, state: State<'_, ConnectionState>, slot: String) {
    state.replace(&slot, None);
    let _ = app.emit(
        &state_channel(&slot),
        StateEvent {
            connected: false,
            detail: Some("disconnected".into()),
        },
    );
}
