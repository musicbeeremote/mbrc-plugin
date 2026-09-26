//! Per-installation client identities: the tiebreaker for a duplicated
//! `client_id`.
//!
//! A `client_id` only has to be unique. The case where it stops being unique is a
//! restored device backup: the clone carries the same persisted UUID, both
//! installs claim it, and the two evict each other's main connection in a loop.
//! So the first handshake for an unseen id is issued a random token, which the
//! client persists and presents thereafter; whoever returns with it is the
//! original install.
//!
//! The token is not authentication on its own: anyone who can reach the port
//! gets a fresh id and token for the asking. It does hold a Party Mode role once
//! the host has given one, so an app with a role is pinned and never pruned. The
//! store is otherwise bounded on both axes (`IDENTITY_TTL_MS`, `MAX_IDENTITIES`)
//! because any peer can invent ids to put in it.

use serde::{Deserialize, Serialize};

use redb::{Durability, ReadableTable};

use crate::store::{CLIENT_IDENTITIES, Db};

/// How long an unseen identity is kept before it is pruned.
///
/// Long enough that a phone left in a drawer over a holiday still comes back as
/// itself; short enough that a household's worth of one-off ids does not
/// accumulate for the life of the install.
const IDENTITY_TTL_MS: i64 = 30 * 24 * 60 * 60 * 1000;

/// The most identities kept, after which the least recently seen is evicted.
///
/// A hard bound is what makes the store safe to fill from the handshake at all:
/// a peer inventing ids can churn the table but never grow it. An evicted client
/// is simply unknown again and is issued a fresh token.
const MAX_IDENTITIES: usize = 200;

/// The longest `client_id` accepted, so one row cannot be made arbitrarily large.
pub const MAX_CLIENT_ID_LEN: usize = 128;

/// What a handshake's identity check decided.
#[derive(Debug, PartialEq, Eq)]
pub enum Identity {
    /// The token matched a known id, or the store is not in use.
    Known,
    /// First contact: this token was issued and must be persisted by the client.
    Issued(String),
    /// The id is spoken for and the presented token does not match it.
    Refused,
}

/// A record of one installation. The token is stored hashed: the store is a
/// plain file, and a token read out of it would be a working impersonation.
#[derive(Debug, Serialize, Deserialize)]
struct Record {
    token_hash: String,
    first_seen_ms: i64,
    last_seen_ms: i64,
    /// What the app calls its device, from the handshake's `client_name`.
    #[serde(default)]
    name: String,
}

/// A known app, for the panel's device list. The token hash stays behind.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct KnownApp {
    pub client_id: String,
    pub name: String,
    pub last_seen_ms: i64,
}

/// Longest device name kept; it is a display string in a narrow column.
const MAX_NAME_LEN: usize = 64;

/// The identity store, backed by the shared redb database.
pub struct ClientIdentities {
    db: Db,
}

impl ClientIdentities {
    pub fn new(db: Db) -> Self {
        Self { db }
    }

    /// Resolves a handshake's `client_id` to an identity, issuing a token on
    /// first contact.
    ///
    /// Without a database - a test `Config`, or a host with no storage path -
    /// every id is [`Identity::Known`]: an identity store that cannot persist
    /// would refuse every returning client on the next restart, which is worse
    /// than not enforcing at all.
    pub fn identify(&self, client_id: &str, token: Option<&str>, name: Option<&str>) -> Identity {
        if !self.db.is_active() {
            return Identity::Known;
        }
        let now = now_ms();
        let existing = self.db.read(|txn| {
            let table = match txn.open_table(CLIENT_IDENTITIES) {
                Ok(table) => table,
                Err(_) => return Ok(None),
            };
            Ok(table
                .get(client_id)?
                .and_then(|v| rmp_serde::from_slice::<Record>(v.value()).ok()))
        });

        match existing.flatten() {
            Some(record) => match token {
                Some(token) if hash(token) == record.token_hash => {
                    let mut record = record;
                    if let Some(name) = name {
                        record.name = clamp_name(name);
                    }
                    self.touch(client_id, record, now);
                    Identity::Known
                }
                _ => Identity::Refused,
            },
            None => {
                let token = new_token();
                let record = Record {
                    token_hash: hash(&token),
                    first_seen_ms: now,
                    last_seen_ms: now,
                    name: name.map(clamp_name).unwrap_or_default(),
                };
                self.store(client_id, &record, now);
                Identity::Issued(token)
            }
        }
    }

