//! `POST /api/v6/{op}` - the V6 op catalog over HTTP.
//!
//! The body is the V6 request `data` and the response is the V6 response `data`,
//! so a caller who knows `docs/protocol-v6.md` already knows this API. The
//! handler adds routing and a status code; the op itself runs through the same
//! dispatch the socket path uses, which is why the two can never drift.

use axum::Json;
use axum::body::Bytes;
use axum::extract::rejection::BytesRejection;
use axum::extract::{Path, State};
use axum::http::{HeaderMap, StatusCode, header};
use axum::response::{IntoResponse, Response};
use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use crate::server::commands_v6::{self, V6Error};
use crate::server::permissions;
use crate::web::router::WebState;

/// Ops the session state machine answers itself. They are meaningless without a
/// connection to hold the state, so HTTP does not offer them.
const SESSION_ONLY_OPS: &[&str] = &["handshake", "ping"];

/// The HTTP status carrying a V6 error code.
///
/// A client that understands the V6 codes needs no status at all, but a browser,
/// a proxy and `curl` all do, so the mapping has to be deliberate rather than a
/// blanket 400.
fn status_for(code: ErrorCode) -> StatusCode {
    match code {
        ErrorCode::MalformedFrame
        | ErrorCode::MissingField
        | ErrorCode::InvalidField
        | ErrorCode::UnsupportedVersion => StatusCode::BAD_REQUEST,
        ErrorCode::Unauthorized | ErrorCode::PairingRequired | ErrorCode::InvalidToken => {
            StatusCode::UNAUTHORIZED
        }
        ErrorCode::NotAllowed | ErrorCode::Forbidden => StatusCode::FORBIDDEN,
        ErrorCode::UnknownOp | ErrorCode::NotFound => StatusCode::NOT_FOUND,
        ErrorCode::StaleList | ErrorCode::StaleTrack => StatusCode::CONFLICT,
        ErrorCode::Unavailable => StatusCode::SERVICE_UNAVAILABLE,
        ErrorCode::Internal => StatusCode::INTERNAL_SERVER_ERROR,
    }
}

fn error_response(err: &V6Error) -> Response {
    error_response_with(status_for(err.code), err)
}

fn error_response_with(status: StatusCode, err: &V6Error) -> Response {
    let mut body = json!({ "code": err.code.as_str(), "message": err.message });
    if let Some(field) = &err.field {
        body["field"] = Value::String(field.clone());
    }
    (status, Json(json!({ "error": body }))).into_response()
}

/// Whether a `Content-Type` value is JSON, parameters and case aside.
fn is_json(content_type: &str) -> bool {
    let essence = content_type
        .split(';')
        .next()
        .unwrap_or_default()
        .trim()
        .to_ascii_lowercase();
    essence == "application/json" || essence.ends_with("+json")
}

/// The op's `data` from a request body, or the failure in the V6 error shape.
///
/// A JSON `Content-Type` is mandatory, even on an empty body. It is what forces a
/// browser to send a CORS preflight, so a cross-site form or `fetch` cannot fire an
/// op that needs no arguments, such as `player_stop`. An empty body then means `{}`.
fn request_data(headers: &HeaderMap, body: &[u8]) -> Result<Value, (StatusCode, V6Error)> {
    let json_declared = headers
        .get(header::CONTENT_TYPE)
        .and_then(|value| value.to_str().ok())
        .is_some_and(is_json);
    if !json_declared {
        return Err((
            StatusCode::UNSUPPORTED_MEDIA_TYPE,
            V6Error::new(
                ErrorCode::MalformedFrame,
                "`Content-Type: application/json` is required",
            ),
        ));
    }
    if body.is_empty() {
        return Ok(json!({}));
    }
    serde_json::from_slice(body).map_err(|e| {
        (
            StatusCode::BAD_REQUEST,
            V6Error::new(
                ErrorCode::MalformedFrame,
                format!("the body is not JSON: {e}"),
            ),
        )
    })
}

