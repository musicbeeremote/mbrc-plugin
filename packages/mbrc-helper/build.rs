//! Stamps the product version into the helper at compile time.
//!
//! The helper ships in the release bundle next to the two DLLs and is listed in
//! the update manifest, so it has to report the same version as the rest of the
//! product. `mbrc-buildinfo` reads it from `Directory.Build.props`, the one place
//! the version is bumped, and embeds it with the application manifest in the
//! exe's resources.

fn main() {
    mbrc_buildinfo::emit_version();
    mbrc_buildinfo::embed_resources(&mbrc_buildinfo::Binary {
        description: "MusicBee Remote Helper",
        file_name: "mbrc-helper.exe",
        kind: mbrc_buildinfo::Kind::Exe,
        manifest: Some("mbrc-helper.exe.manifest"),
    });
}
