//! Who a client is, and the role the host has given it.
//!
//! A role rests on something a guest cannot copy: a V6 app's `client_id` only
//! counts once its `client_token` has been checked, and a browser is known by
//! its pairing token. Android 1.6 sends a plain-text `client_id` and nothing
//! more, which is weaker trust; the panel says so beside any such device. Every
//! other client is [`Principal::Anonymous`] and always has the default role.
//!
//! Roles are one table in `mbrc.redb` keyed by [`Principal::key`], mirrored in
//! memory so the check on every request is a map lookup.

use std::collections::{HashMap, HashSet};
use std::sync::Mutex;

use redb::{Durability, ReadableTable};

use super::Role;
use crate::store::{Db, PARTY_ROLES};

const APP_PREFIX: &str = "app:";

/// Who a connection or request is, as far as Party Mode can tell.
#[derive(Debug, Clone, Default, PartialEq, Eq, Hash)]
pub enum Principal {
    /// A V6 app whose `client_id` was verified by its `client_token`.
    App(String),
    /// A paired browser, by the id its pairing token hashes to.
    Browser(String),
    /// An Android 1.6 V4 client, by its unverified `client_id`.
    LegacyDevice(String),
    /// A client that cannot be told apart from any other.
    #[default]
    Anonymous,
}

impl Principal {
    /// The key its role is stored under; `None` for a principal that cannot hold one.
    pub fn key(&self) -> Option<String> {
        match self {
            Principal::App(id) => Some(format!("{APP_PREFIX}{id}")),
            Principal::Browser(id) => Some(format!("browser:{id}")),
            Principal::LegacyDevice(id) => Some(format!("v4:{id}")),
            Principal::Anonymous => None,
        }
    }
}

impl Role {
    /// The stored and on-wire name.
    pub fn as_str(self) -> &'static str {
        match self {
            Role::Host => "host",
            Role::Dj => "dj",
            Role::Guest => "guest",
            Role::Listener => "listener",
        }
    }

    pub fn parse(name: &str) -> Option<Role> {
        Some(match name {
            "host" => Role::Host,
            "dj" => Role::Dj,
            "guest" => Role::Guest,
            "listener" => Role::Listener,
            _ => return None,
        })
    }
}

/// The roles the host has assigned.
#[derive(Default)]
pub struct Roles {
    assigned: Mutex<HashMap<String, Role>>,
    db: Mutex<Option<Db>>,
}

impl std::fmt::Debug for Roles {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Roles")
            .field("assigned", &self.lock().len())
            .finish_non_exhaustive()
    }
}

impl Roles {
    /// Attaches the store and reads back the roles assigned in an earlier run.
    pub fn open(&self, db: Db) {
        let stored = db
            .read(|txn| {
                let table = match txn.open_table(PARTY_ROLES) {
                    Ok(table) => table,
                    Err(_) => return Ok(Vec::new()),
                };
                let mut rows = Vec::new();
                for row in table.iter()? {
                    let (key, role) = row?;
                    if let Some(role) = Role::parse(role.value()) {
                        rows.push((key.value().to_owned(), role));
                    }
                }
                Ok(rows)
            })
            .unwrap_or_default();
        self.lock().extend(stored);
        *self.store() = Some(db);
    }

    /// The role `principal` holds: the one assigned to it, or the default.
    pub fn role_of(&self, principal: &Principal) -> Role {
        principal
            .key()
            .and_then(|key| self.lock().get(&key).copied())
            .unwrap_or_default()
    }

    /// Gives `principal` a role. False for a principal that cannot hold one.
    pub fn assign(&self, principal: &Principal, role: Role) -> bool {
        let Some(key) = principal.key() else {
            return false;
        };
        self.lock().insert(key.clone(), role);
        self.persist(|txn| {
            txn.open_table(PARTY_ROLES)?
                .insert(key.as_str(), role.as_str())?;
            Ok(())
        });
        true
    }

    /// Returns `principal` to the default role.
    pub fn unassign(&self, principal: &Principal) {
        let Some(key) = principal.key() else {
            return;
        };
        self.lock().remove(&key);
        self.persist(|txn| {
            txn.open_table(PARTY_ROLES)?.remove(key.as_str())?;
            Ok(())
        });
    }

