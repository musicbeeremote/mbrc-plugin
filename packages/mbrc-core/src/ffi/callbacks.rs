//! Safe wrapper around the raw C function pointers in `MbrcCallbacks`.
//!
//! Null-checks the callbacks and does MessagePack serialization/deserialization
//! for the `query_data` / `execute_command` fat callbacks. All methods are safe
//! to call from any thread.
//!
//! The typed, per-`QueryType`/`CommandType` wrappers live in the `Providers`
//! layer (`crate::providers`) so this stays a thin, generic boundary.

use serde::de::DeserializeOwned;
use serde::Serialize;

use crate::ffi::types::{CommandType, HostEventType, MbrcCallbacks, QueryType};

pub struct SafeCallbacks {
    raw: MbrcCallbacks,
}

// The raw callbacks are Send + Sync (function pointers are just addresses),
// so the wrapper is too.
unsafe impl Send for SafeCallbacks {}
unsafe impl Sync for SafeCallbacks {}

impl SafeCallbacks {
    pub fn new(raw: MbrcCallbacks) -> Self {
        Self { raw }
    }

    // ── Fat callbacks (MessagePack) ──────────────────────────────────

    /// Run a query via `query_data`: serialize `params` to a named-map
    /// MessagePack payload, hand it to C#, copy and deserialize the reply.
    pub fn query<P: Serialize, R: DeserializeOwned>(
        &self,
        query_type: QueryType,
        params: &P,
    ) -> Result<R, String> {
        let query_fn = self
            .raw
            .query_data
            .ok_or_else(|| "query_data callback is null".to_string())?;
        let free_fn = self
            .raw
            .free_buffer
            .ok_or_else(|| "free_buffer callback is null".to_string())?;

        // Named map, NOT positional array: C#'s ContractlessStandardResolver
        // reads structs as name-keyed maps. Plain `to_vec` writes arrays and
        // fails on the C# read with "Unexpected msgpack code 147 (fixarray)".
        let params_buf = rmp_serde::to_vec_named(params)
            .map_err(|e| format!("failed to serialize query params: {e}"))?;

        let mut result_buf: *mut u8 = std::ptr::null_mut();
        let mut result_len: u32 = 0;

        // Perf tracing: time only the FFI crossing (the C# + MusicBee work);
        // Rust-side (de)serialization is negligible. Off by default - logs at
        // `trace` on the `mbrc_core::ffi::timing` target, which the Debug
        // fallback filter (`mbrc_core=debug`) does NOT enable. Turn it on for a
        // measurement pass by adding `mbrc_core::ffi::timing=trace` to the
        // filter in `logging::init`.
        let start = std::time::Instant::now();
        let status = query_fn(
            query_type as i32,
            params_buf.as_ptr(),
            params_buf.len() as u32,
            &mut result_buf,
            &mut result_len,
        );
        tracing::trace!(
            target: "mbrc_core::ffi::timing",
            query = ?query_type,
            ms = start.elapsed().as_secs_f64() * 1000.0,
            bytes = result_len,
            "query_data",
        );

        if status != 0 {
            // Per the contract, a non-zero status means the C# provider threw.
            if !result_buf.is_null() {
                free_fn(result_buf);
            }
            return Err(format!(
                "query_data: C# provider returned error status {status}"
            ));
        }
        if result_buf.is_null() {
            // Contract violation: success must carry a buffer.
            return Err("query_data: success status but null result buffer".to_string());
        }
        if result_len == 0 {
            // Contract violation: success must carry a non-empty payload (domain
            // "not found" is encoded inside the payload). Free it so the C#
            // allocation (AllocHGlobal(0) is non-null on Windows) does not leak.
            free_fn(result_buf);
            return Err("query_data: success status but empty result buffer".to_string());
        }

        // Copy the C#-owned buffer into Rust memory, then free it via C#.
        let result_slice = unsafe { std::slice::from_raw_parts(result_buf, result_len as usize) };
        let result_vec = result_slice.to_vec();
        free_fn(result_buf);

        rmp_serde::from_slice(&result_vec)
            .map_err(|e| format!("failed to deserialize query result: {e}"))
    }

    /// A query that takes no parameters (sends an empty msgpack payload).
    pub fn query_no_params<R: DeserializeOwned>(&self, query_type: QueryType) -> Result<R, String> {
        self.query(query_type, &())
    }

