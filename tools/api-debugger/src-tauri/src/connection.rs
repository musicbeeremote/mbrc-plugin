//! Direct connection(s) to a MusicBee Remote plugin instance.
//!
//! The wire protocol is newline (`\r\n`) delimited JSON of the shape
//! `{"context": "...", "data": ...}`. Connecting performs the plugin handshake
//! automatically: send `player`, then on the plugin's `player` reply send
//! `protocol`, and reply to any `ping` with `pong`.
//!
//! Connections are keyed by a `slot` string ("primary" / "secondary") so the UI
//! can hold two independent sockets - the secondary mirrors the Android client's
//! separate `no_broadcast` data-fetch connection. Events are emitted on
//! slot-scoped channels: `mbrc://message/<slot>` and `mbrc://state/<slot>`.

use std::collections::HashMap;
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, State};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::sync::mpsc;
use tokio::task::JoinHandle;

const TERMINATOR: &str = "\r\n";

/// Base event channel names; the actual channel is suffixed with `/<slot>`.
pub const EVENT_MESSAGE: &str = "mbrc://message";
pub const EVENT_STATE: &str = "mbrc://state";

fn message_channel(slot: &str) -> String {
    format!("{EVENT_MESSAGE}/{slot}")
}
fn state_channel(slot: &str) -> String {
    format!("{EVENT_STATE}/{slot}")
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
}

fn default_client_type() -> String {
    "Android".to_string()
}
fn default_protocol_version() -> u8 {
    4
}

/// A single wire message surfaced to the UI log.
#[derive(Debug, Clone, Serialize)]
pub struct WireMessage {
    /// "sent" | "received" | "info" | "error"
    pub direction: String,
    pub context: String,
    pub raw: String,
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

/// Pull the `context` field out of a JSON line without a full typed parse.
fn context_of(line: &str) -> String {
    serde_json::from_str::<serde_json::Value>(line)
        .ok()
        .and_then(|v| v.get("context").and_then(|c| c.as_str()).map(str::to_owned))
        .unwrap_or_else(|| "unknown".to_string())
}

/// Send a line and mirror it into the slot's UI log as a "sent" message.
fn emit_send(app: &AppHandle, slot: &str, tx: &mpsc::UnboundedSender<String>, line: String) {
    let context = context_of(&line);
    let _ = app.emit(
        &message_channel(slot),
        WireMessage {
            direction: "sent".into(),
            context,
            raw: line.clone(),
        },
    );
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

    // Writer task: frame each outbound line with the terminator.
    let writer = tokio::spawn(async move {
        while let Some(line) = rx.recv().await {
            let framed = format!("{line}{TERMINATOR}");
            if write_half.write_all(framed.as_bytes()).await.is_err() {
                break;
            }
            let _ = write_half.flush().await;
        }
    });

    // Reader task: split on terminator, automate handshake, surface messages.
    let app_reader = app.clone();
    let tx_reader = tx.clone();
    let slot_reader = slot.clone();
    let protocol_version = options.protocol_version;
    let no_broadcast = options.no_broadcast;
    let reader = tokio::spawn(async move {
        let mut buf = vec![0u8; 8192];
        let mut acc = String::new();
        let mut player_acknowledged = false;

        loop {
            match read_half.read(&mut buf).await {
                Ok(0) => break, // peer closed
                Ok(n) => {
                    acc.push_str(&String::from_utf8_lossy(&buf[..n]));
                    while let Some(idx) = acc.find(TERMINATOR) {
                        let line = acc[..idx].to_string();
                        acc = acc[idx + TERMINATOR.len()..].to_string();
                        if line.trim().is_empty() {
                            continue;
                        }
                        let context = context_of(&line);

                        // Handshake + keepalive automation.
                        match context.as_str() {
                            "ping" => {
                                emit_send(
                                    &app_reader,
                                    &slot_reader,
                                    &tx_reader,
                                    r#"{"context":"pong","data":""}"#.to_string(),
                                );
                            }
                            "player" if !player_acknowledged => {
                                player_acknowledged = true;
                                let msg = format!(
                                    r#"{{"context":"protocol","data":{{"protocol_version":{protocol_version},"no_broadcast":{no_broadcast}}}}}"#
                                );
                                emit_send(&app_reader, &slot_reader, &tx_reader, msg);
                            }
                            _ => {}
                        }

                        let _ = app_reader.emit(
                            &message_channel(&slot_reader),
                            WireMessage {
                                direction: "received".into(),
                                context,
                                raw: line,
                            },
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
    emit_send(
        &app,
        &slot,
        &tx,
        format!(r#"{{"context":"player","data":"{}"}}"#, options.client_type),
    );

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
