//! `mbrc-core` - the Rust core for the MusicBee Remote plugin.
//!
//! Owns all networking, the V4 wire protocol, and server state; reaches back
//! into the C# plugin (for MusicBee data) through the frozen C ABI in
//! [`ffi::types`]. This file holds the `#[no_mangle]` exports C# calls; the
//! bodies are wired up slice by slice (Slice 0 = FFI boundary only).

pub mod config;
pub mod cover;
pub mod discovery;
pub mod ffi;
pub mod logging;
pub mod metadata_cache;
pub mod nowplaying;
pub mod protocol;
pub mod providers;
pub mod server;
pub mod state;
pub mod store;
pub mod wire;

use std::ffi::{c_char, c_int, CStr, CString};
use std::sync::Arc;

use crate::ffi::callbacks::SafeCallbacks;
use crate::ffi::types::{
    HostCommandType, HostQueryType, MbrcCallbacks, MbrcResult, NotificationType, MBRC_ABI_VERSION,
};
use crate::providers::{FfiProviders, Providers};

/// Read a C string pointer into an owned `String`; null becomes empty.
///
/// # Safety
/// `ptr` must be null or point to a valid NUL-terminated C string.
unsafe fn cstr(ptr: *const c_char) -> String {
    if ptr.is_null() {
        String::new()
    } else {
        unsafe { CStr::from_ptr(ptr) }
            .to_string_lossy()
            .into_owned()
    }
}

/// Run an FFI export body, catching any Rust panic and turning it into an error
/// code instead of unwinding across the C ABI (undefined behavior) or aborting
/// the MusicBee host process. Effective when the build unwinds; a no-op under
/// `panic = "abort"`, but the guard is written once so it works either way.
fn ffi_guard(export: &'static str, body: impl FnOnce() -> c_int) -> c_int {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(body)) {
        Ok(code) => code,
        Err(_) => {
            tracing::error!(export, "panic caught at the FFI boundary");
            MbrcResult::RuntimeError as c_int
        }
    }
}

/// Initialize the core with the C# callback table and a storage directory.
/// Call exactly once. The callback struct is copied; the caller may free it.
///
/// `abi_version` is the contract version C# was compiled against; it must equal
/// [`MBRC_ABI_VERSION`] or init is rejected (guards against a stale
/// `mbrc_core.dll` beside a newer shim). C# should run degraded on rejection.
///
/// # Safety
/// `storage_path` must be a valid, NUL-terminated C string (or null).
#[no_mangle]
pub unsafe extern "C" fn mbrc_initialize(
    abi_version: c_int,
    callbacks: MbrcCallbacks,
    storage_path: *const c_char,
) -> c_int {
    ffi_guard("mbrc_initialize", move || {
        if abi_version != MBRC_ABI_VERSION {
            tracing::error!(
                got = abi_version,
                expected = MBRC_ABI_VERSION,
                "FFI ABI version mismatch; refusing to initialize"
            );
            return MbrcResult::AbiVersionMismatch as c_int;
        }
        if storage_path.is_null() {
            return MbrcResult::NullPointer as c_int;
        }
        let storage_path = unsafe { cstr(storage_path) };
        logging::init(&storage_path);

        // Rust owns settings: if only the legacy C# settings.xml exists, migrate
        // it to core_settings.json before loading (one-time, best-effort).
        config::migrate_legacy_settings(&storage_path);
        let config = config::Config::load(&storage_path);
        let providers: Arc<dyn Providers> =
            Arc::new(FfiProviders::new(SafeCallbacks::new(callbacks)));
        state::initialize(providers, config) as c_int
    })
}

/// Stop networking and release the core (a later `mbrc_initialize` may re-init).
#[no_mangle]
pub extern "C" fn mbrc_shutdown() -> c_int {
    ffi_guard("mbrc_shutdown", || state::shutdown() as c_int)
}

/// Start the TCP command server + UDP discovery responder.
#[no_mangle]
pub extern "C" fn mbrc_start_networking() -> c_int {
    ffi_guard("mbrc_start_networking", || {
        state::start_networking() as c_int
    })
}

