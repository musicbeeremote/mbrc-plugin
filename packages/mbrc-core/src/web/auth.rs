//! Pairing codes and browser session tokens.
//!
//! A browser cannot be identified the way a native client is, so it earns a token
//! by echoing back a short code the user reads out of the MusicBee panel. The
//! token then travels as `Authorization: Bearer` on HTTP and as an additive
//! `token` field in the V6 WebSocket handshake.
//!
//! Enforcement is `web_auth_required`, which defaults off; the admission check is
//! the same function either way, so the guarded path is never an untested branch.
//!
//! Pairings outlive the process: a phone is paired once, not once per launch,
//! and the toggle that enforces pairing restarts the core. The token is stored
//! hashed, so the file cannot be replayed as a credential - only the browser
//! that was issued one can present it.

use std::collections::HashMap;
use std::net::IpAddr;
use std::sync::Mutex;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use redb::{Durability, ReadableTable};
use serde::{Deserialize, Serialize};

use crate::server::permissions::Role;
use crate::store::{Db, PAIRED_BROWSERS};

/// Long enough to read off a screen and type on a phone, short enough that
/// guessing it inside the window is not worth attempting. Six digits over a
/// two-minute window is the same shape as every other pairing code a user has
/// met, which matters more here than the extra entropy of a longer one.
const CODE_DIGITS: u32 = 6;

/// How long a generated pairing code stays valid.
const CODE_TTL: Duration = Duration::from_secs(120);

/// Wrong codes an address may send before it has to wait.
const FREE_WRONG_CODES: u32 = 3;

/// The first wait after an address runs out of free wrong codes; each further
/// wrong code doubles it, up to [`MAX_BACKOFF`].
const FIRST_BACKOFF: Duration = Duration::from_secs(5);

const MAX_BACKOFF: Duration = Duration::from_secs(5 * 60);

/// Wrong codes a live code survives, from every address together.
///
/// A code grants a role up to Host, so this bounds a guess at ten in a million
/// per code however many addresses a guest brings.
const MAX_WRONG_PER_CODE: u32 = 10;

/// Addresses whose wrong codes are remembered at once.
const MAX_TRACKED_ADDRESSES: usize = 1024;

/// Longest label kept for a browser.
///
/// It is a display string in a fixed-width column, and the browser that sends
/// one is not the side that has to read it back.
const MAX_LABEL: usize = 64;

/// Bytes of entropy in a session token. Tokens are bearer credentials with no
/// expiry, so this is sized to be permanently unguessable rather than merely
/// unguessable for a window.
const TOKEN_BYTES: usize = 32;

/// A paired browser.
#[derive(Debug, Clone)]
pub struct PairedClient {
    /// A short handle for showing and for unpairing this one browser.
    ///
    /// Derived from the token rather than being part of it, so a panel can name
    /// a browser, and a screenshot of that panel gives nothing away.
    pub id: String,
    pub token: String,
    pub label: String,
    /// Unix seconds, so the panel can say how long ago rather than just how many.
    pub paired_at: i64,
    /// Unix seconds of the last request this token was accepted on.
    pub last_seen: i64,
}

/// What is written down about a paired browser.
///
/// The token is hashed, so this file is not a set of working credentials. The
/// rest is what the panel lists, and is not a secret.
#[derive(Debug, Clone, Serialize, Deserialize)]
struct Record {
    id: String,
    label: String,
    paired_at: i64,
    last_seen: i64,
}

/// Why a pairing code was not accepted.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PairingRefused {
    /// No code is live, or it lapsed.
    NoCode,
    WrongCode,
    /// This address sent too many wrong codes and must wait this long.
    Backoff(Duration),
}

impl PairingRefused {
    /// What the client is told.
    pub fn message(self) -> String {
        match self {
            PairingRefused::NoCode | PairingRefused::WrongCode => {
                "pairing code is wrong or expired".to_string()
            }
            PairingRefused::Backoff(wait) => format!(
                "too many wrong pairing codes from this address; try again in {} s",
                wait.as_secs().max(1)
            ),
        }
    }
}

/// A browser that has just paired.
#[derive(Debug, Clone)]
pub struct NewPairing {
    pub token: String,
    pub id: String,
    /// The role the code carried.
    pub role: Role,
}

