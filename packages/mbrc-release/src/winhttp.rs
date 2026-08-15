//! The production [`HttpClient`]: WinHTTP.
//!
//! Chosen over a Rust HTTP stack for two properties an unattended updater cannot
//! add later (see `docs/updates.md`): the *system* proxy, PAC and WPAD included,
//! which is the only route out of a managed desktop; and the OS root store, which
//! Windows Update keeps current long after this build was cut. A third reason is
//! build tooling - rustls' crypto providers want NASM or CMake for the i686
//! target, and this target is not negotiable.
//!
//! The cost is this file: flat C API, manual handle lifetimes, `GetLastError`
//! mapping, and nothing here can run on a non-Windows host. That cost is bounded
//! by the [`HttpClient`] seam - everything that makes a decision sits above it and
//! is tested against a stub.
//!
//! Two things are deliberate rather than incidental:
//!
//! - **TLS is pinned to 1.2 and 1.3.** Older Windows still negotiates TLS 1.0 by
//!   default, and GitHub refuses it. Left unset, an update check fails on exactly
//!   the machines least likely to be updated by hand.
//! - **Only HTTPS is fetched at all.** The redirect policy refuses an
//!   HTTPS-to-HTTP downgrade, and [`WinHttpClient::get`] refuses a plain-HTTP URL
//!   before a socket is opened. Signature verification does not depend on this,
//!   but there is no reason to let the bytes travel in the clear either.

use std::ffi::c_void;
use std::ptr::{null, null_mut};

use windows_sys::Win32::Foundation::{GetLastError, ERROR_INSUFFICIENT_BUFFER};
use windows_sys::Win32::Networking::WinHttp::{
    WinHttpAddRequestHeaders, WinHttpCloseHandle, WinHttpConnect, WinHttpCrackUrl, WinHttpOpen,
    WinHttpOpenRequest, WinHttpQueryDataAvailable, WinHttpQueryHeaders, WinHttpReadData,
    WinHttpReceiveResponse, WinHttpSendRequest, WinHttpSetOption, WinHttpSetTimeouts,
    URL_COMPONENTS, WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
    WINHTTP_ACCESS_TYPE_NAMED_PROXY, WINHTTP_ADDREQ_FLAG_ADD, WINHTTP_ADDREQ_FLAG_REPLACE,
    WINHTTP_FLAG_SECURE, WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2, WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3,
    WINHTTP_INTERNET_SCHEME_HTTPS, WINHTTP_OPTION_REDIRECT_POLICY,
    WINHTTP_OPTION_REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP, WINHTTP_OPTION_SECURE_PROTOCOLS,
    WINHTTP_QUERY_ETAG, WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_QUERY_STATUS_CODE,
};

use crate::error::{Result, UpdateError};
use crate::http::{HttpClient, HttpResponse};

/// WinHTTP reports a missing header rather than an empty one; asking for an
/// `ETag` that is not there is normal, not a failure.
const ERROR_WINHTTP_HEADER_NOT_FOUND: u32 = 12150;

/// The largest response this client will hold. The biggest thing the updater
/// ever fetches is the release zip, a few megabytes; a cap keeps a hostile or
/// broken server from growing the process until it dies. It is a ceiling, not a
/// budget - nothing legitimate approaches it.
const MAX_BODY: usize = 64 * 1024 * 1024;

/// Read granularity. `WinHttpQueryDataAvailable` reports what is buffered, which
/// is usually less than this; the constant only bounds one read.
const READ_CHUNK: usize = 32 * 1024;

// Milliseconds. Resolve and connect are short because a machine that cannot get
// out should fail and back off rather than hang a background tick; receive is
// generous because it applies per read on a multi-megabyte download.
const RESOLVE_TIMEOUT_MS: i32 = 10_000;
const CONNECT_TIMEOUT_MS: i32 = 15_000;
const SEND_TIMEOUT_MS: i32 = 30_000;
const RECEIVE_TIMEOUT_MS: i32 = 60_000;

/// An owned `HINTERNET`. Every WinHTTP handle in this file is wrapped in one, so
/// the `?` on the next line cannot leak the handle on the line above.
struct Handle(*mut c_void);

