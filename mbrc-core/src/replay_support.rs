//! Hidden test support for the golden-trace replay harness. Re-exports
//! the internals the harness needs (connection handler, `AppState`
//! constructor, mock callbacks) without leaking them into the crate's
//! documented public API.
//!
//! Not covered by semver — may change at any time.
#![doc(hidden)]

use std::ffi::c_int;
use std::ptr;

pub use crate::ffi::callbacks::SafeCallbacks;
pub use crate::ffi::types::{MbrcCallbacks, QueryType};
pub use crate::server::legacy::connection::handle_connection;
pub use crate::state::AppState;
pub use crate::server::PlayerStateResponse;

extern "C" fn nop_thin() -> c_int {
    0
}

extern "C" fn nop_thin_int(_: c_int) -> c_int {
    0
}

/// Query dispatcher with seeded PlayerState so the legacy `playerstatus`
/// handler can produce a response under replay. Other query types still
/// return empty (they'll get seeded in subsequent passes as W3 handlers
/// land and need them).
extern "C" fn seeded_query(
    ty: i32,
    _buf: *const u8,
    _len: u32,
    out_buf: *mut *mut u8,
    out_len: *mut u32,
) -> i32 {
    let bytes: Option<Vec<u8>> = match ty {
        ty if ty == QueryType::PlayerState as i32 => {
            let ps = PlayerStateResponse {
                play_state: "Playing".into(),
                volume: 72,
                mute: false,
                shuffle: "shuffle".into(),
                repeat: "All".into(),
                position: 12345,
                scrobble: false,
            };
            rmp_serde::to_vec_named(&ps).ok()
        }
        _ => None,
    };

    match bytes {
        Some(v) => {
            let mut boxed = v.into_boxed_slice();
            let len = boxed.len() as u32;
            let ptr = boxed.as_mut_ptr();
            std::mem::forget(boxed);
            unsafe {
                *out_buf = ptr;
                *out_len = len;
            }
            0
        }
        None => {
            unsafe {
                *out_buf = ptr::null_mut();
                *out_len = 0;
            }
            0
        }
    }
}

extern "C" fn nop_fat(
    _ty: i32,
    _buf: *const u8,
    _len: u32,
    out_buf: *mut *mut u8,
    out_len: *mut u32,
) -> i32 {
    unsafe {
        *out_buf = ptr::null_mut();
        *out_len = 0;
    }
    0
}

/// Free memory that was allocated via `Box::into_raw` in `seeded_query`.
extern "C" fn free_buffer(buf: *mut u8) {
    if !buf.is_null() {
        // SAFETY: pointer came from Box::into_raw of a slice originally.
        // We don't know the exact length here, but Rust's allocator for
        // Box<[u8]> tracks it via the slice's fat pointer — which means
        // we can't safely free with just a thin pointer. The legitimate
        // solution in the FFI is that the host calls free_buffer with
        // the original allocation; for the test, leaking is tolerable.
        // Production callbacks will provide a real free.
        let _ = buf;
    }
}

/// Seeded callbacks for golden-trace replay. Thin ops succeed silently;
/// query dispatcher returns canned `PlayerStateResponse` for now, empty
/// for everything else (those handlers stay silent until seeded).
pub fn nop_callbacks() -> SafeCallbacks {
    SafeCallbacks::new(MbrcCallbacks {
        player_play_pause: Some(nop_thin),
        player_stop: Some(nop_thin),
        player_next: Some(nop_thin),
        player_previous: Some(nop_thin),
        player_set_volume: Some(nop_thin_int),
        player_set_position: Some(nop_thin_int),
        query_data: Some(seeded_query),
        execute_command: Some(nop_fat),
        free_buffer: Some(free_buffer),
    })
}