/// Wrong codes one address has sent.
#[derive(Debug, Clone, Copy)]
struct Strikes {
    wrong: u32,
    blocked_until: Option<Instant>,
}

struct LiveCode {
    code: String,
    issued: Instant,
    role: Role,
    wrong: u32,
}

/// The pairing state: at most one live code, plus the tokens it has minted.
#[derive(Default)]
pub struct Pairing {
    inner: Mutex<State>,
    /// The store, once the core has one. Absent in tests that do not need it,
    /// where pairing then behaves exactly as it did before it was persisted.
    db: Mutex<Option<Db>>,
}

#[derive(Default)]
struct State {
    code: Option<LiveCode>,
    /// Whether the last code was voided by too many wrong attempts.
    voided: bool,
    strikes: HashMap<IpAddr, Strikes>,
    /// Keyed by the token itself while the process runs, so admitting one is a
    /// lookup rather than a scan. What reaches disk is keyed by id and hashed.
    tokens: HashMap<String, PairedClient>,
}

/// Prints nothing from the state: it holds the live code and every token.
impl std::fmt::Debug for Pairing {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Pairing").finish_non_exhaustive()
    }
}

impl Pairing {
    fn lock(&self) -> std::sync::MutexGuard<'_, State> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn store(&self) -> std::sync::MutexGuard<'_, Option<Db>> {
        self.db.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Attaches the store and reads back what was paired before.
    ///
    /// The tokens themselves are gone - only their hashes were written - so the
    /// in-memory map is keyed by hash here, and `is_paired` hashes what it is
    /// given before looking. A browser paired in an earlier run is admitted;
    /// nothing in the file lets anyone else be.
    pub fn open(&self, db: Db) {
        let stored = read_all(&db);
        *self.store() = Some(db);

        let mut state = self.lock();
        for record in stored {
            state.tokens.insert(
                record.id.clone(),
                PairedClient {
                    id: record.id,
                    token: String::new(),
                    label: record.label,
                    paired_at: record.paired_at,
                    last_seen: record.last_seen,
                },
            );
        }
    }

    fn write(&self, client: &PairedClient) {
        let Some(db) = self.store().clone() else {
            return;
        };
        let record = Record {
            id: client.id.clone(),
            label: client.label.clone(),
            paired_at: client.paired_at,
            last_seen: client.last_seen,
        };
        let Ok(bytes) = rmp_serde::to_vec_named(&record) else {
            return;
        };
        db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(PAIRED_BROWSERS)?;
            table.insert(record.id.as_str(), bytes.as_slice())?;
            Ok(())
        });
    }

    fn forget(&self, id: &str) {
        let Some(db) = self.store().clone() else {
            return;
        };
        db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(PAIRED_BROWSERS)?;
            table.remove(id)?;
            Ok(())
        });
    }

    /// Mints a fresh pairing code, replacing any code still outstanding.
    ///
    /// Replacing rather than reusing means the panel's button always shows a code
    /// with a full window on it, and a code the user walked away from stops
    /// working the moment they ask for another.
    pub fn new_code(&self) -> String {
        self.new_code_for(Role::Host)
    }

    /// Mints a fresh pairing code that grants `role`, replacing any code still outstanding.
    pub fn new_code_for(&self, role: Role) -> String {
        let code = random_digits(CODE_DIGITS);
        let mut state = self.lock();
        state.code = Some(LiveCode {
            code: code.clone(),
            issued: Instant::now(),
            role,
            wrong: 0,
        });
        state.voided = false;
        code
    }

    /// The role the outstanding code grants, if one is still inside its window.
    pub fn current_code_role(&self) -> Option<Role> {
        self.lock()
            .code
            .as_ref()
            .filter(|live| live.issued.elapsed() < CODE_TTL)
            .map(|live| live.role)
    }

    /// Whether the last code was voided because too many wrong codes were tried.
    pub fn code_voided(&self) -> bool {
        self.lock().voided
    }

    /// The outstanding code, if one is still inside its window.
    pub fn current_code(&self) -> Option<String> {
        let state = self.lock();
        state
            .code
            .as_ref()
            .filter(|live| live.issued.elapsed() < CODE_TTL)
            .map(|live| live.code.clone())
    }

    /// Seconds left on the outstanding code, or zero when none is live.
    ///
    /// Reported rather than left to the panel's own clock: a code that lapsed
    /// while the dialog sat open should stop being offered, and only the side
    /// that issued it knows when that is.
    pub fn code_expires_in(&self) -> i32 {
        let state = self.lock();
        state
            .code
            .as_ref()
            .and_then(|live| CODE_TTL.checked_sub(live.issued.elapsed()))
            .map(|left| left.as_secs() as i32)
            .unwrap_or(0)
    }

    /// Checks a code offered from `from`, consuming it and returning its role when right.
    ///
    /// Rate limited, because a code that grants Host is worth guessing: see
    /// [`FREE_WRONG_CODES`] and [`MAX_WRONG_PER_CODE`]. An address that must wait
    /// is refused without its code being looked at.
    pub fn take_code(&self, offered: &str, from: IpAddr) -> Result<Role, PairingRefused> {
        self.take_code_at(offered, from, Instant::now())
    }

    fn take_code_at(
        &self,
        offered: &str,
        from: IpAddr,
        now: Instant,
    ) -> Result<Role, PairingRefused> {
        let mut state = self.lock();
        if let Some(until) = state.strikes.get(&from).and_then(|s| s.blocked_until)
            && until > now
        {
            return Err(PairingRefused::Backoff(until - now));
        }
        let Some(live) = state
            .code
            .as_mut()
            .filter(|live| now.saturating_duration_since(live.issued) < CODE_TTL)
        else {
            return Err(PairingRefused::NoCode);
        };
        if constant_time_eq(&live.code, offered) {
            let role = live.role;
            state.code = None;
            state.strikes.remove(&from);
            return Ok(role);
        }
        live.wrong += 1;
        if live.wrong >= MAX_WRONG_PER_CODE {
            state.code = None;
            state.voided = true;
            tracing::warn!("pairing code voided after {MAX_WRONG_PER_CODE} wrong attempts");
        }
        state.strike(from, now);
        Err(PairingRefused::WrongCode)
    }

    /// Exchanges a pairing code offered from `from` for a browser session token.
    pub fn redeem(
        &self,
        offered: &str,
        label: &str,
        from: IpAddr,
    ) -> Result<NewPairing, PairingRefused> {
        let role = self.take_code(offered, from)?;
        let mut state = self.lock();
        let token = random_token();
        let now = now_unix_seconds();
        let client = PairedClient {
            id: client_id(&token),
            token: token.clone(),
            label: clamp_label(label),
            paired_at: now,
            last_seen: now,
        };
        state.tokens.insert(client.id.clone(), client.clone());
        drop(state);

        self.write(&client);
        Ok(NewPairing {
            token,
            id: client.id,
            role,
        })
    }

    /// Whether a token names a paired browser.
    ///
    /// Records the moment as well: a list of paired browsers that cannot say
    /// which are still in use is a list nobody can act on.
    pub fn is_paired(&self, token: &str) -> bool {
        self.paired_id(token).is_some()
    }

    /// The id of the paired browser a token names, recording the moment as
    /// [`is_paired`](Self::is_paired) does.
    pub fn paired_id(&self, token: &str) -> Option<String> {
        let updated = {
            let mut state = self.lock();
            match state.tokens.get_mut(&client_id(token)) {
                Some(client) => {
                    client.last_seen = now_unix_seconds();
                    Some(client.clone())
                }
                None => None,
            }
        };
        let client = updated?;
        self.write(&client);
        Some(client.id)
    }

    /// Every paired browser, for the panel's list.
    pub fn paired(&self) -> Vec<PairedClient> {
        self.lock().tokens.values().cloned().collect()
    }

    /// Renames one browser, so a list of lookalike names can be told apart.
    ///
    /// The panel is where that ambiguity is seen, and this repairs a browser
    /// that is already paired, which naming one at pairing time cannot.
    pub fn rename(&self, id: &str, label: &str) -> bool {
        let renamed = {
            let mut state = self.lock();
            match state.tokens.get_mut(id) {
                Some(client) => {
                    client.label = clamp_label(label);
                    Some(client.clone())
                }
                None => None,
            }
        };
        match renamed {
            Some(client) => {
                self.write(&client);
                true
            }
            None => false,
        }
    }

    /// Drops one browser's token, naming it by the id the panel shows.
    ///
    /// Reports whether anything was dropped, so a stale panel pressing the
    /// button on a browser that is already gone can be told rather than left
    /// to believe it did something.
    pub fn revoke(&self, id: &str) -> bool {
        let removed = self.lock().tokens.remove(id).is_some();
        if removed {
            self.forget(id);
        }
        removed
    }

    /// Drops every token, so all paired browsers must pair again.
    pub fn revoke_all(&self) {
        let ids: Vec<String> = {
            let mut state = self.lock();
            let ids = state.tokens.keys().cloned().collect();
            state.tokens.clear();
            state.code = None;
            state.voided = false;
            ids
        };
        for id in ids {
            self.forget(&id);
        }
    }
}

