//! The artist picture fetcher (#116): one background worker that turns a
//! queued name into a stored picture, or a recorded miss.
//!
//! MusicBee's own thumbnail comes first, so a client shows what MusicBee shows.
//! The web is the fallback for artists MusicBee has none of. There MusicBrainz
//! resolves the name to an artist id. A wrong picture is worse than
//! none, so the id is taken only when exactly one result carries the name
//! itself (case-insensitive) and that result scores 100; anything else is
//! `ambiguous`. The artist's Wikidata link gives its P18 image, a Commons file
//! fetched pre-sized through `Special:FilePath`.
//!
//! Everything above [`HttpClient`] is tested against recorded responses. Each
//! host is paced ([`Pace`]): MusicBrainz allows one request a second per IP.

use std::collections::HashMap;
use std::sync::Weak;
use std::time::{Duration, Instant};

use mbrc_release::HttpClient;
use serde_json::Value;

use super::artists::{ArtistImages, FetchQueue, Status};
use super::store::{CoverStore, now_unix_secs};
use crate::connect_link::percent_encode;
use crate::credits;
use crate::providers::Providers;

/// The source name recorded for a picture from Wikimedia Commons via Wikidata.
pub const SOURCE_WIKIDATA: &str = "wikidata";

/// The source name recorded for MusicBee's own thumbnail.
pub const SOURCE_MUSICBEE: &str = "musicbee";

/// The width Commons is asked to render, matching the stored box.
const COMMONS_WIDTH: u32 = super::artists::ARTIST_IMAGE_SIZE;

/// How long an idle worker waits for work before looking again; retiring it
/// wakes it at once.
const IDLE_WAIT: Duration = Duration::from_secs(60);

const MUSICBRAINZ: &str = "musicbrainz.org";

/// The least time between two requests to one host, and how long a busy
/// service is left alone.
#[derive(Default)]
pub struct Pace {
    musicbrainz: Duration,
    other: Duration,
    busy_min: Duration,
    busy_max: Duration,
    last: HashMap<String, Instant>,
}

impl Pace {
    /// A little over a second for MusicBrainz, half a second for Wikimedia; a
    /// busy service is paused for 30 seconds, doubling up to 10 minutes.
    pub fn polite() -> Self {
        Self {
            musicbrainz: Duration::from_millis(1100),
            other: Duration::from_millis(500),
            busy_min: Duration::from_secs(30),
            busy_max: Duration::from_secs(600),
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
        failure: Failure,
        mbid: String,
    },
}

/// Why a lookup stopped short of an answer.
#[derive(Debug, PartialEq, Eq)]
pub enum Failure {
    /// Downloading was turned off, or the worker retired, before a request.
    Cancelled,
    /// The service is shedding load (HTTP 429 or 503): a fact about the
    /// moment, not the artist.
    Busy(String),
    Error(String),
}

/// One HTTP answer: the body, `None` for a 404, or why there is none.
type Fetched = Result<Option<Vec<u8>>, Failure>;

/// Where a lookup chain stopped short of a picture.
enum Stop {
    Missing(Status),
    Failed(Failure),
}

/// A step's answer, with a 404 as `not_found`.
fn found<T>(answer: Result<Option<T>, Failure>) -> Result<T, Stop> {
    answer
        .map_err(Stop::Failed)?
        .ok_or(Stop::Missing(Status::NotFound))
}

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
    /// its picture. `proceed` is asked before each request.
    pub fn fetch(&mut self, name: &str, proceed: &dyn Fn() -> bool) -> Outcome {
        let mut mbid = String::new();
        match self.chain(name, proceed, &mut mbid) {
            Ok(image) => Outcome::Found {
                image,
                mbid,
                source: SOURCE_WIKIDATA,
            },
            Err(Stop::Missing(status)) => Outcome::Missing { status, mbid },
            Err(Stop::Failed(failure)) => Outcome::Failed { failure, mbid },
        }
    }

