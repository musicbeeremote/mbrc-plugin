//! The TCP connection the client subcommands open to the plugin.

use tokio::net::TcpStream;
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};

/// Connects with Nagle off, so each frame goes out as soon as it is written.
pub async fn connect(host: &str, port: u16) -> std::io::Result<(OwnedReadHalf, OwnedWriteHalf)> {
    let stream = TcpStream::connect((host, port)).await?;
    stream.set_nodelay(true).ok();
    Ok(stream.into_split())
}
