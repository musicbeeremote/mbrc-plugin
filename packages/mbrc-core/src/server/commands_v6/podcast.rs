//! V6 podcast domain: subscriptions, their episodes, and playing one (#37).
//!
//! Wholly additive. Nothing here touches the player, library, playlist or
//! now-playing surface, and playing an episode is the ordinary queue command
//! with a URL the core resolved.
//!
//! An episode is addressed by `(subscription id, index)` because that is the
//! only key MusicBee takes. The feed's own episode `id` travels as a field so a
//! client can recognise an episode across a re-read, but it opens nothing.
//! Addressing is best-effort: MusicBee announces nothing about podcasts, so no
//! version guards the indices the way one guards the now-playing queue. A client
//! acting on an index the subscription has since shifted hits its neighbour,
//! which is the whole cost of the drift.
//!
//! The host enumerates a subscription's episodes in one call and reads metadata
//! for the served window alone, so a feed of hundreds costs a page.

use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use super::{OpResult, V6Error, i32_saturating, internal, page_args, page_json, req_i64, req_str};
use crate::cover::store::CoverStore;
use crate::protocol::messages::{PodcastEpisode, PodcastSubscription, QueueType};
use crate::providers::Providers;
use crate::server::commands_v6::nowplaying_list::parse_queue_type;
use crate::server::commands_v6::track::parse_duration_ms;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "podcast_subscriptions",
    "podcast_subscription",
    "podcast_episodes",
    "podcast_episode",
    "podcast_episode_play",
];

/// Dispatch a `podcast_*` op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    store: Option<&CoverStore>,
) -> Option<OpResult> {
    Some(match op {
        "podcast_subscriptions" => subscriptions(data, p, store),
        "podcast_subscription" => subscription(data, p, store),
        "podcast_episodes" => episodes(data, p),
        "podcast_episode" => episode(data, p),
        "podcast_episode_play" => episode_play(data, p),
        _ => return None,
    })
}

fn subscriptions(data: &Value, p: &dyn Providers, store: Option<&CoverStore>) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let page = p
        .podcast_subscriptions(i32_saturating(offset), i32_saturating(limit))
        .map_err(internal)?;
    let items = page
        .data
        .iter()
        .map(|s| subscription_json(s, artwork_hash(p, store, &s.id).as_deref()))
        .collect();
    Ok(page_json(page.total.max(0) as usize, offset, items))
}

fn subscription(data: &Value, p: &dyn Providers, store: Option<&CoverStore>) -> OpResult {
    let id = req_str(data, "id")?;
    let found = p
        .podcast_subscription(id)
        .map_err(internal)?
        .into_iter()
        .next()
        .ok_or_else(|| not_found(format!("no subscription with id: {id}")))?;
    let hash = artwork_hash(p, store, &found.id);
    Ok(subscription_json(&found, hash.as_deref()))
}

fn episodes(data: &Value, p: &dyn Providers) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let id = req_str(data, "id")?;
    let page = p
        .podcast_episodes(id, i32_saturating(offset), i32_saturating(limit))
        .map_err(internal)?;
    let items = page.data.iter().map(episode_json).collect();
    Ok(page_json(page.total.max(0) as usize, offset, items))
}

fn episode(data: &Value, p: &dyn Providers) -> OpResult {
    Ok(episode_json(&one_episode(data, p)?))
}

/// Plays an episode, or queues it, by handing its URL to the ordinary queue.
///
/// `mode` defaults to `now`: reaching for one episode of one podcast is asking
/// to hear it, where reaching into the library is usually building a queue.
/// Downloaded or not makes no difference; MusicBee streams a remote URL exactly
/// as it does from its own window, so gating on `is_downloaded` would refuse
/// what the application itself allows.
fn episode_play(data: &Value, p: &dyn Providers) -> OpResult {
    let found = one_episode(data, p)?;
    let mode = data.get("mode").and_then(Value::as_str).unwrap_or("now");
    let queue_type = parse_queue_type(mode)?;
    if found.url.is_empty() {
        return Err(not_found("that episode has no url to play".to_string()));
    }
    let play = match queue_type {
        QueueType::AddAndPlay => found.url.clone(),
        _ => String::new(),
    };
    p.queue(queue_type, vec![found.url], &play)
        .map_err(internal)?;
    Ok(json!({}))
}

/// The episode a request names, or `not_found` for an id or index that is not there.
fn one_episode(data: &Value, p: &dyn Providers) -> Result<PodcastEpisode, V6Error> {
    let id = req_str(data, "id")?;
    let index = req_i64(data, "index")?;
    p.podcast_episode(id, i32_saturating(index))
        .map_err(internal)?
        .into_iter()
        .next()
        .ok_or_else(|| not_found(format!("no episode {index} in subscription: {id}")))
}

/// The hash of a subscription's artwork, fetched from the host the first time.
///
/// Resolved for the page being served rather than up front: a client looking at
/// ten subscriptions must not pay to ingest the art of a hundred. The bytes go
/// into the same content-addressed store album art uses, under a namespace of
/// their own, so the client fetches them by hash like any other cover.
fn artwork_hash(p: &dyn Providers, store: Option<&CoverStore>, id: &str) -> Option<String> {
    let store = store?;
    let key = artwork_key(id);
    if let Some(hash) = store.hash_for(&key) {
        return Some(hash);
    }
    let raw = p.podcast_artwork(id).ok()?;
    if raw.is_empty() {
        return None;
    }
    let bytes = crate::cover::from_base64(&raw)?;
    store.cache_cover(&key, &bytes).ok()
}

/// The store key a subscription's artwork lives under.
///
/// Its own namespace beside `album:`-style keys: a subscription id is not an
/// artist-and-album pair, and two namespaces sharing a key space would let one
/// answer for the other.
fn artwork_key(id: &str) -> String {
    format!("podcast:{id}")
}

