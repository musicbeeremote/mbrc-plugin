//! Integration test: library-changing MusicBee notifications emit the V6
//! `library_changed` event from the Scanner's pass, after the debounce - NOT the
//! pure `notifications_v6::build` builder (which returns nothing for them, since
//! it has no owned `Arc<Core>`). Verifies the marker actually reaches a
//! subscribed V6 main over a real socket.

#![allow(clippy::unwrap_used)]

mod harness;

use std::sync::Arc;
use std::time::{Duration, Instant};

use mbrc_core::config::Config;
use mbrc_core::ffi::types::NotificationType;
use mbrc_core::state::{self, Core};

use harness::test_dir::TestDir;
use harness::{connect, null_core, read_frame, send, start_core, v6_handshake};

fn core(port: u16, dir: &TestDir) -> Arc<Core> {
    null_core(Config {
        storage_path: dir.to_string_lossy().into_owned(),
        ..Config::for_test(port)
    })
}

/// Waits out the reconcile `start` began, which files what it finds changed and
/// says so, and so would answer for an edit it knows nothing about.
fn wait_for_startup_reconcile(core: &Core) {
    let deadline = Instant::now() + Duration::from_secs(5);
    while !core.metadata_cache.is_validated() || core.is_reconciling() {
        assert!(Instant::now() < deadline, "the reconcile did not finish");
        std::thread::sleep(Duration::from_millis(5));
    }
}

/// A subscribed V6 main receives `library_changed` once the Scanner has applied
/// a reported change (the pure builder is silent for these notifications; see
/// `notifications_v6` unit tests).
#[test]
fn a_reported_edit_emits_library_changed_to_v6_main() {
    let dir = TestDir::new("notifications-v6");
    let (port, core, net) = start_core(|port| self::core(port, &dir));
    wait_for_startup_reconcile(&core);
    let library = vec!["/a.mp3".to_string()];
    core.metadata_cache.replace_track_index(&library);
    core.metadata_cache.record_changes(Some(&library), &[]);
    let (mut w, mut r) = connect(port, Duration::from_secs(6));

    send(&mut w, &v6_handshake("notif-test", false));
    assert_eq!(read_frame(&mut r)["data"]["server_version"], 6);
    // Ping barrier: once its response returns, the connection is registered with
    // the V6 broadcaster, so the event can't race ahead of the subscription.
    send(&mut w, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
    assert_eq!(read_frame(&mut r)["id"], 1);

    state::dispatch_notification(&core, NotificationType::TagsChanged, Some("/a.mp3"));

    // The cover build's last word can still be on its way.
    let ev = std::iter::repeat_with(|| read_frame(&mut r))
        .find(|frame| frame["event"] != "cover_cache_changed")
        .unwrap();
    assert_eq!(ev["kind"], "event", "{ev}");
    assert_eq!(ev["event"], "library_changed", "{ev}");

    net.stop();
}
