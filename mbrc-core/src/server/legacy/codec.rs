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
                                // Two iOS-v4 wire bugs to recover from:
                                //   1. `\'` escape sequences in strings.
                                //   2. Unquoted barewords after `"data":`,
                                //      e.g. `{"context":"nowplayingposition",
                                //      "data":status}` — Swift enum-case
                                //      string-interpolation slipping through.
                                // Both transforms are idempotent on well-formed
                                // JSON so applying them speculatively is safe.
                                let sanitized = sanitize_ios_data_bareword(
                                    &sanitize_ios_quotes(trimmed),
                                );
                                if sanitized != trimmed {
                                    match serde_json::from_str::<SocketMessage>(&sanitized) {
                                        Ok(msg) => {
                                            tracing::debug!(
                                                "Recovered iOS-v4 frame after sanitize"
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

/// Recover from the iOS-v4 `"data":<bareword>` wire bug by quoting any
/// alphabetic-only bareword that follows `"data":` and is terminated by
/// `,` or `}`. Captured fixture pattern: iOS sends
/// `{"context":"nowplayingposition","data":status}` — the unquoted
/// `status` token is invalid JSON and would otherwise drop the frame
/// (and disconnect the client). Quoting yields `"data":"status"`,
/// which the handler ignores when the data isn't relevant for the
/// command.
///
/// Lossless on well-formed input: the function only transforms
/// when `"data":` is immediately followed by an alphabetic character
/// (i.e. clearly not a number, string, object, or array).
fn sanitize_ios_data_bareword(s: &str) -> String {
    let needle = r#""data":"#;
    let Some(start) = s.find(needle) else {
        return s.to_owned();
    };
    let value_start = start + needle.len();
    let rest = &s[value_start..];
    let first = match rest.chars().next() {
        Some(c) => c,
        None => return s.to_owned(),
    };
    // Only intervene on bareword starts. Numbers, quotes, brackets,
    // braces, and `null/true/false` (also barewords but valid JSON)
    // are left alone — `null`, `true`, `false` parse fine on their
    // own so reaching this path means the rest of the document broke,
    // not that token.
    if !first.is_ascii_alphabetic() {
        return s.to_owned();
    }
    let token_end = rest
        .find(|c: char| c == ',' || c == '}')
        .unwrap_or(rest.len());
    let token = &rest[..token_end];
    // Whitelist alphanumeric+underscore only — protects against weirder
    // payloads we haven't seen in the wild.
    if token.is_empty() || !token.chars().all(|c| c.is_ascii_alphanumeric() || c == '_') {
        return s.to_owned();
    }
    // Skip the known-good JSON literals so they aren't double-quoted.
    if matches!(token, "null" | "true" | "false") {
        return s.to_owned();
    }
    let mut out = String::with_capacity(s.len() + 2);
    out.push_str(&s[..value_start]);
    out.push('"');
    out.push_str(token);
    out.push('"');
    out.push_str(&rest[token_end..]);
    out
}

#[cfg(test)]
mod tests {
    use super::{sanitize_ios_data_bareword, sanitize_ios_quotes};

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

    #[test]
    fn quotes_data_bareword() {
        assert_eq!(
            sanitize_ios_data_bareword(r#"{"context":"nowplayingposition","data":status}"#),
            r#"{"context":"nowplayingposition","data":"status"}"#
        );
    }

    #[test]
    fn ios_bareword_sanitize_makes_it_parse() {
        let raw = r#"{"context":"nowplayingposition","data":status}"#;
        let fixed = sanitize_ios_data_bareword(raw);
        let parsed: serde_json::Value = serde_json::from_str(&fixed).unwrap();
        assert_eq!(parsed["context"], "nowplayingposition");
        assert_eq!(parsed["data"], "status");
    }

    #[test]
    fn leaves_quoted_data_untouched() {
        let s = r#"{"context":"ping","data":""}"#;
        assert_eq!(sanitize_ios_data_bareword(s), s);
    }

    #[test]
    fn leaves_numeric_data_untouched() {
        let s = r#"{"context":"protocol","data":4}"#;
        assert_eq!(sanitize_ios_data_bareword(s), s);
    }

    #[test]
    fn leaves_object_data_untouched() {
        let s = r#"{"context":"q","data":{"a":1}}"#;
        assert_eq!(sanitize_ios_data_bareword(s), s);
    }

    #[test]
    fn leaves_null_data_untouched() {
        let s = r#"{"context":"q","data":null}"#;
        assert_eq!(sanitize_ios_data_bareword(s), s);
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
