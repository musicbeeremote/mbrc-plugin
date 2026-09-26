//! Party Mode permissions (#107): what each role may do, and what a request needs.
//!
//! A request is classified into an [`Action`] from its op or context *and* its
//! data, because the queue mode decides whether adding tracks appends, cuts in,
//! or replaces the queue. A [`Role`] then permits the action or not. Reads are
//! allowed in every role.
//!
//! Classification returns `None` for an op the map does not know. The tests
//! walk every advertised V6 op and every dispatched V4 context, so a new op
//! cannot ship without a place in the map.

pub mod gate;
pub mod v4;
pub mod v6;

pub use gate::{PartyMode, Refusal};

use crate::protocol::messages::QueueType;

/// A group of state-changing operations a role may be granted.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Capability {
    /// Play, pause, stop, skip, seek, jump to a queued track.
    Playback,
    /// Append to the end of the queue.
    QueueAdd,
    /// Insert after the playing track.
    QueueInsert,
    /// Replace the queue: play now, add-all, play all, play a playlist.
    QueueReplace,
    /// Remove, move, clear.
    QueueEdit,
    Volume,
    /// Shuffle, repeat, stop-after-current, scrobbling.
    Modes,
    /// Rating, love/ban, tag edits.
    LibraryEdit,
    PlaylistEdit,
    /// Switch the output device.
    Output,
}

impl Capability {
    pub const ALL: &[Capability] = &[
        Capability::Playback,
        Capability::QueueAdd,
        Capability::QueueInsert,
        Capability::QueueReplace,
        Capability::QueueEdit,
        Capability::Volume,
        Capability::Modes,
        Capability::LibraryEdit,
        Capability::PlaylistEdit,
        Capability::Output,
    ];

    /// The name clients and the panel see.
    pub fn as_str(self) -> &'static str {
        match self {
            Capability::Playback => "playback",
            Capability::QueueAdd => "queue_add",
            Capability::QueueInsert => "queue_insert",
            Capability::QueueReplace => "queue_replace",
            Capability::QueueEdit => "queue_edit",
            Capability::Volume => "volume",
            Capability::Modes => "modes",
            Capability::LibraryEdit => "library_edit",
            Capability::PlaylistEdit => "playlist_edit",
            Capability::Output => "output",
        }
    }

    /// The capability that queueing at `placement` needs.
    pub fn for_queue(placement: QueueType) -> Self {
        match placement {
            QueueType::Last => Capability::QueueAdd,
            QueueType::Next => Capability::QueueInsert,
            QueueType::PlayNow | QueueType::AddAndPlay => Capability::QueueReplace,
        }
    }
}

/// A client's standing while Party Mode is on. Unassigned clients are [`Role::Guest`].
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum Role {
    Host,
    Dj,
    #[default]
    Guest,
    Listener,
}

impl Role {
    /// The capabilities this role holds.
    pub fn capabilities(self) -> &'static [Capability] {
        match self {
            Role::Host => Capability::ALL,
            Role::Dj => &[
                Capability::Playback,
                Capability::QueueAdd,
                Capability::QueueInsert,
                Capability::QueueReplace,
                Capability::QueueEdit,
                Capability::Volume,
                Capability::Modes,
            ],
            Role::Guest => &[Capability::QueueAdd],
            Role::Listener => &[],
        }
    }

    /// How many tracks one request may queue, when the role is limited.
    pub fn max_tracks_per_add(self) -> Option<usize> {
        match self {
            Role::Guest => Some(1),
            _ => None,
        }
    }

    /// Whether this role may perform `action`.
    pub fn permits(self, action: &Action) -> bool {
        match *action {
            Action::Read => true,
            Action::Change(capability) => self.capabilities().contains(&capability),
            Action::Queue { capability, tracks } => {
                self.capabilities().contains(&capability)
                    && self
                        .max_tracks_per_add()
                        .is_none_or(|max| tracks.is_some_and(|n| n <= max))
            }
        }
    }
}

/// What a request does, as far as permissions care.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Action {
    /// Reads state; allowed in every role.
    Read,
    /// Changes state under one capability.
    Change(Capability),
    /// Adds tracks to the queue.
    ///
    /// `tracks` is `None` when the request names a scope rather than paths, so
    /// its size is unknown until it runs; a role with a per-request limit refuses it.
    Queue {
        capability: Capability,
        tracks: Option<usize>,
    },
}

impl Action {
    /// The capability this action needs; `None` for a read.
    pub fn capability(&self) -> Option<Capability> {
        match *self {
            Action::Read => None,
            Action::Change(capability) | Action::Queue { capability, .. } => Some(capability),
        }
    }

    /// Queueing `tracks` at `placement`.
    pub fn queue(placement: QueueType, tracks: Option<usize>) -> Self {
        Action::Queue {
            capability: Capability::for_queue(placement),
            tracks,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn host_holds_every_capability_and_no_track_limit() {
        for capability in Capability::ALL {
            assert!(Role::Host.permits(&Action::Change(*capability)));
        }
        assert!(Role::Host.permits(&Action::queue(QueueType::PlayNow, None)));
    }

    #[test]
    fn every_role_may_read() {
        for role in [Role::Host, Role::Dj, Role::Guest, Role::Listener] {
            assert!(role.permits(&Action::Read), "{role:?}");
        }
    }

    #[test]
    fn a_dj_runs_the_queue_but_not_the_library_playlists_or_output() {
        assert!(Role::Dj.permits(&Action::queue(QueueType::AddAndPlay, None)));
        assert!(Role::Dj.permits(&Action::Change(Capability::QueueEdit)));
        for capability in [
            Capability::LibraryEdit,
            Capability::PlaylistEdit,
            Capability::Output,
        ] {
            assert!(
                !Role::Dj.permits(&Action::Change(capability)),
                "{capability:?}"
            );
        }
    }

    #[test]
    fn a_guest_may_append_exactly_one_track() {
        assert!(Role::Guest.permits(&Action::queue(QueueType::Last, Some(1))));
        assert!(!Role::Guest.permits(&Action::queue(QueueType::Last, Some(2))));
        assert!(!Role::Guest.permits(&Action::queue(QueueType::Last, None)));
        assert!(!Role::Guest.permits(&Action::queue(QueueType::Next, Some(1))));
        assert!(!Role::Guest.permits(&Action::queue(QueueType::PlayNow, Some(1))));
        assert!(!Role::Guest.permits(&Action::Change(Capability::Playback)));
    }

    #[test]
    fn a_guest_appending_no_tracks_is_a_harmless_no_op() {
        assert!(Role::Guest.permits(&Action::queue(QueueType::Last, Some(0))));
    }

    #[test]
    fn a_listener_changes_nothing() {
        for capability in Capability::ALL {
            assert!(!Role::Listener.permits(&Action::Change(*capability)));
        }
        assert!(!Role::Listener.permits(&Action::queue(QueueType::Last, Some(1))));
    }
}
