//! `mbrc-lint` - the comment rules from CLAUDE.md, enforced.
//!
//! Runs over the staged change (pre-commit), a diff range (CI), or the whole
//! tree (cleanup). Diff modes report only findings that touch changed lines, so
//! the rules can land without first cleaning every file in the repo.
//!
//! Usage: `mbrc-lint --staged | --diff <range> | --all | <path>...`

mod rules;
mod scan;

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::process::{Command, ExitCode};

/// Directories that hold no source we own.
const SKIP_DIRS: &[&str] = &[
    ".git",
    "target",
    "node_modules",
    "build",
    "bin",
    "obj",
    "dist",
    "app",
    "Generated",
];

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let files = match select(&args) {
        Ok(f) => f,
        Err(e) => {
            eprintln!("mbrc-lint: {e}");
            return ExitCode::from(2);
        }
    };

    // Indexed once: every rule that asks "does this still exist?" needs it.
    let repo = repo_files(Path::new("."));

    let mut findings = 0usize;
    for (path, ranges) in files {
        let Ok(source) = std::fs::read_to_string(&path) else {
            continue;
        };
        let lang = if path.extension().and_then(|e| e.to_str()) == Some("cs") {
            scan::Lang::CSharp
        } else {
            scan::Lang::Rust
        };
        let scanned = scan::scan(&source, lang);
        let mut found = rules::check(&scanned);
        found.extend(rules::check_paths(&scanned, &repo));
        found.sort_by_key(|f| (f.line, f.rule));
        for f in found {
            if let Some(ranges) = &ranges {
                if !ranges.iter().any(|(a, b)| f.line <= *b && f.end >= *a) {
                    continue;
                }
            }
            findings += 1;
            println!("{}:{}: [{}] {}", path.display(), f.line, f.rule, f.message);
        }
    }

    if findings > 0 {
        println!("\n{findings} finding(s). Every rule here fails the build.");
        println!("See the \"Comments and documentation\" section of CLAUDE.md.");
        return ExitCode::FAILURE;
    }
    ExitCode::SUCCESS
}

type Selection = Vec<(PathBuf, Option<Vec<(usize, usize)>>)>;

/// Resolves the argv into files, each with the line ranges to report on.
fn select(args: &[String]) -> Result<Selection, String> {
    match args.first().map(String::as_str) {
        None | Some("--help" | "-h") => {
            Err("usage: mbrc-lint --staged | --diff <range> | --all | <path>...".into())
        }
        Some("--all") => Ok(walk(Path::new("."))
            .into_iter()
            .map(|p| (p, None))
            .collect()),
        Some("--staged") => from_git(&["diff", "--cached"], None),
        Some("--diff") => {
            let range = args.get(1).ok_or("--diff needs a range")?;
            from_git(&["diff"], Some(range))
        }
        Some(_) => Ok(args
            .iter()
            .map(PathBuf::from)
            .filter(|p| is_source(p))
            .map(|p| (p, None))
            .collect()),
    }
}

/// Changed source files plus the added-line ranges within them.
fn from_git(base: &[&str], range: Option<&String>) -> Result<Selection, String> {
    let mut names: Vec<String> = base.iter().map(|s| (*s).to_string()).collect();
    names.push("--name-only".into());
    names.push("--diff-filter=ACM".into());
    if let Some(r) = range {
        names.push(r.clone());
    }
    let listed = git(&names)?;

    let mut out = Selection::new();
    for name in listed.lines().filter(|l| !l.trim().is_empty()) {
        let path = PathBuf::from(name);
        if !is_source(&path) || skipped(&path) {
            continue;
        }
        let mut hunk: Vec<String> = base.iter().map(|s| (*s).to_string()).collect();
        hunk.push("--unified=0".into());
        if let Some(r) = range {
            hunk.push(r.clone());
        }
        hunk.push("--".into());
        hunk.push(name.to_string());
        let ranges = hunks(&git(&hunk)?);
        if !ranges.is_empty() {
            out.push((path, Some(ranges)));
        }
    }
    Ok(out)
}

