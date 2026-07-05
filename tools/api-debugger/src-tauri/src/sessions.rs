//! Saved sessions - a managed directory of JSONL capture files under the app
//! data dir (`<app_data>/sessions`).
//!
//! A session is just an `mbrc-capture/2` JSONL file: either a Proxy/Direct
//! buffer saved from the UI, a proxy capture written here, or a file imported
//! from elsewhere. The Sessions panel lists/loads/deletes them; the Compare
//! panel shape-diffs two of them. File I/O uses `std::fs` directly (not the
//! Tauri fs plugin), so no capability grants are needed.

use std::fs;
use std::path::{Path, PathBuf};
use std::time::UNIX_EPOCH;

use serde::Serialize;
use tauri::{AppHandle, Manager};

#[derive(Debug, Clone, Serialize)]
pub struct SessionInfo {
    /// File name without the `.jsonl` extension.
    pub name: String,
    /// Absolute path to the file.
    pub path: String,
    pub bytes: u64,
    /// Last-modified time as epoch milliseconds (0 if unavailable).
    pub modified_ms: u64,
    /// Number of frame records (lines carrying a `"dir"` field).
    pub frames: usize,
}

/// The managed sessions directory, created if missing.
fn sessions_dir(app: &AppHandle) -> Result<PathBuf, String> {
    let dir = app
        .path()
        .app_data_dir()
        .map_err(|e| format!("no app data dir: {e}"))?
        .join("sessions");
    fs::create_dir_all(&dir).map_err(|e| format!("create {} failed: {e}", dir.display()))?;
    Ok(dir)
}

fn modified_ms(meta: &fs::Metadata) -> u64 {
    meta.modified()
        .ok()
        .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// Count frame records cheaply, without a full JSON parse: non-empty lines
/// carrying a `"dir"` field (meta/lifecycle lines have none).
fn count_frames(contents: &str) -> usize {
    contents.lines().filter(|l| l.contains("\"dir\"")).count()
}

fn info_for(path: &Path) -> Option<SessionInfo> {
    if path.extension().and_then(|e| e.to_str()) != Some("jsonl") {
        return None;
    }
    let meta = fs::metadata(path).ok()?;
    if !meta.is_file() {
        return None;
    }
    let name = path.file_stem()?.to_string_lossy().into_owned();
    let frames = fs::read_to_string(path)
        .map(|c| count_frames(&c))
        .unwrap_or(0);
    Some(SessionInfo {
        name,
        path: path.to_string_lossy().into_owned(),
        bytes: meta.len(),
        modified_ms: modified_ms(&meta),
        frames,
    })
}

/// Sanitize a user-supplied session name into a safe file stem.
fn sanitize(name: &str) -> String {
    let base: String = name
        .trim()
        .chars()
        .map(|c| {
            if c.is_alphanumeric() || matches!(c, '-' | '_' | '.') {
                c
            } else {
                '_'
            }
        })
        .collect();
    let base = base.trim_matches(|c| c == '.' || c == '_').to_string();
    // Require at least one real character; an all-punctuation name is useless.
    if base.chars().any(|c| c.is_alphanumeric()) {
        base
    } else {
        "session".to_string()
    }
}

#[tauri::command]
pub fn list_sessions(app: AppHandle) -> Result<Vec<SessionInfo>, String> {
    let dir = sessions_dir(&app)?;
    let mut out = Vec::new();
    for entry in fs::read_dir(&dir).map_err(|e| format!("read {} failed: {e}", dir.display()))? {
        let entry = entry.map_err(|e| e.to_string())?;
        if let Some(info) = info_for(&entry.path()) {
            out.push(info);
        }
    }
    // Newest first.
    out.sort_by_key(|s| std::cmp::Reverse(s.modified_ms));
    Ok(out)
}

#[tauri::command]
pub fn save_session(app: AppHandle, name: String, contents: String) -> Result<SessionInfo, String> {
    let dir = sessions_dir(&app)?;
    let path = dir.join(format!("{}.jsonl", sanitize(&name)));
    fs::write(&path, contents).map_err(|e| format!("write {} failed: {e}", path.display()))?;
    info_for(&path).ok_or_else(|| "saved but could not stat".to_string())
}

#[tauri::command]
pub fn read_session(path: String) -> Result<String, String> {
    fs::read_to_string(&path).map_err(|e| format!("read {path} failed: {e}"))
}

#[tauri::command]
pub fn delete_session(path: String) -> Result<(), String> {
    fs::remove_file(&path).map_err(|e| format!("delete {path} failed: {e}"))
}

#[tauri::command]
pub fn import_session(app: AppHandle, src: String) -> Result<SessionInfo, String> {
    let dir = sessions_dir(&app)?;
    let src_path = PathBuf::from(&src);
    let name = src_path
        .file_name()
        .ok_or_else(|| "source has no file name".to_string())?;
    let dest = dir.join(name);
    fs::copy(&src_path, &dest).map_err(|e| format!("import {src} failed: {e}"))?;
    info_for(&dest).ok_or_else(|| "imported but could not stat".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sanitize_strips_unsafe_chars() {
        assert_eq!(sanitize("my session/01"), "my_session_01");
        assert_eq!(sanitize("  keep-dots.v2  "), "keep-dots.v2");
        assert_eq!(sanitize("///"), "session");
        assert_eq!(sanitize(""), "session");
    }

    #[test]
    fn count_frames_ignores_meta_lines() {
        let jsonl = concat!(
            "{\"type\":\"meta\",\"event\":\"capture-start\"}\n",
            "{\"type\":\"frame\",\"dir\":\"c2s\",\"seq\":0}\n",
            "{\"type\":\"frame\",\"dir\":\"s2c\",\"seq\":1}\n",
            "\n",
        );
        assert_eq!(count_frames(jsonl), 2);
    }
}