    /// Returns every paired browser to the default role.
    pub fn unassign_browsers(&self) {
        let keys: Vec<String> = {
            let mut assigned = self.lock();
            let keys = assigned
                .keys()
                .filter(|key| key.starts_with("browser:"))
                .cloned()
                .collect();
            assigned.retain(|key, _| !key.starts_with("browser:"));
            keys
        };
        self.persist(|txn| {
            let mut table = txn.open_table(PARTY_ROLES)?;
            for key in &keys {
                table.remove(key.as_str())?;
            }
            Ok(())
        });
    }

    fn persist(&self, f: impl FnOnce(&redb::WriteTransaction) -> Result<(), redb::Error>) {
        if let Some(db) = self.store().clone() {
            db.write(Durability::Immediate, f);
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, HashMap<String, Role>> {
        self.assigned.lock().unwrap_or_else(|p| p.into_inner())
    }

    fn store(&self) -> std::sync::MutexGuard<'_, Option<Db>> {
        self.db.lock().unwrap_or_else(|p| p.into_inner())
    }
}

/// The V6 app `client_id`s that hold a role, read inside a write transaction.
///
/// An app with a role is pinned: its identity record never ages out, because its
/// `client_token` is what keeps the role from being claimed by someone else.
pub(crate) fn pinned_apps(txn: &redb::WriteTransaction) -> Result<HashSet<String>, redb::Error> {
    let table = txn.open_table(PARTY_ROLES)?;
    let mut pinned = HashSet::new();
    for row in table.range::<&str>(APP_PREFIX..)? {
        let (key, _) = row?;
        match key.value().strip_prefix(APP_PREFIX) {
            Some(id) => pinned.insert(id.to_owned()),
            None => break,
        };
    }
    Ok(pinned)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn app(id: &str) -> Principal {
        Principal::App(id.to_owned())
    }

    #[test]
    fn an_unassigned_or_anonymous_client_has_the_default_role() {
        let roles = Roles::default();
        assert_eq!(roles.role_of(&app("a")), Role::Guest);
        assert_eq!(roles.role_of(&Principal::Anonymous), Role::Guest);
        assert!(!roles.assign(&Principal::Anonymous, Role::Host));
    }

    #[test]
    fn the_same_id_on_different_transports_is_a_different_principal() {
        let roles = Roles::default();
        roles.assign(&app("x"), Role::Host);
        assert_eq!(roles.role_of(&app("x")), Role::Host);
        assert_eq!(
            roles.role_of(&Principal::LegacyDevice("x".into())),
            Role::Guest
        );
        assert_eq!(roles.role_of(&Principal::Browser("x".into())), Role::Guest);
    }

    #[test]
    fn every_role_name_round_trips() {
        for role in [Role::Host, Role::Dj, Role::Guest, Role::Listener] {
            assert_eq!(Role::parse(role.as_str()), Some(role));
        }
    }

    #[test]
    fn unpairing_every_browser_leaves_other_roles_alone() {
        let roles = Roles::default();
        roles.assign(&Principal::Browser("b".into()), Role::Dj);
        roles.assign(&app("a"), Role::Dj);
        roles.unassign_browsers();
        assert_eq!(roles.role_of(&Principal::Browser("b".into())), Role::Guest);
        assert_eq!(roles.role_of(&app("a")), Role::Dj);
    }

    #[test]
    fn roles_outlive_a_restart() {
        let dir = std::env::temp_dir().join(format!("mbrc-roles-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        {
            let roles = Roles::default();
            roles.open(Db::open(&path));
            roles.assign(&app("a"), Role::Host);
            roles.assign(&Principal::Browser("b".into()), Role::Dj);
            roles.unassign(&Principal::Browser("b".into()));
        }
        let roles = Roles::default();
        roles.open(Db::open(&path));
        assert_eq!(roles.role_of(&app("a")), Role::Host);
        assert_eq!(roles.role_of(&Principal::Browser("b".into())), Role::Guest);
    }
}
