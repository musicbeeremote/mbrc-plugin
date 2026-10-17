//! Binds a golden's library references to items in the live library.
//!
//! A trimmed golden names pseudonymized playlists, tracks, albums, artists and
//! genres, plus the capture machine's now-playing positions and audio devices.
//! Replayed verbatim against another library those commands fail, their replies
//! never arrive, and the diff reports them missing. Binding rewrites each
//! reference to a live item of the same kind, drawn from one probe of the server:
//!
//! - the same golden value always maps to the same live value, and distinct
//!   values map to distinct items while the library has enough of them;
//! - an album is drawn from the albums of the artist bound just before it, and an
//!   artist the golden asks several album covers of binds to one with as many;
//! - now-playing indexes wrap to the live list length;
//! - an output switch targets the active device, so a replay never moves audio;
//! - empty strings stay empty, since they exercise the empty-argument path.

use std::collections::{HashMap, HashSet};

use mbrc_capture::{Record, parse_line};
use serde_json::Value;

/// Commands the probe connection sends; each reply fills one part of [`LiveLibrary`].
pub const PROBE_COMMANDS: [&str; 4] = [
    r#"{"context":"browsetracks","data":{"offset":0,"limit":2000}}"#,
    r#"{"context":"playlistlist","data":{"offset":0,"limit":500}}"#,
    r#"{"context":"nowplayinglist","data":{"offset":0,"limit":800}}"#,
    r#"{"context":"playeroutput","data":""}"#,
];

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Kind {
    Playlist,
    Track,
    Artist,
    Album,
    Genre,
}

#[derive(Debug, Default, Clone)]
struct LiveTrack {
    src: String,
    title: String,
    artist: String,
    album: String,
    genre: String,
}

/// What the probe learned about the live library and player.
#[derive(Debug, Default)]
pub struct LiveLibrary {
    tracks: Vec<LiveTrack>,
    playlists: Vec<String>,
    now_playing_titles: Vec<String>,
    active_output: Option<String>,
}

impl LiveLibrary {
    /// Reads the probe connection's recorded replies (capture JSONL lines).
    pub fn from_probe(lines: &[String]) -> Self {
        let mut live = Self::default();
        for line in lines {
            let Some(Record::Frame(f)) = parse_line(line) else {
                continue;
            };
            if f.dir != "s2c" {
                continue;
            }
            let Some(data) = f.frame.as_ref().and_then(|fr| fr.get("data")) else {
                continue;
            };
            let items = data.get("data").and_then(Value::as_array);
            match f.context() {
                Some("browsetracks") => {
                    live.tracks = items
                        .into_iter()
                        .flatten()
                        .map(|t| LiveTrack {
                            src: str_field(t, "src"),
                            title: str_field(t, "title"),
                            artist: non_empty_or(str_field(t, "album_artist"), || {
                                str_field(t, "artist")
                            }),
                            album: str_field(t, "album"),
                            genre: str_field(t, "genre"),
                        })
                        .collect();
                }
                Some("playlistlist") => {
                    live.playlists = items
                        .into_iter()
                        .flatten()
                        .map(|p| str_field(p, "url"))
                        .collect();
                }
                Some("nowplayinglist") => {
                    live.now_playing_titles = items
                        .into_iter()
                        .flatten()
                        .map(|t| str_field(t, "title"))
                        .collect();
                }
                Some("playeroutput") => {
                    live.active_output = data
                        .get("active")
                        .and_then(Value::as_str)
                        .map(str::to_string);
                }
                _ => {}
            }
        }
        live
    }

