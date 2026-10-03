//! The library change log behind V6 `library_changes` (#15).
//!
//! Every index pass that finds a track added, edited or removed bumps one
//! persisted `generation` and files each moved path under it. A client that
//! keeps its own copy of the library asks for what moved after the generation
//! it last read, instead of paging the whole library again.
//!
//! The log is diffed against itself rather than against the ordinal index, so a
//! cache rebuild that empties the index does not read as the library vanishing.
//! Each path keeps only its latest change, so the log grows with the library,
//! not with its history. Deletes are kept as tombstones up to
//! [`MAX_TOMBSTONES`]; dropping one raises the `floor`, and a cursor below the
//! floor can no longer be answered and is told to resync.
//!
//! The `epoch` names one run of the log. It is new whenever the log is reset
//! (a library switch), which is how a client learns that what it holds belongs
//! to a different library.

use std::ops::Bound;

use redb::{Durability, ReadableTable, TableDefinition, WriteTransaction};

use crate::store::{Db, META};

/// Path -> `(generation, live)`: the latest change filed for each path.
const TRACK_CHANGES: TableDefinition<&str, (u64, bool)> = TableDefinition::new("track_changes");
/// `(generation, path)` -> live: the same changes in the order a reader walks.
const CHANGE_LOG: TableDefinition<(u64, &str), bool> = TableDefinition::new("change_log");

/// [`META`] key holding the log's epoch (UTF-8 hex).
const EPOCH: &str = "changes_epoch";
/// [`META`] key holding the newest generation (u64 LE).
const GENERATION: &str = "changes_generation";
/// [`META`] key holding the oldest generation a cursor may still name (u64 LE).
const FLOOR: &str = "changes_floor";
/// [`META`] key holding how many tombstones the log keeps (u64 LE).
const TOMBSTONES: &str = "changes_tombstones";
/// [`META`] key holding how many paths the log holds as in the library (u64 LE).
const LIVE: &str = "changes_live";

/// Deletes remembered before the oldest are forgotten.
///
/// Bounded by count rather than age: a phone that syncs once a month after
/// three deletes should get them, not a full resync.
pub const MAX_TOMBSTONES: u64 = 10_000;

/// Where a reader stands in the log: what it last read, in which run of it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Cursor {
    pub epoch: String,
    pub generation: u64,
}

/// A resumable position inside one read of the log.
///
/// `since` and `until` pin the read's window so every page of it answers the
/// same question, and `total` is what the first page counted in it;
/// `generation` and `src` are the last change served.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Continuation {
    pub epoch: String,
    pub since: Option<u64>,
    pub until: u64,
    pub total: u64,
    pub generation: u64,
    pub src: String,
}

/// One filed change: the path, and whether it is in the library now.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Change {
    pub generation: u64,
    pub src: String,
    pub live: bool,
}

/// One page of a read.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ChangePage {
    /// The cursor cannot be answered from this log; start again from nothing.
    Resync { head: Cursor },
    Changes {
        /// The cursor a reader stores once it has read every page.
        head: Cursor,
        /// How many changes the whole read holds, counted when it started.
        total: u64,
        changes: Vec<Change>,
        next: Option<Continuation>,
    },
}

