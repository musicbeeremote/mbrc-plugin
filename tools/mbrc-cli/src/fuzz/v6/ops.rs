//! What the V6 fuzzer knows about each read-only op: the fields it reads, and
//! where a real value for each comes from.
//!
//! A valid request is built only from values the library itself returned, so it
//! reaches the handler body instead of a validation reject. A test against the
//! core's own op classification keeps this table complete: a new read-only op
//! fails it until it has a row here. Write ops never appear, since the fuzzer
//! must be safe to point at a real library.

use serde_json::{Map, Value, json};

use super::super::{MAX_BLOB, random_scalar, random_value};
use crate::rng::Rng;

/// A source of real values, filled by the harvest pass.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Pool {
    Genre,
    Artist,
    Src,
    CoverHash,
    PlaylistUrl,
    PodcastId,
    Query,
}

#[derive(Clone, Copy, Debug)]
pub enum Kind {
    Offset,
    Limit,
    Bool,
    Choice(&'static [&'static str]),
    From(Pool),
    /// `album` and `artist` from one harvested album, so they name a real one.
    Album,
    /// `id` and `index` from one harvested podcast episode.
    Episode,
}

#[derive(Clone, Copy, Debug)]
pub struct Field {
    pub name: &'static str,
    pub kind: Kind,
    pub required: bool,
}

const fn opt(name: &'static str, kind: Kind) -> Field {
    Field {
        name,
        kind,
        required: false,
    }
}

const fn req(name: &'static str, kind: Kind) -> Field {
    Field {
        name,
        kind,
        required: true,
    }
}

pub struct OpSpec {
    pub op: &'static str,
    pub fields: &'static [Field],
}

const OFFSET: Field = opt("offset", Kind::Offset);
const LIMIT: Field = opt("limit", Kind::Limit);
const ORDER: Field = opt("order", Kind::Choice(&["asc", "desc"]));
const QUERY: Field = opt("query", Kind::From(Pool::Query));
const TRACK_SORTS: &[&str] = &[
    "title",
    "artist",
    "album",
    "album_artist",
    "track",
    "year",
    "rating",
    "date_added",
];

pub const SPECS: &[OpSpec] = &[
    OpSpec {
        op: "system_info",
        fields: &[],
    },
    OpSpec {
        op: "player_status",
        fields: &[],
    },
    OpSpec {
        op: "player_output",
        fields: &[],
    },
    OpSpec {
        op: "now_playing_state",
        fields: &[opt("include_list_order", Kind::Bool)],
    },
    OpSpec {
        op: "now_playing_details",
        fields: &[],
    },
    OpSpec {
        op: "now_playing_position",
        fields: &[],
    },
    OpSpec {
        op: "now_playing_lyrics",
        fields: &[],
    },
    OpSpec {
        op: "now_playing_list",
        fields: &[
            OFFSET,
            LIMIT,
            opt("up_next", Kind::Bool),
            opt("totals", Kind::Bool),
        ],
    },
    OpSpec {
        op: "library_genres",
        fields: &[
            OFFSET,
            LIMIT,
            QUERY,
            opt("sort", Kind::Choice(&["name"])),
            ORDER,
        ],
    },
    OpSpec {
        op: "library_artists",
        fields: &[
            OFFSET,
            LIMIT,
            opt("genre", Kind::From(Pool::Genre)),
            opt("album_artists", Kind::Bool),
            QUERY,
            opt("sort", Kind::Choice(&["name"])),
            ORDER,
        ],
    },
    OpSpec {
        op: "library_albums",
        fields: &[
            OFFSET,
            LIMIT,
            opt("artist", Kind::From(Pool::Artist)),
            QUERY,
            opt("sort", Kind::Choice(&["name", "artist", "year"])),
            ORDER,
        ],
    },
    OpSpec {
        op: "library_tracks",
        fields: &[
            OFFSET,
            LIMIT,
            QUERY,
            opt("sort", Kind::Choice(TRACK_SORTS)),
            ORDER,
            opt("genre", Kind::From(Pool::Genre)),
            opt("artist", Kind::From(Pool::Artist)),
            opt("album", Kind::Album),
        ],
    },
    OpSpec {
        op: "library_radio",
        fields: &[OFFSET, LIMIT],
    },
    OpSpec {
        op: "library_changes",
        fields: &[LIMIT],
    },
    OpSpec {
        op: "track_get",
        fields: &[req("src", Kind::From(Pool::Src))],
    },
    OpSpec {
        op: "cover_get",
        fields: &[
            req("hash", Kind::From(Pool::CoverHash)),
            opt("client_hash", Kind::From(Pool::CoverHash)),
        ],
    },
    OpSpec {
        op: "playlist_list",
        fields: &[OFFSET, LIMIT],
    },
    OpSpec {
        op: "playlist_tracks",
        fields: &[
            req("url", Kind::From(Pool::PlaylistUrl)),
            OFFSET,
            LIMIT,
            QUERY,
            opt(
                "query_field",
                Kind::Choice(&["any", "title", "artist", "album"]),
            ),
            opt("totals", Kind::Bool),
        ],
    },
    OpSpec {
        op: "podcast_subscriptions",
        fields: &[OFFSET, LIMIT],
    },
    OpSpec {
        op: "podcast_subscription",
        fields: &[req("id", Kind::From(Pool::PodcastId))],
    },
    OpSpec {
        op: "podcast_episodes",
        fields: &[req("id", Kind::From(Pool::PodcastId)), OFFSET, LIMIT],
    },
    OpSpec {
        op: "podcast_episode",
        fields: &[req("id", Kind::Episode)],
    },
];

