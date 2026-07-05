//! `mbrc inspect <capture.jsonl>` - summarise an mbrc-capture/2 trace.
//!
//! Reads a capture through the shared `mbrc-capture` reader so the CLI and the
//! debugger agree on what a valid record is.

use std::collections::BTreeMap;
use std::process::ExitCode;

use mbrc_capture::{parse_line, Record};

/// Frame/meta tallies for a capture file.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct Summary {
    pub frames: usize,
    pub c2s: usize,
    pub s2c: usize,
    pub meta: usize,
    pub unparsable: usize,
    /// Frame `context` -> count, sorted by context name.
    pub contexts: BTreeMap<String, usize>,
}

/// Tally every line of a capture. Blank lines are ignored; lines that aren't a
/// valid frame/meta record count as `unparsable`.
pub fn summarize(contents: &str) -> Summary {
    let mut s = Summary::default();
    for line in contents.lines() {
        if line.trim().is_empty() {
            continue;
        }
        match parse_line(line) {
            Some(Record::Frame(f)) => {
                s.frames += 1;
                match f.dir.as_str() {
                    "c2s" => s.c2s += 1,
                    "s2c" => s.s2c += 1,
                    _ => {}
                }
                if let Some(ctx) = f.context() {
                    *s.contexts.entry(ctx.to_string()).or_insert(0) += 1;
                }
            }
            Some(Record::Meta(_)) => s.meta += 1,
            None => s.unparsable += 1,
        }
    }
    s
}

impl Summary {
    fn print(&self) {
        println!(
            "frames: {} (c2s {}, s2c {})   meta: {}   unparsable: {}",
            self.frames, self.c2s, self.s2c, self.meta, self.unparsable
        );
        if !self.contexts.is_empty() {
            println!("contexts:");
            for (ctx, n) in &self.contexts {
                println!("  {ctx}\t{n}");
            }
        }
    }
}

pub fn run(args: &[String]) -> ExitCode {
    let Some(path) = args.first() else {
        eprintln!("usage: mbrc inspect <capture.jsonl>");
        return ExitCode::from(2);
    };
    let contents = match std::fs::read_to_string(path) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("read {path} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    summarize(&contents).print();
    ExitCode::SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn summarises_frames_meta_and_contexts() {
        let jsonl = concat!(
            r#"{"type":"meta","event":"capture-start","format":"mbrc-capture/2"}"#,
            "\n",
            r#"{"type":"frame","conn_id":0,"seq":0,"ts":"t","dir":"c2s","elapsed_ms":0,"raw":"x","frame":{"context":"player","data":"Android"}}"#,
            "\n",
            r#"{"type":"frame","conn_id":0,"seq":1,"ts":"t","dir":"s2c","elapsed_ms":1,"raw":"x","frame":{"context":"player","data":""}}"#,
            "\n",
            r#"{"type":"frame","conn_id":0,"seq":2,"ts":"t","dir":"s2c","elapsed_ms":2,"raw":"x","frame":{"context":"nowplaying","data":{}}}"#,
            "\n",
            "not json\n",
            "\n",
        );
        let s = summarize(jsonl);
        assert_eq!(s.frames, 3);
        assert_eq!(s.c2s, 1);
        assert_eq!(s.s2c, 2);
        assert_eq!(s.meta, 1);
        assert_eq!(s.unparsable, 1);
        assert_eq!(s.contexts.get("player"), Some(&2));
        assert_eq!(s.contexts.get("nowplaying"), Some(&1));
    }
}
