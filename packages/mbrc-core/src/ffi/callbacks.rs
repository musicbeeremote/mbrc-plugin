//! Safe wrapper around the raw C function pointers in `MbrcCallbacks`.
//!
//! Null-checks the callbacks and does MessagePack serialization/deserialization
//! for the `query_data` / `execute_command` fat callbacks. All methods are safe
//! to call from any thread.
//!
//! The typed, per-`QueryType`/`CommandType` wrappers live in the `Providers`
//! layer (`crate::providers`) so this stays a thin, generic boundary.

use serde::Serialize;
use serde::de::DeserializeOwned;

use crate::ffi::types::{CommandType, HostEventType, MbrcCallbacks, QueryType};

pub struct SafeCallbacks {
    raw: MbrcCallbacks,
}

// SAFETY: the raw callbacks are function pointers, which are just addresses, so
// the wrapper carries nothing that is unsafe to move between threads.
unsafe impl Send for SafeCallbacks {}
// SAFETY: as above, and there is no interior mutability to share.
unsafe impl Sync for SafeCallbacks {}

impl SafeCallbacks {
    pub fn new(raw: MbrcCallbacks) -> Self {
        Self { raw }
    }

    // ── Fat callbacks (MessagePack) ──────────────────────────────────

    /// Runs a query via `query_data`: serialize `params` to a named-map
    /// MessagePack payload, hand it to C#, copy and deserialize the reply.
    ///
    /// # Errors
    /// The callback table is missing an entry, the parameters fail to serialize,
    /// the host returned a non-zero status, or it reported success with no payload.
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

        // Named map: `to_vec` would write a fixarray the C# side cannot read.
        let params_buf = rmp_serde::to_vec_named(params)
            .map_err(|e| format!("failed to serialize query params: {e}"))?;

        let mut result_buf: *mut u8 = std::ptr::null_mut();
        let mut result_len: u32 = 0;

        // Off unless `mbrc_core::ffi::timing=trace` is in the filter.
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
            if !result_buf.is_null() {
                free_fn(result_buf);
            }
            return Err(format!(
                "query_data: C# provider returned error status {status}"
            ));
        }
        if result_buf.is_null() {
            return Err("query_data: success status but null result buffer".to_string());
        }
        if result_len == 0 {
            // Freed anyway: `AllocHGlobal(0)` is still non-null on Windows.
            free_fn(result_buf);
            return Err("query_data: success status but empty result buffer".to_string());
        }

        // SAFETY: the pointer is null-checked above and the contract says it covers that
        // many readable bytes.
        let result_slice = unsafe { std::slice::from_raw_parts(result_buf, result_len as usize) };
        let result_vec = result_slice.to_vec();
        free_fn(result_buf);

        rmp_serde::from_slice(&result_vec)
            .map_err(|e| format!("failed to deserialize query result: {e}"))
    }

    /// A query that takes no parameters (sends an empty msgpack payload).
    ///
    /// # Errors
    /// As [`SafeCallbacks::query`], minus the parameter serialization.
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

    /// Pushes a core -> host event via `on_event` (one-way). No-op when the host
    /// registered no `on_event` callback. Safe to call from any thread; the C#
    /// side marshals to its UI thread. `payload` is an optional MessagePack
    /// buffer (empty = "the host should re-query").
    pub fn emit_event(&self, event_type: HostEventType, payload: &[u8]) {
        if let Some(on_event) = self.raw.on_event {
            on_event(event_type as i32, payload.as_ptr(), payload.len() as u32);
        }
    }
}
