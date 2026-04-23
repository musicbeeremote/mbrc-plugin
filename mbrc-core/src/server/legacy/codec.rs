use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader, BufWriter};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tracing::trace;

use crate::protocol::messages::SocketMessage;

/// Reads CRLF-delimited JSON messages from a TCP stream.
pub struct MessageReader {
    reader: BufReader<OwnedReadHalf>,
    buf: String,
}

impl MessageReader {
    pub fn new(read_half: OwnedReadHalf) -> Self {
        Self {
            reader: BufReader::new(read_half),
            buf: String::with_capacity(4096),
        }
    }

    /// Read the next message. Returns `None` on clean disconnect or read error.
    pub async fn read_message(&mut self) -> Option<SocketMessage> {
        loop {
            self.buf.clear();
            match self.reader.read_line(&mut self.buf).await {
                Ok(0) => return None, // EOF — client disconnected
                Ok(_) => {
                    let trimmed = self.buf.trim();
                    if trimmed.is_empty() {
                        continue; // Empty line — skip
                    }
                    trace!(raw = trimmed, "Received raw message");
                    match serde_json::from_str::<SocketMessage>(trimmed) {
                        Ok(msg) => return Some(msg),
                        Err(e) => {
                            tracing::warn!(error = %e, raw = trimmed, "Failed to parse message JSON");
                            return None;
                        }
                    }
                }
                Err(e) => {
                    tracing::warn!(error = %e, "Read error on legacy TCP connection");
                    return None;
                }
            }
        }
    }
}

/// Writes CRLF-delimited JSON messages to a TCP stream.
pub struct MessageWriter {
    writer: BufWriter<OwnedWriteHalf>,
}

impl MessageWriter {
    pub fn new(write_half: OwnedWriteHalf) -> Self {
        Self {
            writer: BufWriter::new(write_half),
        }
    }

    /// Send a message as JSON + CRLF.
    pub async fn write_message(&mut self, msg: &SocketMessage) -> Result<(), std::io::Error> {
        let json = serde_json::to_string(msg)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))?;
        trace!(json = json.as_str(), "Sending message");
        self.writer.write_all(json.as_bytes()).await?;
        self.writer.write_all(b"\r\n").await?;
        self.writer.flush().await?;
        Ok(())
    }
}