impl Handle {
    /// Takes ownership of what a `WinHttp*` constructor returned, or turns its
    /// null into the error it stands for.
    fn new(raw: *mut c_void, op: &str) -> Result<Self> {
        if raw.is_null() {
            Err(last_error(op))
        } else {
            Ok(Self(raw))
        }
    }

    fn as_ptr(&self) -> *mut c_void {
        self.0
    }
}

impl Drop for Handle {
    fn drop(&mut self) {
        // SAFETY: `self.0` is non-null (checked in `new`), came from WinHTTP, and
        // is closed exactly once because `Handle` is not `Clone`.
        unsafe { WinHttpCloseHandle(self.0) };
    }
}

// SAFETY: WinHTTP handles used synchronously are documented as thread-safe, and
// this client never uses the asynchronous API (no callback, no context). The
// session handle is only ever read from `&self`.
unsafe impl Send for Handle {}
unsafe impl Sync for Handle {}

/// A blocking HTTPS client over WinHTTP, holding one session for its lifetime.
///
/// Reusing the session is what makes proxy auto-detection affordable: WPAD
/// discovery and PAC evaluation happen once and are cached by WinHTTP, instead of
/// once per request.
pub struct WinHttpClient {
    session: Handle,
}

impl WinHttpClient {
    /// Opens a session.
    ///
    /// `user_agent` is sent as `User-Agent` on every request; GitHub rejects
    /// requests without one, so it is required rather than defaulted. `proxy` is
    /// the user's override from settings: empty or `None` means auto-detect.
    pub fn new(user_agent: &str, proxy: Option<&str>) -> Result<Self> {
        let agent = wide(user_agent);
        let proxy = proxy.filter(|p| !p.trim().is_empty()).map(wide);

        let session = match &proxy {
            // An explicit override is the user telling us auto-detection got it
            // wrong, so it replaces detection rather than seeding it.
            Some(proxy) => {
                // SAFETY: both strings are null-terminated and outlive the call.
                let raw = unsafe {
                    WinHttpOpen(
                        agent.as_ptr(),
                        WINHTTP_ACCESS_TYPE_NAMED_PROXY,
                        proxy.as_ptr(),
                        null(),
                        0,
                    )
                };
                Handle::new(raw, "WinHttpOpen (named proxy)")?
            }
            None => open_autodetecting_session(&agent)?,
        };

        set_secure_protocols(&session)?;
        set_option(
            &session,
            WINHTTP_OPTION_REDIRECT_POLICY,
            WINHTTP_OPTION_REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP,
        )?;

        // SAFETY: the session handle is live for the duration of the call.
        let ok = unsafe {
            WinHttpSetTimeouts(
                session.as_ptr(),
                RESOLVE_TIMEOUT_MS,
                CONNECT_TIMEOUT_MS,
                SEND_TIMEOUT_MS,
                RECEIVE_TIMEOUT_MS,
            )
        };
        if ok == 0 {
            return Err(last_error("WinHttpSetTimeouts"));
        }

        Ok(Self { session })
    }
}

impl HttpClient for WinHttpClient {
    fn get(&self, url: &str, etag: Option<&str>) -> Result<HttpResponse> {
        let target = Target::parse(url)?;

        // SAFETY: the host string is null-terminated and outlives the call.
        let connection = Handle::new(
            unsafe { WinHttpConnect(self.session.as_ptr(), target.host.as_ptr(), target.port, 0) },
            "WinHttpConnect",
        )?;

        let verb = wide("GET");
        let accept_all = wide("*/*");
        let accept_types = [accept_all.as_ptr(), null()];
        // SAFETY: every pointer is null-terminated and outlives the call;
        // `accept_types` is the required null-terminated array of pointers.
        let request = Handle::new(
            unsafe {
                WinHttpOpenRequest(
                    connection.as_ptr(),
                    verb.as_ptr(),
                    target.path.as_ptr(),
                    null(),
                    null(),
                    accept_types.as_ptr(),
                    WINHTTP_FLAG_SECURE,
                )
            },
            "WinHttpOpenRequest",
        )?;

        if let Some(etag) = etag {
            add_header(&request, &format!("If-None-Match: {etag}"))?;
        }

        // SAFETY: no additional headers or body are supplied, so every pointer
        // is null and every length zero, as WinHTTP requires for that case.
        let ok = unsafe { WinHttpSendRequest(request.as_ptr(), null(), 0, null(), 0, 0, 0) };
        if ok == 0 {
            return Err(last_error("WinHttpSendRequest"));
        }

        // SAFETY: the request handle is live and the reserved argument is null.
        let ok = unsafe { WinHttpReceiveResponse(request.as_ptr(), null_mut()) };
        if ok == 0 {
            return Err(last_error("WinHttpReceiveResponse"));
        }

        Ok(HttpResponse {
            status: status_code(&request)?,
            etag: header(&request, WINHTTP_QUERY_ETAG)?,
            body: read_body(&request)?,
        })
    }
}