fn subscription_json(s: &PodcastSubscription, image_hash: Option<&str>) -> Value {
    let mut obj = json!({
        "id": s.id,
        "title": s.title,
        "grouping": s.grouping,
        "genre": s.genre,
        "description": s.description,
        "downloaded_count": s.downloaded_count.max(0),
        "episode_count": s.episode_count.max(0),
    });
    if let Some(hash) = image_hash {
        obj["image_hash"] = json!(hash);
    }
    obj
}

/// An episode, with the host's strings parsed into the types #112/#114 ask for.
fn episode_json(e: &PodcastEpisode) -> Value {
    json!({
        "index": e.index,
        "id": e.id,
        "title": e.title,
        "date": (!e.date.is_empty()).then_some(e.date.as_str()),
        "description": e.description,
        "duration_ms": parse_duration_ms(&e.duration),
        "is_downloaded": e.is_downloaded,
        "has_been_played": e.has_been_played,
        "url": e.url,
        "author": e.author,
    })
}

fn not_found(message: String) -> V6Error {
    V6Error::new(ErrorCode::NotFound, message)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::Page;
    use crate::providers::MockProviders;

    fn subscription(id: &str) -> PodcastSubscription {
        PodcastSubscription {
            id: id.into(),
            title: format!("{id} the podcast"),
            grouping: "Grouping".into(),
            genre: "Technology".into(),
            description: "About things".into(),
            downloaded_count: 3,
            episode_count: 12,
        }
    }

    fn episode(index: i32) -> PodcastEpisode {
        PodcastEpisode {
            index,
            id: format!("ep-{index}"),
            title: format!("Episode {index}"),
            date: "2026-01-15T00:00:00Z".into(),
            description: "What happens".into(),
            duration: "1:23:45".into(),
            is_downloaded: true,
            has_been_played: false,
            url: format!("https://feed/{index}.mp3"),
            author: "The Hosts".into(),
        }
    }

    fn providers() -> MockProviders {
        MockProviders {
            podcast_subscriptions: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![subscription("a"), subscription("b")],
            },
            podcast_episodes: Page {
                total: 3,
                offset: 0,
                limit: 0,
                data: vec![episode(0), episode(1)],
            },
            ..Default::default()
        }
    }

    #[test]
    fn subscriptions_are_a_page_of_canonical_items() {
        let m = providers();
        let out = dispatch("podcast_subscriptions", &json!({}), &m, None)
            .unwrap()
            .unwrap();
        assert_eq!(out["total"], 2);
        assert_eq!(out["items"][0]["id"], "a");
        assert_eq!(out["items"][0]["title"], "a the podcast");
        assert_eq!(out["items"][0]["downloaded_count"], 3);
        assert_eq!(out["items"][0]["episode_count"], 12);
        // No store, so no art was resolved and the field stays off the item
        // rather than arriving as a null a client has to test for.
        assert!(out["items"][0].get("image_hash").is_none());
    }

    /// The host hands back display strings; #112/#114 say the wire carries types.
    #[test]
    fn an_episode_carries_parsed_duration_and_typed_flags() {
        let m = providers();
        let out = dispatch(
            "podcast_episode",
            &json!({ "id": "a", "index": 1 }),
            &m,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["index"], 1);
        assert_eq!(out["id"], "ep-1");
        assert_eq!(out["duration_ms"], 5_025_000);
        assert_eq!(out["date"], "2026-01-15T00:00:00Z");
        assert_eq!(out["is_downloaded"], true);
        assert_eq!(out["has_been_played"], false);
        assert_eq!(out["author"], "The Hosts");
        // Playing takes the index, but the url is what names an episode outside
        // MusicBee, so a client can still see it.
        assert_eq!(out["url"], "https://feed/1.mp3");
    }

    #[test]
    fn an_episode_the_subscription_does_not_have_is_not_found() {
        let m = providers();
        let err = dispatch(
            "podcast_episode",
            &json!({ "id": "a", "index": 99 }),
            &m,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::NotFound);
    }

    #[test]
    fn an_unknown_subscription_is_not_found() {
        let m = providers();
        let err = dispatch("podcast_subscription", &json!({ "id": "nope" }), &m, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, ErrorCode::NotFound);
    }

    /// Playing an episode is the ordinary queue command with a resolved URL, so
    /// it reaches MusicBee by the path everything else already uses.
    #[test]
    fn playing_an_episode_queues_its_url() {
        let m = providers();
        dispatch(
            "podcast_episode_play",
            &json!({ "id": "a", "index": 1 }),
            &m,
            None,
        )
        .unwrap()
        .unwrap();
        assert!(
            m.recorded()
                .contains(&"queue(PlayNow,[https://feed/1.mp3],)".to_string()),
            "recorded: {:?}",
            m.recorded()
        );
    }

    /// Reaching for one episode is asking to hear it; the library's `next` default
    /// belongs to building a queue, not to opening a podcast.
    #[test]
    fn an_episode_plays_now_unless_told_otherwise() {
        let m = providers();
        dispatch(
            "podcast_episode_play",
            &json!({ "id": "a", "index": 0, "mode": "last" }),
            &m,
            None,
        )
        .unwrap()
        .unwrap();
        assert!(
            m.recorded().iter().any(|c| c.starts_with("queue(Last,")),
            "recorded: {:?}",
            m.recorded()
        );
    }

    #[test]
    fn an_unknown_mode_is_refused_rather_than_coerced() {
        let m = providers();
        let err = dispatch(
            "podcast_episode_play",
            &json!({ "id": "a", "index": 0, "mode": "someday" }),
            &m,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m, None).is_none());
    }
}
