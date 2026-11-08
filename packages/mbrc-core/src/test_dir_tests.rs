//! Removal behaviour of [`crate::test_dir`].

use std::path::PathBuf;

use crate::test_dir::TestDir;

#[test]
fn a_folder_goes_with_its_contents_when_dropped() {
    let dir = TestDir::new("test-dir-drop");
    std::fs::create_dir_all(dir.join("nested")).unwrap();
    std::fs::write(dir.join("nested").join("file"), b"x").unwrap();
    let path = dir.to_path_buf();
    drop(dir);
    assert!(!path.exists());
}

#[test]
fn a_failing_test_still_removes_its_folder() {
    let path = std::thread::spawn(|| {
        let dir = TestDir::new("test-dir-panic");
        let path = dir.to_path_buf();
        std::panic::panic_any(path);
    })
    .join()
    .unwrap_err();
    let path = path.downcast::<PathBuf>().unwrap();
    assert!(!path.exists());
}

#[test]
fn folders_of_the_same_name_are_separate() {
    let first = TestDir::new("test-dir-same");
    let second = TestDir::new("test-dir-same");
    assert_ne!(&*first, &*second);
}

#[test]
fn a_value_is_dropped_before_its_folder_goes() {
    struct SeesFolderOnDrop(PathBuf);
    impl Drop for SeesFolderOnDrop {
        fn drop(&mut self) {
            assert!(self.0.exists());
        }
    }

    let dir = TestDir::new("test-dir-holding");
    let path = dir.to_path_buf();
    drop(dir.holding(SeesFolderOnDrop(path.clone())));
    assert!(!path.exists());
}