/// Opens a session that works out its own proxy, falling back where it cannot.
///
/// `WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY` is the one that understands PAC and
/// WPAD, but it only exists from Windows 8.1 on and fails outright below that.
/// The fallback is the static WinHTTP/IE configuration, which is what a Rust HTTP
/// stack would have given us at best.
fn open_autodetecting_session(agent: &[u16]) -> Result<Handle> {
    // SAFETY: `agent` is null-terminated; the proxy arguments are unused for
    // these access types and so are null.
    let raw = unsafe {
        WinHttpOpen(
            agent.as_ptr(),
            WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
            null(),
            null(),
            0,
        )
    };
    if !raw.is_null() {
        return Handle::new(raw, "WinHttpOpen (automatic proxy)");
    }

    // SAFETY: as above.
    let raw = unsafe {
        WinHttpOpen(
            agent.as_ptr(),
            WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
            null(),
            null(),
            0,
        )
    };
    Handle::new(raw, "WinHttpOpen (default proxy)")
}

/// The TLS versions the session is allowed to negotiate. Not a preference: the
/// two that are left are the two that are still sound.
fn secure_protocols() -> u32 {
    WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2 | WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3
}

/// Restricts the session to sound TLS versions, narrowing rather than failing on
/// a Windows that has never heard of TLS 1.3.
///
/// WinHTTP validates the mask and rejects unknown bits outright with
/// `ERROR_INVALID_PARAMETER`, and the TLS 1.3 bit is unknown before Windows 10
/// 1903. Treating that as fatal would mean the whole updater refuses to start on
/// exactly the older machines the proxy fallback above is there to support - and
/// it would say so as error 87, which tells the user nothing. So the combined
/// mask is attempted first and TLS 1.2 alone is the fallback.
///
/// The one thing that never happens is giving up on the option: unset, the
/// session may offer TLS 1.0, which GitHub refuses.
fn set_secure_protocols(session: &Handle) -> Result<()> {
    if set_option(session, WINHTTP_OPTION_SECURE_PROTOCOLS, secure_protocols()).is_ok() {
        return Ok(());
    }
    set_option(
        session,
        WINHTTP_OPTION_SECURE_PROTOCOLS,
        WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2,
    )
}

/// Sets one `u32`-valued option on a handle.
fn set_option(handle: &Handle, option: u32, value: u32) -> Result<()> {
    // SAFETY: `value` lives across the call and its length is its real size,
    // which is what every option used here expects.
    let ok = unsafe {
        WinHttpSetOption(
            handle.as_ptr(),
            option,
            &value as *const u32 as *const c_void,
            size_of::<u32>() as u32,
        )
    };
    if ok == 0 {
        Err(last_error(&format!("WinHttpSetOption({option})")))
    } else {
        Ok(())
    }
}

/// Adds one `Name: value` header, replacing any the stack supplied itself.
fn add_header(request: &Handle, header: &str) -> Result<()> {
    let header = wide(header);
    // SAFETY: `header` is null-terminated and outlives the call; the length is
    // in characters, and `u32::MAX` tells WinHTTP to measure it itself.
    let ok = unsafe {
        WinHttpAddRequestHeaders(
            request.as_ptr(),
            header.as_ptr(),
            u32::MAX,
            WINHTTP_ADDREQ_FLAG_ADD | WINHTTP_ADDREQ_FLAG_REPLACE,
        )
    };
    if ok == 0 {
        Err(last_error("WinHttpAddRequestHeaders"))
    } else {
        Ok(())
    }
}