/// Where a read starts.
pub enum ReadFrom<'a> {
    /// Everything in the library now; deletes are left out.
    Start,
    Since(&'a Cursor),
    Resume(&'a Continuation),
}

/// The library change log. Every method is a best-effort no-op on a disabled store.
pub struct ChangeLog {
    db: Db,
}

impl ChangeLog {
    pub fn new(db: Db) -> Self {
        Self { db }
    }

    /// The log's current epoch and generation, or `None` before its first pass.
    pub fn head(&self) -> Option<Cursor> {
        self.db
            .read(|txn| {
                let meta = match txn.open_table(META) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                    Err(e) => return Err(e.into()),
                };
                let Some(epoch) = meta.get(EPOCH)? else {
                    return Ok(None);
                };
                let epoch = String::from_utf8_lossy(epoch.value()).into_owned();
                let generation = read_u64(meta.get(GENERATION)?.map(|g| g.value().to_vec()));
                Ok(Some(Cursor { epoch, generation }))
            })
            .flatten()
    }

    /// Files one index pass: what `paths` added or removed against the log, plus
    /// the `updated` paths still in the library.
    ///
    /// `paths` is `None` when the pass did not list the library; only the
    /// updates are filed then. Returns the new generation, or `None` when
    /// nothing moved. Only a listing starts an epoch, and its first pass files
    /// the whole library as added.
    pub fn record(&self, paths: Option<&[String]>, updated: &[String]) -> Option<u64> {
        let mut filed = None;
        self.db.write(Durability::Immediate, |txn| {
            filed = record_in(txn, paths, updated)?;
            Ok(())
        });
        filed
    }

    /// Reads up to `limit` changes from `from`.
    ///
    /// `None` before the log's first pass. A cursor from another epoch, below
    /// the floor, or ahead of the log is answered with [`ChangePage::Resync`].
    pub fn read(&self, from: ReadFrom<'_>, limit: usize) -> Option<ChangePage> {
        self.db
            .read(|txn| {
                let meta = match txn.open_table(META) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                    Err(e) => return Err(e.into()),
                };
                let Some(epoch) = meta.get(EPOCH)? else {
                    return Ok(None);
                };
                let epoch = String::from_utf8_lossy(epoch.value()).into_owned();
                let generation = read_u64(meta.get(GENERATION)?.map(|g| g.value().to_vec()));
                let floor = read_u64(meta.get(FLOOR)?.map(|g| g.value().to_vec()));
                let live = read_u64(meta.get(LIVE)?.map(|g| g.value().to_vec()));
                let head = Cursor {
                    epoch: epoch.clone(),
                    generation,
                };

                let (since, until, after) = match from {
                    ReadFrom::Start => (None, generation, None),
                    ReadFrom::Since(c) => (Some(c.generation), generation, None),
                    ReadFrom::Resume(c) => (c.since, c.until, Some((c.generation, c.src.as_str()))),
                };
                let foreign = match from {
                    ReadFrom::Start => false,
                    ReadFrom::Since(c) => c.epoch != epoch,
                    ReadFrom::Resume(c) => c.epoch != epoch || c.until > generation,
                };
                let unanswerable = since.is_some_and(|s| s < floor || s > generation);
                if foreign || unanswerable {
                    return Ok(Some(ChangePage::Resync { head }));
                }

                let lower = match after {
                    Some(key) => Bound::Excluded(key),
                    None => Bound::Included((since.map_or(0, |s| s + 1), "")),
                };
                let upper = Bound::Excluded((until + 1, ""));
                let log = match txn.open_table(CHANGE_LOG) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => {
                        return Ok(Some(ChangePage::Changes {
                            head: Cursor {
                                epoch,
                                generation: until,
                            },
                            total: 0,
                            changes: Vec::new(),
                            next: None,
                        }));
                    }
                    Err(e) => return Err(e.into()),
                };

                let total = match from {
                    ReadFrom::Start => live,
                    ReadFrom::Since(_) => log.range::<(u64, &str)>((lower, upper))?.count() as u64,
                    ReadFrom::Resume(c) => c.total,
                };
                let mut changes = Vec::new();
                let mut more = false;
                for row in log.range::<(u64, &str)>((lower, upper))? {
                    let (key, live) = row?;
                    let (generation, src) = key.value();
                    let live = live.value();
                    if !live && since.is_none() {
                        continue;
                    }
                    if changes.len() >= limit {
                        more = true;
                        break;
                    }
                    changes.push(Change {
                        generation,
                        src: src.to_string(),
                        live,
                    });
                }
                let next = more.then(|| {
                    let last = changes.last().expect("a full page has a last change");
                    Continuation {
                        epoch: epoch.clone(),
                        since,
                        until,
                        total,
                        generation: last.generation,
                        src: last.src.clone(),
                    }
                });
                Ok(Some(ChangePage::Changes {
                    head: Cursor {
                        epoch,
                        generation: until,
                    },
                    total,
                    changes,
                    next,
                }))
            })
            .flatten()
    }

    /// Forgets the log, so the next pass starts a new epoch.
    pub fn reset(&self) {
        self.db.write(Durability::Immediate, |txn| {
            txn.delete_table(TRACK_CHANGES)?;
            txn.delete_table(CHANGE_LOG)?;
            let mut meta = txn.open_table(META)?;
            for key in [EPOCH, GENERATION, FLOOR, TOMBSTONES, LIVE] {
                meta.remove(key)?;
            }
            Ok(())
        });
    }
}