    /// MusicBrainz search, its Wikidata link, P18, then the Commons file,
    /// leaving the resolved id in `mbid` for whatever the chain ends in.
    fn chain(
        &mut self,
        name: &str,
        proceed: &dyn Fn() -> bool,
        mbid: &mut String,
    ) -> Result<Vec<u8>, Stop> {
        let search = found(self.json(&musicbrainz_search_url(name), proceed))?;
        *mbid = pick_artist(name, &search).map_err(Stop::Missing)?;
        let artist = found(self.json(&musicbrainz_lookup_url(mbid), proceed))?;
        let item = wikidata_item(&artist).ok_or(Stop::Missing(Status::NotFound))?;
        let claims = found(self.json(&wikidata_image_url(&item), proceed))?;
        let file = image_file(&claims).ok_or(Stop::Missing(Status::NotFound))?;
        found(self.get(&commons_url(&file), proceed))
    }

    fn get(&mut self, url: &str, proceed: &dyn Fn() -> bool) -> Fetched {
        self.pace.wait_for(url);
        if !proceed() {
            return Err(Failure::Cancelled);
        }
        let response = self
            .client
            .get(url, None)
            .map_err(|e| Failure::Error(e.to_string()))?;
        match response.status {
            404 => Ok(None),
            _ if response.is_success() => Ok(Some(response.body)),
            status @ (429 | 503) => Err(Failure::Busy(format!("HTTP {status} from {}", host(url)))),
            status => Err(Failure::Error(format!("HTTP {status} from {}", host(url)))),
        }
    }

    fn json(&mut self, url: &str, proceed: &dyn Fn() -> bool) -> Result<Option<Value>, Failure> {
        self.get(url, proceed)?
            .map(|body| {
                serde_json::from_slice(&body)
                    .map_err(|e| Failure::Error(format!("{} JSON: {e}", host(url))))
            })
            .transpose()
    }
}

/// Fetches queued artists until this worker is retired or the store is gone,
/// calling `landed` with each picture stored.
///
/// MusicBee's thumbnail first; the web only for what MusicBee has none of, once
/// that artist's last web miss is due. The store is reached only through
/// [`FetchQueue::with_store`], so a retired worker never keeps `mbrc.redb` open.
/// A busy web source puts the artist back and pauses the worker.
pub fn run(
    queue: &FetchQueue,
    store: &Weak<CoverStore>,
    musicbee: &dyn Providers,
    fetcher: &mut Fetcher<'_>,
    generation: u64,
    landed: &dyn Fn(&str, &str),
) {
    super::store::below_normal_priority();
    let mut budget_noted = false;
    let mut busy_pause = fetcher.pace.busy_min;
    while queue.is_current(generation) {
        let Some(item) = queue.next(IDLE_WAIT) else {
            continue;
        };
        let name = item.name.as_str();
        let lead = credits::lead_artist(name);
        let Some(ready) = queue.with_store(generation, store, |images| {
            is_ready(images, name, &mut budget_noted) && !share_lead(images, name, lead, landed)
        }) else {
            return;
        };
        if !ready || !queue.is_current(generation) || !queue.is_enabled() {
            continue;
        }
        // Outside the store: retiring never waits on MusicBee, joining the thread does.
        let thumb = musicbee_thumb(musicbee, lead);
        let Some(next) = queue.with_store(generation, store, |images| {
            if thumb.is_some_and(|raw| store_thumb(images, lead, &raw, landed)) {
                share_lead(images, name, lead, landed);
                Next::Skip
            } else if images.web_due(lead, now_unix_secs()) {
                Next::Web
            } else {
                Next::Skip
            }
        }) else {
            return;
        };
        if next == Next::Skip {
            continue;
        }
        let proceed = || queue.is_current(generation) && queue.is_enabled();
        match fetcher.fetch(lead, &proceed) {
            Outcome::Failed {
                failure: Failure::Cancelled,
                ..
            } => {}
            Outcome::Failed {
                failure: Failure::Busy(error),
                ..
            } => {
                tracing::debug!(artist = item.name, %error, pause = ?busy_pause, "artist picture source busy");
                queue.push(item);
                queue.pause(generation, busy_pause);
                busy_pause = (busy_pause * 2).min(fetcher.pace.busy_max);
            }
            outcome => {
                busy_pause = fetcher.pace.busy_min;
                let settled = queue.with_store(generation, store, |images| {
                    settle(images, lead, outcome, landed);
                    share_lead(images, &item.name, lead, landed);
                });
                if settled.is_none() {
                    return;
                }
            }
        }
    }
}

