//! The V6 per-connection protocol state machine.
//!
//! Pure (no IO) like the legacy [`Session`](super::session::Session), so it is
//! unit-testable without sockets: feed it a wire line, get back the frames to
//! send and whether to close.
//!
//! Contract highlights (all from #118 §2-§5):
//! - First frame must be `op:"handshake"` (`id:0`); it carries `protocol_version`,
//!   a required per-install `client_id`, and a `client_type`.
//! - Any non-handshake op before the handshake -> `unauthorized` + close.
//! - Reject bad *structure* (non-object, missing/mistyped envelope fields, unknown
//!   `kind`/`op`); *ignore* unknown additive keys inside `data`.
//! - Success `{id, kind:"response", data}` XOR failure `{id, kind:"response", error}`.

use std::net::IpAddr;
use std::sync::Arc;

use serde_json::{Value, json};

use mbrc_wire::v6::{self, ClientType, ErrorCode, RequestError};

use super::clients::{ClientIdentities, Identity, Introduction, MAX_CLIENT_ID_LEN};
use super::commands_v6;
use super::permissions::{self, PartyMode, Principal};
use super::session::Outcome;
use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::providers::Providers;
use crate::server_identity::ServerIdentity;
use crate::web::auth::Pairing;

/// Per-connection V6 state, built up at the handshake.
#[derive(Debug, Default)]
pub struct V6Session {
    /// Whether the handshake has completed.
    handshaked: bool,
    /// Required per-install client id (session key + caps grouping).
    client_id: String,
    /// Client identification (metadata only; never forks wire behavior).
    client_type: Option<ClientType>,
    /// If true, this connection does not receive event broadcasts (an auxiliary /
    /// command-only socket). Default false = a main connection.
    no_broadcast: bool,
    /// Inbound frame counter, for wire-log correlation.
    frames_in: u64,
    /// The Party Mode gate every op passes; absent in unit tests.
    party_mode: Option<Arc<PartyMode>>,
    /// Who Party Mode judges this connection as; settled at the handshake
    /// unless the transport already knew.
    principal: Option<Principal>,
    /// The pairing state the `pair` op redeems codes against, and the address
    /// its wrong-code limits are kept for; absent in unit tests.
    pairing: Option<(Arc<Pairing>, IpAddr)>,
    /// The auto-shutdown timer and its permission list (#108).
    power: Option<Arc<crate::power::Power>>,
    /// The permissions this client was last told, in its handshake reply.
    told: Option<Value>,
    /// Who this server is, sent in the handshake reply; absent in unit tests.
    server: Option<ServerIdentity>,
    /// Whether every op but `ping` and `pair` waits for a pairing.
    pairing_required: bool,
    /// Whether the transport settled the principal, as a browser's WebSocket does.
    settled_by_transport: bool,
    /// What the device calls itself, used as the label of a pairing it makes.
    client_name: Option<String>,
    /// Set when `pair` succeeds, until the transport has acted on it.
    newly_paired: bool,
}

/// Log an inbound V6 frame. DEBUG caps list bodies to a sample + schema summary;
/// TRACE keeps the full body. Only one fires (TRACE implies DEBUG), so a busy list
/// frame is never logged twice. This mirrors the legacy session's wire logging on
/// purpose: a diagnostics capture raises `mbrc::wire` to DEBUG, so logging V6 at
/// TRACE would make every V6 request and response invisible in a user's capture
/// while V4 traffic showed up (#188).
fn log_c2s(seq: u64, op: &str, line: &str) {
    if tracing::enabled!(target: "mbrc::wire", tracing::Level::TRACE) {
        tracing::trace!(
            target: "mbrc::wire",
            dir = "c2s",
            proto = "v6",
            seq,
            op,
            bytes = line.len(),
            "{}",
            crate::logging::redact_frame(line, None)
        );
    } else {
        tracing::debug!(
            target: "mbrc::wire",
            dir = "c2s",
            proto = "v6",
            seq,
            op,
            bytes = line.len(),
            "{}",
            crate::logging::redact_frame(line, Some(crate::logging::WIRE_LIST_SAMPLE))
        );
    }
}

/// Log an outbound V6 frame, correlated to the request that produced it. Same
/// DEBUG/TRACE split as [`log_c2s`] - the list-heavy browse responses are exactly
/// the bodies DEBUG must sample.
fn log_s2c(reply_to: u64, frame: &str) {
    if tracing::enabled!(target: "mbrc::wire", tracing::Level::TRACE) {
        tracing::trace!(
            target: "mbrc::wire",
            dir = "s2c",
            proto = "v6",
            reply_to,
            bytes = frame.len(),
            "{}",
            crate::logging::redact_frame(frame, None)
        );
    } else {
        tracing::debug!(
            target: "mbrc::wire",
            dir = "s2c",
            proto = "v6",
            reply_to,
            bytes = frame.len(),
            "{}",
            crate::logging::redact_frame(frame, Some(crate::logging::WIRE_LIST_SAMPLE))
        );
    }
}

/// What stopping playback needs under Party Mode, for the auto-shutdown `stop`.
const PLAYBACK: permissions::Action =
    permissions::Action::Change(permissions::Capability::Playback);

/// An op's answer as the response frame to request `id`.
fn render(id: u64, answer: Result<Value, commands_v6::V6Error>) -> String {
    match answer {
        Ok(data) => v6::response_ok(id, data),
        Err(e) => match &e.field {
            Some(field) => v6::response_error_field(id, e.code, &e.message, field),
            None => v6::response_error(id, e.code, &e.message),
        },
    }
}

impl V6Session {
    /// Attaches the Party Mode gate. Builder-style.
    pub fn with_party_mode(mut self, gate: &Arc<PartyMode>) -> Self {
        self.party_mode = Some(Arc::clone(gate));
        self
    }