/// The response's status line code, as a number rather than text.
fn status_code(request: &Handle) -> Result<u16> {
    let mut status: u32 = 0;
    let mut len = size_of::<u32>() as u32;
    // SAFETY: the buffer is a `u32` and `len` says so; no header index is used.
    let ok = unsafe {
        WinHttpQueryHeaders(
            request.as_ptr(),
            WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            null(),
            &mut status as *mut u32 as *mut c_void,
            &mut len,
            null_mut(),
        )
    };
    if ok == 0 {
        return Err(last_error("WinHttpQueryHeaders(status)"));
    }
    u16::try_from(status)
        .map_err(|_| UpdateError::Network(format!("nonsensical HTTP status {status}")))
}

/// One string-valued response header, or `None` when the server did not send it.
fn header(request: &Handle, info_level: u32) -> Result<Option<String>> {
    let mut bytes: u32 = 0;
    // SAFETY: a null buffer with a zero length is the documented way to ask for
    // the required size; it always "fails", with the size written to `bytes`.
    let ok = unsafe {
        WinHttpQueryHeaders(
            request.as_ptr(),
            info_level,
            null(),
            null_mut(),
            &mut bytes,
            null_mut(),
        )
    };
    if ok != 0 {
        // A header that needs no buffer is an empty one.
        return Ok(None);
    }
    // SAFETY: no pointers are dereferenced; this reads the calling thread's code.
    match unsafe { GetLastError() } {
        ERROR_INSUFFICIENT_BUFFER => {}
        ERROR_WINHTTP_HEADER_NOT_FOUND => return Ok(None),
        _ => return Err(last_error("WinHttpQueryHeaders(header)")),
    }

    // The size is in bytes and includes the terminator.
    let mut buffer = vec![0u16; (bytes as usize).div_ceil(2)];
    // SAFETY: the buffer is `bytes` long, which is the size WinHTTP just asked
    // for, and `bytes` is passed unchanged.
    let ok = unsafe {
        WinHttpQueryHeaders(
            request.as_ptr(),
            info_level,
            null(),
            buffer.as_mut_ptr() as *mut c_void,
            &mut bytes,
            null_mut(),
        )
    };
    if ok == 0 {
        return Err(last_error("WinHttpQueryHeaders(header)"));
    }

    let chars = (bytes as usize) / 2;
    let value = String::from_utf16_lossy(&buffer[..chars.min(buffer.len())]);
    let value = sanitize(value.trim_end_matches('\0'));
    Ok(if value.is_empty() { None } else { Some(value) })
}

/// The most a header value may be before it is treated as junk. An `ETag` is a
/// short opaque token; anything near this is not one.
const MAX_HEADER_VALUE: usize = 512;

/// Makes a server-supplied header value safe to store and echo back.
///
/// The `ETag` read here is persisted to `update_state.json` and sent out again as
/// `If-None-Match` on the next check. `WinHttpAddRequestHeaders` takes a raw
/// header block, so a value carrying CR or LF would be split into headers of the
/// server's choosing. Reaching that needs a server that emits a folded or
/// malformed value, which GitHub does not - but the fix is a filter, and the
/// alternative is trusting a remote string with the shape of a request.
fn sanitize(value: &str) -> String {
    value
        .chars()
        .filter(|c| !matches!(c, '\r' | '\n' | '\0'))
        .take(MAX_HEADER_VALUE)
        .collect::<String>()
        .trim()
        .to_owned()
}

/// Drains the response body.
fn read_body(request: &Handle) -> Result<Vec<u8>> {
    let mut body = Vec::new();
    loop {
        let mut available: u32 = 0;
        // SAFETY: the request handle is live and `available` outlives the call.
        let ok = unsafe { WinHttpQueryDataAvailable(request.as_ptr(), &mut available) };
        if ok == 0 {
            return Err(last_error("WinHttpQueryDataAvailable"));
        }
        if available == 0 {
            return Ok(body);
        }

        let want = (available as usize).min(READ_CHUNK);
        if body.len() + want > MAX_BODY {
            return Err(UpdateError::Network(format!(
                "response exceeds the {MAX_BODY} byte limit"
            )));
        }

        let start = body.len();
        body.resize(start + want, 0);
        let mut read: u32 = 0;
        // SAFETY: the buffer has `want` bytes free from `start`, which is what
        // is passed as the length.
        let ok = unsafe {
            WinHttpReadData(
                request.as_ptr(),
                body[start..].as_mut_ptr() as *mut c_void,
                want as u32,
                &mut read,
            )
        };
        if ok == 0 {
            return Err(last_error("WinHttpReadData"));
        }
        body.truncate(start + read as usize);
        // A zero-length read after a non-zero `available` is the end of the
        // response; without this the loop would spin.
        if read == 0 {
            return Ok(body);
        }
    }
}