/// Real values read back from the library before fuzzing starts.
#[derive(Default, Debug)]
pub struct Harvest {
    pub genres: Vec<String>,
    pub artists: Vec<String>,
    pub albums: Vec<(String, String)>,
    pub srcs: Vec<String>,
    pub covers: Vec<String>,
    pub playlists: Vec<String>,
    pub podcasts: Vec<String>,
    pub episodes: Vec<(String, i64)>,
    pub queries: Vec<String>,
}

impl Harvest {
    fn pool(&self, pool: Pool) -> &[String] {
        match pool {
            Pool::Genre => &self.genres,
            Pool::Artist => &self.artists,
            Pool::Src => &self.srcs,
            Pool::CoverHash => &self.covers,
            Pool::PlaylistUrl => &self.playlists,
            Pool::PodcastId => &self.podcasts,
            Pool::Query => &self.queries,
        }
    }

    /// Sorts and dedupes every pool, so a seed generates the same requests
    /// whatever order the server listed things in, and derives search queries.
    pub fn settle(&mut self) {
        let prefixes = self.genres.iter().chain(&self.artists).take(8);
        let mut queries: Vec<String> = prefixes
            .map(|name| name.chars().take(3).collect::<String>().to_lowercase())
            .collect();
        queries.extend(["the".to_string(), "a".to_string(), " ".to_string()]);
        self.queries = queries;
        for pool in [
            &mut self.genres,
            &mut self.artists,
            &mut self.srcs,
            &mut self.covers,
            &mut self.playlists,
            &mut self.podcasts,
            &mut self.queries,
        ] {
            pool.sort();
            pool.dedup();
        }
        self.albums.sort();
        self.albums.dedup();
        self.episodes.sort();
        self.episodes.dedup();
    }

    pub fn summary(&self) -> String {
        format!(
            "{} genres, {} artists, {} albums, {} tracks, {} covers, {} playlists, {} podcasts, {} episodes",
            self.genres.len(),
            self.artists.len(),
            self.albums.len(),
            self.srcs.len(),
            self.covers.len(),
            self.playlists.len(),
            self.podcasts.len(),
            self.episodes.len(),
        )
    }
}

/// A request whose every field is valid, or `None` when a required field has no
/// harvested value to draw on. The flag says whether it named a library item,
/// which can have gone since the harvest.
pub fn valid(rng: &mut Rng, spec: &OpSpec, harvest: &Harvest) -> Option<(Value, bool)> {
    let mut data = Map::new();
    let mut names_an_item = false;
    for field in spec.fields {
        if !field.required && rng.bool() {
            continue;
        }
        let filled = fill(rng, field, harvest, &mut data);
        if !filled && field.required {
            return None;
        }
        names_an_item |=
            filled && matches!(field.kind, Kind::From(_) | Kind::Album | Kind::Episode);
    }
    Some((Value::Object(data), names_an_item))
}

fn fill(rng: &mut Rng, field: &Field, harvest: &Harvest, data: &mut Map<String, Value>) -> bool {
    let value = match field.kind {
        Kind::Offset => json!(rng.below(300)),
        Kind::Limit => json!(*rng.choice(&[1, 5, 20, 100, 200])),
        Kind::Bool => json!(rng.bool()),
        Kind::Choice(options) => json!(*rng.choice(options)),
        Kind::From(pool) => match harvest.pool(pool) {
            [] => return false,
            values => json!(rng.choice(values)),
        },
        Kind::Album => {
            let Some((album, artist)) = pick(rng, &harvest.albums) else {
                return false;
            };
            data.insert("artist".into(), json!(artist));
            json!(album)
        }
        Kind::Episode => {
            let Some((id, index)) = pick(rng, &harvest.episodes) else {
                return false;
            };
            data.insert("index".into(), json!(index));
            json!(id)
        }
    };
    data.insert(field.name.into(), value);
    true
}