/// Gives a name nobody split its lead artist's picture, when that is stored.
fn share_lead(images: &ArtistImages, name: &str, lead: &str, landed: &dyn Fn(&str, &str)) -> bool {
    if lead == name {
        return false;
    }
    let Some(hash) = images.hash_for(lead) else {
        return false;
    };
    images.link(name, &hash);
    landed(name, &hash);
    true
}

/// What one queued artist needs after MusicBee was asked.
#[derive(PartialEq, Eq)]
enum Next {
    Skip,
    Web,
}

/// MusicBee's thumbnail of `name`, when it has or can get one: about a second
/// when it downloads, instant once it has answered.
fn musicbee_thumb(musicbee: &dyn Providers, name: &str) -> Option<Vec<u8>> {
    let path = match musicbee.artist_picture_thumb(name) {
        Ok(path) if !path.is_empty() => path,
        Ok(_) => return None,
        Err(e) => {
            tracing::debug!(artist = name, error = %e, "MusicBee artist thumbnail failed");
            return None;
        }
    };
    std::fs::read(&path).ok().filter(|raw| !raw.is_empty())
}

/// Stores MusicBee's thumbnail of `name`, announcing it; false when it does not decode.
fn store_thumb(images: &ArtistImages, name: &str, raw: &[u8], landed: &dyn Fn(&str, &str)) -> bool {
    match images.store(name, raw, "", SOURCE_MUSICBEE) {
        Ok(hash) => {
            tracing::debug!(artist = name, %hash, source = SOURCE_MUSICBEE, "artist picture stored");
            landed(name, &hash);
            true
        }
        Err(e) => {
            tracing::debug!(artist = name, error = %e, "MusicBee artist thumbnail unusable");
            false
        }
    }
}

/// Whether `name` is still worth a lookup: no picture, and room for one.
fn is_ready(images: &ArtistImages, name: &str, budget_noted: &mut bool) -> bool {
    if !images.lacks_picture(name) {
        return false;
    }
    if images.over_budget() {
        if !*budget_noted {
            tracing::info!(
                used = images.used_bytes(),
                "artist pictures reached their disk budget; not fetching more"
            );
            *budget_noted = true;
        }
        return false;
    }
    true
}