/// A URL split into the three pieces WinHTTP wants separately.
#[cfg_attr(test, derive(Debug))]
struct Target {
    host: Vec<u16>,
    port: u16,
    /// Path and query together, which is what `WinHttpOpenRequest` calls the
    /// object name.
    path: Vec<u16>,
}

impl Target {
    fn parse(url: &str) -> Result<Self> {
        let wide_url = wide(url);
        let mut components: URL_COMPONENTS = unsafe { std::mem::zeroed() };
        components.dwStructSize = size_of::<URL_COMPONENTS>() as u32;
        // A null pointer with a non-zero length asks WinHTTP for a pointer into
        // `wide_url` rather than a copy, which is why `wide_url` must outlive it.
        components.dwHostNameLength = u32::MAX;
        components.dwUrlPathLength = u32::MAX;
        components.dwExtraInfoLength = u32::MAX;

        // SAFETY: `wide_url` is null-terminated (hence the zero length) and
        // outlives every pointer WinHTTP writes into `components`.
        let ok = unsafe { WinHttpCrackUrl(wide_url.as_ptr(), 0, 0, &mut components) };
        if ok == 0 {
            return Err(UpdateError::Network(format!("{url} is not a valid URL")));
        }

        // Refused here rather than trusted to the redirect policy: that only
        // covers a downgrade *during* a request, not a plain-HTTP URL to start
        // with. Every URL the updater fetches comes from GitHub over TLS.
        if components.nScheme != WINHTTP_INTERNET_SCHEME_HTTPS {
            return Err(UpdateError::Network(format!(
                "refusing a non-HTTPS URL: {url}"
            )));
        }

        // SAFETY: both pointers came from `WinHttpCrackUrl` and point into
        // `wide_url` with the lengths it reported.
        let host = unsafe { slice(components.lpszHostName, components.dwHostNameLength) };
        // SAFETY: as above.
        let url_path = unsafe { slice(components.lpszUrlPath, components.dwUrlPathLength) };
        // SAFETY: as above.
        let query = unsafe { slice(components.lpszExtraInfo, components.dwExtraInfoLength) };

        // Taken as two pieces and rejoined rather than as one slice spanning
        // both: `https://host?x=1` cracks to an empty path and a non-empty query,
        // and a slice of the pair would hand WinHttpOpenRequest an object name
        // starting at `?`, which is not a request target. The root has to be put
        // back explicitly.
        let mut object = Vec::with_capacity(url_path.len() + query.len() + 1);
        if url_path.is_empty() {
            object.push(u16::from(b'/'));
        } else {
            object.extend_from_slice(url_path);
        }
        object.extend_from_slice(query);

        Ok(Self {
            host: null_terminated(host),
            port: components.nPort,
            path: null_terminated(&object),
        })
    }
}

/// Borrows `len` UTF-16 units from a pointer WinHTTP handed back.
///
/// # Safety
///
/// `ptr` must be valid for `len` units, or null when `len` is zero.
unsafe fn slice<'a>(ptr: *const u16, len: u32) -> &'a [u16] {
    if ptr.is_null() || len == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(ptr, len as usize) }
    }
}

/// A null-terminated copy of a UTF-16 slice.
fn null_terminated(units: &[u16]) -> Vec<u16> {
    let mut out = Vec::with_capacity(units.len() + 1);
    out.extend_from_slice(units);
    out.push(0);
    out
}

