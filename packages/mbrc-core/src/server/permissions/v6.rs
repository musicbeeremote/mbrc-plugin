//! V6 op -> [`Action`] map.

use serde_json::Value;

use super::{Action, Capability};
use crate::protocol::messages::QueueType;
use crate::server::commands_v6::nowplaying_list::parse_queue_type;

/// The action a V6 request performs. `None` for an op the map does not know.
pub fn action(op: &str, data: &Value) -> Option<Action> {
    use Capability::*;
    let change = Action::Change;
    Some(match op {
        "player_play"
        | "player_pause"
        | "player_play_pause"
        | "player_stop"
        | "player_next"
        | "player_previous"
        | "now_playing_seek"
        | "now_playing_list_play"
        | "now_playing_list_search" => change(Playback),
        "now_playing_queue" => Action::queue(
            placement(data, "next"),
            data.get("paths").and_then(Value::as_array).map(Vec::len),
        ),
        "library_queue" => Action::queue(placement(data, "last"), None),
        "podcast_episode_play" => Action::queue(placement(data, "now"), Some(1)),
        "library_play_all" | "playlist_play" => change(QueueReplace),
        "now_playing_list_remove" | "now_playing_list_move" | "now_playing_list_clear" => {
            change(QueueEdit)
        }
        "player_set_volume" | "player_set_mute" => change(Volume),
        "player_set_shuffle"
        | "player_set_repeat"
        | "player_set_stop_after_current"
        | "player_set_scrobbling" => change(Modes),
        "now_playing_set_rating" | "now_playing_set_lfm" | "now_playing_set_tag" => {
            change(LibraryEdit)
        }
        "playlist_create"
        | "playlist_delete"
        | "playlist_add_tracks"
        | "playlist_remove_tracks"
        | "playlist_move_tracks"
        | "playlist_set_tracks" => change(PlaylistEdit),
        "player_set_output" => change(Output),
        "handshake"
        | "ping"
        | "pair"
        | "player_status"
        | "player_output"
        | "system_info"
        | "track_get"
        | "cover_get"
        | "library_genres"
        | "library_artists"
        | "library_albums"
        | "library_tracks"
        | "library_radio"
        | "library_changes"
        | "playlist_list"
        | "playlist_tracks"
        | "now_playing_state"
        | "now_playing_details"
        | "now_playing_position"
        | "now_playing_lyrics"
        | "now_playing_list"
        | "podcast_subscriptions"
        | "podcast_subscription"
        | "podcast_episodes"
        | "podcast_episode" => Action::Read,
        _ => return None,
    })
}

/// Where a queueing request puts its tracks, with the op's own default.
///
/// A `mode` the handler would reject counts as replacing the queue, the most
/// privileged placement, so a malformed request never passes as an append.
fn placement(data: &Value, default: &str) -> QueueType {
    let mode = match data.get("mode") {
        None | Some(Value::Null) => default,
        Some(Value::String(mode)) => mode,
        Some(_) => return QueueType::PlayNow,
    };
    parse_queue_type(mode).unwrap_or(QueueType::PlayNow)
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use super::*;
    use crate::server::commands_v6::capabilities;
    use crate::server::permissions::Role;

    #[test]
    fn every_advertised_op_has_a_place_in_the_map() {
        let caps = capabilities();
        for op in caps["ops"].as_array().unwrap() {
            let op = op.as_str().unwrap();
            assert!(action(op, &json!({})).is_some(), "{op} has no capability");
        }
    }

    #[test]
    fn an_unknown_op_is_not_mapped() {
        assert_eq!(action("player_self_destruct", &json!({})), None);
    }

    #[test]
    fn the_queue_mode_decides_the_capability() {
        let q = |mode| {
            action(
                "now_playing_queue",
                &json!({ "paths": ["a"], "mode": mode }),
            )
        };
        assert_eq!(q("last"), Some(Action::queue(QueueType::Last, Some(1))));
        assert_eq!(q("next"), Some(Action::queue(QueueType::Next, Some(1))));
        assert_eq!(q("now"), Some(Action::queue(QueueType::PlayNow, Some(1))));
        assert_eq!(
            q("add_all"),
            Some(Action::queue(QueueType::AddAndPlay, Some(1)))
        );
    }

    #[test]
    fn each_queueing_op_is_judged_by_its_own_default_mode() {
        let q = |op, data| action(op, &data).unwrap();
        assert_eq!(
            q("now_playing_queue", json!({ "paths": ["a"] })),
            Action::queue(QueueType::Next, Some(1))
        );
        assert_eq!(
            q("library_queue", json!({ "artist": "x" })),
            Action::queue(QueueType::Last, None)
        );
        assert_eq!(
            q("podcast_episode_play", json!({ "id": "p", "index": 0 })),
            Action::queue(QueueType::PlayNow, Some(1))
        );
    }

    #[test]
    fn a_malformed_mode_counts_as_replacing_the_queue() {
        for mode in [json!("sideways"), json!(3), json!(["last"])] {
            let got = action(
                "now_playing_queue",
                &json!({ "paths": ["a"], "mode": mode }),
            );
            assert_eq!(
                got,
                Some(Action::queue(QueueType::PlayNow, Some(1))),
                "{mode}"
            );
        }
    }

    #[test]
    fn a_guest_may_append_one_path_or_one_episode_and_nothing_wider() {
        let guest = |op, data| Role::Guest.permits(&action(op, &data).unwrap());
        assert!(guest(
            "now_playing_queue",
            json!({ "paths": ["a"], "mode": "last" })
        ));
        assert!(guest(
            "podcast_episode_play",
            json!({ "id": "p", "index": 0, "mode": "last" })
        ));
        assert!(!guest(
            "now_playing_queue",
            json!({ "paths": ["a", "b"], "mode": "last" })
        ));
        assert!(!guest("now_playing_queue", json!({ "mode": "last" })));
        assert!(!guest(
            "library_queue",
            json!({ "album": "x", "mode": "last" })
        ));
        assert!(!guest(
            "podcast_episode_play",
            json!({ "id": "p", "index": 0 })
        ));
        assert!(!guest("playlist_play", json!({ "url": "p" })));
    }
}