    /// Who to watch Party Mode for, and what they were told at the handshake.
    ///
    /// `None` before the handshake, or without a gate. The transport hands this
    /// to [`PartyMode::watch`] for a connection that takes events.
    pub fn permissions_watch(&self) -> Option<(Principal, Value)> {
        Some((self.principal.clone()?, self.told.clone()?))
    }

    /// Lets the `pair` op redeem codes, as offered from `peer`. Builder-style.
    pub fn with_pairing(mut self, pairing: &Arc<Pairing>, peer: IpAddr) -> Self {
        self.pairing = Some((Arc::clone(pairing), peer));
        self
    }

    /// Names this installation in the handshake reply. Builder-style.
    pub fn with_server(mut self, server: &ServerIdentity) -> Self {
        self.server = Some(server.clone());
        self
    }

    /// Attaches the auto-shutdown timer. Builder-style.
    pub fn with_power(mut self, power: &Arc<crate::power::Power>) -> Self {
        self.power = Some(Arc::clone(power));
        self
    }

    /// Fixes who this connection is, overriding what the handshake would settle.
    ///
    /// A browser is known by its pairing token, not by the `client_id` its page
    /// makes up, so the WebSocket settles the principal before any frame arrives.
    pub fn with_principal(mut self, principal: Principal) -> Self {
        self.principal = Some(principal);
        self.settled_by_transport = true;
        self
    }

    /// Holds every op but `ping` and `pair` until this client has paired. Builder-style.
    pub fn requiring_pairing(mut self, required: bool) -> Self {
        self.pairing_required = required;
        self
    }

    fn is_paired(&self) -> bool {
        matches!(self.principal, Some(Principal::Paired(_)))
    }

    /// Whether events reach this connection: never on a `no_broadcast` socket, and not
    /// before pairing where pairing is required, since events describe the player.
    pub fn takes_events(&self) -> bool {
        self.handshaked && !self.no_broadcast && (!self.pairing_required || self.is_paired())
    }

    /// Whether `pair` has succeeded since this was last asked.
    pub fn take_newly_paired(&mut self) -> bool {
        std::mem::take(&mut self.newly_paired)
    }

    /// Whether this connection's pairing was revoked while it was open.
    fn pairing_was_revoked(&self) -> bool {
        match (&self.principal, &self.pairing) {
            (Some(Principal::Paired(id)), Some((pairing, _))) => {
                !self.settled_by_transport && !pairing.holds(id)
            }
            _ => false,
        }
    }

    /// Process one inbound V6 wire line. `providers`/`now_playing` are the same
    /// read/write context the legacy `Session` gets; op handlers use them.
    #[allow(clippy::too_many_arguments)]
    pub fn handle_frame(
        &mut self,
        line: &str,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
        clients: Option<&ClientIdentities>,
    ) -> Outcome {
        let seq = self.frames_in;
        self.frames_in += 1;

        let outcome = match v6::parse_request(line) {
            Ok(req) => {
                log_c2s(seq, &req.op, line);
                self.handle_request(
                    req,
                    providers,
                    now_playing,
                    cover_store,
                    metadata_cache,
                    clients,
                )
            }
            Err(err) => {
                // No op to name on an unparseable frame; unlike V4, which drops
                // it silently, the reply below carries a typed error.
                tracing::debug!(
                    target: "mbrc::wire",
                    dir = "c2s",
                    proto = "v6",
                    seq,
                    bytes = line.len(),
                    parseable = false,
                    handshaken = self.handshaked,
                    "rejecting unparseable frame: {}",
                    crate::logging::redact_frame(line, None)
                );
                self.reject(err)
            }
        };

        // One choke point for the response side: every reply this session produces
        // (op responses, typed errors, the handshake reply) passes through here.
        for f in &outcome.replies {
            log_s2c(seq, f);
        }
        outcome
    }

    /// Whether the handshake has completed (cheap; no allocation).
    pub fn is_handshaked(&self) -> bool {
        self.handshaked
    }

    /// Inbound frame count, for the connection post-mortem log.
    pub fn frames_in(&self) -> u64 {
        self.frames_in
    }

    /// Registration metadata once handshaked, `None` before.
    ///
    /// A main connection unless the client opted out, so it subscribes to the V6
    /// broadcaster; either way it registers for the per-client and per-IP caps
    /// via its required `client_id`.
    pub fn reg_meta(&self) -> Option<super::RegMeta> {
        if !self.handshaked {
            return None;
        }
        Some(super::RegMeta {
            client_id: Some(self.client_id.clone()),
            is_main: !self.no_broadcast,
            platform: self.client_type.map(|c| c.as_str().to_string()),
            protocol: v6::PROTOCOL_VERSION as u8,
        })
    }