/// Gracefully stop networking (leaves the core initialized).
#[no_mangle]
pub extern "C" fn mbrc_stop_networking() -> c_int {
    ffi_guard("mbrc_stop_networking", || state::stop_networking() as c_int)
}

/// Read the core's current settings as a MessagePack buffer for the settings
/// panel. Writes the byte length to `out_len` and returns a Rust-owned pointer
/// that C# must release via [`mbrc_free_bytes`]. Returns null (and leaves
/// `out_len` at 0) if the core is not initialized or on error.
///
/// # Safety
/// `out_len` must be null or point to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn mbrc_read_settings(out_len: *mut u32) -> *mut u8 {
    if !out_len.is_null() {
        unsafe { *out_len = 0 };
    }
    let bytes = match std::panic::catch_unwind(state::read_settings_bytes) {
        Ok(Some(bytes)) => bytes,
        _ => return std::ptr::null_mut(),
    };
    // Hand ownership to C# as a boxed slice (cap == len), reclaimed by
    // mbrc_free_bytes. into_boxed_slice makes the from_raw_parts in free exact.
    let mut boxed = bytes.into_boxed_slice();
    let ptr = boxed.as_mut_ptr();
    let len = boxed.len() as u32;
    std::mem::forget(boxed);
    if !out_len.is_null() {
        unsafe { *out_len = len };
    }
    ptr
}

/// Validate and persist new settings from a MessagePack buffer (Rust owns
/// `core_settings.json`, written as JSON). Does NOT reload the running core; the
/// host re-inits when the change needs it. On invalid input the write is refused
/// and an error status returned.
///
/// # Safety
/// `buf` must be null or point to `len` readable bytes.
#[no_mangle]
pub unsafe extern "C" fn mbrc_write_settings(buf: *const u8, len: u32) -> c_int {
    ffi_guard("mbrc_write_settings", || {
        if buf.is_null() {
            return MbrcResult::NullPointer as c_int;
        }
        let bytes = unsafe { std::slice::from_raw_parts(buf, len as usize) };
        match state::write_settings_bytes(bytes) {
            Ok(()) => MbrcResult::Ok as c_int,
            Err(e) => {
                tracing::error!(error = %e, "mbrc_write_settings failed");
                MbrcResult::InvalidArgument as c_int
            }
        }
    })
}

/// Generic host -> core query (request/response). Dispatches on the
/// [`HostQueryType`] discriminant and returns a Rust-owned MessagePack buffer
/// (released via [`mbrc_free_bytes`]) with its byte length in `out_len`. Returns
/// null (and leaves `out_len` at 0) on an unknown query, a not-initialized core,
/// or a handler with no answer. Params are an optional MessagePack buffer.
///
/// # Safety
/// `params_buf` must be null or point to `params_len` readable bytes; `out_len`
/// must be null or point to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn mbrc_query(
    query_type: c_int,
    params_buf: *const u8,
    params_len: u32,
    out_len: *mut u32,
) -> *mut u8 {
    if !out_len.is_null() {
        unsafe { *out_len = 0 };
    }
    let Some(kind) = HostQueryType::from_i32(query_type) else {
        return std::ptr::null_mut();
    };
    let params: &[u8] = if params_buf.is_null() {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(params_buf, params_len as usize) }
    };
    let call = std::panic::AssertUnwindSafe(|| state::host_query(kind, params));
    let bytes = match std::panic::catch_unwind(call) {
        Ok(Some(bytes)) => bytes,
        _ => return std::ptr::null_mut(),
    };
    // Hand ownership to C# as a boxed slice (cap == len), reclaimed by
    // mbrc_free_bytes.
    let mut boxed = bytes.into_boxed_slice();
    let ptr = boxed.as_mut_ptr();
    let len = boxed.len() as u32;
    std::mem::forget(boxed);
    if !out_len.is_null() {
        unsafe { *out_len = len };
    }
    ptr
}