    /// Distinct non-empty values of one kind, in library order.
    fn pool(&self, kind: Kind, artist: Option<&str>) -> Vec<&str> {
        let values: Box<dyn Iterator<Item = &str>> = match kind {
            Kind::Playlist => Box::new(self.playlists.iter().map(String::as_str)),
            Kind::Track => Box::new(self.tracks.iter().map(|t| t.src.as_str())),
            Kind::Artist => Box::new(self.tracks.iter().map(|t| t.artist.as_str())),
            Kind::Genre => Box::new(self.tracks.iter().map(|t| t.genre.as_str())),
            Kind::Album => Box::new(
                self.tracks
                    .iter()
                    .filter(move |t| artist.is_none_or(|a| t.artist == a))
                    .map(|t| t.album.as_str()),
            ),
        };
        let mut seen = HashSet::new();
        values
            .filter(|v| !v.is_empty() && seen.insert(*v))
            .collect()
    }

    fn album_counts(&self) -> HashMap<&str, usize> {
        let mut albums: HashMap<&str, HashSet<&str>> = HashMap::new();
        for t in self.tracks.iter().filter(|t| !t.album.is_empty()) {
            albums
                .entry(t.artist.as_str())
                .or_default()
                .insert(t.album.as_str());
        }
        albums.into_iter().map(|(a, set)| (a, set.len())).collect()
    }
}

fn str_field(v: &Value, key: &str) -> String {
    v.get(key)
        .and_then(Value::as_str)
        .unwrap_or_default()
        .to_string()
}

fn non_empty_or(s: String, fallback: impl FnOnce() -> String) -> String {
    if s.is_empty() { fallback() } else { s }
}

/// Rewrites golden commands against one [`LiveLibrary`], remembering every choice.
pub struct Binder<'a> {
    live: &'a LiveLibrary,
    bound: HashMap<(Kind, String), String>,
    used: HashSet<(Kind, String)>,
    last_artist: Option<String>,
    albums_wanted: HashMap<String, usize>,
    /// Every binding made, in first-use order: kind, golden value, live value.
    pub bindings: Vec<(Kind, String, String)>,
    /// Golden values left as captured because the live library had no item of that kind.
    pub unbound: Vec<(Kind, String)>,
}

impl<'a> Binder<'a> {
    pub fn new(live: &'a LiveLibrary) -> Self {
        Self {
            live,
            bound: HashMap::new(),
            used: HashSet::new(),
            last_artist: None,
            albums_wanted: HashMap::new(),
            bindings: Vec::new(),
            unbound: Vec::new(),
        }
    }

