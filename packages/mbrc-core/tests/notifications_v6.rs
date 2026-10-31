//! Integration test: library-changing MusicBee notifications emit the V6
//! `library_changed` event from the Scanner's pass, after the debounce - NOT the
//! pure `notifications_v6::build` builder (which returns nothing for them, since
//! it has no owned `Arc<Core>`). Verifies the marker actually reaches a
//! subscribed V6 main over a real socket.

#![allow(clippy::unwrap_used)]

mod harness;

use std::sync::Arc;
use std::time::Duration;

use mbrc_core::config::Config;
use mbrc_core::ffi::types::NotificationType;
use mbrc_core::state::{self, Core};

use harness::{connect, null_core, read_frame, send, start_core, v6_handshake};

/// A core whose music library holds `/a.mp3`, so an edit to it is the library's.
fn core(port: u16) -> Arc<Core> {
    let dir = std::env::temp_dir().join(format!("mbrc-notifications-v6-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    let config = Config {
        storage_path: dir.to_string_lossy().into_owned(),
        ..Config::for_test(port)
    };
    let core = null_core(config);
    let library = vec!["/a.mp3".to_string()];
    core.metadata_cache.reconcile(&[], 1);
    core.metadata_cache.replace_track_index(&library);
    core.metadata_cache.record_changes(Some(&library), &[]);
    core
}

/// A subscribed V6 main receives `library_changed` once the Scanner has applied
/// a reported change (the pure builder is silent for these notifications; see
/// `notifications_v6` unit tests).
#[test]
fn a_reported_edit_emits_library_changed_to_v6_main() {
    let (port, core, net) = start_core(core);
    let (mut w, mut r) = connect(port, Duration::from_secs(6));

    send(&mut w, &v6_handshake("notif-test", false));
    assert_eq!(read_frame(&mut r)["data"]["server_version"], 6);
    // Ping barrier: once its response returns, the connection is registered with
    // the V6 broadcaster, so the event can't race ahead of the subscription.
    send(&mut w, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
    assert_eq!(read_frame(&mut r)["id"], 1);

    state::dispatch_notification(&core, NotificationType::TagsChanged, Some("/a.mp3"));

    let ev = read_frame(&mut r);
    assert_eq!(ev["kind"], "event", "{ev}");
    assert_eq!(ev["event"], "library_changed", "{ev}");

    net.stop();
}