    fn touch(&self, client_id: &str, mut record: Record, now: i64) {
        record.last_seen_ms = now;
        self.store(client_id, &record, now);
    }

    fn store(&self, client_id: &str, record: &Record, now: i64) {
        let Ok(bytes) = rmp_serde::to_vec_named(record) else {
            return;
        };
        self.db.write(Durability::Immediate, |txn| {
            {
                let mut table = txn.open_table(CLIENT_IDENTITIES)?;
                table.insert(client_id, bytes.as_slice())?;
            }
            prune(txn, now)
        });
    }

    /// Whether a handshake that [`identify`](Self::identify) accepted proved its id.
    ///
    /// Without a database every id is accepted unchecked, so none is proven.
    pub fn verifies(&self) -> bool {
        self.db.is_active()
    }

    /// Every identity the store holds, newest contact first. For the diagnostics
    /// report and the settings panel; the token hash is never exposed.
    pub fn seen(&self) -> Vec<KnownApp> {
        let mut all = self
            .db
            .read(|txn| {
                let table = match txn.open_table(CLIENT_IDENTITIES) {
                    Ok(table) => table,
                    Err(_) => return Ok(Vec::new()),
                };
                let mut out = Vec::new();
                for entry in table.range::<&str>(..)? {
                    let (id, value) = entry?;
                    if let Ok(record) = rmp_serde::from_slice::<Record>(value.value()) {
                        out.push(KnownApp {
                            client_id: id.value().to_owned(),
                            name: record.name,
                            last_seen_ms: record.last_seen_ms,
                        });
                    }
                }
                Ok(out)
            })
            .unwrap_or_default();
        all.sort_by_key(|app| std::cmp::Reverse(app.last_seen_ms));
        all
    }
}

/// Drops what has aged out, then what does not fit.
///
/// Runs inside the write that added an entry, so the bound is enforced at the
/// only moment the table can grow - no background sweep, and no window in which
/// an unbounded table exists.
fn prune(txn: &redb::WriteTransaction, now: i64) -> Result<(), redb::Error> {
    let pinned = crate::server::permissions::roles::pinned_apps(txn)?;
    let mut table = txn.open_table(CLIENT_IDENTITIES)?;
    let mut live: Vec<(String, i64)> = Vec::new();
    let mut expired: Vec<String> = Vec::new();
    for entry in table.range::<&str>(..)? {
        let (id, value) = entry?;
        let id = id.value().to_owned();
        if pinned.contains(&id) {
            continue;
        }
        match rmp_serde::from_slice::<Record>(value.value()) {
            Ok(record) if now.saturating_sub(record.last_seen_ms) < IDENTITY_TTL_MS => {
                live.push((id, record.last_seen_ms));
            }
            // Aged out, or a record this build cannot read: either way it is not
            // holding a live identity and the row is worth its space back.
            _ => expired.push(id),
        }
    }
    for id in expired {
        table.remove(id.as_str())?;
    }
    if live.len() > MAX_IDENTITIES {
        live.sort_by_key(|(_, last_seen)| *last_seen);
        for (id, _) in live.iter().take(live.len() - MAX_IDENTITIES) {
            table.remove(id.as_str())?;
        }
    }
    Ok(())
}

/// A device name as it is kept: trimmed, bounded, no control characters.
fn clamp_name(name: &str) -> String {
    name.trim()
        .chars()
        .filter(|c| !c.is_control())
        .take(MAX_NAME_LEN)
        .collect()
}

/// A 160-bit token, hex encoded, from the OS entropy source.
fn new_token() -> String {
    let mut bytes = [0u8; 20];
    if getrandom::fill(&mut bytes).is_err() {
        // Entropy is not optional here: a predictable token is worse than an
        // unusable one, so fail closed with a value nothing can present.
        return String::new();
    }
    hex(&bytes)
}