fn git(args: &[String]) -> Result<String, String> {
    let out = Command::new("git")
        .args(args)
        .output()
        .map_err(|e| format!("running git: {e}"))?;
    if !out.status.success() {
        return Err(format!(
            "git {}: {}",
            args.join(" "),
            String::from_utf8_lossy(&out.stderr).trim()
        ));
    }
    Ok(String::from_utf8_lossy(&out.stdout).into_owned())
}

/// Added-line ranges from `@@ -a,b +c,d @@` headers.
fn hunks(diff: &str) -> Vec<(usize, usize)> {
    let mut out = Vec::new();
    for line in diff.lines().filter(|l| l.starts_with("@@")) {
        let Some(plus) = line.split('+').nth(1) else {
            continue;
        };
        let spec: String = plus
            .chars()
            .take_while(|c| c.is_ascii_digit() || *c == ',')
            .collect();
        let mut parts = spec.split(',');
        let Some(start) = parts.next().and_then(|s| s.parse::<usize>().ok()) else {
            continue;
        };
        let count = parts
            .next()
            .and_then(|s| s.parse::<usize>().ok())
            .unwrap_or(1);
        if count == 0 {
            // A pure deletion: nothing was added to report on.
            continue;
        }
        out.push((start, start + count - 1));
    }
    out
}

fn is_source(path: &Path) -> bool {
    if !matches!(path.extension().and_then(|e| e.to_str()), Some("rs" | "cs")) {
        return false;
    }
    // Designer and csbindgen output is regenerated, so editing it achieves
    // nothing and the next build undoes it.
    let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("");
    !name.ends_with(".Designer.cs") && !name.ends_with(".Generated.cs")
}

fn skipped(path: &Path) -> bool {
    path.components().any(|c| {
        c.as_os_str()
            .to_str()
            .is_some_and(|s| SKIP_DIRS.contains(&s))
    })
}

/// Every `.rs` and `.cs` path under `root`, for resolving comment references.
fn repo_files(root: &Path) -> Vec<String> {
    let mut out = Vec::new();
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(entries) = std::fs::read_dir(&dir) else {
            continue;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            let name = entry.file_name();
            let name = name.to_string_lossy();
            if SKIP_DIRS.contains(&name.as_ref()) && name != "Generated" {
                continue;
            }
            if path.is_dir() {
                stack.push(path);
            } else if matches!(path.extension().and_then(|e| e.to_str()), Some("rs" | "cs")) {
                out.push(path.to_string_lossy().replace('\\', "/").replace("./", ""));
            }
        }
    }
    out
}

/// Every source file under `root`, minus the directories we do not own.
fn walk(root: &Path) -> Vec<PathBuf> {
    let mut found = BTreeMap::new();
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(entries) = std::fs::read_dir(&dir) else {
            continue;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            let name = entry.file_name();
            let name = name.to_string_lossy();
            if SKIP_DIRS.contains(&name.as_ref()) {
                continue;
            }
            if path.is_dir() {
                stack.push(path);
            } else if is_source(&path) {
                found.insert(path.to_string_lossy().replace('\\', "/"), path);
            }
        }
    }
    found.into_values().collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hunk_headers_become_added_line_ranges() {
        let diff = "@@ -1,2 +3,4 @@ fn x()\n@@ -9 +12 @@\n";
        assert_eq!(hunks(diff), vec![(3, 6), (12, 12)]);
    }

    #[test]
    fn a_pure_deletion_contributes_no_range() {
        assert!(hunks("@@ -4,3 +3,0 @@\n").is_empty());
    }

    #[test]
    fn only_rust_and_csharp_are_linted() {
        assert!(is_source(Path::new("a/b.rs")));
        assert!(is_source(Path::new("a/b.cs")));
        assert!(!is_source(Path::new("a/b.md")));
    }

    #[test]
    fn generated_csharp_is_not_ours_to_lint() {
        assert!(!is_source(Path::new("Properties/Resources.Designer.cs")));
        assert!(!is_source(Path::new("Ffi/NativeBridge.Generated.cs")));
    }

    #[test]
    fn generated_and_vendored_paths_are_skipped() {
        assert!(skipped(Path::new("packages/plugin/Ffi/Generated/x.cs")));
        assert!(skipped(Path::new("target/debug/x.rs")));
        assert!(!skipped(Path::new("packages/mbrc-core/src/lib.rs")));
    }
}
