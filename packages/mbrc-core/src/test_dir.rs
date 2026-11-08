//! Scratch folders for tests that remove themselves.
//!
//! Every test folder in this crate comes from here. `packages/mbrc-core/clippy.toml`
//! rejects `std::env::temp_dir` anywhere else, because a test run that leaves its
//! folders behind fills the disk over repeated runs. The integration tests include
//! this file through `tests/harness`.

use std::ops::{Deref, DerefMut};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU32, Ordering};
use std::time::{Duration, Instant};

/// How long a drop keeps retrying a folder that something still has locked.
const REMOVAL_PATIENCE: Duration = Duration::from_secs(10);

static NEXT_DIR: AtomicU32 = AtomicU32::new(0);

/// A new empty folder under the system temp directory, removed on drop.
///
/// Declare it before anything holding a `Db` under the folder, since locals drop
/// last-declared first and the handles should close before their files go. A folder
/// that cannot be removed fails the test instead of leaking quietly.
pub struct TestDir(PathBuf);

impl TestDir {
    /// `name` keeps the folder recognisable; the process and a counter make it unique.
    pub fn new(name: &str) -> Self {
        #[allow(clippy::disallowed_methods)]
        let parent = std::env::temp_dir();
        let unique = NEXT_DIR.fetch_add(1, Ordering::Relaxed);
        let path = parent.join(format!("mbrc-{name}-{}-{unique}", std::process::id()));
        let _ = std::fs::remove_dir_all(&path);
        std::fs::create_dir_all(&path).unwrap();
        Self(path)
    }

    /// Ties `value` to this folder, for a helper whose callers only want the value.
    pub fn holding<T>(self, value: T) -> InTestDir<T> {
        InTestDir { value, dir: self }
    }
}

impl Deref for TestDir {
    type Target = Path;

    fn deref(&self) -> &Path {
        &self.0
    }
}

impl AsRef<Path> for TestDir {
    fn as_ref(&self) -> &Path {
        &self.0
    }
}

impl From<&TestDir> for PathBuf {
    fn from(dir: &TestDir) -> Self {
        dir.0.clone()
    }
}

impl Drop for TestDir {
    fn drop(&mut self) {
        let deadline = Instant::now() + REMOVAL_PATIENCE;
        while let Err(error) = std::fs::remove_dir_all(&self.0) {
            if error.kind() == std::io::ErrorKind::NotFound {
                return;
            }
            if Instant::now() >= deadline {
                assert!(
                    std::thread::panicking(),
                    "{} is still in use, so a test left it behind: {error}",
                    self.0.display()
                );
                return;
            }
            std::thread::sleep(Duration::from_millis(20));
        }
    }
}

/// A value that lives in a [`TestDir`], dropped before the folder is removed.
pub struct InTestDir<T> {
    value: T,
    dir: TestDir,
}

impl<T> InTestDir<T> {
    pub fn dir(&self) -> &TestDir {
        &self.dir
    }

    /// The parts to bind separately, folder first so that it still drops last.
    pub fn into_parts(self) -> (TestDir, T) {
        (self.dir, self.value)
    }
}

impl<T> Deref for InTestDir<T> {
    type Target = T;

    fn deref(&self) -> &T {
        &self.value
    }
}

impl<T> DerefMut for InTestDir<T> {
    fn deref_mut(&mut self) -> &mut T {
        &mut self.value
    }
}