pub async fn call(
    State(state): State<WebState>,
    Path(op): Path<String>,
    headers: HeaderMap,
    body: Result<Bytes, BytesRejection>,
) -> Response {
    if SESSION_ONLY_OPS.contains(&op.as_str()) {
        return error_response(&V6Error::new(
            ErrorCode::UnknownOp,
            format!("`{op}` is a session op and is only available over the WebSocket transport"),
        ));
    }

    if op == "pair" {
        return error_response(&V6Error::new(
            ErrorCode::UnknownOp,
            "a browser pairs through `POST /api/pair`; `pair` is for V6 apps on a socket",
        ));
    }

    let body = match body {
        Ok(body) => body,
        Err(rejection) => {
            return error_response_with(
                rejection.status(),
                &V6Error::new(ErrorCode::MalformedFrame, rejection.body_text()),
            );
        }
    };
    let data = match request_data(&headers, &body) {
        Ok(data) => data,
        Err((status, refused)) => return error_response_with(status, &refused),
    };
    let party_mode = state.core.party_mode.is_enabled();
    let principal =
        (party_mode || crate::power::OPS.contains(&op.as_str())).then(|| state.principal(&headers));
    if let Some(principal) = principal.as_ref().filter(|_| party_mode) {
        let action = permissions::v6::action(&op, &data);
        if let Err(refusal) = state.core.party_mode.check(principal, &op, action, "http") {
            return error_response(&V6Error::new(ErrorCode::Forbidden, refusal.message));
        }
    }
    if let Some(answer) = principal.as_ref().and_then(|principal| {
        let may_stop = || {
            let playback = permissions::Action::Change(permissions::Capability::Playback);
            (state.core.party_mode)
                .check(principal, &op, Some(playback), "http")
                .map_err(|refusal| refusal.message)
        };
        state.core.power.handle(principal, &op, &data, &may_stop)
    }) {
        return respond(answer);
    }
    let core = state.core.clone();
    let outcome = tokio::task::spawn_blocking(move || {
        commands_v6::dispatch(
            &op,
            &data,
            core.providers.as_ref(),
            Some(&core.now_playing),
            Some(core.cover_store.as_ref()),
            Some(core.metadata_cache.as_ref()),
        )
        .ok_or(op)
    })
    .await;

    match outcome {
        Ok(Ok(answer)) => respond(answer),
        Ok(Err(op)) => error_response(&V6Error::new(
            ErrorCode::UnknownOp,
            format!("unknown op `{op}`"),
        )),
        Err(e) => error_response(&V6Error::new(ErrorCode::Internal, e.to_string())),
    }
}

/// An op's answer as the HTTP response.
fn respond(answer: Result<Value, V6Error>) -> Response {
    match answer {
        Ok(value) => (StatusCode::OK, Json(value)).into_response(),
        Err(err) => error_response(&err),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn json_content_types_are_recognized_with_parameters_and_any_case() {
        assert!(is_json("application/json"));
        assert!(is_json("Application/JSON; charset=utf-8"));
        assert!(is_json("application/problem+json"));
    }

    /// These three are what a browser may send cross-site with no preflight.
    #[test]
    fn a_preflight_free_content_type_is_not_json() {
        assert!(!is_json("text/plain"));
        assert!(!is_json("application/x-www-form-urlencoded"));
        assert!(!is_json("multipart/form-data; boundary=x"));
    }

    #[test]
    fn every_error_code_maps_to_its_class_of_status() {
        assert_eq!(status_for(ErrorCode::InvalidField), StatusCode::BAD_REQUEST);
        assert_eq!(
            status_for(ErrorCode::Unauthorized),
            StatusCode::UNAUTHORIZED
        );
        assert_eq!(status_for(ErrorCode::NotAllowed), StatusCode::FORBIDDEN);
        assert_eq!(status_for(ErrorCode::Forbidden), StatusCode::FORBIDDEN);
        assert_eq!(status_for(ErrorCode::NotFound), StatusCode::NOT_FOUND);
        assert_eq!(status_for(ErrorCode::StaleList), StatusCode::CONFLICT);
        assert_eq!(
            status_for(ErrorCode::Unavailable),
            StatusCode::SERVICE_UNAVAILABLE
        );
        assert_eq!(
            status_for(ErrorCode::Internal),
            StatusCode::INTERNAL_SERVER_ERROR
        );
    }

    #[test]
    fn no_error_code_maps_to_a_success_status() {
        for code in [
            ErrorCode::MalformedFrame,
            ErrorCode::UnsupportedVersion,
            ErrorCode::MissingField,
            ErrorCode::InvalidField,
            ErrorCode::UnknownOp,
            ErrorCode::Unauthorized,
            ErrorCode::PairingRequired,
            ErrorCode::NotAllowed,
            ErrorCode::Forbidden,
            ErrorCode::InvalidToken,
            ErrorCode::StaleList,
            ErrorCode::Internal,
            ErrorCode::NotFound,
            ErrorCode::Unavailable,
        ] {
            assert!(status_for(code).is_client_error() || status_for(code).is_server_error());
        }
    }
}