    /// Records how many distinct album covers the golden asks of each artist.
    pub fn prescan<'c>(&mut self, commands: impl IntoIterator<Item = &'c String>) {
        let mut albums: HashMap<String, HashSet<String>> = HashMap::new();
        for raw in commands {
            let Ok(frame) = serde_json::from_str::<Value>(raw) else {
                continue;
            };
            if frame.get("context").and_then(Value::as_str) != Some("libraryalbumcover") {
                continue;
            }
            let field = |k| str_field(&frame["data"], k);
            let (artist, album) = (field("artist"), field("album"));
            if !artist.is_empty() && !album.is_empty() {
                albums.entry(artist).or_default().insert(album);
            }
        }
        self.albums_wanted = albums.into_iter().map(|(a, set)| (a, set.len())).collect();
    }

    /// The live value standing in for `golden`, or `None` to keep it as captured.
    pub fn bind(&mut self, kind: Kind, golden: &str) -> Option<String> {
        if golden.is_empty() {
            return None;
        }
        let key = (kind, golden.to_string());
        if let Some(live) = self.bound.get(&key) {
            let live = live.clone();
            self.note_artist(kind, &live);
            return Some(live);
        }
        let artist = self.last_artist.as_deref().filter(|_| kind == Kind::Album);
        let mut pool = self.live.pool(kind, artist);
        if pool.is_empty() && artist.is_some() {
            pool = self.live.pool(kind, None);
        }
        if let Some(&wanted) = self
            .albums_wanted
            .get(golden)
            .filter(|_| kind == Kind::Artist)
        {
            let counts = self.live.album_counts();
            pool.sort_by_key(|a| counts.get(a).copied().unwrap_or(0) < wanted);
        }
        let Some(choice) = self.pick(kind, &pool) else {
            if !self.unbound.contains(&key) {
                self.unbound.push(key);
            }
            return None;
        };
        self.used.insert((kind, choice.clone()));
        self.bound.insert(key, choice.clone());
        self.bindings
            .push((kind, golden.to_string(), choice.clone()));
        self.note_artist(kind, &choice);
        Some(choice)
    }

    /// The first unused item in `pool`; once all are used, cycles through them.
    fn pick(&self, kind: Kind, pool: &[&str]) -> Option<String> {
        let unused = pool
            .iter()
            .find(|v| !self.used.contains(&(kind, v.to_string())));
        let bound_so_far = self.bindings.iter().filter(|b| b.0 == kind).count();
        unused
            .or_else(|| pool.get(bound_so_far % pool.len().max(1)))
            .map(|v| v.to_string())
    }

    fn note_artist(&mut self, kind: Kind, live: &str) {
        if kind == Kind::Artist {
            self.last_artist = Some(live.to_string());
        }
    }

    /// A now-playing index wrapped into the live list, or `None` when it is empty.
    fn index(&self, golden: u64) -> Option<u64> {
        let len = self.live.now_playing_titles.len() as u64;
        (len > 0).then(|| golden % len)
    }

    /// The commonest title word in the library and now-playing list.
    ///
    /// A search must hit whatever the list holds when it runs, and earlier
    /// replayed commands replace the list the probe saw, so the term is the one
    /// most likely to match any list drawn from this library.
    fn search_term(&self) -> Option<String> {
        let titles = self.live.tracks.iter().map(|t| t.title.as_str());
        let titles = titles.chain(self.live.now_playing_titles.iter().map(String::as_str));
        let mut counts: HashMap<String, usize> = HashMap::new();
        for word in titles.flat_map(str::split_whitespace) {
            if word.chars().filter(|c| c.is_alphabetic()).count() >= 3 {
                *counts.entry(word.to_lowercase()).or_default() += 1;
            }
        }
        counts
            .into_iter()
            .max_by(|(a, x), (b, y)| x.cmp(y).then_with(|| b.cmp(a)))
            .map(|(word, _)| word)
    }

    /// The command with its library references bound, or `None` when nothing changed.
    pub fn rewrite(&mut self, raw: &str) -> Option<String> {
        let mut frame: Value = serde_json::from_str(raw).ok()?;
        let context = frame.get("context")?.as_str()?.to_string();
        let data = frame.get_mut("data")?;
        let changed = match context.as_str() {
            "playlistplay" => self.bind_str(data, Kind::Playlist),
            "libraryartistalbums" => self.bind_str(data, Kind::Artist),
            "libraryalbumtracks" => self.bind_str(data, Kind::Album),
            "librarygenreartists" => self.bind_str(data, Kind::Genre),
            "libraryalbumcover" => {
                if data.get("artist").and_then(Value::as_str) == Some("") {
                    self.last_artist = None;
                }
                let artist = data
                    .get_mut("artist")
                    .is_some_and(|a| self.bind_str(a, Kind::Artist));
                let album = data
                    .get_mut("album")
                    .is_some_and(|a| self.bind_str(a, Kind::Album));
                artist || album
            }
            "nowplayingqueue" => match data.get_mut("data").and_then(Value::as_array_mut) {
                Some(paths) => paths
                    .iter_mut()
                    .fold(false, |acc, p| self.bind_str(p, Kind::Track) | acc),
                None => false,
            },
            "nowplayinglistplay" | "nowplayinglistremove" => self.bind_index(data),
            "nowplayinglistmove" => {
                let from = data.get_mut("from").is_some_and(|v| self.bind_index(v));
                let to = data.get_mut("to").is_some_and(|v| self.bind_index(v));
                from || to
            }
            "nowplayinglistsearch" => replace_str(data, self.search_term()),
            "playeroutputswitch" => replace_str(data, self.live.active_output.clone()),
            _ => false,
        };
        changed
            .then(|| serde_json::to_string(&frame).ok())
            .flatten()
    }

    fn bind_str(&mut self, v: &mut Value, kind: Kind) -> bool {
        let live = v.as_str().and_then(|golden| self.bind(kind, golden));
        replace_str(v, live)
    }

    fn bind_index(&self, v: &mut Value) -> bool {
        match v.as_u64().and_then(|i| self.index(i)) {
            Some(live) if Some(live) != v.as_u64() => {
                *v = Value::from(live);
                true
            }
            _ => false,
        }
    }
}

