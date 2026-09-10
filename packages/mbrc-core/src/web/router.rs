//! Route table and the per-connection HTTP server.

use std::net::SocketAddr;
use std::sync::Arc;

use axum::Router;
use axum::extract::{Request, State};
use axum::http::{HeaderName, HeaderValue, StatusCode, header};
use axum::middleware::Next;
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post};
use axum::{Json, middleware};
use hyper::server::conn::http1;
use hyper_util::rt::TokioIo;
use hyper_util::service::TowerToHyperService;
use serde_json::{Value, json};
use tokio::net::TcpStream;
use tower_http::set_header::SetResponseHeaderLayer;

use crate::state::Core;
use crate::web::{assets, cover, events, origin, rpc, ws};

/// Per-request context handed to every handler.
#[derive(Clone)]
pub struct WebState {
    pub core: Arc<Core>,
    pub peer: SocketAddr,
}

impl WebState {
    /// Whether a request carrying `token` may proceed.
    ///
    /// The gate is one early return rather than a separate route table, so the
    /// enforced and unenforced configurations run the same code.
    pub fn admits(&self, token: Option<&str>) -> bool {
        if !self.core.config.web_auth_required {
            return true;
        }
        token.is_some_and(|token| self.core.pairing.is_paired(token))
    }

    /// Whether a request may proceed, from whichever place its token rides in.
    ///
    /// The cookie is what a browser sends without being asked, on an `<img>` and
    /// a socket alike, which is why nothing has to put the token in a URL. The
    /// header is for a caller that builds its own requests, and the query is
    /// what a browser paired by an older build still has.
    pub fn admits_request(&self, headers: &axum::http::HeaderMap, query: Option<&str>) -> bool {
        if !self.core.config.web_auth_required {
            return true;
        }
        let cookie = cookie_token(headers);
        self.admits(cookie.as_deref()) || self.admits(bearer(headers)) || self.admits(query)
    }
}

/// The name the pairing token is stored under in the browser.
const TOKEN_COOKIE: &str = "mbrc_token";

/// How long a browser stays paired without being used. Long, because the whole
/// point is pairing a phone once.
const COOKIE_MAX_AGE_SECS: i64 = 365 * 24 * 60 * 60;

/// Reads the pairing token out of a `Cookie` header.
fn cookie_token(headers: &axum::http::HeaderMap) -> Option<String> {
    let raw = headers.get(header::COOKIE)?.to_str().ok()?;
    raw.split(';')
        .filter_map(|pair| pair.split_once('='))
        .find(|(name, _)| name.trim() == TOKEN_COOKIE)
        .map(|(_, value)| value.trim().to_string())
}

/// The cookie a paired browser is given.
///
/// `HttpOnly` because no script needs to read it and a stored one must not be
/// able to: it is the difference between a credential a page holds and one the
/// browser holds on its behalf. `SameSite=Strict` because every request that
/// should carry it starts on this origin. No `Secure`, which would stop it
/// being sent at all over the plain HTTP a LAN address is served on.
fn token_cookie(token: &str) -> String {
    format!(
        "{TOKEN_COOKIE}={token}; HttpOnly; SameSite=Strict; Path=/; Max-Age={COOKIE_MAX_AGE_SECS}"
    )
}

/// Restrictive policy for a page that loads only its own embedded assets and
/// talks only to its own origin. `connect-src` admits `ws:` because the live half
/// of the UI is a WebSocket back to this same host.
const CSP: &str = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; \
                   connect-src 'self' ws: wss:; object-src 'none'; base-uri 'none'";

/// The routes that require a paired token in a header.
///
/// The pairing endpoints and the bundle are deliberately outside it: a browser
/// that cannot load the page has no way to reach the screen asking for the code.
/// So are the three routes a browser reaches without a request of its own to
/// attach a header to - `/ws`, the event stream and the covers - which check the
/// same token from the query string themselves.
fn guarded_routes(state: &WebState) -> Router<WebState> {
    Router::new()
        .route("/api/v6/capabilities", get(capabilities))
        .route("/api/v6/{op}", post(rpc::call))
        .route_layer(middleware::from_fn_with_state(state.clone(), gate))
}

