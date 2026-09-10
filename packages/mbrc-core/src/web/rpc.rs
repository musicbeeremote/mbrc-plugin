//! `POST /api/v6/{op}` - the V6 op catalog over HTTP.
//!
//! The body is the V6 request `data` and the response is the V6 response `data`,
//! so a caller who knows `docs/protocol-v6.md` already knows this API. The
//! handler adds routing and a status code; the op itself runs through the same
//! dispatch the socket path uses, which is why the two can never drift.

use axum::Json;
use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::response::{IntoResponse, Response};
use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use crate::server::commands_v6::{self, V6Error};
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
        ErrorCode::Unauthorized | ErrorCode::InvalidToken => StatusCode::UNAUTHORIZED,
        ErrorCode::NotAllowed => StatusCode::FORBIDDEN,
        ErrorCode::UnknownOp | ErrorCode::NotFound => StatusCode::NOT_FOUND,
        ErrorCode::StaleList => StatusCode::CONFLICT,
        ErrorCode::Unavailable => StatusCode::SERVICE_UNAVAILABLE,
        ErrorCode::Internal => StatusCode::INTERNAL_SERVER_ERROR,
    }
}

fn error_response(err: &V6Error) -> Response {
    let mut body = json!({ "code": err.code.as_str(), "message": err.message });
    if let Some(field) = &err.field {
        body["field"] = Value::String(field.clone());
    }
    (status_for(err.code), Json(json!({ "error": body }))).into_response()
}

pub async fn call(
    State(state): State<WebState>,
    Path(op): Path<String>,
    body: Option<Json<Value>>,
) -> Response {
    if SESSION_ONLY_OPS.contains(&op.as_str()) {
        return error_response(&V6Error::new(
            ErrorCode::UnknownOp,
            format!("`{op}` is a session op and is only available over the WebSocket transport"),
        ));
    }

    let data = body.map(|Json(v)| v).unwrap_or_else(|| json!({}));
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
        Ok(Ok(Ok(value))) => (StatusCode::OK, Json(value)).into_response(),
        Ok(Ok(Err(err))) => error_response(&err),
        Ok(Err(op)) => error_response(&V6Error::new(
            ErrorCode::UnknownOp,
            format!("unknown op `{op}`"),
        )),
        Err(e) => error_response(&V6Error::new(ErrorCode::Internal, e.to_string())),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_error_code_maps_to_its_class_of_status() {
        assert_eq!(status_for(ErrorCode::InvalidField), StatusCode::BAD_REQUEST);
        assert_eq!(
            status_for(ErrorCode::Unauthorized),
            StatusCode::UNAUTHORIZED
        );
        assert_eq!(status_for(ErrorCode::NotAllowed), StatusCode::FORBIDDEN);
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
            ErrorCode::NotAllowed,
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
