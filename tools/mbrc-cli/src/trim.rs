//! `mbrc trim` - turn raw `mbrc-capture/2` traces into committable golden
//! fixtures, one JSONL per `(platform, protocol)` bucket, plus the placeholder
//! cover PNG the replay harness seeds. All the trimming logic lives in
//! `mbrc-capture`; this is the file I/O around it.
//!
//! Usage: mbrc trim --in <file|dir> [--out <dir>]   (default out: tests/golden)

use std::fs;
use std::path::{Path, PathBuf};
use std::process::ExitCode;

use mbrc_capture::{trim_capture, PLACEHOLDER_PNG_B64};

use crate::args::flag_value;

pub fn run(args: &[String]) -> ExitCode {
    let Some(input) = flag_value(args, "--in") else {
        eprintln!("usage: mbrc trim --in <file|dir> [--out <dir>]");
        return ExitCode::from(2);
    };
    let out_dir = flag_value(args, "--out").unwrap_or_else(|| "tests/golden".to_string());

    let contents = match read_all(&input) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("read {input} failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    let result = trim_capture(&contents);

    if let Err(e) = write_output(&out_dir, &result) {
        eprintln!("write {out_dir} failed: {e}");
        return ExitCode::FAILURE;
    }

    for (bucket, lines) in &result.buckets {
        println!("  {bucket}.jsonl\t{} frames", lines.len());
    }
    println!(
        "Wrote {} bucket(s), {} frames total to {out_dir}",
        result.buckets.len(),
        result.total_frames()
    );
    ExitCode::SUCCESS
}

/// Read a single `.jsonl` file, or concatenate every `.jsonl` in a directory
/// (sorted, so output is deterministic).
pub(crate) fn read_all(input: &str) -> std::io::Result<String> {
    let path = Path::new(input);
    if path.is_dir() {
        let mut files: Vec<PathBuf> = fs::read_dir(path)?
            .filter_map(|e| e.ok().map(|e| e.path()))
            .filter(|p| p.extension().and_then(|s| s.to_str()) == Some("jsonl"))
            .collect();
        files.sort();
        let mut out = String::new();
        for f in files {
            out.push_str(&fs::read_to_string(f)?);
            out.push('\n');
        }
        Ok(out)
    } else {
        fs::read_to_string(path)
    }
}

fn write_output(out_dir: &str, result: &mbrc_capture::TrimOutput) -> std::io::Result<()> {
    fs::create_dir_all(out_dir)?;
    for (bucket, lines) in &result.buckets {
        let path = Path::new(out_dir).join(format!("{bucket}.jsonl"));
        let mut body = String::new();
        for line in lines {
            body.push_str(&serde_json::to_string(line).unwrap_or_default());
            body.push('\n');
        }
        fs::write(path, body)?;
    }
    // Placeholder cover asset - the deterministic bytes cover payloads were
    // rewritten to, so the replay harness can seed the same image.
    let assets = Path::new(out_dir).join("_assets");
    fs::create_dir_all(&assets)?;
    fs::write(
        assets.join("placeholder-cover.png"),
        base64_decode(PLACEHOLDER_PNG_B64),
    )?;
    Ok(())
}

/// Minimal standard-alphabet base64 decoder - avoids a dependency for the one
/// place the CLI needs it (writing the placeholder PNG).
fn base64_decode(s: &str) -> Vec<u8> {
    fn val(c: u8) -> Option<u8> {
        match c {
            b'A'..=b'Z' => Some(c - b'A'),
            b'a'..=b'z' => Some(c - b'a' + 26),
            b'0'..=b'9' => Some(c - b'0' + 52),
            b'+' => Some(62),
            b'/' => Some(63),
            _ => None,
        }
    }
    let mut out = Vec::new();
    let mut buf = 0u32;
    let mut bits = 0u32;
    for &c in s.as_bytes() {
        if c == b'=' {
            break;
        }
        let Some(v) = val(c) else { continue };
        buf = (buf << 6) | v as u32;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push((buf >> bits) as u8);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decodes_placeholder_png_magic() {
        let bytes = base64_decode(PLACEHOLDER_PNG_B64);
        // PNG signature.
        assert_eq!(
            &bytes[..8],
            &[0x89, b'P', b'N', b'G', 0x0d, 0x0a, 0x1a, 0x0a]
        );
    }
}