fn app(state: WebState) -> Router {
    Router::new()
        .merge(guarded_routes(&state))
        // Registered before the `{hash}` route it would otherwise look like.
        .route("/api/cover/now-playing", get(cover::now_playing))
        .route("/api/cover/{hash}", get(cover::get))
        .route("/api/pair", post(pair))
        .route("/api/pair/status", get(pair_status))
        .route("/api/events", get(events::stream))
        .route("/ws", get(ws::upgrade))
        .fallback(assets::serve)
        .layer(middleware::from_fn(origin::guard))
        .layer(SetResponseHeaderLayer::overriding(
            header::CONTENT_SECURITY_POLICY,
            HeaderValue::from_static(CSP),
        ))
        .layer(SetResponseHeaderLayer::overriding(
            HeaderName::from_static("x-content-type-options"),
            HeaderValue::from_static("nosniff"),
        ))
        .layer(SetResponseHeaderLayer::overriding(
            header::REFERRER_POLICY,
            HeaderValue::from_static("no-referrer"),
        ))
        .with_state(state)
}

/// Rejects a request with no paired token, when pairing is enforced.
async fn gate(State(state): State<WebState>, request: Request, next: Next) -> Response {
    if !state.admits_request(request.headers(), None) {
        return unauthorized();
    }
    next.run(request).await
}

/// The one shape an unpaired caller ever sees, on HTTP and on the socket alike.
pub fn unauthorized() -> Response {
    (
        StatusCode::UNAUTHORIZED,
        Json(json!({ "error": { "code": "unauthorized", "message": "pair this browser first" } })),
    )
        .into_response()
}

async fn capabilities() -> Json<Value> {
    Json(crate::server::commands_v6::capabilities())
}

/// Tells an unpaired browser whether it needs to pair at all, so the UI can skip
/// the pairing screen entirely in the default configuration.
async fn pair_status(State(state): State<WebState>, headers: axum::http::HeaderMap) -> Json<Value> {
    // `paired` is about this requester, not the server: the token is in a cookie
    // the page cannot read, so asking is the only way a browser can know.
    Json(json!({
        "auth_required": state.core.config.web_auth_required,
        "paired": state.admits_request(&headers, None),
    }))
}

async fn pair(State(state): State<WebState>, Json(body): Json<Value>) -> Response {
    let code = body.get("code").and_then(Value::as_str).unwrap_or_default();
    let label = body
        .get("label")
        .and_then(Value::as_str)
        .unwrap_or("browser");

    match state.core.pairing.redeem(code, label) {
        Some(token) => (
            [(header::SET_COOKIE, token_cookie(&token))],
            Json(json!({ "token": token })),
        )
            .into_response(),
        None => (
            StatusCode::UNAUTHORIZED,
            Json(json!({ "error": { "code": "unauthorized", "message": "pairing code is wrong or expired" } })),
        )
            .into_response(),
    }
}

/// Serves one already-accepted connection as HTTP/1.1.
///
/// `with_upgrades` is what lets a `/ws` request leave HTTP behind and become a
/// WebSocket on the same socket. HTTP/1.1 only: there is no TLS here, so no
/// browser will ever negotiate h2, and excluding it keeps `h2` out of the DLL.
pub async fn serve(stream: TcpStream, peer: SocketAddr, core: Arc<Core>) -> std::io::Result<()> {
    let service = TowerToHyperService::new(app(WebState { core, peer }));

    if let Err(e) = http1::Builder::new()
        .serve_connection(TokioIo::new(stream), service)
        .with_upgrades()
        .await
    {
        tracing::debug!(%peer, error = %e, "http connection ended with error");
    }
    Ok(())
}

/// Reads the bearer token off a request's `Authorization` header.
pub fn bearer(headers: &axum::http::HeaderMap) -> Option<&str> {
    headers
        .get(header::AUTHORIZATION)?
        .to_str()
        .ok()?
        .strip_prefix("Bearer ")
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::http::HeaderMap;

    #[test]
    fn a_bearer_header_yields_its_token() {
        let mut headers = HeaderMap::new();
        headers.insert(header::AUTHORIZATION, "Bearer abc123".parse().unwrap());
        assert_eq!(bearer(&headers), Some("abc123"));
    }

    #[test]
    fn other_authorization_schemes_yield_nothing() {
        let mut headers = HeaderMap::new();
        headers.insert(header::AUTHORIZATION, "Basic abc123".parse().unwrap());
        assert_eq!(bearer(&headers), None);
        assert_eq!(bearer(&HeaderMap::new()), None);
    }
}