/// Generic host -> core command (fire-and-forget). Dispatches on the
/// [`HostCommandType`] discriminant and returns a status code (`0` = success).
/// Params are an optional MessagePack buffer.
///
/// # Safety
/// `params_buf` must be null or point to `params_len` readable bytes.
#[no_mangle]
pub unsafe extern "C" fn mbrc_command(
    command_type: c_int,
    params_buf: *const u8,
    params_len: u32,
) -> c_int {
    ffi_guard("mbrc_command", || {
        let Some(kind) = HostCommandType::from_i32(command_type) else {
            return MbrcResult::InvalidArgument as c_int;
        };
        let params: &[u8] = if params_buf.is_null() {
            &[]
        } else {
            unsafe { std::slice::from_raw_parts(params_buf, params_len as usize) }
        };
        state::host_command(kind, params) as c_int
    })
}

/// Free a byte buffer returned by [`mbrc_read_settings`]. `len` must be the
/// length that call reported. A null pointer is ignored; never free twice.
///
/// # Safety
/// `ptr`/`len` must come from `mbrc_read_settings` and be freed exactly once.
#[no_mangle]
pub unsafe extern "C" fn mbrc_free_bytes(ptr: *mut u8, len: u32) {
    if ptr.is_null() {
        return;
    }
    unsafe {
        drop(Vec::from_raw_parts(ptr, len as usize, len as usize));
    }
}

/// Forward a MusicBee notification. Carries an optional MessagePack payload
/// (`params_buf`/`params_len`, e.g. the added/changed file URL) so the fan-out
/// in Slice 3 can build the right broadcast. Empty payload = null/0.
#[no_mangle]
pub extern "C" fn mbrc_handle_notification(
    notification_type: c_int,
    _params_buf: *const u8,
    _params_len: u32,
) -> c_int {
    ffi_guard("mbrc_handle_notification", || {
        match NotificationType::from_i32(notification_type) {
            // The V4 broadcast set re-queries current state, so the optional
            // params payload is unused here (reserved for future targeted
            // notifications like tags-changed).
            Some(notification) => state::handle_notification(notification) as c_int,
            None => MbrcResult::InvalidArgument as c_int,
        }
    })
}

/// Emit a log line from C# through the core's logger. `level`: 0=trace..4=error.
///
/// # Safety
/// `target` and `message` must be null or valid NUL-terminated C strings.
#[no_mangle]
pub unsafe extern "C" fn mbrc_log(
    level: c_int,
    target: *const c_char,
    message: *const c_char,
) -> c_int {
    ffi_guard("mbrc_log", || {
        let target = unsafe { cstr(target) };
        let message = unsafe { cstr(message) };
        logging::log(level, &target, &message);
        MbrcResult::Ok as c_int
    })
}

/// Set the log-filter directive (e.g. `"mbrc_core=debug,info"`).
///
/// # Safety
/// `directive` must be null or a valid NUL-terminated C string.
#[no_mangle]
pub unsafe extern "C" fn mbrc_set_log_level(directive: *const c_char) -> c_int {
    ffi_guard("mbrc_set_log_level", || {
        let directive = unsafe { cstr(directive) };
        match logging::set_level(&directive) {
            Ok(()) => MbrcResult::Ok as c_int,
            Err(_) => MbrcResult::InvalidArgument as c_int,
        }
    })
}

/// Free a string previously returned to C# by the core. Null-safe. This is the
/// only Rust-owned allocation the C# side frees through us.
///
/// # Safety
/// `ptr` must be null or a pointer previously returned by this core (from
/// `CString::into_raw`); it must not be freed twice.
#[no_mangle]
pub unsafe extern "C" fn mbrc_free_string(ptr: *mut c_char) {
    // Void return, so guard inline rather than via `ffi_guard`.
    let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if ptr.is_null() {
            return;
        }
        unsafe {
            let _ = CString::from_raw(ptr);
        }
    }));
}

#[cfg(test)]
mod ffi_smoke_tests {
    //! End-to-end FFI boundary proof (before any protocol work): a mock C#
    //! callback table serves the two fat callbacks, and we verify the MessagePack
    //! round-trip both directions plus matched alloc/free.

    use super::*;
    use crate::ffi::dtos::{PaginationParams, SetIntParams};
    use crate::ffi::types::{CommandType, QueryType};
    use crate::protocol::messages::PlaybackPositionResponse;
    use crate::providers::Providers;
    use std::alloc::{alloc, dealloc, Layout};
    use std::cell::RefCell;
    use std::collections::HashMap;