impl State {
    /// Records a wrong code from `from`, starting or doubling its wait once it
    /// has used up its free attempts.
    fn strike(&mut self, from: IpAddr, now: Instant) {
        if self.strikes.len() >= MAX_TRACKED_ADDRESSES && !self.strikes.contains_key(&from) {
            self.strikes
                .retain(|_, s| s.blocked_until.is_some_and(|until| until > now));
            if self.strikes.len() >= MAX_TRACKED_ADDRESSES {
                self.strikes.clear();
            }
        }
        let strikes = self.strikes.entry(from).or_insert(Strikes {
            wrong: 0,
            blocked_until: None,
        });
        strikes.wrong += 1;
        if let Some(over) = strikes.wrong.checked_sub(FREE_WRONG_CODES) {
            strikes.blocked_until = Some(now + backoff_after(over));
        }
    }
}

/// The wait after the `over`-th wrong code past the free ones, counting from zero.
fn backoff_after(over: u32) -> Duration {
    FIRST_BACKOFF
        .saturating_mul(2u32.saturating_pow(over))
        .min(MAX_BACKOFF)
}

/// Compares without an early return on the first differing byte, so the time a
/// comparison takes says nothing about how much of the code was right.
fn constant_time_eq(a: &str, b: &str) -> bool {
    if a.len() != b.len() {
        return false;
    }
    a.bytes()
        .zip(b.bytes())
        .fold(0u8, |acc, (x, y)| acc | (x ^ y))
        == 0
}