/// A Rust string as a null-terminated wide string.
fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Turns the thread's last error into a [`UpdateError::Network`], naming the call
/// that failed. WinHTTP's own codes are translated where the difference matters
/// to whoever reads the log: "the proxy refused us" and "the certificate is not
/// trusted" call for very different responses from a user.
fn last_error(op: &str) -> UpdateError {
    // SAFETY: no pointers are involved; this reads the calling thread's code.
    let code = unsafe { GetLastError() };
    UpdateError::Network(format!("{op}: {} ({code})", describe(code)))
}

/// A plain-language description of the WinHTTP error codes an update check can
/// realistically hit. Anything else is reported by number: an exhaustive table
/// would be transcription, and the number is searchable.
fn describe(code: u32) -> &'static str {
    match code {
        12002 => "the request timed out",
        12005 | 12006 => "the URL is not http or https",
        12007 => "the server name could not be resolved",
        12015 | 12016 => "the proxy or server requires authentication",
        12017 => "the request was cancelled",
        12029 => "could not connect to the server",
        12030 | 12031 => "the connection was closed by the server",
        12044 => "the server asked for a client certificate",
        12057 => "the certificate's revocation status could not be checked",
        12103 => "the proxy could not be reached",
        12150 => "the response has no such header",
        12152 => "the server's response could not be parsed",
        12175 => "the TLS handshake failed",
        12180 => "WPAD proxy auto-discovery failed",
        _ => "see the Windows error code",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn text(units: &[u16]) -> String {
        String::from_utf16_lossy(units.strip_suffix(&[0]).unwrap_or(units))
    }

    #[test]
    fn a_url_splits_into_host_port_and_object() {
        let target = Target::parse("https://api.github.com/repos/a/b/releases/latest").unwrap();
        assert_eq!(text(&target.host), "api.github.com");
        assert_eq!(target.port, 443);
        assert_eq!(text(&target.path), "/repos/a/b/releases/latest");
    }

    #[test]
    fn the_query_string_stays_with_the_path() {
        // Asset downloads redirect to a URL whose signature lives in the query,
        // so dropping it turns a 200 into a 403.
        let target =
            Target::parse("https://objects.githubusercontent.com/x/y.zip?token=abc&exp=1").unwrap();
        assert_eq!(text(&target.path), "/x/y.zip?token=abc&exp=1");
    }

    #[test]
    fn a_bare_host_still_asks_for_the_root() {
        let target = Target::parse("https://example.com").unwrap();
        assert_eq!(text(&target.path), "/");
    }

    #[test]
    fn a_query_with_no_path_keeps_the_root_in_front_of_it() {
        // WinHttpCrackUrl reports an empty path here, and an object name that
        // begins at `?` is not a request target.
        let target = Target::parse("https://example.com?x=1").unwrap();
        assert_eq!(text(&target.path), "/?x=1");
    }

    #[test]
    fn an_explicit_port_is_kept() {
        let target = Target::parse("https://example.com:8443/x").unwrap();
        assert_eq!(target.port, 8443);
    }

    #[test]
    fn plain_http_is_refused_before_a_socket_is_opened() {
        let err = Target::parse("http://example.com/x").unwrap_err();
        assert!(matches!(err, UpdateError::Network(m) if m.contains("non-HTTPS")));
    }

    #[test]
    fn nonsense_is_refused() {
        assert!(Target::parse("not a url").is_err());
    }

    #[test]
    fn tls_is_pinned_to_the_two_sound_versions() {
        let protocols = secure_protocols();
        assert_ne!(protocols & WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2, 0);
        assert_ne!(protocols & WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3, 0);
        // The point of setting the option at all: the defaults below TLS 1.2 are
        // what GitHub refuses.
        assert_eq!(protocols & 0x0000_0008, 0, "SSL 2.0 must not be enabled");
        assert_eq!(protocols & 0x0000_0020, 0, "SSL 3.0 must not be enabled");
        assert_eq!(protocols & 0x0000_0080, 0, "TLS 1.0 must not be enabled");
        assert_eq!(protocols & 0x0000_0200, 0, "TLS 1.1 must not be enabled");
        // The fallback for a Windows without TLS 1.3 narrows to 1.2; it must not
        // widen to anything the loop above rules out.
        assert_eq!(WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2, 0x0000_0800);
    }

    #[test]
    fn winhttp_rejects_a_protocol_bit_it_does_not_know() {
        // The premise of the TLS 1.3 fallback: WinHTTP validates this mask rather
        // than ignoring what it cannot use, so a session on a Windows without TLS
        // 1.3 would fail to open unless the narrower mask is tried. If this ever
        // starts passing, the fallback is dead code and can go.
        let client = WinHttpClient::new("mbrc-test/0.0", None).unwrap();
        let unknown = 0x0000_4000;
        assert!(
            set_option(&client.session, WINHTTP_OPTION_SECURE_PROTOCOLS, unknown).is_err(),
            "an unknown protocol bit was accepted"
        );
        // And the mask the fallback uses is one it does know.
        assert!(set_option(
            &client.session,
            WINHTTP_OPTION_SECURE_PROTOCOLS,
            WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2
        )
        .is_ok());
    }

    #[test]
    fn a_header_value_cannot_smuggle_in_a_second_header() {
        // What matters is the CR/LF: the value is echoed back as `If-None-Match`
        // into a raw header block on the next check.
        assert_eq!(
            sanitize("\"abc\"\r\nX-Injected: yes"),
            "\"abc\"X-Injected: yes"
        );
        assert!(!sanitize("\"abc\"\r\nX-Injected: yes").contains('\r'));
        assert_eq!(sanitize("  \"abc\"  "), "\"abc\"");
        assert_eq!(sanitize(&"x".repeat(10_000)).len(), MAX_HEADER_VALUE);
    }

    #[test]
    fn a_session_opens_and_closes() {
        // No network: this covers the option and timeout calls, and the Drop that
        // closes the session.
        WinHttpClient::new("mbrc-test/0.0", None).unwrap();
        WinHttpClient::new("mbrc-test/0.0", Some("  ")).unwrap();
        WinHttpClient::new("mbrc-test/0.0", Some("http://127.0.0.1:9")).unwrap();
    }

    /// The only test that leaves the machine, so it is opt-in:
    /// `cargo test -p mbrc-release --target i686-pc-windows-msvc -- --ignored`.
    /// It is what proves the parts a stub cannot: real TLS against GitHub, the
    /// system proxy, redirects, and `ETag` round-tripping.
    #[test]
    #[ignore = "requires network access"]
    fn a_real_request_to_github_round_trips_its_etag() {
        let client = WinHttpClient::new("mbrc-plugin-test/0.0", None).unwrap();
        let url = "https://api.github.com/repos/musicbeeremote/mbrc-plugin";

        let first = client.get(url, None).unwrap();
        assert_eq!(
            first.status,
            200,
            "{}",
            String::from_utf8_lossy(&first.body)
        );
        assert!(!first.body.is_empty());
        let etag = first.etag.expect("GitHub always sends an ETag");

        let second = client.get(url, Some(&etag)).unwrap();
        assert!(second.is_not_modified(), "status {}", second.status);
        assert!(second.body.is_empty());
    }

    /// The other half a stub cannot cover: a release asset is served from
    /// `github.com` by redirect to a signed `objects.githubusercontent.com` URL,
    /// so this is what proves the cross-host redirect is followed and the query
    /// string survives it. Also opt-in; a small sidecar from a shipped release
    /// stands in for the multi-megabyte zip.
    #[test]
    #[ignore = "requires network access"]
    fn a_release_asset_download_follows_its_redirects() {
        let client = WinHttpClient::new("mbrc-plugin-test/0.0", None).unwrap();
        let response = client
            .get(
                "https://github.com/musicbeeremote/mbrc-plugin/releases/download/\
                 v1.4.1/musicbee_remote_1.4.1.zip.sha512",
                None,
            )
            .unwrap();

        assert_eq!(response.status, 200);
        // Byte-oriented on purpose: what is under test is that the bytes arrived
        // intact through the redirect. This particular sidecar happens to be
        // UTF-16 with a BOM, which is between whoever cut the 2021 release and
        // their shell, and none of the updater's business.
        let hex: Vec<u8> = response
            .body
            .iter()
            .copied()
            .filter(|b| b.is_ascii_hexdigit())
            .collect();
        assert_eq!(hex.len(), 128, "not a sha512 sidecar: {:?}", response.body);
    }
}
