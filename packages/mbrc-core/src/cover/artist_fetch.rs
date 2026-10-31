//! The artist picture fetcher (#116): one background worker that turns a
//! queued name into a stored picture, or a recorded miss.
//!
//! MusicBrainz resolves the name to an artist id. A wrong picture is worse than
//! none, so the id is taken only when exactly one result carries the name
//! itself (case-insensitive) and that result scores 100; anything else is
//! `ambiguous`. The artist's Wikidata link gives its P18 image, a Commons file
//! fetched pre-sized through `Special:FilePath`.
//!
//! Everything above [`HttpClient`] is tested against recorded responses. Each
//! host is paced ([`Pace`]): MusicBrainz allows one request a second per IP.

use std::collections::HashMap;
use std::time::{Duration, Instant};

use mbrc_release::HttpClient;
use serde_json::Value;

use super::artists::{ArtistImages, Status};
use crate::connect_link::percent_encode;

/// The source name recorded for a picture from Wikimedia Commons via Wikidata.
pub const SOURCE_WIKIDATA: &str = "wikidata";

/// The width Commons is asked to render, matching the stored box.
const COMMONS_WIDTH: u32 = super::artists::ARTIST_IMAGE_SIZE;

/// How long an idle worker waits for work before checking whether to stop.
const IDLE_WAIT: Duration = Duration::from_secs(1);

const MUSICBRAINZ: &str = "musicbrainz.org";

/// The least time between two requests to one host.
pub struct Pace {
    musicbrainz: Duration,
    other: Duration,
    last: HashMap<String, Instant>,
}

impl Pace {
    /// A little over a second for MusicBrainz, half a second for Wikimedia.
    pub fn polite() -> Self {
        Self {
            musicbrainz: Duration::from_millis(1100),
            other: Duration::from_millis(500),
            last: HashMap::new(),
        }
    }

    #[cfg(test)]
    fn none() -> Self {
        Self {
            musicbrainz: Duration::ZERO,
            other: Duration::ZERO,
            last: HashMap::new(),
        }
    }

    fn wait_for(&mut self, url: &str) {
        let host = host(url);
        let gap = if host == MUSICBRAINZ {
            self.musicbrainz
        } else {
            self.other
        };
        if let Some(since) = self.last.get(host).map(Instant::elapsed)
            && since < gap
        {
            std::thread::sleep(gap - since);
        }
        self.last.insert(host.to_owned(), Instant::now());
    }
}

fn host(url: &str) -> &str {
    let rest = url.split_once("://").map_or(url, |(_, rest)| rest);
    rest.split(['/', '?']).next().unwrap_or(rest)
}

/// What looking for one artist's picture came to.
#[derive(Debug, PartialEq, Eq)]
pub enum Outcome {
    Found {
        image: Vec<u8>,
        mbid: String,
        source: &'static str,
    },
    /// No picture, as a fact about the sources rather than a failure.
    Missing {
        status: Status,
        mbid: String,
    },
    Failed {
        error: String,
        mbid: String,
    },
}

/// One HTTP answer: the body, `None` for a 404, or why it failed.
type Fetched = Result<Option<Vec<u8>>, String>;

/// Resolves names and downloads pictures over one client.
pub struct Fetcher<'a> {
    client: &'a dyn HttpClient,
    pace: Pace,
}

impl<'a> Fetcher<'a> {
    pub fn new(client: &'a dyn HttpClient, pace: Pace) -> Self {
        Self { client, pace }
    }