    /// A structural rejection from the envelope parser. Pre-handshake structural
    /// errors are fatal (the connection never established); post-handshake ones are
    /// reported but leave the connection open (lenient continuation).
    fn reject(&self, err: RequestError) -> Outcome {
        let message = match &err {
            RequestError::Malformed => "frame is not a JSON object".to_string(),
            RequestError::MissingField { field, .. } => format!("missing required field: {field}"),
            RequestError::InvalidField { field, .. } => format!("invalid field: {field}"),
        };
        let frame = match &err {
            RequestError::Malformed => v6::response_error(err.id(), err.code(), &message),
            RequestError::MissingField { field, .. } | RequestError::InvalidField { field, .. } => {
                v6::response_error_field(err.id(), err.code(), &message, field)
            }
        };
        if self.handshaked {
            Outcome::reply(frame)
        } else {
            Outcome::reply_and_close(frame)
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn handle_request(
        &mut self,
        req: v6::IncomingRequest,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
        clients: Option<&ClientIdentities>,
    ) -> Outcome {
        if req.op == v6::OP_HANDSHAKE {
            if self.handshaked {
                // A repeat handshake is a protocol-state error: the connection
                // stays open but is not re-negotiated (#118 `not_allowed`).
                return Outcome::reply(v6::response_error(
                    req.id,
                    ErrorCode::NotAllowed,
                    "handshake already completed on this connection",
                ));
            }
            return self.handle_handshake(req, clients);
        }

        if !self.handshaked {
            // Any op before the handshake: reject and close so the client
            // re-establishes with a proper handshake first.
            tracing::debug!(op = %req.op, "v6 command before handshake; closing");
            return Outcome::reply_and_close(v6::response_error(
                req.id,
                ErrorCode::Unauthorized,
                "handshake required before any other op",
            ));
        }

        self.dispatch_op(req, providers, now_playing, cover_store, metadata_cache)
    }

    /// Route a post-handshake op: `ping` is built in; everything else goes through
    /// the V6 command catalog. An unrecognized op is `unknown_op`.
    #[allow(clippy::too_many_arguments)]
    fn dispatch_op(
        &mut self,
        req: v6::IncomingRequest,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
    ) -> Outcome {
        if self.pairing_was_revoked() {
            return Outcome::reply_and_close(v6::response_error(
                req.id,
                ErrorCode::PairingRequired,
                "this client was unpaired",
            ));
        }
        // Echo the request data back, proving id-correlated round-trips.
        if req.op == "ping" {
            return Outcome::reply(v6::response_ok(req.id, req.data));
        }
        if req.op == "pair" {
            return Outcome::reply(self.pair(req.id, &req.data));
        }
        if self.pairing_required && !self.is_paired() {
            return Outcome::reply(v6::response_error(
                req.id,
                ErrorCode::PairingRequired,
                "pair this client first, with the code MusicBee shows",
            ));
        }
        if let Some(gate) = &self.party_mode {
            let action = permissions::v6::action(&req.op, &req.data);
            let principal = self.principal.as_ref().unwrap_or(&Principal::Anonymous);
            if let Err(refusal) = gate.check(principal, &req.op, action, &self.client_id) {
                return Outcome::reply(v6::response_error(
                    req.id,
                    ErrorCode::Forbidden,
                    &refusal.message,
                ));
            }
        }
        if let Some(power) = &self.power {
            let principal = self.principal.as_ref().unwrap_or(&Principal::Anonymous);
            let may_stop = || match &self.party_mode {
                Some(gate) => gate
                    .check(principal, &req.op, Some(PLAYBACK), &self.client_id)
                    .map_err(|refusal| refusal.message),
                None => Ok(()),
            };
            if let Some(answer) = power.handle(principal, &req.op, &req.data, &may_stop) {
                return Outcome::reply(render(req.id, answer));
            }
        }
        match commands_v6::dispatch(
            &req.op,
            &req.data,
            providers,
            now_playing,
            cover_store,
            metadata_cache,
        ) {
            Some(answer) => Outcome::reply(render(req.id, answer)),
            None => Outcome::reply(v6::response_error(
                req.id,
                ErrorCode::UnknownOp,
                &format!("unknown op: {}", req.op),
            )),
        }
    }

    /// Validates the three required handshake fields - `protocol_version` exactly
    /// the version we speak, a non-empty `client_id`, a known `client_type` - and
    /// records the negotiated session, or rejects and closes.
    fn handle_handshake(
        &mut self,
        req: v6::IncomingRequest,
        clients: Option<&ClientIdentities>,
    ) -> Outcome {
        let data = &req.data;

        match data.get("protocol_version") {
            None => return self.reject_handshake(ErrorCode::MissingField, "protocol_version"),
            Some(v) => match v.as_u64() {
                Some(n) if n == v6::PROTOCOL_VERSION => {}
                Some(_) => {
                    return self
                        .reject_handshake(ErrorCode::UnsupportedVersion, "protocol_version");
                }
                None => return self.reject_handshake(ErrorCode::InvalidField, "protocol_version"),
            },
        }

        let client_id = match data.get("client_id") {
            None => return self.reject_handshake(ErrorCode::MissingField, "client_id"),
            Some(Value::String(s)) if !s.is_empty() && s.len() <= MAX_CLIENT_ID_LEN => s.clone(),
            Some(_) => return self.reject_handshake(ErrorCode::InvalidField, "client_id"),
        };

        let client_type = match data.get("client_type") {
            None => return self.reject_handshake(ErrorCode::MissingField, "client_type"),
            Some(Value::String(s)) => match ClientType::parse(s) {
                Some(ct) => ct,
                None => return self.reject_handshake(ErrorCode::InvalidField, "client_type"),
            },
            Some(_) => return self.reject_handshake(ErrorCode::InvalidField, "client_type"),
        };

        // Absent or wrong-typed is false - a main connection - per the
        // ignore-unknown-additive-fields policy.
        let no_broadcast = data
            .get("no_broadcast")
            .and_then(Value::as_bool)
            .unwrap_or(false);

        let token = data.get("client_token").and_then(Value::as_str);
        let name = data.get("client_name").and_then(Value::as_str);
        let about = Introduction {
            name,
            client_type: client_type.as_str(),
            address: self.pairing.as_ref().map(|(_, peer)| *peer),
        };
        let issued = match clients.map(|c| c.identify(&client_id, token, &about)) {
            Some(Identity::Refused) => {
                tracing::info!(
                    client_id = %crate::logging::redact_frame(&client_id, None),
                    "v6 handshake refused: client_id held by another installation"
                );
                return self.reject_handshake(ErrorCode::InvalidToken, "client_token");
            }
            Some(Identity::Issued(token)) => Some(token),
            Some(Identity::Known) | None => None,
        };

        if self.principal.is_none() {
            let paired = data
                .get("token")
                .and_then(Value::as_str)
                .zip(self.pairing.as_ref())
                .and_then(|(token, (pairing, peer))| pairing.paired_id(token, Some(*peer)));
            self.principal = Some(paired.map_or(Principal::Anonymous, Principal::Paired));
        }
        self.client_name = name.map(str::to_owned);
        self.handshaked = true;
        self.client_id = client_id;
        self.client_type = Some(client_type);
        self.no_broadcast = no_broadcast;
        tracing::debug!(
            client_type = client_type.as_str(),
            no_broadcast,
            issued_token = issued.is_some(),
            "v6 handshake complete"
        );
        // Advertise the op/event surface so a client can degrade gracefully.
        // Additive (#118 §9 Q5); older clients ignore it.
        let mut reply = json!({ "server_version": v6::PROTOCOL_VERSION });
        if let Some(server) = &self.server {
            if let Some(id) = &server.id {
                reply["server_id"] = json!(id.as_ref());
            }
            reply["server_name"] = json!(server.name.as_ref());
        }
        reply["capabilities"] = commands_v6::capabilities();
        if let Some(token) = issued {
            reply["client_token"] = json!(token);
        }
        reply["paired"] = json!(self.is_paired());
        reply["pairing_required"] = json!(self.pairing_required);
        if let (Some(gate), Some(principal)) = (&self.party_mode, &self.principal) {
            let permissions = gate.permissions(principal);
            reply["permissions"] = permissions.clone();
            self.told = Some(permissions);
        }
        Outcome::reply(v6::response_ok(0, reply))
    }

    /// The `pair` op: redeems a pairing code for this client's token and the code's role.
    ///
    /// Answered before every gate, since pairing is how a client gets past them. A
    /// browser pairs over HTTP instead, where its token can be set as a cookie.
    fn pair(&mut self, id: u64, data: &Value) -> String {
        let code = match data.get("code") {
            None => {
                return v6::response_error_field(
                    id,
                    ErrorCode::MissingField,
                    "missing required field: code",
                    "code",
                );
            }
            Some(Value::String(code)) => code.as_str(),
            Some(_) => {
                return v6::response_error_field(
                    id,
                    ErrorCode::InvalidField,
                    "code must be a string",
                    "code",
                );
            }
        };
        if self.settled_by_transport {
            return v6::response_error(
                id,
                ErrorCode::NotAllowed,
                "a browser pairs through `POST /api/pair`",
            );
        }
        let Some((pairing, peer)) = &self.pairing else {
            return v6::response_error(id, ErrorCode::Unavailable, "pairing is not available here");
        };
        let label = self
            .client_name
            .as_deref()
            .filter(|name| !name.trim().is_empty())
            .or(self.client_type.map(ClientType::as_str))
            .unwrap_or("app");
        let paired = match pairing.redeem(code, label, *peer) {
            Ok(paired) => paired,
            Err(refused) => {
                return v6::response_error(id, ErrorCode::Unauthorized, &refused.message());
            }
        };
        let principal = Principal::Paired(paired.id);
        let mut reply = json!({ "role": paired.role.as_str(), "token": paired.token });
        if let Some(gate) = &self.party_mode {
            gate.assign_role(&principal, paired.role);
            let permissions = gate.permissions(&principal);
            reply["permissions"] = permissions.clone();
            self.told = Some(permissions);
        }
        self.principal = Some(principal);
        self.newly_paired = true;
        v6::response_ok(id, reply)
    }

    /// A handshake validation failure: reply a typed error (echoing id 0) and close,
    /// since the connection never established.
    fn reject_handshake(&self, code: ErrorCode, field: &str) -> Outcome {
        let message = match code {
            ErrorCode::UnsupportedVersion => {
                format!(
                    "unsupported {field}; this server speaks {}",
                    v6::PROTOCOL_VERSION
                )
            }
            ErrorCode::MissingField => format!("missing required field: {field}"),
            ErrorCode::InvalidToken => format!(
                "{field} does not match the one issued for this client_id;                  generate a new client_id and handshake again"
            ),
            _ => format!("invalid field: {field}"),
        };
        tracing::info!(field, code = code.as_str(), "rejecting v6 handshake");
        Outcome::reply_and_close(v6::response_error_field(0, code, &message, field))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::NullProviders;
    use serde_json::Value;

    fn parse(frame: &str) -> Value {
        serde_json::from_str(frame).expect("reply is JSON")
    }

    /// An identity store on a real (temporary) database, since the whole point of
    /// the token is that it outlives the process.
    ///
    /// The directory carries the pid so two concurrent test runs cannot open the
    /// same redb file and fail on its lock.
    fn identities(name: &str) -> crate::test_dir::InTestDir<ClientIdentities> {
        let dir = crate::test_dir::TestDir::new(&format!("session-token-{name}"));
        let identities = ClientIdentities::new(crate::store::Db::open(&dir.to_string_lossy()));
        dir.holding(identities)
    }

    fn handshake_with(s: &mut V6Session, clients: &ClientIdentities, token: Option<&str>) -> Value {
        let data = match token {
            Some(token) => format!(
                r#"{{"protocol_version":6,"client_id":"install-1","client_type":"android","client_token":"{token}"}}"#
            ),
            None => r#"{"protocol_version":6,"client_id":"install-1","client_type":"android"}"#
                .to_string(),
        };
        let line = format!(r#"{{"id":0,"kind":"request","op":"handshake","data":{data}}}"#);
        let out = s.handle_frame(&line, &NullProviders, None, None, None, Some(clients));
        parse(&out.replies[0])
    }

    /// First contact is issued a token; presenting it is what makes the next
    /// handshake the same installation rather than a claim to be it.
    #[test]
    fn the_first_handshake_is_issued_a_token_it_can_return_with() {
        let clients = identities("issue");

        let first = handshake_with(&mut V6Session::default(), &clients, None);
        let token = first["data"]["client_token"]
            .as_str()
            .expect("first contact is issued a token")
            .to_owned();

        let again = handshake_with(&mut V6Session::default(), &clients, Some(&token));
        assert_eq!(again["data"]["server_version"], 6);
        // Only first contact is issued one; a client that has it keeps it.
        assert!(again["data"].get("client_token").is_none());
    }

    /// The restored-backup case, which is what this is really for.
    #[test]
    fn a_second_install_claiming_the_same_id_is_refused() {
        let clients = identities("refuse");
        handshake_with(&mut V6Session::default(), &clients, None);

        let mut clone = V6Session::default();
        let refused = handshake_with(&mut clone, &clients, None);
        assert_eq!(refused["error"]["code"], "invalid_token");
        assert_eq!(refused["error"]["field"], "client_token");
        assert!(!clone.handshaked, "a refused handshake establishes nothing");
    }

    /// Without a store there is nothing to remember a token with, so the check
    /// cannot be enforced - and must not lock every client out instead.
    #[test]
    fn no_identity_store_means_no_token_is_demanded() {
        let mut s = V6Session::default();
        let out = feed(&mut s, GOOD_HANDSHAKE);
        assert!(s.handshaked);
        assert!(parse(&out.replies[0])["data"].get("client_token").is_none());
    }

    /// An id is a key in a durable store and a label in every log line, so its
    /// length is bounded like any other client-supplied value.
    #[test]
    fn an_overlong_client_id_is_an_invalid_field() {
        let mut s = V6Session::default();
        let id = "x".repeat(MAX_CLIENT_ID_LEN + 1);
        let line = format!(
            r#"{{"id":0,"kind":"request","op":"handshake","data":{{"protocol_version":6,"client_id":"{id}","client_type":"android"}}}}"#
        );
        let out = feed(&mut s, &line);
        let v = parse(&out.replies[0]);
        assert_eq!(v["error"]["code"], "invalid_field");
        assert_eq!(v["error"]["field"], "client_id");
        assert!(out.close);
    }

    /// A handshaked session, for the tests that are about what comes after one.
    fn handshaked() -> V6Session {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        s
    }

    /// A handshaked guest behind a Party Mode gate that is switched on.
    fn guest_at_a_party() -> V6Session {
        let gate = Arc::new(PartyMode::default());
        gate.set_enabled(true);
        let mut s = V6Session::default().with_party_mode(&gate);
        feed(&mut s, GOOD_HANDSHAKE);
        s
    }

    #[test]
    fn an_op_the_role_lacks_is_forbidden_and_names_the_capability() {
        let mut s = guest_at_a_party();
        let reply = feed(
            &mut s,
            r#"{"id":3,"kind":"request","op":"player_next","data":{}}"#,
        );
        let v: Value = serde_json::from_str(&reply.replies[0]).expect("a JSON frame");
        assert_eq!(v["id"], 3);
        assert_eq!(v["error"]["code"], "forbidden");
        assert!(
            v["error"]["message"]
                .as_str()
                .unwrap()
                .contains("`playback`")
        );
        assert!(!reply.close);
    }

    #[test]
    fn a_guest_may_read_and_append_one_track() {
        let mut s = guest_at_a_party();
        for line in [
            r#"{"id":4,"kind":"request","op":"player_status","data":{}}"#,
            r#"{"id":5,"kind":"request","op":"now_playing_queue","data":{"paths":["a"],"mode":"last"}}"#,
            r#"{"id":6,"kind":"request","op":"ping","data":{}}"#,
        ] {
            let reply = feed(&mut s, line);
            let v: Value = serde_json::from_str(&reply.replies[0]).expect("a JSON frame");
            assert_ne!(v["error"]["code"], "forbidden", "{line}");
        }
    }

    fn party_gate() -> Arc<PartyMode> {
        let gate = Arc::new(PartyMode::default());
        gate.set_enabled(true);
        gate
    }

    fn next_track(s: &mut V6Session) -> Value {
        let out = feed(
            s,
            r#"{"id":7,"kind":"request","op":"player_next","data":{}}"#,
        );
        parse(&out.replies[0])
    }

    /// The token from a pairing, on a later connection, is the same client.
    #[test]
    fn a_token_presented_at_the_handshake_brings_its_role_back() {
        let gate = party_gate();
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code_for(permissions::Role::Dj);
        let mut first = V6Session::default()
            .with_party_mode(&gate)
            .with_pairing(&pairing, LAN);
        feed(&mut first, GOOD_HANDSHAKE);
        let token = pair_with(&mut first, &code)["data"]["token"]
            .as_str()
            .expect("pairing issues a token")
            .to_owned();

        let mut back = V6Session::default()
            .with_party_mode(&gate)
            .with_pairing(&pairing, LAN);
        let reply = handshake_with_pairing_token(&mut back, &token);
        assert_eq!(reply["data"]["paired"], true);
        assert_eq!(reply["data"]["permissions"]["role"], "dj");
        assert!(next_track(&mut back).get("error").is_none());
    }

    /// `client_token` only tells two installs apart; it buys no standing.
    #[test]
    fn a_client_token_alone_holds_no_role() {
        let clients = identities("no-role");
        let gate = party_gate();
        let mut first = V6Session::default().with_party_mode(&gate);
        let token = handshake_with(&mut first, &clients, None)["data"]["client_token"]
            .as_str()
            .expect("first contact is issued a token")
            .to_owned();

        let mut back = V6Session::default().with_party_mode(&gate);
        let reply = handshake_with(&mut back, &clients, Some(&token));
        assert_eq!(reply["data"]["paired"], false);
        assert_eq!(next_track(&mut back)["error"]["code"], "forbidden");
    }

    /// Pairing on: a client can reach only what lets it pair.
    #[test]
    fn with_pairing_required_only_ping_and_pair_answer_an_unpaired_client() {
        let pairing = Arc::new(Pairing::default());
        let mut s = V6Session::default()
            .with_pairing(&pairing, LAN)
            .requiring_pairing(true);
        let reply = parse(&feed(&mut s, GOOD_HANDSHAKE).replies[0]);
        assert_eq!(reply["data"]["pairing_required"], true);
        assert_eq!(reply["data"]["paired"], false);
        assert!(!s.takes_events(), "events describe the player");

        let refused = parse(
            &feed(
                &mut s,
                r#"{"id":3,"kind":"request","op":"player_status","data":{}}"#,
            )
            .replies[0],
        );
        assert_eq!(refused["error"]["code"], "pairing_required");
        let ping =
            parse(&feed(&mut s, r#"{"id":4,"kind":"request","op":"ping","data":{}}"#).replies[0]);
        assert!(ping.get("error").is_none());
    }

    fn set_timer(s: &mut V6Session, action: &str) -> Value {
        let line = format!(
            r#"{{"id":9,"kind":"request","op":"system_set_auto_shutdown","data":{{"action":"{action}","minutes":30}}}}"#
        );
        parse(&feed(s, &line).replies[0])
    }

    /// The power permission is the user's tick, not a role: Party Mode neither
    /// grants it to a Host nor takes it from a Listener.
    #[test]
    fn only_a_ticked_device_may_shut_down_whatever_its_party_mode_role() {
        let gate = party_gate();
        let power = Arc::new(crate::power::Power::default());
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_party_mode(&gate)
            .with_power(&power)
            .with_pairing(&pairing, LAN);
        feed(&mut s, GOOD_HANDSHAKE);
        pair_with(&mut s, &code);
        let me = s.principal.clone().expect("paired");

        gate.assign_role(&me, permissions::Role::Host);
        assert_eq!(set_timer(&mut s, "shutdown")["error"]["code"], "forbidden");

        gate.assign_role(&me, permissions::Role::Listener);
        power.grants.grant(&me, 1);
        assert_eq!(set_timer(&mut s, "shutdown")["data"]["action"], "shutdown");
        assert_eq!(
            set_timer(&mut s, "stop")["error"]["code"],
            "forbidden",
            "stopping playback is still a Party Mode capability"
        );

        gate.assign_role(&me, permissions::Role::Host);
        power.grants.revoke(&me);
        assert_eq!(
            set_timer(&mut s, "cancel")["error"]["code"],
            "forbidden",
            "a Host without the tick may not cancel a shutdown"
        );
        gate.assign_role(&me, permissions::Role::Listener);
        power.grants.grant(&me, 1);
        assert_eq!(set_timer(&mut s, "cancel")["data"]["active"], false);
    }

    #[test]
    fn pairing_lets_a_held_socket_through_and_starts_its_events() {
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_pairing(&pairing, LAN)
            .requiring_pairing(true);
        feed(&mut s, GOOD_HANDSHAKE);

        assert_eq!(pair_with(&mut s, &code)["data"]["role"], "host");

        assert!(s.takes_events());
        assert!(s.take_newly_paired());
        assert!(!s.take_newly_paired(), "the transport acts on it once");
        let status = parse(
            &feed(
                &mut s,
                r#"{"id":3,"kind":"request","op":"player_status","data":{}}"#,
            )
            .replies[0],
        );
        assert_ne!(status["error"]["code"], "pairing_required");
    }

    #[test]
    fn a_stale_token_leaves_the_client_unpaired_but_connected() {
        let pairing = Arc::new(Pairing::default());
        let mut s = V6Session::default()
            .with_pairing(&pairing, LAN)
            .requiring_pairing(true);
        let reply = handshake_with_pairing_token(&mut s, "forgotten");
        assert!(
            reply.get("error").is_none(),
            "it still has to be able to pair"
        );
        assert_eq!(reply["data"]["paired"], false);
    }

    #[test]
    fn unpairing_closes_a_socket_on_its_next_frame() {
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_pairing(&pairing, LAN)
            .requiring_pairing(true);
        feed(&mut s, GOOD_HANDSHAKE);
        pair_with(&mut s, &code);
        let Some(Principal::Paired(id)) = s.principal.clone() else {
            panic!("paired");
        };
        assert!(pairing.revoke(&id));

        let out = feed(&mut s, r#"{"id":4,"kind":"request","op":"ping","data":{}}"#);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "pairing_required");
        assert!(out.close);
    }

    #[test]
    fn with_pairing_off_an_unpaired_client_is_served_and_takes_events() {
        let mut s = V6Session::default();
        let reply = parse(&feed(&mut s, GOOD_HANDSHAKE).replies[0]);
        assert_eq!(reply["data"]["pairing_required"], false);
        assert!(s.takes_events());
    }

    fn handshake_with_pairing_token(s: &mut V6Session, token: &str) -> Value {
        let line = format!(
            r#"{{"id":0,"kind":"request","op":"handshake","data":{{"protocol_version":6,"client_id":"install-1","client_type":"android","token":"{token}"}}}}"#
        );
        parse(&feed(s, &line).replies[0])
    }

    #[test]
    fn a_principal_the_transport_settled_survives_the_handshake() {
        let clients = identities("role-browser");
        let gate = party_gate();
        let browser = Principal::Paired("paired".into());
        gate.assign_role(&browser, permissions::Role::Dj);
        let mut s = V6Session::default()
            .with_party_mode(&gate)
            .with_principal(browser);
        handshake_with(&mut s, &clients, None);
        assert!(next_track(&mut s).get("error").is_none());
    }

    const LAN: IpAddr = IpAddr::V4(std::net::Ipv4Addr::new(192, 168, 1, 20));

    fn pair_with(s: &mut V6Session, code: &str) -> Value {
        let line = format!(r#"{{"id":8,"kind":"request","op":"pair","data":{{"code":"{code}"}}}}"#);
        parse(&feed(s, &line).replies[0])
    }

    #[test]
    fn the_handshake_tells_the_client_its_permissions_and_offers_a_watch() {
        let clients = identities("perm-handshake");
        let gate = party_gate();
        let mut s = V6Session::default().with_party_mode(&gate);
        let reply = handshake_with(&mut s, &clients, None);
        assert_eq!(reply["data"]["permissions"]["role"], "guest");
        let (principal, told) = s.permissions_watch().expect("a watch");
        assert_eq!(principal, Principal::Anonymous);
        assert_eq!(told, reply["data"]["permissions"]);
    }

    #[test]
    fn an_app_that_pairs_takes_the_role_of_the_code_and_a_token() {
        let clients = identities("pair-proven");
        let gate = party_gate();
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code_for(permissions::Role::Dj);
        let mut s = V6Session::default()
            .with_party_mode(&gate)
            .with_pairing(&pairing, LAN);
        handshake_with(&mut s, &clients, None);
        assert_eq!(next_track(&mut s)["error"]["code"], "forbidden");

        let paired = pair_with(&mut s, &code);
        assert_eq!(paired["data"]["role"], "dj");
        assert_eq!(paired["data"]["permissions"]["role"], "dj");
        assert!(paired["data"]["token"].is_string());
        assert!(next_track(&mut s).get("error").is_none());
    }

    #[test]
    fn a_wrong_pairing_code_is_unauthorized() {
        let clients = identities("pair-wrong");
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_party_mode(&party_gate())
            .with_pairing(&pairing, LAN);
        handshake_with(&mut s, &clients, None);
        let wrong = if code == "000000" { "111111" } else { "000000" };
        assert_eq!(pair_with(&mut s, wrong)["error"]["code"], "unauthorized");
    }

    #[test]
    fn pairing_needs_no_identity_store() {
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_party_mode(&party_gate())
            .with_pairing(&pairing, LAN);
        handshake_with(
            &mut s,
            &ClientIdentities::new(crate::store::Db::disabled()),
            None,
        );
        assert_eq!(pair_with(&mut s, &code)["data"]["role"], "host");
    }

    /// An unpaired browser, too, is told how it pairs rather than that it cannot.
    #[test]
    fn an_anonymous_browser_socket_is_sent_to_http_to_pair() {
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_pairing(&pairing, LAN)
            .with_principal(Principal::Anonymous);
        feed(&mut s, GOOD_HANDSHAKE);
        assert_eq!(pair_with(&mut s, &code)["error"]["code"], "not_allowed");
    }

    #[test]
    fn a_browser_socket_cannot_pair_as_an_app() {
        let clients = identities("pair-browser");
        let pairing = Arc::new(Pairing::default());
        let code = pairing.new_code();
        let mut s = V6Session::default()
            .with_party_mode(&party_gate())
            .with_pairing(&pairing, LAN)
            .with_principal(Principal::Paired("paired".into()));
        handshake_with(&mut s, &clients, None);
        assert_eq!(pair_with(&mut s, &code)["error"]["code"], "not_allowed");
    }

    /// A client cannot act on a validation failure it has to read as prose.
    #[test]
    fn a_field_error_names_the_field_that_failed() {
        let mut s = handshaked();
        let reply = feed(
            &mut s,
            r#"{"id":9,"kind":"request","op":"track_get","data":{}}"#,
        );
        let v: Value = serde_json::from_str(&reply.replies[0]).expect("a JSON frame");
        assert_eq!(v["error"]["code"], "missing_field");
        assert_eq!(v["error"]["field"], "src");
    }

    /// An envelope-level rejection knows the field too, and says so the same way.
    #[test]
    fn an_envelope_error_names_its_field() {
        let mut s = V6Session::default();
        let reply = feed(&mut s, r#"{"id":1,"kind":"request","data":{}}"#);
        let v: Value = serde_json::from_str(&reply.replies[0]).expect("a JSON frame");
        assert_eq!(v["error"]["code"], "missing_field");
        assert_eq!(v["error"]["field"], "op");
    }

    /// An error that is not about one field carries no `field` key at all, rather
    /// than an empty one a client would have to test for.
    #[test]
    fn an_error_with_no_field_omits_the_key() {
        let mut s = handshaked();
        let reply = feed(&mut s, r#"{"id":2,"kind":"request","op":"nope","data":{}}"#);
        let v: Value = serde_json::from_str(&reply.replies[0]).expect("a JSON frame");
        assert_eq!(v["error"]["code"], "unknown_op");
        assert!(v["error"].get("field").is_none());
    }

    /// Drives one frame through the session against a no-op provider context.
    ///
    /// The envelope and handshake never reach a provider; the op handlers have
    /// their own tests in `commands_v6`.
    fn feed(s: &mut V6Session, line: &str) -> Outcome {
        s.handle_frame(line, &NullProviders, None, None, None, None)
    }

    const GOOD_HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"install-1","client_type":"android"}}"#;

    #[test]
    fn handshake_is_accepted_and_reports_server_version() {
        let mut s = V6Session::default();
        let out = feed(&mut s, GOOD_HANDSHAKE);
        assert!(!out.close);
        assert!(s.handshaked);
        let v = parse(&out.replies[0]);
        assert_eq!(v["id"], 0);
        assert_eq!(v["kind"], "response");
        assert_eq!(v["data"]["server_version"], 6);
        // Registration metadata is exposed post-handshake; a default handshake (no
        // no_broadcast) is a main connection.
        let meta = s.reg_meta().unwrap();
        assert_eq!(meta.client_id.as_deref(), Some("install-1"));
        assert!(meta.is_main);
        assert_eq!(meta.protocol, 6);
    }

    #[test]
    fn handshake_names_the_server_after_its_version() {
        let server = ServerIdentity {
            id: Some(Arc::from("3f2b8c1e-9d4a-4b6f-8e2d-1a7c5b9e0f34")),
            name: Arc::from("LIVING-ROOM-PC"),
        };
        let mut s = V6Session::default().with_server(&server);
        let v = parse(&feed(&mut s, GOOD_HANDSHAKE).replies[0]);
        assert_eq!(
            v["data"]["server_id"],
            "3f2b8c1e-9d4a-4b6f-8e2d-1a7c5b9e0f34"
        );
        assert_eq!(v["data"]["server_name"], "LIVING-ROOM-PC");
        let keys: Vec<&str> = v["data"]
            .as_object()
            .unwrap()
            .keys()
            .map(String::as_str)
            .collect();
        assert_eq!(
            keys[..4],
            ["server_version", "server_id", "server_name", "capabilities"]
        );
    }

    #[test]
    fn handshake_without_a_stored_id_omits_server_id_only() {
        let server = ServerIdentity {
            id: None,
            name: Arc::from("LIVING-ROOM-PC"),
        };
        let mut s = V6Session::default().with_server(&server);
        let v = parse(&feed(&mut s, GOOD_HANDSHAKE).replies[0]);
        assert!(v["data"].get("server_id").is_none());
        assert_eq!(v["data"]["server_name"], "LIVING-ROOM-PC");
    }

    #[test]
    fn no_broadcast_handshake_is_not_a_main() {
        let mut s = V6Session::default();
        feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"x","client_type":"android","no_broadcast":true}}"#,
        );
        assert!(!s.reg_meta().unwrap().is_main);
    }

    #[test]
    fn ping_round_trips_and_echoes_data() {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        let out = feed(
            &mut s,
            r#"{"id":1,"kind":"request","op":"ping","data":{"n":42}}"#,
        );
        assert!(!out.close);
        let v = parse(&out.replies[0]);
        assert_eq!(v["id"], 1);
        assert_eq!(v["kind"], "response");
        assert_eq!(v["data"]["n"], 42);
    }

    #[test]
    fn missing_client_id_is_missing_field_and_closes() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_type":"ios"}}"#,
        );
        assert!(out.close, "a rejected handshake closes the connection");
        assert!(!s.handshaked);
        let v = parse(&out.replies[0]);
        assert_eq!(v["error"]["code"], "missing_field");
    }

    #[test]
    fn empty_client_id_is_invalid_field() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"","client_type":"ios"}}"#,
        );
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "invalid_field");
    }

    #[test]
    fn wrong_version_is_unsupported_version() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":5,"client_id":"x","client_type":"ios"}}"#,
        );
        assert!(out.close);
        assert_eq!(
            parse(&out.replies[0])["error"]["code"],
            "unsupported_version"
        );
    }

    #[test]
    fn unknown_client_type_is_invalid_field() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"x","client_type":"toaster"}}"#,
        );
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "invalid_field");
    }

    #[test]
    fn op_before_handshake_is_unauthorized_and_closes() {
        let mut s = V6Session::default();
        let out = feed(&mut s, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "unauthorized");
    }

    #[test]
    fn second_handshake_is_rejected_without_closing() {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        let out = feed(&mut s, GOOD_HANDSHAKE);
        assert!(
            !out.close,
            "a repeat handshake does not tear down the session"
        );
        assert_eq!(parse(&out.replies[0])["error"]["code"], "not_allowed");
        assert!(s.handshaked);
    }

    #[test]
    fn unknown_op_after_handshake_is_unknown_op() {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        let out = feed(
            &mut s,
            r#"{"id":2,"kind":"request","op":"teleport","data":{}}"#,
        );
        assert!(!out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "unknown_op");
    }

    #[test]
    fn unknown_data_key_is_ignored() {
        // An unknown additive key in the handshake data is accepted (structure is
        // valid; only unknown *structure* is rejected).
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"x","client_type":"cli","future_flag":true}}"#,
        );
        assert!(!out.close);
        assert!(s.handshaked);
    }

    #[test]
    fn malformed_pre_handshake_frame_closes() {
        let mut s = V6Session::default();
        let out = feed(&mut s, "not json");
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "malformed_frame");
    }

    /// The regression guard for capture visibility. A diagnostics capture raises
    /// `mbrc::wire` to DEBUG, never to TRACE, so V6 frames logged at TRACE would be
    /// missing from every capture while the V4 frames beside them showed up. Both
    /// directions must be on the DEBUG line.
    #[test]
    fn v6_frames_are_logged_at_debug_in_both_directions() {
        let lines = crate::logging::test_support::capture_wire_lines(|| {
            let mut s = V6Session::default();
            feed(&mut s, GOOD_HANDSHAKE);
        });

        let dirs: Vec<&str> = lines.iter().map(|l| l.dir.as_str()).collect();
        assert!(
            dirs.contains(&"c2s"),
            "the request must reach a capture: {lines:?}"
        );
        assert!(
            dirs.contains(&"s2c"),
            "the response must reach a capture: {lines:?}"
        );
    }

    /// DEBUG must not spill a full list body: the sampled render is what a capture
    /// carries, and a browse response can be megabytes.
    #[test]
    fn debug_samples_a_long_list_body() {
        let items: Vec<String> = (0..50).map(|i| format!(r#"{{"n":{i}}}"#)).collect();
        let frame = format!(
            r#"{{"id":1,"kind":"response","data":{{"total":50,"offset":0,"items":[{}]}}}}"#,
            items.join(",")
        );
        let lines = crate::logging::test_support::capture_wire_lines(|| log_s2c(1, &frame));

        assert_eq!(lines.len(), 1, "{lines:?}");
        let msg = &lines[0].message;
        assert!(msg.contains("more items"), "body was not sampled: {msg}");
        assert!(
            msg.len() < frame.len(),
            "the DEBUG render must be shorter than the frame: {msg}"
        );
    }
}