/// Records what one lookup came to; nothing when downloading was turned off
/// while it ran.
fn settle(images: &ArtistImages, name: &str, outcome: Outcome, landed: &dyn Fn(&str, &str)) {
    if !images.is_enabled() {
        return;
    }
    match outcome {
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
        Outcome::Failed { failure, mbid } => {
            tracing::debug!(artist = name, ?failure, "artist picture fetch failed");
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
    let wanted = credits::key(name);
    let exact: Vec<&Value> = results
        .iter()
        .filter(|a| {
            a["name"]
                .as_str()
                .is_some_and(|n| credits::key(n) == wanted)
        })
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

#[cfg(test)]
mod tests {
    use std::sync::{Arc, Mutex};

    use mbrc_release::{HttpResponse, UpdateError};
    use serde_json::json;

    use super::*;
    use crate::cover::artists::Priority;
    use crate::cover::test_jpeg_bytes;
    use crate::providers::MockProviders;

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
        asked: Mutex<Vec<String>>,
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
            self.asked.lock().unwrap().push(url.to_owned());
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

    impl Recorded {
        fn asked(&self) -> Vec<String> {
            self.asked.lock().unwrap().clone()
        }
    }

    fn fetch(client: &Recorded, name: &str) -> Outcome {
        Fetcher::new(client, Pace::default()).fetch(name, &|| true)
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
            client.asked()[3].ends_with(
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
        assert_eq!(client.asked().len(), 1);
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
    fn busy() -> Recorded {
        Recorded::default().with(
            musicbrainz_search_url("Eluveitie"),
            503,
            fixture("mb_busy.json"),
        )
    }

    #[test]
    fn a_busy_server_is_told_apart_from_a_failure() {
        assert!(matches!(
            fetch(&busy(), "Eluveitie"),
            Outcome::Failed {
                failure: Failure::Busy(_),
                ..
            }
        ));
        let offline = Recorded {
            offline: true,
            ..Recorded::default()
        };
        assert!(matches!(
            fetch(&offline, "Eluveitie"),
            Outcome::Failed {
                failure: Failure::Error(_),
                ..
            }
        ));
    }

    #[test]
    fn nothing_is_sent_once_told_to_stop() {
        let client = Recorded::eluveitie();
        let outcome = Fetcher::new(&client, Pace::default()).fetch("Eluveitie", &|| false);
        assert!(matches!(
            outcome,
            Outcome::Failed {
                failure: Failure::Cancelled,
                ..
            }
        ));
        assert!(client.asked().is_empty());
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

    /// Runs a worker on its own thread until `done` holds, then retires it.
    fn run_until(
        store: &Arc<CoverStore>,
        musicbee: MockProviders,
        client: Recorded,
        done: impl Fn(&ArtistImages) -> bool,
    ) -> (Recorded, Vec<(String, String)>) {
        let queue = store.artists().queue();
        let generation = queue.begin_worker();
        let weak = Arc::downgrade(store);
        let worker = {
            let queue = queue.clone();
            std::thread::spawn(move || {
                let landed = Mutex::new(Vec::new());
                let mut fetcher = Fetcher::new(&client, Pace::default());
                run(
                    &queue,
                    &weak,
                    &musicbee,
                    &mut fetcher,
                    generation,
                    &|name, hash| {
                        landed
                            .lock()
                            .unwrap()
                            .push((name.to_owned(), hash.to_owned()));
                    },
                );
                drop(fetcher);
                (client, landed.into_inner().unwrap())
            })
        };
        let deadline = Instant::now() + Duration::from_secs(10);
        while !done(store.artists()) && Instant::now() < deadline {
            std::thread::sleep(Duration::from_millis(10));
        }
        queue.retire();
        worker.join().unwrap()
    }

    fn worker_store(name: &str) -> Arc<CoverStore> {
        let store = Arc::new(crate::cover::test_cover_store(&format!(
            "artist-fetch-{name}"
        )));
        store.artists().set_enabled(true);
        store
    }

    #[test]
    fn the_worker_stores_what_it_finds_records_what_it_does_not_and_announces_pictures() {
        let store = worker_store("worker");
        let images = store.artists();
        images.request("Kaleidoscope", Priority::Requested);
        images.request("Eluveitie", Priority::NowPlaying);
        let client = Recorded::eluveitie().with(
            musicbrainz_search_url("Kaleidoscope"),
            200,
            fixture("mb_search_kaleidoscope.json"),
        );
        let (_, landed) = run_until(&store, MockProviders::default(), client, |images| {
            images.lookup("Kaleidoscope").is_some()
        });

        let hash = images.hash_for("Eluveitie").expect("stored");
        assert_eq!(landed, [("Eluveitie".to_owned(), hash)]);
        assert_eq!(
            images.lookup("Kaleidoscope").map(|l| l.status),
            Some(Status::Ambiguous)
        );
    }

    #[test]
    fn a_busy_service_puts_the_artist_back_instead_of_charging_it_an_error() {
        let store = worker_store("busy");
        store.artists().request("Eluveitie", Priority::Requested);
        let polls = std::cell::Cell::new(0);
        let (client, _) = run_until(&store, MockProviders::default(), busy(), |_| {
            polls.set(polls.get() + 1);
            polls.get() > 20
        });
        assert!(client.asked().len() > 1, "asked again after the pause");
        assert_eq!(store.artists().lookup("Eluveitie"), None);
    }

    #[test]
    fn a_retired_worker_lets_go_of_the_store() {
        let store = worker_store("retired");
        let (_, landed) = run_until(
            &store,
            MockProviders::default(),
            Recorded::default(),
            |_| true,
        );
        assert!(landed.is_empty());
        assert_eq!(Arc::strong_count(&store), 1);
    }

    /// The whole chain against the live services through WinHTTP, Commons'
    /// redirects included. Run by hand: `cargo test ... -- --ignored live_`.
    #[cfg(windows)]
    #[test]
    #[ignore = "live network"]
    fn live_eluveitie_resolves_to_a_picture_through_winhttp() {
        let client = crate::updates::http_client(&crate::config::Config::default()).unwrap();
        let outcome = Fetcher::new(&client, Pace::polite()).fetch("Eluveitie", &|| true);
        let Outcome::Found { image, mbid, .. } = outcome else {
            panic!("expected a picture, got {outcome:?}");
        };
        assert_eq!(mbid, ELUVEITIE_MBID);
        let picture = image::load_from_memory(&image).expect("an image");
        assert!(picture.width() <= 500 && picture.height() <= 500);
    }

    /// A stand-in MusicBee whose thumbnails are files in `dir`: a picture for
    /// `with`, an empty file (MusicBee found none) for `without`.
    fn musicbee(dir: &std::path::Path, with: &[&str], without: &[&str]) -> MockProviders {
        std::fs::create_dir_all(dir).unwrap();
        let mut thumbs = HashMap::new();
        for (name, bytes) in with
            .iter()
            .map(|n| (n, test_jpeg_bytes(188, 188)))
            .chain(without.iter().map(|n| (n, Vec::new())))
        {
            let path = dir.join(format!("{name}.png"));
            std::fs::write(&path, bytes).unwrap();
            thumbs.insert((*name).to_owned(), path.to_string_lossy().into_owned());
        }
        MockProviders {
            artist_thumbs: thumbs,
            ..MockProviders::default()
        }
    }

    #[test]
    fn musicbee_answers_first_and_the_web_is_not_asked() {
        let store = worker_store("musicbee-first");
        store.artists().request("Eluveitie", Priority::Requested);
        let thumbs = std::env::temp_dir().join("mbrc-test-musicbee-first-thumbs");
        let (client, landed) = run_until(
            &store,
            musicbee(&thumbs, &["Eluveitie"], &[]),
            Recorded::eluveitie(),
            |images| images.hash_for("Eluveitie").is_some(),
        );

        assert!(client.asked().is_empty());
        assert_eq!(landed.len(), 1);
        assert_eq!(
            store.artists().lookup("Eluveitie").map(|l| l.source),
            Some(SOURCE_MUSICBEE.to_owned())
        );
    }

    #[test]
    fn the_web_is_the_fallback_when_musicbee_found_none() {
        let store = worker_store("musicbee-none");
        store.artists().request("Eluveitie", Priority::Requested);
        let thumbs = std::env::temp_dir().join("mbrc-test-musicbee-none-thumbs");
        let (client, _) = run_until(
            &store,
            musicbee(&thumbs, &[], &["Eluveitie"]),
            Recorded::eluveitie(),
            |images| images.hash_for("Eluveitie").is_some(),
        );

        assert_eq!(client.asked().len(), 4);
        assert_eq!(
            store.artists().lookup("Eluveitie").map(|l| l.source),
            Some(SOURCE_WIKIDATA.to_owned())
        );
    }

    #[test]
    fn a_recent_web_miss_still_takes_a_picture_musicbee_has_since() {
        let store = worker_store("musicbee-later");
        store.artists().record_miss("Jazzbit", Status::NotFound, "");
        store
            .artists()
            .record_miss("Jormundgard", Status::NotFound, "");
        store.artists().request("Jazzbit", Priority::Requested);
        store.artists().request("Jormundgard", Priority::Requested);
        let thumbs = std::env::temp_dir().join("mbrc-test-musicbee-later-thumbs");
        let (client, _) = run_until(
            &store,
            musicbee(&thumbs, &["Jazzbit"], &["Jormundgard"]),
            Recorded::default(),
            |images| images.hash_for("Jazzbit").is_some() && images.summary().waiting == 0,
        );

        assert!(
            client.asked().is_empty(),
            "the web waits out its miss for both"
        );
        assert_eq!(store.artists().hash_for("Jormundgard"), None);
    }

    /// Retiring stops the worker without waiting on MusicBee; the host waits by
    /// joining the thread, and nothing the late answer brings is stored.
    #[test]
    fn retiring_does_not_wait_on_musicbee_but_joining_the_thread_does() {
        let store = worker_store("retire-mid-call");
        store.artists().request("Slow", Priority::Requested);
        let musicbee = Arc::new(MockProviders {
            artist_thumb_delay: Duration::from_millis(400),
            ..MockProviders::default()
        });
        let queue = store.artists().queue();
        let generation = queue.begin_worker();
        let weak = Arc::downgrade(&store);
        let worker = {
            let (queue, musicbee) = (queue.clone(), musicbee.clone());
            std::thread::spawn(move || {
                let client = Recorded::default();
                let mut fetcher = Fetcher::new(&client, Pace::default());
                run(
                    &queue,
                    &weak,
                    musicbee.as_ref(),
                    &mut fetcher,
                    generation,
                    &|_, _| {},
                );
                client.asked()
            })
        };
        let deadline = Instant::now() + Duration::from_secs(5);
        while musicbee.recorded().is_empty() && Instant::now() < deadline {
            std::thread::sleep(Duration::from_millis(5));
        }

        let started = Instant::now();
        queue.retire();
        assert!(
            started.elapsed() < Duration::from_millis(200),
            "retire waited on MusicBee"
        );
        let asked = worker.join().unwrap();
        assert!(
            started.elapsed() >= Duration::from_millis(200),
            "the join waits for the call"
        );
        assert!(asked.is_empty(), "no web lookup after retiring");
        assert_eq!(store.artists().lookup("Slow"), None);
    }

    #[test]
    fn a_featured_name_takes_its_lead_artists_picture_from_musicbee() {
        let store = worker_store("feat-musicbee");
        let joined = "Adelitas Way feat. Tyler Connolly";
        store.artists().request(joined, Priority::Requested);
        let thumbs = std::env::temp_dir().join("mbrc-test-feat-musicbee-thumbs");
        let musicbee = musicbee(&thumbs, &["Adelitas Way"], &[]);
        let (client, landed) = run_until(&store, musicbee, Recorded::default(), |images| {
            images.hash_for(joined).is_some()
        });

        let lead = store
            .artists()
            .hash_for("Adelitas Way")
            .expect("lead stored");
        assert_eq!(store.artists().hash_for(joined), Some(lead.clone()));
        assert_eq!(
            landed,
            [
                ("Adelitas Way".to_owned(), lead.clone()),
                (joined.to_owned(), lead)
            ]
        );
        assert!(client.asked().is_empty());
    }

    #[test]
    fn a_featured_name_looks_its_lead_artist_up_on_the_web() {
        let store = worker_store("feat-web");
        store
            .artists()
            .request("Eluveitie feat. Anna Murphy", Priority::Requested);
        let (client, _) = run_until(
            &store,
            MockProviders::default(),
            Recorded::eluveitie(),
            |images| images.hash_for("Eluveitie feat. Anna Murphy").is_some(),
        );

        assert_eq!(client.asked()[0], musicbrainz_search_url("Eluveitie"));
        assert!(store.artists().hash_for("Eluveitie").is_some());
    }

    #[test]
    fn a_featured_name_whose_lead_has_a_picture_asks_nobody() {
        let store = worker_store("feat-stored");
        let hash = store
            .artists()
            .store("Tico", &test_jpeg_bytes(64, 64), "", SOURCE_MUSICBEE)
            .unwrap();
        store
            .artists()
            .request("Tico feat. Gadiel", Priority::Requested);
        let musicbee = MockProviders::default();
        let (client, landed) = run_until(&store, musicbee, Recorded::default(), |images| {
            images.hash_for("Tico feat. Gadiel").is_some()
        });

        assert_eq!(landed, [("Tico feat. Gadiel".to_owned(), hash)]);
        assert!(client.asked().is_empty());
    }
}
