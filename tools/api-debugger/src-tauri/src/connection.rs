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

use mbrc_wire::{frame_line, parse_context, ClientHandshake, FrameAccumulator};
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
            if write_half
                .write_all(frame_line(&line).as_bytes())
                .await
                .is_err()
            {
                break;
            }
            let _ = write_half.flush().await;
        }
    });

    // Reader task: split on terminator, automate handshake, surface messages.
    let app_reader = app.clone();
    let tx_reader = tx.clone();
    let slot_reader = slot.clone();
    let mut handshake = ClientHandshake::new(
        options.client_type.clone(),
        options.protocol_version,
        options.no_broadcast,
    );
    // Capture the initial frame before `handshake` moves into the reader task.
    let initial = handshake.initial();
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
                        let context = context_of(&line);

                        // Handshake + keepalive automation (player -> protocol,
                        // ping -> pong), driven by the shared wire crate.
                        if let Some(reply) = handshake.on_incoming(&context) {
                            emit_send(&app_reader, &slot_reader, &tx_reader, reply);
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
