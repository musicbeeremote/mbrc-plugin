//! `mbrc compare <a> <b>` - diff two captures by `(dir, context)` endpoint.
//!
//! Default mode diffs the *schema* (field presence/type); `--values` diffs the
//! actual `data` values (with `--ignore f1,f2` to drop volatile fields). Both
//! the report and the diff engine (`diff_report`) are reused by `mbrc replay`.
//!
//! Intended for parity work: capture the same session from two servers (e.g. the
//! C# plugin and the Rust core) and diff to find where they drift.
//!
//! Exit status: 0 if they match, 1 if any difference was found, 2 on a usage or
//! I/O error - so it composes like `diff` in scripts.

use std::collections::BTreeSet;
use std::process::ExitCode;

use mbrc_capture::{endpoint_schemas, endpoint_values, FieldMap};

use crate::args::{flag_value, has_flag};
use crate::trim::read_all;

pub fn run(args: &[String]) -> ExitCode {
    let positionals: Vec<&String> = args.iter().filter(|a| !a.starts_with("--")).collect();
    let (Some(a_path), Some(b_path)) = (positionals.first(), positionals.get(1)) else {
        eprintln!("usage: mbrc compare <a.jsonl|dir> <b.jsonl|dir> [--values] [--ignore f1,f2]");
        return ExitCode::from(2);
    };
    let values_mode = has_flag(args, "--values");
    let ignore = parse_ignore(args);

    let a = match read_all(a_path) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("read {a_path} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    let b = match read_all(b_path) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("read {b_path} failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    if diff_report(&a, &b, values_mode, &ignore) == 0 {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}

/// Parse `--ignore f1,f2` into a field list.
pub(crate) fn parse_ignore(args: &[String]) -> Vec<String> {
    flag_value(args, "--ignore")
        .map(|s| s.split(',').map(|f| f.trim().to_string()).collect())
        .unwrap_or_default()
}

/// Print a per-endpoint diff of two captures (schema or value mode) and return
/// the number of differing endpoints (0 == the captures match).
pub(crate) fn diff_report(a: &str, b: &str, values_mode: bool, ignore: &[String]) -> usize {
    if values_mode {
        value_diff(a, b, ignore)
    } else {
        schema_diff(a, b)
    }
}

fn schema_diff(a: &str, b: &str) -> usize {
    let sa = endpoint_schemas(a);
    let sb = endpoint_schemas(b);
    let endpoints: BTreeSet<&(String, String)> = sa.keys().chain(sb.keys()).collect();
    let mut differing = 0usize;
    let mut identical = 0usize;

    for ep in endpoints {
        let (dir, ctx) = ep;
        match (sa.get(ep), sb.get(ep)) {
            (Some(fa), Some(fb)) => {
                let lines = diff_fields(fa, fb);
                if lines.is_empty() {
                    identical += 1;
                } else {
                    differing += 1;
                    println!("~ {dir} {ctx}");
                    for l in lines {
                        println!("    {l}");
                    }
                }
            }
            (Some(_), None) => {
                differing += 1;
                println!("- {dir} {ctx}   (only in A)");
            }
            (None, Some(_)) => {
                differing += 1;
                println!("+ {dir} {ctx}   (only in B)");
            }
            (None, None) => unreachable!(),
        }
    }
    summarize(identical, differing);
    differing
}

fn value_diff(a: &str, b: &str, ignore: &[String]) -> usize {
    let va = endpoint_values(a, ignore);
    let vb = endpoint_values(b, ignore);
    let endpoints: BTreeSet<&(String, String)> = va.keys().chain(vb.keys()).collect();
    let empty = BTreeSet::new();
    let mut differing = 0usize;
    let mut identical = 0usize;

    for ep in endpoints {
        let (dir, ctx) = ep;
        let sa = va.get(ep).unwrap_or(&empty);
        let sb = vb.get(ep).unwrap_or(&empty);
        if sa == sb {
            identical += 1;
            continue;
        }
        differing += 1;
        println!("~ {dir} {ctx}");
        for only in sa.difference(sb) {
            println!("    - A: {}", truncate(only));
        }
        for only in sb.difference(sa) {
            println!("    + B: {}", truncate(only));
        }
    }
    summarize(identical, differing);
    differing
}

fn summarize(identical: usize, differing: usize) {
    println!(
        "\n{} endpoint(s): {identical} identical, {differing} differing",
        identical + differing
    );
}

fn truncate(s: &str) -> String {
    if s.chars().count() > 120 {
        let head: String = s.chars().take(120).collect();
        format!("{head}\u{2026}")
    } else {
        s.to_string()
    }
}

/// Field-level differences between two endpoint schemas: fields only on one
/// side, and fields whose type changed.
fn diff_fields(a: &FieldMap, b: &FieldMap) -> Vec<String> {
    let mut out = Vec::new();
    let paths: BTreeSet<&String> = a.keys().chain(b.keys()).collect();
    for p in paths {
        let label = if p.is_empty() { "(data)" } else { p.as_str() };
        match (a.get(p), b.get(p)) {
            (Some(ta), Some(tb)) if ta != tb => {
                out.push(format!("{label}: type A={ta} B={tb}"));
            }
            (Some(ta), None) => out.push(format!("{label}: only in A ({ta})")),
            (None, Some(tb)) => out.push(format!("{label}: only in B ({tb})")),
            _ => {}
        }
    }
    out
}
