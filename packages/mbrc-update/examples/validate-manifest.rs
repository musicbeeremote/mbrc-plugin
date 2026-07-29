//! Validates a generated manifest with the same parser the plugin uses.
//!
//! Run by the release workflow immediately before signing, so a drift between
//! the generator in `.github/actions/package` and this crate fails the release
//! rather than shipping a manifest no installed plugin can read.
//!
//! ```text
//! cargo run -p mbrc-update --example validate-manifest -- build/dist/manifest.json
//! ```

use std::{fs, process::ExitCode};

use mbrc_update::Manifest;

fn main() -> ExitCode {
    let Some(path) = std::env::args().nth(1) else {
        eprintln!("usage: validate-manifest <manifest.json>");
        return ExitCode::FAILURE;
    };

    let bytes = match fs::read(&path) {
        Ok(bytes) => bytes,
        Err(e) => {
            eprintln!("cannot read {path}: {e}");
            return ExitCode::FAILURE;
        }
    };

    match Manifest::parse(&bytes) {
        Ok(manifest) => {
            println!(
                "{path} is valid: {} {} (schema {}, abi {}, {} files)",
                manifest.version,
                serde_json::to_string(&manifest.channel).unwrap_or_default(),
                manifest.schema,
                manifest.abi_version,
                manifest.files.len(),
            );
            for file in &manifest.files {
                println!("  {} {}", &file.sha512[..16], file.path);
            }
            ExitCode::SUCCESS
        }
        Err(e) => {
            eprintln!("{path} is not a valid release manifest: {e}");
            ExitCode::FAILURE
        }
    }
}