/// The body of [`ChangeLog::record`], inside its write transaction.
fn record_in(
    txn: &WriteTransaction,
    paths: Option<&[String]>,
    updated: &[String],
) -> Result<Option<u64>, redb::Error> {
    let mut meta = txn.open_table(META)?;
    let fresh = meta.get(EPOCH)?.is_none();
    if fresh && paths.is_none() {
        return Ok(None);
    }
    if fresh {
        meta.insert(EPOCH, new_epoch().as_bytes())?;
    }
    let generation = read_u64(meta.get(GENERATION)?.map(|g| g.value().to_vec()));
    let mut tombstones = read_u64(meta.get(TOMBSTONES)?.map(|g| g.value().to_vec()));
    let mut in_library = read_u64(meta.get(LIVE)?.map(|g| g.value().to_vec()));

    let mut known = txn.open_table(TRACK_CHANGES)?;
    let (added, removed) = match paths {
        Some(paths) => diff(&known, paths)?,
        None => (Vec::new(), Vec::new()),
    };
    let mut edited: Vec<&str> = Vec::new();
    for path in updated {
        let live = matches!(known.get(path.as_str())?, Some(g) if g.value().1);
        if live && added.binary_search(&path.as_str()).is_err() {
            edited.push(path.as_str());
        }
    }
    edited.sort_unstable();
    edited.dedup();

    if added.is_empty() && removed.is_empty() && edited.is_empty() {
        if fresh {
            meta.insert(GENERATION, generation.to_le_bytes().as_slice())?;
        }
        return Ok(None);
    }

    let next = generation + 1;
    let mut log = txn.open_table(CHANGE_LOG)?;
    let filings = added
        .iter()
        .chain(&edited)
        .map(|p| (*p, true))
        .chain(removed.iter().map(|p| (p.as_str(), false)));
    for (path, live) in filings {
        let was_live = match known.get(path)?.map(|g| g.value()) {
            Some((was, was_live)) => {
                log.remove((was, path))?;
                if !was_live {
                    tombstones = tombstones.saturating_sub(1);
                }
                was_live
            }
            None => false,
        };
        match (was_live, live) {
            (false, true) => in_library += 1,
            (true, false) => in_library = in_library.saturating_sub(1),
            _ => {}
        }
        if !live {
            tombstones += 1;
        }
        known.insert(path, (next, live))?;
        log.insert((next, path), live)?;
    }

    if tombstones > MAX_TOMBSTONES {
        let floor = forget_oldest_tombstones(&mut known, &mut log, tombstones - MAX_TOMBSTONES)?;
        tombstones = MAX_TOMBSTONES;
        if let Some(floor) = floor {
            meta.insert(FLOOR, floor.to_le_bytes().as_slice())?;
        }
    }
    meta.insert(GENERATION, next.to_le_bytes().as_slice())?;
    meta.insert(TOMBSTONES, tombstones.to_le_bytes().as_slice())?;
    meta.insert(LIVE, in_library.to_le_bytes().as_slice())?;
    Ok(Some(next))
}

