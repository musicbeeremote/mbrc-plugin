//! V4 / V5 context -> [`Action`] map.
//!
//! Most V4 setters double as getters: `playervolume` with a value sets it, with
//! none it answers the current volume. Only the set form needs a capability, so
//! each one is judged with the same parse its handler uses.

use serde_json::Value;

use super::{Action, Capability};
use crate::server::commands::{as_bool_lenient, as_int_lenient, as_set_string};
use crate::wire::WireCodec;

/// The contexts that set a value when given one and answer it when not.
///
/// A refused set is answered with the current value, so the client's control
/// snaps back instead of showing a value that never applied.
pub const GET_OR_SET: &[&str] = &[
    "playervolume",
    "playermute",
    "scrobbler",
    "playershuffle",
    "playerrepeat",
    "nowplayingposition",
    "nowplayingrating",
    "nowplayinglfmrating",
];

/// The action a V4 request performs. `None` for a context the map does not know.
pub fn action(context: &str, data: &Value, codec: &dyn WireCodec) -> Option<Action> {
    use Capability::*;
    let set_if = |sets: bool, capability| {
        if sets {
            Action::Change(capability)
        } else {
            Action::Read
        }
    };
    let text = data.as_str();
    let toggles = text == Some("toggle");
    Some(match context {
        "playerplay"
        | "playerpause"
        | "playerplaypause"
        | "playerstop"
        | "playernext"
        | "playerprevious"
        | "nowplayinglistplay"
        | "nowplayinglistsearch" => Action::Change(Playback),
        "nowplayingposition" => set_if(as_int_lenient(data).is_some(), Playback),
        "nowplayingqueue" => Action::queue(
            codec.parse_queue_type(data.get("queue").and_then(Value::as_str).unwrap_or("next")),
            Some(queued_paths(data)),
        ),
        "libraryplayall" | "playlistplay" => Action::Change(QueueReplace),
        "nowplayinglistremove" | "nowplayinglistmove" => Action::Change(QueueEdit),
        "playervolume" => set_if(as_int_lenient(data).is_some(), Volume),
        "playermute" => set_if(toggles || as_bool_lenient(data).is_some(), Volume),
        "scrobbler" => set_if(toggles || as_bool_lenient(data).is_some(), Modes),
        "playershuffle" => set_if(
            matches!(text, Some("toggle" | "autodj" | "shuffle" | "off")),
            Modes,
        ),
        "playerrepeat" => set_if(
            text.is_some_and(|t| t == "toggle" || codec.parse_repeat(t).is_some()),
            Modes,
        ),
        "nowplayingrating" => set_if(as_set_string(data).is_some(), LibraryEdit),
        "nowplayinglfmrating" => set_if(
            text.is_some_and(|t| t.eq_ignore_ascii_case("toggle") || codec.parse_lfm(t).is_some()),
            LibraryEdit,
        ),
        "nowplayingtagchange" => Action::Change(LibraryEdit),
        "playeroutputswitch" => Action::Change(Output),
        "playerstatus"
        | "playeroutput"
        | "nowplayingtrack"
        | "nowplayingdetails"
        | "nowplayingcover"
        | "nowplayinglyrics"
        | "nowplayingcurrentposition"
        | "nowplayinglist"
        | "browsegenres"
        | "browseartists"
        | "browsealbums"
        | "browsetracks"
        | "librarygenreartists"
        | "libraryartistalbums"
        | "libraryalbumtracks"
        | "libraryalbumcover"
        | "librarycovercachebuildstatus"
        | "radiostations"
        | "playlistlist"
        | "pluginversion"
        | "init" => Action::Read,
        _ => return None,
    })
}

/// How many paths a `nowplayingqueue` request would queue: its string entries,
/// which is what the handler keeps.
fn queued_paths(data: &Value) -> usize {
    data.get("data")
        .and_then(Value::as_array)
        .map_or(0, |items| items.iter().filter(|v| v.is_string()).count())
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use super::*;
    use crate::protocol::messages::QueueType;
    use crate::server::commands::DISPATCHED_CONTEXTS;
    use crate::server::permissions::Role;
    use crate::wire::V4_CODEC;

    fn act(context: &str, data: Value) -> Action {
        action(context, &data, &V4_CODEC).unwrap()
    }

    #[test]
    fn every_dispatched_context_has_a_place_in_the_map() {
        for context in DISPATCHED_CONTEXTS
            .iter()
            .chain(&["nowplayingcurrentposition"])
        {
            assert!(
                action(context, &Value::Null, &V4_CODEC).is_some(),
                "{context} has no capability"
            );
        }
    }

    #[test]
    fn a_get_or_set_context_queried_is_a_read() {
        for context in GET_OR_SET {
            assert_eq!(act(context, Value::Null), Action::Read, "{context} null");
            assert_eq!(act(context, json!({})), Action::Read, "{context} object");
        }
    }

    #[test]
    fn a_get_or_set_context_with_a_value_needs_its_capability() {
        use Capability::*;
        for (context, data, capability) in [
            ("playervolume", json!(40), Volume),
            ("playervolume", json!("40"), Volume),
            ("playermute", json!("toggle"), Volume),
            ("playermute", json!(true), Volume),
            ("scrobbler", json!("false"), Modes),
            ("playershuffle", json!("autodj"), Modes),
            ("playerrepeat", json!("toggle"), Modes),
            ("playerrepeat", json!("All"), Modes),
            ("nowplayingposition", json!(1000), Playback),
            ("nowplayingrating", json!(5), LibraryEdit),
            ("nowplayinglfmrating", json!("Toggle"), LibraryEdit),
            ("nowplayinglfmrating", json!("love"), LibraryEdit),
        ] {
            assert_eq!(
                act(context, data.clone()),
                Action::Change(capability),
                "{context} {data}"
            );
        }
    }

    #[test]
    fn an_unrecognised_setter_value_is_a_read_as_the_handler_treats_it() {
        assert_eq!(act("playerrepeat", json!("all")), Action::Read);
        assert_eq!(act("playershuffle", json!("sideways")), Action::Read);
        assert_eq!(act("nowplayinglfmrating", json!("meh")), Action::Read);
    }

    #[test]
    fn the_queue_field_decides_the_capability_and_an_unknown_one_means_next() {
        let q = |queue| act("nowplayingqueue", json!({ "queue": queue, "data": ["a"] }));
        assert_eq!(q("last"), Action::queue(QueueType::Last, Some(1)));
        assert_eq!(q("now"), Action::queue(QueueType::PlayNow, Some(1)));
        assert_eq!(q("add-all"), Action::queue(QueueType::AddAndPlay, Some(1)));
        assert_eq!(q("sideways"), Action::queue(QueueType::Next, Some(1)));
        assert_eq!(
            act("nowplayingqueue", json!({ "data": ["a"] })),
            Action::queue(QueueType::Next, Some(1))
        );
    }

    #[test]
    fn a_guest_may_append_one_path_as_on_v6() {
        let guest = |data| Role::Guest.permits(&act("nowplayingqueue", data));
        assert!(guest(json!({ "queue": "last", "data": ["a"] })));
        assert!(guest(json!({ "queue": "last", "data": ["a", 7, null] })));
        assert!(!guest(json!({ "queue": "last", "data": ["a", "b"] })));
        assert!(!guest(json!({ "queue": "next", "data": ["a"] })));
        assert!(!guest(json!({ "data": ["a"] })));
    }
}