fn replace_str(v: &mut Value, live: Option<String>) -> bool {
    match live {
        Some(live) if v.as_str().is_some_and(|old| old != live) => {
            *v = Value::String(live);
            true
        }
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn track(src: &str, artist: &str, album: &str, genre: &str) -> LiveTrack {
        LiveTrack {
            src: src.into(),
            title: format!("{album} Blue"),
            artist: artist.into(),
            album: album.into(),
            genre: genre.into(),
        }
    }

    fn library() -> LiveLibrary {
        LiveLibrary {
            tracks: vec![
                track("C:\\a1.mp3", "Alpha", "First", "Rock"),
                track("C:\\a2.mp3", "Alpha", "Second", "Rock"),
                track("C:\\b1.mp3", "Beta", "Third", "Jazz"),
            ],
            playlists: vec!["C:\\one.mbp".into(), "C:\\two.mbp".into()],
            now_playing_titles: vec!["Blue in Green".into(), "So What".into(), "Freddie".into()],
            active_output: Some("Headphones".into()),
        }
    }

    fn data(rewritten: Option<String>) -> Value {
        serde_json::from_str::<Value>(&rewritten.expect("rewritten")).expect("json")["data"].clone()
    }

    #[test]
    fn the_same_golden_value_binds_to_the_same_live_item() {
        let live = library();
        let mut b = Binder::new(&live);
        let first = b.bind(Kind::Playlist, "\\\\host\\media\\item-1.mbp");
        assert_eq!(first, b.bind(Kind::Playlist, "\\\\host\\media\\item-1.mbp"));
    }

    #[test]
    fn distinct_golden_values_bind_to_distinct_live_items_until_the_pool_runs_out() {
        let live = library();
        let mut b = Binder::new(&live);
        let one = b.bind(Kind::Playlist, "item-1").unwrap();
        let two = b.bind(Kind::Playlist, "item-2").unwrap();
        assert_ne!(one, two);
        assert!(
            b.bind(Kind::Playlist, "item-3").is_some(),
            "reuses once exhausted"
        );
    }

    #[test]
    fn an_album_is_drawn_from_the_artist_bound_before_it() {
        let live = library();
        let mut b = Binder::new(&live);
        assert_eq!(b.bind(Kind::Artist, "Artist 1").as_deref(), Some("Alpha"));
        let album = b.bind(Kind::Album, "Album 9").unwrap();
        assert!(album == "First" || album == "Second", "{album}");
    }

    #[test]
    fn an_artist_asked_for_several_album_covers_binds_to_one_with_as_many_albums() {
        let mut live = library();
        live.tracks.rotate_right(1);
        let mut b = Binder::new(&live);
        let cover = |album: &str| {
            format!(
                r#"{{"context":"libraryalbumcover","data":{{"artist":"Artist 1","album":"{album}"}}}}"#
            )
        };
        let commands = vec![cover("Album 1"), cover("Album 2")];
        b.prescan(&commands);
        let first = data(b.rewrite(&commands[0]));
        let second = data(b.rewrite(&commands[1]));
        assert_eq!(
            first["artist"],
            json!("Alpha"),
            "Beta is listed first but has one album"
        );
        assert_ne!(first["album"], second["album"]);
    }

    #[test]
    fn an_empty_golden_value_is_kept_for_the_empty_argument_path() {
        let live = library();
        let mut b = Binder::new(&live);
        assert_eq!(b.bind(Kind::Album, ""), None);
        assert_eq!(
            b.rewrite(r#"{"context":"libraryalbumtracks","data":""}"#),
            None
        );
    }

    #[test]
    fn a_kind_missing_from_the_live_library_is_reported_unbound() {
        let live = LiveLibrary::default();
        let mut b = Binder::new(&live);
        assert_eq!(
            b.rewrite(r#"{"context":"playlistplay","data":"item-1.mbp"}"#),
            None
        );
        assert_eq!(b.unbound, vec![(Kind::Playlist, "item-1.mbp".to_string())]);
    }

    #[test]
    fn queued_paths_are_bound_to_live_tracks() {
        let live = library();
        let mut b = Binder::new(&live);
        let raw = r#"{"context":"nowplayingqueue","data":{"queue":"now","data":["x\\item-6.mp3","x\\item-7.mp3"],"play":null}}"#;
        let d = data(b.rewrite(raw));
        assert_eq!(d["data"], json!(["C:\\a1.mp3", "C:\\a2.mp3"]));
        assert_eq!(d["queue"], json!("now"), "other fields untouched");
    }

    #[test]
    fn now_playing_indexes_wrap_to_the_live_list_length() {
        let live = library();
        let mut b = Binder::new(&live);
        assert_eq!(
            data(b.rewrite(r#"{"context":"nowplayinglistplay","data":7}"#)),
            json!(1)
        );
        let moved = data(b.rewrite(r#"{"context":"nowplayinglistmove","data":{"from":1,"to":4}}"#));
        assert_eq!(moved, json!({"from": 1, "to": 1}));
    }

    #[test]
    fn an_output_switch_targets_the_active_device() {
        let live = library();
        let mut b = Binder::new(&live);
        let raw = r#"{"context":"playeroutputswitch","data":"Speakers (Realtek(R) Audio)"}"#;
        assert_eq!(data(b.rewrite(raw)), json!("Headphones"));
    }

    #[test]
    fn a_now_playing_search_uses_the_commonest_title_word() {
        let live = library();
        let mut b = Binder::new(&live);
        let raw = r#"{"context":"nowplayinglistsearch","data":"oooh"}"#;
        assert_eq!(data(b.rewrite(raw)), json!("blue"));
    }

    #[test]
    fn a_probe_capture_fills_the_live_library() {
        let line = |ctx: &str, d: Value| {
            let raw = json!({"context": ctx, "data": d}).to_string();
            serde_json::to_string(&mbrc_capture::Frame::new(0, 0, "s2c", 0, raw.as_bytes()))
                .unwrap()
        };
        let lines = vec![
            line(
                "browsetracks",
                json!({"data": [{"src": "C:\\x.mp3", "artist": "A", "album_artist": "", "album": "B", "genre": "G"}]}),
            ),
            line(
                "playlistlist",
                json!({"data": [{"url": "C:\\p.mbp", "name": "p"}]}),
            ),
            line(
                "nowplayinglist",
                json!({"data": [{"title": "Song", "position": 1}]}),
            ),
            line(
                "playeroutput",
                json!({"active": "Speakers", "devices": ["Speakers"]}),
            ),
        ];
        let live = LiveLibrary::from_probe(&lines);
        assert_eq!(
            live.tracks[0].artist, "A",
            "falls back to artist when album_artist is empty"
        );
        assert_eq!(live.playlists, vec!["C:\\p.mbp"]);
        assert_eq!(live.now_playing_titles, vec!["Song"]);
        assert_eq!(live.active_output.as_deref(), Some("Speakers"));
    }
}