/// The paths `paths` adds to the log and the ones it no longer lists, both sorted.
///
/// A merge of the sorted list against the path-ordered log, so the pass holds
/// one borrowed list rather than a second copy of the library.
fn diff<'p>(
    known: &impl ReadableTable<&'static str, (u64, bool)>,
    paths: &'p [String],
) -> Result<(Vec<&'p str>, Vec<String>), redb::Error> {
    let mut listed: Vec<&str> = paths.iter().map(String::as_str).collect();
    listed.sort_unstable();
    listed.dedup();

    let mut added = Vec::new();
    let mut removed = Vec::new();
    let mut listed_iter = listed.iter().peekable();
    for row in known.iter()? {
        let (path, state) = row?;
        let (path, (_, live)) = (path.value(), state.value());
        while let Some(&&p) = listed_iter.peek() {
            if p < path {
                added.push(p);
                listed_iter.next();
            } else {
                break;
            }
        }
        let still_listed = listed_iter.peek().is_some_and(|&&p| p == path);
        if still_listed {
            listed_iter.next();
            if !live {
                added.push(path_from(&listed, path));
            }
        } else if live {
            removed.push(path.to_string());
        }
    }
    added.extend(listed_iter.copied());
    added.sort_unstable();
    Ok((added, removed))
}

/// The borrowed copy of `path` in `listed`, which is known to hold it.
fn path_from<'p>(listed: &[&'p str], path: &str) -> &'p str {
    let at = listed
        .binary_search(&path)
        .expect("a path matched in the merge is listed");
    listed[at]
}

/// Drops the `count` oldest tombstones; returns the newest generation dropped.
fn forget_oldest_tombstones(
    known: &mut redb::Table<&str, (u64, bool)>,
    log: &mut redb::Table<(u64, &str), bool>,
    count: u64,
) -> Result<Option<u64>, redb::Error> {
    let mut oldest: Vec<(u64, String)> = Vec::new();
    for row in log.iter()? {
        let (key, live) = row?;
        if live.value() {
            continue;
        }
        let (generation, src) = key.value();
        oldest.push((generation, src.to_string()));
        if oldest.len() as u64 >= count {
            break;
        }
    }
    for (generation, src) in &oldest {
        log.remove((*generation, src.as_str()))?;
        known.remove(src.as_str())?;
    }
    Ok(oldest.last().map(|(generation, _)| *generation))
}

/// A little-endian u64 read from `META`, zero when absent or malformed.
fn read_u64(bytes: Option<Vec<u8>>) -> u64 {
    bytes
        .and_then(|b| <[u8; 8]>::try_from(b.as_slice()).ok())
        .map_or(0, u64::from_le_bytes)
}

