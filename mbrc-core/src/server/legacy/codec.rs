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
    ///
    /// `lenient_ios_quotes` should be set only on authenticated iOS v4
    /// connections; when true, a parse failure triggers a retry with
    /// `\'` escapes rewritten to `'` (the iOS client sends malformed
    /// JSON on frames like `nowplayingqueue` where track titles contain
    /// apostrophes — `\'` is never a valid JSON escape so rescuing it
    /// is lossless for well-formed input).
    pub async fn read_message(&mut self, lenient_ios_quotes: bool) -> Option<SocketMessage> {
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
                            if lenient_ios_quotes {
                                let sanitized = sanitize_ios_quotes(trimmed);
                                if sanitized != trimmed {
                                    match serde_json::from_str::<SocketMessage>(&sanitized) {
                                        Ok(msg) => {
                                            tracing::debug!(
                                                "Recovered iOS-v4 frame after \\' sanitize"
                                            );
                                            return Some(msg);
                                        }
                                        Err(e2) => {
                                            tracing::warn!(
                                                error = %e2, raw = trimmed,
                                                "iOS sanitize retry also failed"
                                            );
                                            return None;
                                        }
                                    }
                                }
                            }
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

/// Rewrite the invalid JSON escape `\'` to `'`, leaving other escapes
/// (including `\\`) intact. `\'` is never valid JSON, so this is a
/// lossless transform for any well-formed input. Only applied to iOS
/// v4 connections where the client ships malformed strings containing
/// apostrophes.
fn sanitize_ios_quotes(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut iter = s.chars().peekable();
    while let Some(c) = iter.next() {
        if c == '\\' {
            match iter.peek() {
                Some('\'') => {
                    // Drop the backslash; the quote is emitted next iteration.
                    continue;
                }
                Some('\\') => {
                    // Preserve escaped backslash atomically so we don't
                    // collapse `\\'` into `\'` and then into `'`.
                    out.push('\\');
                    out.push('\\');
                    iter.next();
                }
                _ => out.push('\\'),
            }
        } else {
            out.push(c);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::sanitize_ios_quotes;

    #[test]
    fn replaces_bare_escaped_quote() {
        assert_eq!(sanitize_ios_quotes(r#""it\'s""#), r#""it's""#);
    }

    #[test]
    fn preserves_escaped_backslash_followed_by_quote() {
        assert_eq!(sanitize_ios_quotes(r#""path\\'tail""#), r#""path\\'tail""#);
    }

    #[test]
    fn leaves_valid_escapes_untouched() {
        assert_eq!(sanitize_ios_quotes(r#""\n\r\t""#), r#""\n\r\t""#);
        assert_eq!(sanitize_ios_quotes(r#""a\"b""#), r#""a\"b""#);
    }

    #[test]
    fn noop_on_clean_input() {
        let s = r#"{"context":"ping","data":""}"#;
        assert_eq!(sanitize_ios_quotes(s), s);
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