    /// Fire-and-forget command via `execute_command` (one-way). A non-zero
    /// status means the C# provider threw; there is no result buffer.
    pub fn execute_command<P: Serialize>(
        &self,
        command_type: CommandType,
        params: &P,
    ) -> Result<(), String> {
        let exec_fn = self
            .raw
            .execute_command
            .ok_or_else(|| "execute_command callback is null".to_string())?;

        let params_buf = rmp_serde::to_vec_named(params)
            .map_err(|e| format!("failed to serialize command params: {e}"))?;

        // Perf tracing: see the note in `query`. Same gated `timing` target.
        let start = std::time::Instant::now();
        let status = exec_fn(
            command_type as i32,
            params_buf.as_ptr(),
            params_buf.len() as u32,
        );
        tracing::trace!(
            target: "mbrc_core::ffi::timing",
            command = ?command_type,
            ms = start.elapsed().as_secs_f64() * 1000.0,
            "execute_command",
        );

        if status != 0 {
            return Err(format!(
                "execute_command: C# provider returned error status {status}"
            ));
        }
        Ok(())
    }

    /// Push a core -> host event via `on_event` (one-way). No-op when the host
    /// registered no `on_event` callback. Safe to call from any thread; the C#
    /// side marshals to its UI thread. `payload` is an optional MessagePack
    /// buffer (empty = "the host should re-query").
    pub fn emit_event(&self, event_type: HostEventType, payload: &[u8]) {
        if let Some(on_event) = self.raw.on_event {
            on_event(event_type as i32, payload.as_ptr(), payload.len() as u32);
        }
    }
}

#[cfg(test)]
mod ffi_boundary_smoke {
    //! Guards the C#<->Rust MessagePack contract a pure serde round-trip CANNOT:
    //! query params must serialize as a NAMED MAP (`to_vec_named`), not a positional
    //! array (`to_vec`) - C#'s `ContractlessStandardResolver` reads structs as
    //! name-keyed maps and throws "Unexpected msgpack code 147 (fixarray)" on an
    //! array. A fake C# `query_data` here rejects anything that is not a map, so a
    //! `to_vec` regression fails this test instead of only failing live.

    use super::*;
    use crate::ffi::types::{MbrcCallbacks, QueryType};
    use serde::{Deserialize, Serialize};

    #[derive(Serialize)]
    struct Params {
        offset: i32,
        limit: i32,
    }

    #[derive(Serialize, Deserialize, Debug, PartialEq)]
    struct Reply {
        ok: bool,
        n: i32,
    }

    /// True when the msgpack payload's first byte is a map marker (fixmap / map16 /
    /// map32) - i.e. it was serialized with `to_vec_named`, as C# requires.
    fn is_msgpack_map(bytes: &[u8]) -> bool {
        matches!(bytes.first(), Some(b) if (0x80..=0x8f).contains(b) || *b == 0xde || *b == 0xdf)
    }

    /// Fake C# `query_data`: reject non-map params (status 1, as C# would throw),
    /// else return a canned `Reply`. The result buffer intentionally leaks - a thin
    /// `*mut u8` carries no length to reclaim (mirroring C#'s opaque allocator); it
    /// is a few bytes in a test.
    extern "C" fn fake_query(
        _qt: i32,
        params: *const u8,
        params_len: u32,
        out_buf: *mut *mut u8,
        out_len: *mut u32,
    ) -> i32 {
        let slice = unsafe { std::slice::from_raw_parts(params, params_len as usize) };
        if !is_msgpack_map(slice) {
            return 1;
        }
        let mut reply = rmp_serde::to_vec_named(&Reply { ok: true, n: 7 }).unwrap();
        reply.shrink_to_fit();
        let len = reply.len() as u32;
        let ptr = reply.as_mut_ptr();
        std::mem::forget(reply);
        unsafe {
            *out_buf = ptr;
            *out_len = len;
        }
        0
    }

    extern "C" fn fake_free(_ptr: *mut u8) {
        // No-op: see fake_query (the tiny test buffer leaks by design).
    }

    fn callbacks() -> SafeCallbacks {
        SafeCallbacks::new(MbrcCallbacks {
            query_data: Some(fake_query),
            execute_command: None,
            free_buffer: Some(fake_free),
            on_event: None,
        })
    }

    #[test]
    fn query_params_cross_the_boundary_as_a_map() {
        // If `query` ever used `to_vec`, the standin C# rejects the array and this
        // is Err - catching the exact regression that is invisible to serde tests.
        let reply: Reply = callbacks()
            .query(
                QueryType::PlayerState,
                &Params {
                    offset: 0,
                    limit: 10,
                },
            )
            .expect("map params accepted + reply round-tripped");
        assert_eq!(reply, Reply { ok: true, n: 7 });
    }

    #[test]
    fn to_vec_would_regress_to_an_array() {
        // Document the landmine: to_vec_named is a map; to_vec is an array (the C#
        // break). `query` must only ever emit the former.
        let named = rmp_serde::to_vec_named(&Params {
            offset: 0,
            limit: 10,
        })
        .unwrap();
        let positional = rmp_serde::to_vec(&Params {
            offset: 0,
            limit: 10,
        })
        .unwrap();
        assert!(is_msgpack_map(&named), "to_vec_named must be a map");
        assert!(!is_msgpack_map(&positional), "to_vec is an array");
    }
}