/// A fresh epoch: 64 random bits as hex.
fn new_epoch() -> String {
    let mut bytes = [0u8; 8];
    if getrandom::fill(&mut bytes).is_err() {
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_or(0, |d| d.as_nanos() as u64);
        bytes = nanos.to_le_bytes();
    }
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn log(name: &str) -> ChangeLog {
        let dir = std::env::temp_dir().join(format!("mbrc-changes-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        ChangeLog::new(Db::open(dir.to_str().unwrap()))
    }

    fn paths(names: &[&str]) -> Vec<String> {
        names.iter().map(|n| n.to_string()).collect()
    }

    fn read_all(log: &ChangeLog, from: ReadFrom<'_>) -> (Cursor, Vec<Change>) {
        match log.read(from, usize::MAX).unwrap() {
            ChangePage::Changes { head, changes, .. } => (head, changes),
            ChangePage::Resync { .. } => panic!("unexpected resync"),
        }
    }

    fn summary(changes: &[Change]) -> Vec<(&str, bool)> {
        changes.iter().map(|c| (c.src.as_str(), c.live)).collect()
    }

    #[test]
    fn the_first_pass_files_the_whole_library_as_added() {
        let log = log("first");
        assert_eq!(log.head(), None);
        assert_eq!(log.record(Some(&paths(&["/b", "/a"])), &[]), Some(1));

        let (head, changes) = read_all(&log, ReadFrom::Start);
        assert_eq!(head.generation, 1);
        assert_eq!(summary(&changes), vec![("/a", true), ("/b", true)]);
    }

    #[test]
    fn a_pass_that_moves_nothing_keeps_the_generation() {
        let log = log("quiet");
        log.record(Some(&paths(&["/a", "/b"])), &[]);
        assert_eq!(log.record(Some(&paths(&["/b", "/a"])), &[]), None);
        assert_eq!(log.head().unwrap().generation, 1);
    }

    #[test]
    fn a_cursor_reads_only_what_moved_after_it() {
        let log = log("since");
        log.record(Some(&paths(&["/a", "/b", "/c"])), &[]);
        let cursor = log.head().unwrap();

        let filed = log.record(Some(&paths(&["/a", "/c", "/d"])), &paths(&["/c"]));
        assert_eq!(filed, Some(2));

        let (head, changes) = read_all(&log, ReadFrom::Since(&cursor));
        assert_eq!(head.generation, 2);
        assert_eq!(
            summary(&changes),
            vec![("/b", false), ("/c", true), ("/d", true)]
        );
    }

    #[test]
    fn an_update_to_a_path_not_in_the_library_is_not_filed() {
        let log = log("stray-update");
        log.record(Some(&paths(&["/a"])), &[]);
        assert_eq!(log.record(Some(&paths(&["/a"])), &paths(&["/gone"])), None);
    }

    #[test]
    fn an_update_without_a_path_list_is_still_filed() {
        let log = log("no-list");
        log.record(Some(&paths(&["/a", "/b"])), &[]);
        let cursor = log.head().unwrap();
        assert_eq!(log.record(None, &paths(&["/b"])), Some(2));
        let (_, changes) = read_all(&log, ReadFrom::Since(&cursor));
        assert_eq!(summary(&changes), vec![("/b", true)]);
    }

    #[test]
    fn a_path_keeps_only_its_latest_change() {
        let log = log("latest");
        log.record(Some(&paths(&["/a"])), &[]);
        log.record(Some(&paths(&[])), &[]);
        log.record(Some(&paths(&["/a"])), &[]);

        let (_, changes) = read_all(
            &log,
            ReadFrom::Since(&Cursor {
                epoch: log.head().unwrap().epoch,
                generation: 0,
            }),
        );
        assert_eq!(summary(&changes), vec![("/a", true)]);
        assert_eq!(changes[0].generation, 3);
    }

    #[test]
    fn a_full_read_leaves_deletes_out() {
        let log = log("start");
        log.record(Some(&paths(&["/a", "/b"])), &[]);
        log.record(Some(&paths(&["/a"])), &[]);
        let (_, changes) = read_all(&log, ReadFrom::Start);
        assert_eq!(summary(&changes), vec![("/a", true)]);
    }

    #[test]
    fn a_cursor_from_another_epoch_is_told_to_resync() {
        let log = log("epoch");
        log.record(Some(&paths(&["/a"])), &[]);
        let old = log.head().unwrap();
        log.reset();
        log.record(Some(&paths(&["/z"])), &[]);
        assert_ne!(log.head().unwrap().epoch, old.epoch);
        assert!(matches!(
            log.read(ReadFrom::Since(&old), 10),
            Some(ChangePage::Resync { .. })
        ));
    }

    #[test]
    fn a_cursor_ahead_of_the_log_is_told_to_resync() {
        let log = log("ahead");
        log.record(Some(&paths(&["/a"])), &[]);
        let ahead = Cursor {
            generation: 9,
            ..log.head().unwrap()
        };
        assert!(matches!(
            log.read(ReadFrom::Since(&ahead), 10),
            Some(ChangePage::Resync { .. })
        ));
    }

    #[test]
    fn pages_resume_where_the_last_one_stopped() {
        let log = log("pages");
        log.record(Some(&paths(&["/a", "/b", "/c", "/d", "/e"])), &[]);

        let mut seen = Vec::new();
        let mut from = None;
        loop {
            let page = match &from {
                None => log.read(ReadFrom::Start, 2),
                Some(c) => log.read(ReadFrom::Resume(c), 2),
            };
            let Some(ChangePage::Changes { changes, next, .. }) = page else {
                panic!("expected a page");
            };
            seen.extend(changes.into_iter().map(|c| c.src));
            match next {
                Some(n) => from = Some(n),
                None => break,
            }
        }
        assert_eq!(seen, paths(&["/a", "/b", "/c", "/d", "/e"]));
    }

    /// A change filed between two pages lands past the read's `until`, so the
    /// read stays finite and the next one picks it up.
    #[test]
    fn a_change_during_a_read_waits_for_the_next_read() {
        let log = log("during");
        log.record(Some(&paths(&["/a", "/b", "/c"])), &[]);
        let Some(ChangePage::Changes { head, next, .. }) = log.read(ReadFrom::Start, 1) else {
            panic!("expected a page");
        };
        log.record(Some(&paths(&["/b", "/c"])), &[]);

        let mut rest = Vec::new();
        let mut from = next;
        while let Some(c) = from {
            let Some(ChangePage::Changes { changes, next, .. }) = log.read(ReadFrom::Resume(&c), 1)
            else {
                panic!("expected a page");
            };
            rest.extend(changes.into_iter().map(|c| c.src));
            from = next;
        }
        assert_eq!(rest, paths(&["/b", "/c"]));
        assert_eq!(head.generation, 1);

        let (_, after) = read_all(&log, ReadFrom::Since(&head));
        assert_eq!(summary(&after), vec![("/a", false)]);
    }

    #[test]
    fn forgetting_old_deletes_sends_older_cursors_to_resync() {
        let log = log("floor");
        let library: Vec<String> = (0..MAX_TOMBSTONES + 3)
            .map(|i| format!("/{i:06}"))
            .collect();
        log.record(Some(&library), &[]);
        let before = log.head().unwrap();

        log.record(Some(&[]), &[]);
        let after_deletes = log.head().unwrap();
        log.record(Some(&paths(&["/new"])), &[]);
        let survivor = log.head().unwrap();

        assert!(matches!(
            log.read(ReadFrom::Since(&before), 10),
            Some(ChangePage::Resync { .. })
        ));
        let (_, changes) = read_all(&log, ReadFrom::Since(&after_deletes));
        assert_eq!(summary(&changes), vec![("/new", true)]);
        assert_eq!(survivor.generation, 3);
    }

    fn total(page: Option<ChangePage>) -> u64 {
        match page {
            Some(ChangePage::Changes { total, .. }) => total,
            other => panic!("expected a page, got {other:?}"),
        }
    }

    #[test]
    fn a_full_read_counts_the_library_and_not_its_deletes() {
        let log = log("total-full");
        log.record(Some(&paths(&["/a", "/b", "/c"])), &[]);
        log.record(Some(&paths(&["/a", "/c", "/d", "/e"])), &paths(&["/a"]));
        assert_eq!(total(log.read(ReadFrom::Start, 1)), 4);
    }

    #[test]
    fn a_cursor_read_counts_its_changes_deletes_included() {
        let log = log("total-since");
        log.record(Some(&paths(&["/a", "/b", "/c"])), &[]);
        let cursor = log.head().unwrap();
        log.record(Some(&paths(&["/a", "/c", "/d"])), &paths(&["/a"]));
        assert_eq!(total(log.read(ReadFrom::Since(&cursor), 1)), 3);
    }

    /// The count travels in the continuation, so a sync resumed after the app
    /// was killed still knows how far along it is.
    #[test]
    fn every_page_of_a_read_reports_the_same_total() {
        let log = log("total-pages");
        log.record(Some(&paths(&["/a", "/b", "/c"])), &[]);
        let Some(ChangePage::Changes { next, .. }) = log.read(ReadFrom::Start, 1) else {
            panic!("expected a page");
        };
        log.record(Some(&paths(&["/a", "/b", "/c", "/d"])), &[]);
        let next = next.unwrap();
        assert_eq!(total(log.read(ReadFrom::Resume(&next), 1)), 3);
    }

    /// An epoch started by an edit would hold no tracks, and a client reading
    /// it would take the library for empty.
    #[test]
    fn an_edit_cannot_start_an_epoch() {
        let log = log("edit-first");
        assert_eq!(log.record(None, &paths(&["/a"])), None);
        assert_eq!(log.head(), None);
    }

    #[test]
    fn a_disabled_store_has_no_log() {
        let log = ChangeLog::new(Db::disabled());
        assert_eq!(log.record(Some(&paths(&["/a"])), &[]), None);
        assert_eq!(log.head(), None);
        assert!(log.read(ReadFrom::Start, 10).is_none());
    }
}