    /// Looks `name` up and, when every step finds exactly one answer, downloads
    /// its picture.
    pub fn fetch(&mut self, name: &str) -> Outcome {
        let missing = |status, mbid: &str| Outcome::Missing {
            status,
            mbid: mbid.to_owned(),
        };
        let failed = |error, mbid: &str| Outcome::Failed {
            error,
            mbid: mbid.to_owned(),
        };
        let mbid = match self.json(&musicbrainz_search_url(name)) {
            Ok(Some(body)) => match pick_artist(name, &body) {
                Ok(mbid) => mbid,
                Err(status) => return missing(status, ""),
            },
            Ok(None) => return missing(Status::NotFound, ""),
            Err(e) => return failed(e, ""),
        };
        let item = match self.json(&musicbrainz_lookup_url(&mbid)) {
            Ok(Some(body)) => match wikidata_item(&body) {
                Some(item) => item,
                None => return missing(Status::NotFound, &mbid),
            },
            Ok(None) => return missing(Status::NotFound, &mbid),
            Err(e) => return failed(e, &mbid),
        };
        let file = match self.json(&wikidata_image_url(&item)) {
            Ok(Some(body)) => match image_file(&body) {
                Some(file) => file,
                None => return missing(Status::NotFound, &mbid),
            },
            Ok(None) => return missing(Status::NotFound, &mbid),
            Err(e) => return failed(e, &mbid),
        };
        match self.get(&commons_url(&file)) {
            Ok(Some(image)) => Outcome::Found {
                image,
                mbid,
                source: SOURCE_WIKIDATA,
            },
            Ok(None) => missing(Status::NotFound, &mbid),
            Err(e) => failed(e, &mbid),
        }
    }

    fn get(&mut self, url: &str) -> Fetched {
        self.pace.wait_for(url);
        let response = self.client.get(url, None).map_err(|e| e.to_string())?;
        match response.status {
            404 => Ok(None),
            _ if response.is_success() => Ok(Some(response.body)),
            status => Err(format!("HTTP {status} from {}", host(url))),
        }
    }

    fn json(&mut self, url: &str) -> Result<Option<Value>, String> {
        self.get(url)?
            .map(|body| {
                serde_json::from_slice(&body).map_err(|e| format!("{} JSON: {e}", host(url)))
            })
            .transpose()
    }
}

/// Fetches queued artists until `keep_going` says stop or a newer worker
/// replaces this one, calling `landed` with each picture stored.
pub fn run(
    images: &ArtistImages,
    fetcher: &mut Fetcher<'_>,
    generation: u64,
    keep_going: &dyn Fn() -> bool,
    landed: &dyn Fn(&str, &str),
) {
    super::store::below_normal_priority();
    let mut budget_noted = false;
    while images.is_current_worker(generation) && keep_going() {
        let Some(name) = images.next(IDLE_WAIT) else {
            continue;
        };
        if !images.needs_fetch(&name, now_unix_secs()) {
            continue;
        }
        if images.over_budget() {
            if !budget_noted {
                tracing::info!(
                    used = images.used_bytes(),
                    "artist pictures reached their disk budget; not fetching more"
                );
                budget_noted = true;
            }
            continue;
        }
        fetch_one(images, fetcher, &name, landed);
    }
}

fn fetch_one(
    images: &ArtistImages,
    fetcher: &mut Fetcher<'_>,
    name: &str,
    landed: &dyn Fn(&str, &str),
) {
    match fetcher.fetch(name) {
        Outcome::Found {
            image,
            mbid,
            source,
        } => match images.store(name, &image, &mbid, source) {
            Ok(hash) => {
                tracing::debug!(artist = name, %hash, source, "artist picture stored");
                landed(name, &hash);
            }
            Err(e) => {
                tracing::debug!(artist = name, error = %e, "artist picture unusable");
                images.record_miss(name, Status::NotFound, &mbid);
            }
        },
        Outcome::Missing { status, mbid } => {
            tracing::debug!(artist = name, status = status.as_str(), "no artist picture");
            images.record_miss(name, status, &mbid);
        }
        Outcome::Failed { error, mbid } => {
            tracing::debug!(artist = name, %error, "artist picture fetch failed");
            images.record_miss(name, Status::Error, &mbid);
        }
    }
}