    // A matched allocator standing in for C#'s AllocHGlobal/FreeHGlobal. It is
    // thread-local so parallel tests do not see each other's in-flight buffers
    // (each query allocs + frees on the calling test's thread).
    thread_local! {
        static ALLOCS: RefCell<HashMap<usize, Layout>> = RefCell::new(HashMap::new());
    }
    fn c_alloc(data: &[u8]) -> (*mut u8, u32) {
        let layout = Layout::from_size_align(data.len().max(1), 1).unwrap();
        let ptr = unsafe { alloc(layout) };
        unsafe {
            std::ptr::copy_nonoverlapping(data.as_ptr(), ptr, data.len());
        }
        ALLOCS.with(|m| m.borrow_mut().insert(ptr as usize, layout));
        (ptr, data.len() as u32)
    }
    extern "C" fn c_free(ptr: *mut u8) {
        if ptr.is_null() {
            return;
        }
        ALLOCS.with(|m| {
            if let Some(layout) = m.borrow_mut().remove(&(ptr as usize)) {
                unsafe { dealloc(ptr, layout) };
            }
        });
    }
    fn outstanding_allocs() -> usize {
        ALLOCS.with(|m| m.borrow().len())
    }

    // Records the commands the mock C# side received (command_type + any int
    // param), thread-local so parallel tests stay isolated.
    thread_local! {
        static COMMANDS: RefCell<Vec<(i32, Option<i32>)>> = const { RefCell::new(Vec::new()) };
    }

    extern "C" fn mock_query(
        query_type: i32,
        params_buf: *const u8,
        params_len: u32,
        out_result_buf: *mut *mut u8,
        out_result_len: *mut u32,
    ) -> i32 {
        let response: Vec<u8> = if query_type == QueryType::PlaybackPosition as i32 {
            // no-params query: prove the empty-payload path + result decode
            rmp_serde::to_vec_named(&PlaybackPositionResponse {
                current: 1000,
                total: 5000,
            })
            .unwrap()
        } else if query_type == QueryType::NowPlayingList as i32 {
            // with-params query: prove Rust->C# named-map params decode on C#'s side
            let slice = unsafe { std::slice::from_raw_parts(params_buf, params_len as usize) };
            let p: PaginationParams = rmp_serde::from_slice(slice).unwrap();
            rmp_serde::to_vec_named(&PlaybackPositionResponse {
                current: p.offset,
                total: p.limit,
            })
            .unwrap()
        } else if query_type == QueryType::Lyrics as i32 {
            // Non-null but zero-length result: exercises the leak-free error path.
            let (ptr, _len) = c_alloc(&[]);
            unsafe {
                *out_result_buf = ptr;
                *out_result_len = 0;
            }
            return 0;
        } else {
            return -1;
        };
        let (ptr, len) = c_alloc(&response);
        unsafe {
            *out_result_buf = ptr;
            *out_result_len = len;
        }
        0
    }
    extern "C" fn mock_exec(command_type: i32, params_buf: *const u8, params_len: u32) -> i32 {
        // The int-valued commands (SetVolume/SetPosition) carry a SetIntParams;
        // capture the value to prove the param encoding survives the round-trip.
        let value = if command_type == CommandType::SetVolume as i32
            || command_type == CommandType::SetPosition as i32
        {
            let slice = unsafe { std::slice::from_raw_parts(params_buf, params_len as usize) };
            rmp_serde::from_slice::<SetIntParams>(slice)
                .ok()
                .map(|p| p.value)
        } else {
            None
        };
        COMMANDS.with(|c| c.borrow_mut().push((command_type, value)));
        0
    }

    fn mock_callbacks() -> MbrcCallbacks {
        MbrcCallbacks {
            query_data: Some(mock_query),
            execute_command: Some(mock_exec),
            free_buffer: Some(c_free),
            on_event: None,
        }
    }

    #[test]
    fn player_actions_route_through_execute_command() {
        let providers = FfiProviders::new(SafeCallbacks::new(mock_callbacks()));
        COMMANDS.with(|c| c.borrow_mut().clear());
        providers.play_pause().unwrap();
        providers.play_pause().unwrap();
        providers.set_volume(73).unwrap();
        let seen = COMMANDS.with(|c| c.borrow().clone());
        assert_eq!(
            seen,
            vec![
                (CommandType::PlayPause as i32, None),
                (CommandType::PlayPause as i32, None),
                (CommandType::SetVolume as i32, Some(73)),
            ]
        );
    }

