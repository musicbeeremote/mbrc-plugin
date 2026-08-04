//! Stamps the product version into the helper at compile time.
//!
//! The helper ships in the release bundle next to the two DLLs and is listed in
//! the update manifest, so it has to report the same version as the rest of the
//! product. `mbrc-buildinfo` reads it from `Directory.Build.props`, the one place
//! the version is bumped.

fn main() {
    mbrc_buildinfo::emit_version();
}