fn musicbrainz_search_url(name: &str) -> String {
    let phrase = name.trim().replace('\\', "\\\\").replace('"', "\\\"");
    let query = percent_encode(&format!("artist:\"{phrase}\""));
    format!("https://{MUSICBRAINZ}/ws/2/artist/?query={query}&fmt=json&limit=10")
}

fn musicbrainz_lookup_url(mbid: &str) -> String {
    format!(
        "https://{MUSICBRAINZ}/ws/2/artist/{}?inc=url-rels&fmt=json",
        percent_encode(mbid)
    )
}

fn wikidata_image_url(item: &str) -> String {
    format!(
        "https://www.wikidata.org/w/api.php?action=wbgetclaims&entity={}&property=P18&format=json",
        percent_encode(item)
    )
}

fn commons_url(file: &str) -> String {
    format!(
        "https://commons.wikimedia.org/wiki/Special:FilePath/{}?width={COMMONS_WIDTH}",
        percent_encode(&file.replace(' ', "_"))
    )
}

/// The one MusicBrainz id a search answer names `name` with, or why there is
/// none: no result at all is `not_found`, anything short of one sure match is
/// `ambiguous`.
fn pick_artist(name: &str, body: &Value) -> Result<String, Status> {
    let results = body["artists"].as_array().map_or(&[][..], Vec::as_slice);
    if results.is_empty() {
        return Err(Status::NotFound);
    }
    let wanted = name.trim().to_lowercase();
    let exact: Vec<&Value> = results
        .iter()
        .filter(|a| a["name"].as_str().map(|n| n.trim().to_lowercase()) == Some(wanted.clone()))
        .collect();
    match exact.as_slice() {
        [only] if only["score"].as_u64() == Some(100) => only["id"]
            .as_str()
            .filter(|id| !id.is_empty())
            .map(str::to_owned)
            .ok_or(Status::NotFound),
        _ => Err(Status::Ambiguous),
    }
}

/// The Wikidata item (`Q...`) an artist lookup with `url-rels` links to.
fn wikidata_item(body: &Value) -> Option<String> {
    body["relations"]
        .as_array()?
        .iter()
        .filter(|r| r["type"] == "wikidata")
        .filter_map(|r| r["url"]["resource"].as_str())
        .filter_map(|url| url.rsplit('/').next())
        .find(|q| q.len() > 1 && q.starts_with('Q') && q[1..].bytes().all(|b| b.is_ascii_digit()))
        .map(str::to_owned)
}

/// The Commons file named by an item's P18 claims: a preferred one first, never
/// a deprecated one.
fn image_file(body: &Value) -> Option<String> {
    let claims = body["claims"]["P18"].as_array()?;
    let file = |rank: &str| {
        claims
            .iter()
            .filter(|c| c["rank"] == rank && c["mainsnak"]["snaktype"] == "value")
            .find_map(|c| c["mainsnak"]["datavalue"]["value"].as_str())
            .filter(|f| !f.trim().is_empty())
    };
    file("preferred")
        .or_else(|| file("normal"))
        .map(str::to_owned)
}