fn hash(token: &str) -> String {
    use sha1::Digest;
    hex(&sha1::Sha1::digest(token.as_bytes()))
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

fn now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn store(name: &str) -> (ClientIdentities, std::path::PathBuf) {
        let dir = std::env::temp_dir().join(format!("mbrc-clients-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        (ClientIdentities::new(Db::open(&path)), dir)
    }

    #[test]
    fn first_contact_is_issued_a_token_and_the_token_lets_it_back_in() {
        let (clients, _dir) = store("issue");

        let Identity::Issued(token) = clients.identify("install-a", None, None) else {
            panic!("first contact should be issued a token");
        };
        assert!(!token.is_empty());
        assert_eq!(
            clients.identify("install-a", Some(&token), None),
            Identity::Known
        );
    }

    /// The restored-backup case: a second install carrying the same persisted id
    /// but not the token must not be able to take the identity over.
    #[test]
    fn a_known_id_without_its_token_is_refused() {
        let (clients, _dir) = store("refuse");
        let Identity::Issued(token) = clients.identify("install-b", None, None) else {
            panic!("first contact should be issued a token");
        };

        assert_eq!(clients.identify("install-b", None, None), Identity::Refused);
        assert_eq!(
            clients.identify("install-b", Some("not-the-token"), None),
            Identity::Refused
        );
        // The rightful owner is unaffected by the attempts.
        assert_eq!(
            clients.identify("install-b", Some(&token), None),
            Identity::Known
        );
    }

    /// Without a database there is nothing to remember a token with, so enforcing
    /// one would lock every client out after a restart.
    #[test]
    fn without_a_database_every_id_is_known() {
        let clients = ClientIdentities::new(Db::disabled());
        assert_eq!(clients.identify("install-c", None, None), Identity::Known);
        assert_eq!(
            clients.identify("install-c", Some("anything"), None),
            Identity::Known
        );
    }

    /// The bound is what makes the table safe to fill from the handshake:
    /// inventing ids churns it, it never grows it.
    #[test]
    fn the_store_is_capped_and_evicts_the_least_recently_seen() {
        let (clients, _dir) = store("cap");
        for i in 0..(MAX_IDENTITIES + 20) {
            clients.identify(&format!("install-{i:04}"), None, None);
        }
        let seen = clients.seen();
        assert!(
            seen.len() <= MAX_IDENTITIES,
            "{} identities kept, cap is {MAX_IDENTITIES}",
            seen.len()
        );
        // The survivors are the newest: the first ids inserted are the ones gone.
        assert!(!seen.iter().any(|app| app.client_id == "install-0000"));
    }

    /// Its token is what keeps an app's role from being claimed, so an app with
    /// a role survives the cap that would otherwise evict it first.
    #[test]
    fn an_app_with_a_party_mode_role_is_never_evicted() {
        use crate::server::permissions::{Principal, Role, Roles};

        let dir = std::env::temp_dir().join(format!("mbrc-clients-pin-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let db = Db::open(&dir.to_string_lossy());
        let clients = ClientIdentities::new(db.clone());
        let roles = Roles::default();
        roles.open(db);

        clients.identify("install-host", None, None);
        roles.assign(&Principal::App("install-host".into()), Role::Host);
        for i in 0..(MAX_IDENTITIES + 20) {
            clients.identify(&format!("install-{i:04}"), None, None);
        }
        assert!(
            clients
                .seen()
                .iter()
                .any(|app| app.client_id == "install-host")
        );
    }

    #[test]
    fn a_handshake_name_is_kept_bounded_and_a_later_one_replaces_it() {
        let (clients, _dir) = store("name");
        let Identity::Issued(token) = clients.identify("install-n", None, Some("  Pixel 8\n "))
        else {
            panic!("first contact should be issued a token");
        };
        assert_eq!(clients.seen()[0].name, "Pixel 8");
        clients.identify("install-n", Some(&token), None);
        assert_eq!(
            clients.seen()[0].name,
            "Pixel 8",
            "no name keeps the old one"
        );
        let long = "x".repeat(MAX_NAME_LEN + 10);
        clients.identify("install-n", Some(&token), Some(&long));
        assert_eq!(clients.seen()[0].name.len(), MAX_NAME_LEN);
    }

    #[test]
    fn tokens_are_not_stored_in_the_clear() {
        let (clients, _dir) = store("hashing");
        let Identity::Issued(token) = clients.identify("install-d", None, None) else {
            panic!("first contact should be issued a token");
        };
        assert_ne!(hash(&token), token);
        assert_eq!(hash(&token).len(), 40);
    }
}
