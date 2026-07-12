//! V4 protocol types: response DTOs the handlers produce and the context
//! constants. Response DTOs are named structs with serde renames so the wire
//! shape is defined in one place (never ad-hoc `json!()` in handlers).

pub mod messages;
pub mod version;

/// Client platform, taken from the `player` handshake frame. A few V4 behaviors
/// differ by client and the shipped plugin branches on it: iOS receives the
/// "ordered" now-playing list (and always carries album/album_artist), while
/// Android receives the sequential page (and omits those fields, 1-based
/// indices). It is a Rust-internal concept - the FFI/C# side never sees it.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum Platform {
    Android,
    Ios,
    #[default]
    Unknown,
}

impl Platform {
    /// Map the `player` frame's platform string (`"Android"`/`"iOS"`) to the enum.
    pub fn from_name(name: Option<&str>) -> Self {
        match name.map(str::to_ascii_lowercase).as_deref() {
            Some("android") => Platform::Android,
            Some("ios") => Platform::Ios,
            _ => Platform::Unknown,
        }
    }
}