fn now_unix_secs() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use std::cell::RefCell;

    use mbrc_release::{HttpResponse, UpdateError};
    use serde_json::json;

    use super::*;
    use crate::cover::test_jpeg_bytes;
    use crate::store::Db;

    const ELUVEITIE_MBID: &str = "8000598a-5edb-401c-8e6d-36b167feaf38";

    fn fixture(name: &str) -> Vec<u8> {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("tests/fixtures/artist_images")
            .join(name);
        std::fs::read(&path).unwrap_or_else(|e| panic!("{}: {e}", path.display()))
    }

    /// Answers from recorded responses by URL; anything unrecorded is a 404.
    #[derive(Default)]
    struct Recorded {
        answers: HashMap<String, (u16, Vec<u8>)>,
        asked: RefCell<Vec<String>>,
        offline: bool,
    }

    impl Recorded {
        fn with(mut self, url: String, status: u16, body: Vec<u8>) -> Self {
            self.answers.insert(url, (status, body));
            self
        }

        /// The full Eluveitie chain, as recorded from the live services.
        fn eluveitie() -> Self {
            Self::default()
                .with(
                    musicbrainz_search_url("Eluveitie"),
                    200,
                    fixture("mb_search_eluveitie.json"),
                )
                .with(
                    musicbrainz_lookup_url(ELUVEITIE_MBID),
                    200,
                    fixture("mb_lookup_eluveitie.json"),
                )
                .with(
                    wikidata_image_url("Q75788"),
                    200,
                    fixture("wd_claims_q75788.json"),
                )
                .with(
                    commons_url("Eluveitie, 3-Majówka 2023, 056.jpg"),
                    200,
                    test_jpeg_bytes(500, 333),
                )
        }
    }

    impl HttpClient for Recorded {
        fn get(&self, url: &str, _etag: Option<&str>) -> mbrc_release::Result<HttpResponse> {
            self.asked.borrow_mut().push(url.to_owned());
            if self.offline {
                return Err(UpdateError::Network("offline".into()));
            }
            let (status, body) = self.answers.get(url).cloned().unwrap_or((404, Vec::new()));
            Ok(HttpResponse {
                status,
                etag: None,
                body,
            })
        }
    }

    fn fetch(client: &Recorded, name: &str) -> Outcome {
        Fetcher::new(client, Pace::none()).fetch(name)
    }

    #[test]
    fn a_unique_exact_match_resolves_through_wikidata_to_a_commons_picture() {
        let client = Recorded::eluveitie();
        let Outcome::Found {
            image,
            mbid,
            source,
        } = fetch(&client, "Eluveitie")
        else {
            panic!("expected a picture");
        };
        assert_eq!(mbid, ELUVEITIE_MBID);
        assert_eq!(source, SOURCE_WIKIDATA);
        assert_eq!(image, test_jpeg_bytes(500, 333));
        assert!(
            client.asked.borrow()[3].ends_with(
                "Special:FilePath/Eluveitie%2C_3-Maj%C3%B3wka_2023%2C_056.jpg?width=500"
            ),
            "Commons is asked for the file pre-sized"
        );
    }

    /// Recorded: three bands named Kaleidoscope, only the first at score 100.
    #[test]
    fn several_artists_with_the_name_are_ambiguous_not_guessed() {
        let client = Recorded::default().with(
            musicbrainz_search_url("Kaleidoscope"),
            200,
            fixture("mb_search_kaleidoscope.json"),
        );
        assert_eq!(
            fetch(&client, "Kaleidoscope"),
            Outcome::Missing {
                status: Status::Ambiguous,
                mbid: String::new()
            }
        );
        assert_eq!(client.asked.borrow().len(), 1);
    }

    #[test]
    fn a_match_below_score_100_or_by_another_name_is_not_taken() {
        let below = json!({ "artists": [{ "id": "x", "name": "A", "score": 99 }] });
        assert_eq!(pick_artist("A", &below), Err(Status::Ambiguous));
        let other = json!({ "artists": [{ "id": "x", "name": "A & B", "score": 100 }] });
        assert_eq!(pick_artist("A", &other), Err(Status::Ambiguous));
        assert_eq!(
            pick_artist("A", &json!({ "artists": [] })),
            Err(Status::NotFound)
        );
    }

    #[test]
    fn the_name_matches_whatever_its_case() {
        let body = serde_json::from_slice(&fixture("mb_search_eluveitie.json")).unwrap();
        assert_eq!(pick_artist(" ELUVEITIE ", &body), Ok(ELUVEITIE_MBID.into()));
    }

    #[test]
    fn an_artist_without_a_wikidata_link_or_an_image_has_no_picture() {
        let no_link = Recorded::eluveitie().with(
            musicbrainz_lookup_url(ELUVEITIE_MBID),
            200,
            br#"{"relations":[]}"#.to_vec(),
        );
        let no_image = Recorded::eluveitie().with(
            wikidata_image_url("Q75788"),
            200,
            br#"{"claims":{}}"#.to_vec(),
        );
        for client in [no_link, no_image] {
            assert_eq!(
                fetch(&client, "Eluveitie"),
                Outcome::Missing {
                    status: Status::NotFound,
                    mbid: ELUVEITIE_MBID.into()
                }
            );
        }
    }

    /// Recorded: MusicBrainz's answer while over its rate limit.
    #[test]
    fn a_busy_server_or_no_network_is_an_error_to_retry() {
        let busy = Recorded::default().with(
            musicbrainz_search_url("Eluveitie"),
            503,
            fixture("mb_busy.json"),
        );
        let offline = Recorded {
            offline: true,
            ..Recorded::default()
        };
        for client in [busy, offline] {
            assert!(matches!(
                fetch(&client, "Eluveitie"),
                Outcome::Failed { .. }
            ));
        }
    }

    #[test]
    fn a_preferred_image_wins_and_a_deprecated_one_is_never_used() {
        let claim = |file: &str, rank: &str| json!({ "rank": rank, "mainsnak": { "snaktype": "value", "datavalue": { "value": file } } });
        let body = json!({ "claims": { "P18": [
            claim("old.jpg", "deprecated"),
            claim("normal.jpg", "normal"),
            claim("best.jpg", "preferred"),
        ] } });
        assert_eq!(image_file(&body).as_deref(), Some("best.jpg"));
        let only_deprecated = json!({ "claims": { "P18": [claim("old.jpg", "deprecated")] } });
        assert_eq!(image_file(&only_deprecated), None);
    }

    #[test]
    fn a_quote_in_the_name_cannot_break_out_of_the_search_phrase() {
        let url = musicbrainz_search_url("Tico \"El Inmigrante\"");
        assert!(
            url.contains("%22Tico%20%5C%22El%20Inmigrante%5C%22%22"),
            "{url}"
        );
    }

    #[test]
    fn musicbrainz_is_asked_at_most_once_a_second() {
        let mut pace = Pace::polite();
        let started = Instant::now();
        pace.wait_for("https://musicbrainz.org/ws/2/artist/?query=a");
        pace.wait_for("https://www.wikidata.org/w/api.php");
        pace.wait_for("https://musicbrainz.org/ws/2/artist/x");
        assert!(started.elapsed() >= Duration::from_millis(1100));
    }

    #[test]
    fn the_worker_stores_what_it_finds_records_what_it_does_not_and_announces_pictures() {
        let dir = std::env::temp_dir().join("mbrc-artist-fetch-worker");
        let _ = std::fs::remove_dir_all(&dir);
        let images = ArtistImages::new(Db::disabled(), dir.join("covers"));
        images.set_enabled(true);
        images.request("Kaleidoscope", super::super::artists::Priority::Requested);
        images.request("Eluveitie", super::super::artists::Priority::NowPlaying);

        let client = Recorded::eluveitie().with(
            musicbrainz_search_url("Kaleidoscope"),
            200,
            fixture("mb_search_kaleidoscope.json"),
        );
        let mut fetcher = Fetcher::new(&client, Pace::none());
        let generation = images.begin_worker();
        let landed = RefCell::new(Vec::new());
        let rounds = std::cell::Cell::new(0);
        run(
            &images,
            &mut fetcher,
            generation,
            &|| {
                rounds.set(rounds.get() + 1);
                rounds.get() <= 2
            },
            &|name, hash| landed.borrow_mut().push((name.to_owned(), hash.to_owned())),
        );

        let hash = images.hash_for("Eluveitie").expect("stored");
        assert_eq!(*landed.borrow(), [("Eluveitie".to_owned(), hash)]);
        assert_eq!(
            images.lookup("Kaleidoscope").map(|l| l.status),
            Some(Status::Ambiguous)
        );
    }
}