    #[test]
    fn fat_query_no_params_roundtrips_and_frees() {
        let providers = FfiProviders::new(SafeCallbacks::new(mock_callbacks()));
        let before = outstanding_allocs();
        let pos = providers.playback_position().unwrap();
        assert_eq!(
            pos,
            PlaybackPositionResponse {
                current: 1000,
                total: 5000
            }
        );
        // The C#-allocated result buffer was freed via the free_buffer callback.
        assert_eq!(outstanding_allocs(), before, "result buffer leaked");
    }

    #[test]
    fn fat_query_with_params_roundtrips_the_named_map() {
        // Proves the Rust->C# param encoding (named map, not fixarray) decodes
        // on the mock C# side, and the reply decodes back on ours.
        let cb = SafeCallbacks::new(mock_callbacks());
        let reply: PlaybackPositionResponse = cb
            .query(
                QueryType::NowPlayingList,
                &PaginationParams {
                    offset: 7,
                    limit: 42,
                },
            )
            .unwrap();
        assert_eq!(
            reply,
            PlaybackPositionResponse {
                current: 7,
                total: 42
            }
        );
        assert_eq!(outstanding_allocs(), 0, "result buffer leaked");
    }

    #[test]
    fn empty_result_buffer_is_freed_not_leaked() {
        // A non-null, zero-length result buffer must be freed on the error path.
        let cb = SafeCallbacks::new(mock_callbacks());
        let before = outstanding_allocs();
        let result: Result<PlaybackPositionResponse, String> =
            cb.query_no_params(QueryType::Lyrics);
        assert!(result.is_err());
        assert_eq!(outstanding_allocs(), before, "empty result buffer leaked");
    }

    #[test]
    fn abi_version_mismatch_is_rejected_before_init() {
        // A wrong abi_version is rejected up front (before any callback/storage
        // use), so passing null everything is safe and no global state is set.
        let null_cbs = MbrcCallbacks {
            query_data: None,
            execute_command: None,
            free_buffer: None,
            on_event: None,
        };
        let code = unsafe { mbrc_initialize(MBRC_ABI_VERSION + 1, null_cbs, std::ptr::null()) };
        assert_eq!(code, MbrcResult::AbiVersionMismatch as c_int);
    }

    #[test]
    fn emit_event_forwards_to_host_on_event() {
        use crate::ffi::types::HostEventType;
        use std::sync::atomic::{AtomicI32, Ordering};

        static LAST_EVENT: AtomicI32 = AtomicI32::new(-1);
        extern "C" fn record(event_type: i32, _payload: *const u8, _len: u32) {
            LAST_EVENT.store(event_type, Ordering::SeqCst);
        }

        let cbs = MbrcCallbacks {
            query_data: None,
            execute_command: None,
            free_buffer: None,
            on_event: Some(record),
        };
        SafeCallbacks::new(cbs).emit_event(HostEventType::CacheStatusChanged, &[]);
        assert_eq!(
            LAST_EVENT.load(Ordering::SeqCst),
            HostEventType::CacheStatusChanged as i32
        );

        // A null on_event is a harmless no-op (the host wants no events).
        let none = MbrcCallbacks {
            query_data: None,
            execute_command: None,
            free_buffer: None,
            on_event: None,
        };
        SafeCallbacks::new(none).emit_event(HostEventType::CacheStatusChanged, &[]);
    }

    #[test]
    fn ffi_guard_passes_through_and_catches_panics() {
        assert_eq!(ffi_guard("ok", || 42), 42);
        // Silence the default panic hook so the expected panic does not spam
        // the test output, then confirm it is translated to an error code.
        let prev = std::panic::take_hook();
        std::panic::set_hook(Box::new(|_| {}));
        let code = ffi_guard("boom", || panic!("expected"));
        std::panic::set_hook(prev);
        assert_eq!(code, MbrcResult::RuntimeError as c_int);
    }
}