fn pick<'a, T>(rng: &mut Rng, items: &'a [T]) -> Option<&'a T> {
    (!items.is_empty()).then(|| rng.choice(items))
}

/// Breaks one thing about an otherwise valid request.
pub fn mutate(rng: &mut Rng, spec: &OpSpec, data: Value) -> (Value, &'static str) {
    let Value::Object(mut data) = data else {
        return (data, "unchanged");
    };
    let keys: Vec<String> = data.keys().cloned().collect();
    let required: Vec<&str> = spec
        .fields
        .iter()
        .filter(|f| f.required)
        .map(|f| f.name)
        .collect();
    let target = if keys.is_empty() {
        spec.fields.first().map(|f| f.name.to_string())
    } else {
        Some(rng.choice(&keys).clone())
    };
    let note = match (rng.below(6), target) {
        (0, _) if !required.is_empty() => {
            data.remove(*rng.choice(&required));
            "missing required field"
        }
        (1, Some(key)) => {
            data.insert(key, random_scalar(rng));
            "retyped field"
        }
        (2, Some(key)) => {
            data.insert(key, edge_string(rng));
            "edge string"
        }
        (3, Some(key)) => {
            data.insert(key, edge_number(rng));
            "edge number"
        }
        (4, _) => {
            data.insert("unexpected_field".into(), random_value(rng, 2));
            "unknown field"
        }
        _ => return (random_value(rng, 3), "data replaced"),
    };
    (Value::Object(data), note)
}

fn edge_string(rng: &mut Rng) -> Value {
    json!(match rng.below(7) {
        0 => String::new(),
        1 => "A".repeat(1 + rng.below(MAX_BLOB)),
        2 => "\u{0}\u{1}\u{1f}\u{7f}".to_string(),
        3 => "🎵é☃\u{202e}\u{feff}".repeat(1 + rng.below(64)),
        4 => r"..\..\Windows\win.ini".to_string(),
        5 => "%s%n%x{}{{".to_string(),
        _ => " ".repeat(1 + rng.below(32)),
    })
}

fn edge_number(rng: &mut Rng) -> Value {
    match rng.below(6) {
        0 => json!(-1),
        1 => json!(i64::MIN),
        2 => json!(i64::MAX),
        3 => json!(u64::MAX),
        4 => json!(i64::from(i32::MAX) + 1),
        _ => json!(1.5),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use mbrc_core::server::commands_v6::capabilities;
    use mbrc_core::server::permissions::Action;
    use mbrc_core::server::permissions::v6::action;

    /// Session ops shape the connection itself, so they are not fuzzed as reads.
    const SESSION_OPS: &[&str] = &["handshake", "pair", "ping"];

    #[test]
    fn every_read_only_op_the_server_advertises_has_a_spec_and_no_other() {
        let caps = capabilities();
        let mut expected: Vec<&str> = caps["ops"]
            .as_array()
            .expect("ops")
            .iter()
            .filter_map(Value::as_str)
            .filter(|op| !SESSION_OPS.contains(op))
            .filter(|op| matches!(action(op, &json!({})), Some(Action::Read)))
            .collect();
        let mut specced: Vec<&str> = SPECS.iter().map(|s| s.op).collect();
        specced.sort_unstable();
        expected.sort_unstable();
        assert_eq!(specced, expected, "add or drop a row in SPECS");
    }

    #[test]
    fn a_valid_request_is_skipped_when_a_required_value_was_never_harvested() {
        let spec = SPECS.iter().find(|s| s.op == "track_get").expect("spec");
        assert!(valid(&mut Rng::new(1), spec, &Harvest::default()).is_none());
    }

    #[test]
    fn an_album_is_requested_with_the_artist_it_was_harvested_with() {
        let harvest = Harvest {
            albums: vec![("Kid A".into(), "Radiohead".into())],
            ..Harvest::default()
        };
        let spec = SPECS
            .iter()
            .find(|s| s.op == "library_tracks")
            .expect("spec");
        let found = (0..64).find_map(|seed| {
            let (data, _) = valid(&mut Rng::new(seed), spec, &harvest)?;
            data.get("album").is_some().then_some(data)
        });
        let data = found.expect("some seed includes the album");
        assert_eq!(data["album"], "Kid A");
        assert_eq!(data["artist"], "Radiohead");
    }
}