fn random_digits(count: u32) -> String {
    let mut bytes = vec![0u8; count as usize];
    getrandom::fill(&mut bytes).expect("OS entropy");
    bytes.iter().map(|b| char::from(b'0' + b % 10)).collect()
}

fn random_token() -> String {
    let mut bytes = [0u8; TOKEN_BYTES];
    getrandom::fill(&mut bytes).expect("OS entropy");
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

/// A label as it is kept: trimmed, bounded, and never empty.
fn clamp_label(label: &str) -> String {
    let trimmed: String = label
        .trim()
        .chars()
        .filter(|c| !c.is_control())
        .take(MAX_LABEL)
        .collect();
    if trimmed.is_empty() {
        "browser".to_string()
    } else {
        trimmed
    }
}

/// The handle a browser is known by: a hash of its token, not a piece of it.
///
/// It is both what the panel shows and what admission looks up, which is what
/// lets the token itself stay out of the file: the same hash is computed from
/// whatever a browser presents.
fn client_id(token: &str) -> String {
    crate::cover::sha1_hex_str(token)
}

/// Every paired browser written down, ignoring any record that will not read.
fn read_all(db: &Db) -> Vec<Record> {
    db.read(|txn| {
        let table = match txn.open_table(PAIRED_BROWSERS) {
            Ok(table) => table,
            Err(_) => return Ok(Vec::new()),
        };
        let mut records = Vec::new();
        for row in table.iter()? {
            let (_, bytes) = row?;
            if let Ok(record) = rmp_serde::from_slice::<Record>(bytes.value()) {
                records.push(record);
            }
        }
        Ok(records)
    })
    .unwrap_or_default()
}

fn now_unix_seconds() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    const LAN: IpAddr = IpAddr::V4(std::net::Ipv4Addr::new(192, 168, 1, 20));

    /// A phone is paired once, not once per launch - and the toggle that
    /// enforces pairing restarts the core, so without this, turning pairing on
    /// would unpair everything it was turned on for.
    ///
    /// The first handle is dropped before the store is opened again: redb holds
    /// the file exclusively, and a second handle to a live one recreates it
    /// empty, which is a restart in the only sense that matters here.
    #[test]
    fn a_paired_browser_survives_a_restart() {
        let dir = std::env::temp_dir().join("mbrc-pairing-restart");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        let token = {
            let before = Pairing::default();
            before.open(Db::open(dir.to_str().unwrap()));
            let code = before.new_code();
            before
                .redeem(&code, "Vivaldi on Android", LAN)
                .expect("redeem")
                .token
        };

        let after = Pairing::default();
        after.open(Db::open(dir.to_str().unwrap()));

        assert!(after.is_paired(&token), "the browser is still paired");
        let listed = after.paired();
        assert_eq!(listed.len(), 1);
        assert_eq!(listed[0].label, "Vivaldi on Android");
    }

    /// The file is not a set of working credentials: what is written is a hash,
    /// and only a browser holding the token can produce it.
    #[test]
    fn the_stored_record_cannot_be_replayed_as_a_token() {
        let dir = std::env::temp_dir().join("mbrc-pairing-hashed");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        let db = Db::open(dir.to_str().unwrap());
        let pairing = Pairing::default();
        pairing.open(db.clone());
        let code = pairing.new_code();
        let token = pairing.redeem(&code, "browser", LAN).expect("redeem").token;

        let written = read_all(&db);
        assert_eq!(written.len(), 1);
        assert_ne!(written[0].id, token, "the id is not the token");
        assert!(
            !pairing.is_paired(&written[0].id),
            "nor does it work as one"
        );
        assert!(pairing.is_paired(&token));
    }

    #[test]
    fn unpairing_one_browser_leaves_the_others_and_outlives_a_restart() {
        let dir = std::env::temp_dir().join("mbrc-pairing-revoke-one");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        let (phone, desk) = {
            let pairing = Pairing::default();
            pairing.open(Db::open(dir.to_str().unwrap()));
            let phone = pairing
                .redeem(&pairing.new_code(), "phone", LAN)
                .expect("phone pairs")
                .token;
            let desk = pairing
                .redeem(&pairing.new_code(), "desk", LAN)
                .expect("desk pairs")
                .token;

            let phone_id = pairing
                .paired()
                .into_iter()
                .find(|client| client.label == "phone")
                .expect("phone is listed")
                .id;
            assert!(pairing.revoke(&phone_id));
            assert!(!pairing.revoke(&phone_id), "the second attempt says so");

            assert!(!pairing.is_paired(&phone));
            assert!(pairing.is_paired(&desk));
            (phone, desk)
        };

        let after = Pairing::default();
        after.open(Db::open(dir.to_str().unwrap()));
        assert!(!after.is_paired(&phone), "it stays unpaired");
        assert!(after.is_paired(&desk));
    }

    #[test]
    fn a_code_redeems_once_and_yields_a_token() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        let token = pairing
            .redeem(&code, "browser", LAN)
            .expect("first redeem")
            .token;
        assert!(pairing.is_paired(&token));
        assert!(pairing.redeem(&code, "browser", LAN).is_err());
    }

    #[test]
    fn a_wrong_code_mints_nothing() {
        let pairing = Pairing::default();
        pairing.new_code();
        assert!(pairing.redeem("000000", "browser", LAN).is_err());
        assert!(pairing.redeem("", "browser", LAN).is_err());
    }

    #[test]
    fn generating_a_code_invalidates_the_previous_one() {
        let pairing = Pairing::default();
        let first = pairing.new_code();
        let second = pairing.new_code();
        assert!(pairing.redeem(&first, "browser", LAN).is_err());
        assert!(pairing.redeem(&second, "browser", LAN).is_ok());
    }

    #[test]
    fn revoking_unpairs_every_browser() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        let token = pairing.redeem(&code, "browser", LAN).expect("redeem").token;
        pairing.revoke_all();
        assert!(!pairing.is_paired(&token));
        assert!(pairing.paired().is_empty());
    }

    #[test]
    fn an_unminted_token_is_not_paired() {
        let pairing = Pairing::default();
        assert!(!pairing.is_paired("deadbeef"));
    }

    const OTHER: IpAddr = IpAddr::V4(std::net::Ipv4Addr::new(192, 168, 1, 30));

    /// A code that is not `code`, of the same length.
    fn wrong(code: &str) -> String {
        if code == "000000" { "111111" } else { "000000" }.to_string()
    }

    #[test]
    fn a_code_grants_the_role_it_was_made_for() {
        let pairing = Pairing::default();
        let code = pairing.new_code_for(Role::Dj);
        assert_eq!(pairing.take_code(&code, LAN), Ok(Role::Dj));
        assert_eq!(pairing.take_code(&code, LAN), Err(PairingRefused::NoCode));
    }

    #[test]
    fn the_panel_button_makes_host_codes() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        assert_eq!(pairing.take_code(&code, LAN), Ok(Role::Host));
    }

    #[test]
    fn three_wrong_codes_are_free_and_the_fourth_attempt_must_wait() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        let now = Instant::now();
        for _ in 0..FREE_WRONG_CODES {
            let got = pairing.take_code_at(&wrong(&code), LAN, now);
            assert_eq!(got, Err(PairingRefused::WrongCode));
        }
        assert_eq!(
            pairing.take_code_at(&code, LAN, now),
            Err(PairingRefused::Backoff(FIRST_BACKOFF)),
            "the right code is not even looked at while the address waits"
        );
        assert_eq!(pairing.take_code_at(&code, OTHER, now), Ok(Role::Host));
    }

    #[test]
    fn the_wait_doubles_with_each_further_wrong_code() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        let mut now = Instant::now();
        for _ in 0..FREE_WRONG_CODES {
            let _ = pairing.take_code_at(&wrong(&code), LAN, now);
        }
        now += FIRST_BACKOFF;
        let _ = pairing.take_code_at(&wrong(&code), LAN, now);
        assert_eq!(
            pairing.take_code_at(&code, LAN, now),
            Err(PairingRefused::Backoff(FIRST_BACKOFF * 2))
        );
    }

    #[test]
    fn the_wait_is_capped() {
        assert_eq!(backoff_after(0), FIRST_BACKOFF);
        assert_eq!(backoff_after(1), FIRST_BACKOFF * 2);
        assert_eq!(backoff_after(6), MAX_BACKOFF);
        assert_eq!(backoff_after(u32::MAX), MAX_BACKOFF);
    }

    #[test]
    fn the_right_code_clears_an_address_of_its_wrong_ones() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        let now = Instant::now();
        for _ in 0..FREE_WRONG_CODES - 1 {
            let _ = pairing.take_code_at(&wrong(&code), LAN, now);
        }
        assert!(pairing.take_code_at(&code, LAN, now).is_ok());

        let code = pairing.new_code();
        for _ in 0..FREE_WRONG_CODES - 1 {
            let _ = pairing.take_code_at(&wrong(&code), LAN, now);
        }
        assert_eq!(pairing.take_code_at(&code, LAN, now), Ok(Role::Host));
    }

    #[test]
    fn a_code_is_voided_by_the_tenth_wrong_attempt_from_anywhere() {
        let pairing = Pairing::default();
        let code = pairing.new_code();
        let now = Instant::now();
        for n in 0..MAX_WRONG_PER_CODE {
            let from = IpAddr::V4(std::net::Ipv4Addr::new(10, 0, 0, n as u8));
            let _ = pairing.take_code_at(&wrong(&code), from, now);
        }
        assert!(pairing.code_voided());
        assert_eq!(
            pairing.take_code_at(&code, LAN, now),
            Err(PairingRefused::NoCode)
        );
        pairing.new_code();
        assert!(!pairing.code_voided());
    }

    #[test]
    fn trying_a_code_when_none_is_live_is_not_held_against_anyone() {
        let pairing = Pairing::default();
        let now = Instant::now();
        for _ in 0..FREE_WRONG_CODES + 2 {
            let got = pairing.take_code_at("123456", LAN, now);
            assert_eq!(got, Err(PairingRefused::NoCode));
        }
        let code = pairing.new_code();
        assert_eq!(pairing.take_code_at(&code, LAN, now), Ok(Role::Host));
    }

    #[test]
    fn a_code_is_the_advertised_number_of_digits() {
        let code = Pairing::default().new_code();
        assert_eq!(code.len(), CODE_DIGITS as usize);
        assert!(code.chars().all(|c| c.is_ascii_digit()));
    }
}
