//! The comment rules, and what each one is for.
//!
//! Every rule here is mechanical on purpose. "This doc restates the signature"
//! needs judgement and is left to review; what is left is countable, so it can
//! fail a build without argument. Thresholds come from CLAUDE.md.

use crate::scan::{Kind, Lang, Scan};

#[derive(Debug, Clone)]
pub struct Finding {
    pub line: usize,
    /// Last line the finding covers, so a diff-scoped run sees a whole block.
    pub end: usize,
    pub rule: &'static str,
    pub message: String,
}

/// Every rule id, so an `allow` directive can be followed by a plain-English
/// reason without the words being read as rule names.
pub const RULES: &[&str] = &[
    "block-too-long",
    "mid-function-prose",
    "prefer-rustdoc",
    "hedging",
    "placeholder",
    "em-dash",
    "banned-phrase",
    "history-narration",
    "dated-anecdote",
    "overused-word",
    "comment-ratio",
    "comment-heavy-function",
    "planning-leftover",
    "stale-path",
];

/// Phrases that say nothing. Matched case-insensitively.
const BANNED: &[&str] = &[
    "worth noting",
    "needless to say",
    "suffice it to say",
    "as we all know",
    "at the end of the day",
    "in today's",
    "simply put",
    "seamless",
    "delve",
    "tapestry",
];

/// History belongs in git, not in a comment a reader has to scroll past.
const HISTORY: &[&str] = &[
    "was previously",
    "previously named",
    "previously called",
    "previously known",
    "used to be",
    "used to return",
    "used to call",
    "used to have",
    "it used to",
    "we used to",
    "which used to",
    "that used to",
    "was renamed",
    "has since been",
    "in the old c#",
    "the old c# ",
];

/// Uncertainty stated instead of resolved. Borrowed from the antislop rulesets:
/// a comment that hedges is describing work that was never finished.
const HEDGING: &[&str] = &[
    "hopefully",
    "should work",
    "should be fine",
    "presumably",
    "might work",
    "probably works",
    "in theory",
    "for now",
    "temporary fix",
    "not sure why",
    "seems to work",
];

/// Plan vocabulary: words that name a schedule rather than the code, and go
/// stale the moment the schedule moves. Several such comments here were still
/// claiming work was unfinished long after it shipped.
const PLANNING: &[&str] = &[
    "cutover",
    "milestone",
    "sprint",
    "coming soon",
    "not implemented yet",
    "follow-up pr",
    "follow up pr",
    "next pr",
    "tbd",
    "wip",
    "mvp",
];

/// The same idea for words this codebase uses legitimately - a staging
/// directory, a loop iteration, a `.wav` - so they only count with a number
/// after them, which is what makes them a plan.
const PLANNING_NUMBERED: &[&str] = &["phase", "slice", "stage", "iteration", "wave", "layer"];

/// Markers for work that was deferred rather than done.
const PLACEHOLDERS: &[&str] = &["TODO", "FIXME", "HACK", "XXX"];

/// Words per line before a block is denser than the code around it. A full
/// 80-column comment line runs about twelve words, so this allows half again as
/// much and still catches a block dodging the line cap by wrapping wider.
const WORDS_PER_LINE: usize = 17;

/// Whether `needle` appears as a whole word, so a short marker cannot match
/// inside a longer one.
fn contains_word(text: &str, needle: &str) -> bool {
    text.match_indices(needle).any(|(at, _)| {
        let before = text[..at].chars().next_back();
        let after = text[at + needle.len()..].chars().next();
        !before.is_some_and(|c| c.is_alphanumeric()) && !after.is_some_and(|c| c.is_alphanumeric())
    })
}

/// A measurement is evidence for a commit message, not a comment.
const ANECDOTE: &[&str] = &["as measured", "observed twice", "measured across"];

/// Words that stop meaning anything once a file leans on them.
const OVERUSED: &[&str] = &["deliberately", "actually"];
const OVERUSED_CAP: usize = 3;

/// Lines of `//` a function body may carry before the explanation should have
/// been a name, a test, or a doc on the item instead.
const MID_FN_MAX: usize = 2;

/// Lines of prose one function body may carry in total, across however many
/// separate comments. The per-block cap cannot see a function explained two
/// lines at a time; this is the p99 of the tree, so it only catches that.
const FN_PROSE_MAX: usize = 6;

/// Files above this many lines are worth a prose-density opinion.
const RATIO_MIN_LINES: usize = 120;
const RATIO_CAP: f64 = 0.40;

pub fn check(scan: &Scan) -> Vec<Finding> {
    let mut out = Vec::new();

    for block in &scan.blocks {
        let start = block.start();
        let text = block.text().to_lowercase();

        let measured = block.measured_len();
        // A `//` block inside a function answers to `mid-function-prose`, which
        // is stricter; two findings on one block would just be noise.
        let mid_fn = block.kind == Kind::Line && block.in_fn();

        if scan.lang == Lang::Rust
            && block.kind == Kind::Line
            && !mid_fn
            && block.documents_an_item()
            && !scan.allowed("prefer-rustdoc", start)
        {
            out.push(Finding {
                line: start,
                end: start + block.lines.len() - 1,
                rule: "prefer-rustdoc",
                message: "a comment on an item belongs in its rustdoc: use `///`".to_string(),
            });
        }

        let line_cap = if mid_fn { MID_FN_MAX } else { block.kind.cap() };
        // Only when the block is within its line budget: otherwise the line cap
        // already said so, and one block should not produce two findings.
        let word_cap = line_cap * WORDS_PER_LINE;
        if measured <= line_cap
            && block.words() > word_cap
            && !scan.allowed("block-too-long", start)
        {
            out.push(Finding {
                line: start,
                end: start + block.lines.len() - 1,
                rule: "block-too-long",
                message: format!(
                    "{} words in a {}, cap is {word_cap}. Wrapping wider is not shorter",
                    block.words(),
                    block.kind.label()
                ),
            });
        }

        if mid_fn && measured > MID_FN_MAX && !scan.allowed("mid-function-prose", start) {
            out.push(Finding {
                line: start,
                end: start + block.lines.len() - 1,
                rule: "mid-function-prose",
                message: format!(
                    "{measured} lines of prose inside a function. Name it, test it, or move it to the item doc"
                ),
            });
        }
        if !mid_fn && measured > block.kind.cap() && !scan.allowed("block-too-long", start) {
            out.push(Finding {
                line: start,
                end: start + block.lines.len() - 1,
                rule: "block-too-long",
                message: format!(
                    "{} is {} lines, cap is {}. Cut it, or justify it with `// mbrc-lint: allow block-too-long`",
                    block.kind.label(),
                    measured,
                    block.kind.cap()
                ),
            });
        }

        content_rules(
            &text,
            &block.text(),
            start,
            start + block.lines.len() - 1,
            scan,
            &mut out,
        );
    }

    for line in &scan.trailing {
        content_rules(
            &line.text.to_lowercase(),
            &line.text,
            line.number,
            line.number,
            scan,
            &mut out,
        );
    }

    heavy_functions(scan, &mut out);
    overused(scan, &mut out);
    ratio(scan, &mut out);

    out.sort_by_key(|f| (f.line, f.rule));
    out
}

/// Rules that read the prose, applied to blocks and trailing comments alike.
fn content_rules(
    text: &str,
    original: &str,
    line: usize,
    end: usize,
    scan: &Scan,
    out: &mut Vec<Finding>,
) {
    if (text.contains('\u{2014}') || text.contains('\u{2013}')) && !scan.allowed("em-dash", line) {
        out.push(Finding {
            line,
            end,
            rule: "em-dash",
            message: "em/en dash. Use a hyphen, a comma, a colon, or two sentences".to_string(),
        });
    }

    if let Some(hit) = BANNED.iter().find(|p| text.contains(**p)) {
        if !scan.allowed("banned-phrase", line) {
            out.push(Finding {
                line,
                end,
                rule: "banned-phrase",
                message: format!("filler: \"{hit}\""),
            });
        }
    }

    if let Some(hit) = HISTORY.iter().find(|p| text.contains(**p)) {
        if !scan.allowed("history-narration", line) {
            out.push(Finding {
                line,
                end,
                rule: "history-narration",
                message: format!("history narration: \"{hit}\". Git holds that"),
            });
        }
    }

    if let Some(hit) = HEDGING.iter().find(|p| text.contains(**p)) {
        if !scan.allowed("hedging", line) {
            out.push(Finding {
                line,
                end,
                rule: "hedging",
                message: format!("hedging: \"{hit}\". Say what is true, or find out"),
            });
        }
    }

    // Whole word and upper case, or a time format like mm:ss.xxx reads as one.
    if let Some(hit) = PLACEHOLDERS.iter().find(|p| {
        original
            .split(|c: char| !c.is_ascii_alphanumeric())
            .any(|w| w == **p)
    }) {
        if !scan.allowed("placeholder", line) {
            out.push(Finding {
                line,
                end,
                rule: "placeholder",
                message: format!("{hit} marker. Open an issue, or do it"),
            });
        }
    }

    let planning = PLANNING.iter().find(|p| contains_word(text, p));
    let numbered = PLANNING_NUMBERED.iter().find(|w| {
        text.match_indices(**w).any(|(at, _)| {
            text[at + w.len()..]
                .trim_start()
                .starts_with(|c: char| c.is_ascii_digit())
        })
    });
    if let Some(hit) = planning.or(numbered) {
        if !scan.allowed("planning-leftover", line) {
            out.push(Finding {
                line,
                end,
                rule: "planning-leftover",
                message: format!(
                    "plan vocabulary: \"{hit}\". Describe the code, not the schedule it arrived on"
                ),
            });
        }
    }

    let dated = ANECDOTE.iter().find(|p| text.contains(**p)).copied();
    let dated = dated.or_else(|| iso_date(text).map(|_| "a date"));
    if let Some(hit) = dated {
        if !scan.allowed("dated-anecdote", line) {
            out.push(Finding {
                line,
                end,
                rule: "dated-anecdote",
                message: format!(
                    "dated anecdote ({hit}). Keep the finding, move the measurement to the commit message"
                ),
            });
        }
    }
}

/// A `YYYY-MM-DD` anywhere in the text.
fn iso_date(text: &str) -> Option<usize> {
    let chars: Vec<char> = text.chars().collect();
    (0..chars.len().saturating_sub(9)).find(|&i| {
        chars[i..i + 4].iter().all(char::is_ascii_digit)
            && chars[i + 4] == '-'
            && chars[i + 5..i + 7].iter().all(char::is_ascii_digit)
            && chars[i + 7] == '-'
            && chars[i + 8..i + 10].iter().all(char::is_ascii_digit)
    })
}

/// Flag a function body whose comments add up, however small each one is.
fn heavy_functions(scan: &Scan, out: &mut Vec<Finding>) {
    // Spans the comments, not just the signature: a diff that touches one of
    // them has to see this finding, and it may be a hundred lines lower down.
    let mut totals: std::collections::BTreeMap<usize, (usize, usize)> =
        std::collections::BTreeMap::new();
    for block in scan.blocks.iter().filter(|b| b.kind == Kind::Line) {
        if let Some(id) = block.fn_id {
            let entry = totals.entry(id).or_default();
            entry.0 += block.measured_len();
            entry.1 = entry.1.max(block.start() + block.lines.len() - 1);
        }
    }
    for (id, (lines, last)) in totals {
        let Some(&start) = scan.fn_starts.get(&id) else {
            continue;
        };
        if lines > FN_PROSE_MAX && !scan.allowed("comment-heavy-function", start) {
            out.push(Finding {
                line: start,
                end: last,
                rule: "comment-heavy-function",
                message: format!(
                    "{lines} lines of prose in one function, cap is {FN_PROSE_MAX}. Splitting the function usually removes them"
                ),
            });
        }
    }
}

fn overused(scan: &Scan, out: &mut Vec<Finding>) {
    if scan.allowed("overused-word", 0) {
        return;
    }
    let all = scan
        .blocks
        .iter()
        .map(|b| b.text())
        .collect::<Vec<_>>()
        .join(" ")
        .to_lowercase();
    for word in OVERUSED {
        let n = all.matches(word).count();
        if n > OVERUSED_CAP {
            out.push(Finding {
                line: scan.blocks.first().map_or(1, |b| b.start()),
                end: usize::MAX,
                rule: "overused-word",
                message: format!("\"{word}\" appears {n} times; it has stopped meaning anything"),
            });
        }
    }
}

/// Flags a comment naming a source file that is not in the tree.
///
/// Only `.rs` and `.cs`, because those are what a refactor moves and what a
/// reader would try to open; a runtime path like `cache/state.json` names
/// something that was never in the repo. A reference matches if any real path
/// ends with it, so `protocol/messages.rs` resolves the same as a full one.
pub fn check_paths(scan: &Scan, repo: &[String]) -> Vec<Finding> {
    let mut out = Vec::new();
    for block in &scan.blocks {
        for line in &block.lines {
            for quoted in line.text.split('`').skip(1).step_by(2) {
                let path = quoted.trim();
                if !path.contains('/') || !(path.ends_with(".rs") || path.ends_with(".cs")) {
                    continue;
                }
                if path.contains(' ') || path.contains('<') {
                    continue;
                }
                // A reference may be relative to the file it sits in, so the
                // leading hops come off before the suffix match.
                let path = path.trim_start_matches("./");
                let path = path.trim_start_matches("../");
                let found = repo
                    .iter()
                    .any(|known| known == path || known.ends_with(&format!("/{path}")));
                if !found && !scan.allowed("stale-path", line.number) {
                    out.push(Finding {
                        line: line.number,
                        end: line.number,
                        rule: "stale-path",
                        message: format!("`{path}` is not in the tree; it moved or was renamed"),
                    });
                }
            }
        }
    }
    out
}

/// Flags a file that is more prose than code.
///
/// Prose, not documentation: an interface file that is mostly `///` is doing its
/// job, and counting that would push docs out of the files that most need them.
fn ratio(scan: &Scan, out: &mut Vec<Finding>) {
    let total = scan.comment_lines + scan.code_lines;
    if total < RATIO_MIN_LINES || scan.allowed("comment-ratio", 0) {
        return;
    }
    let share = scan.prose_lines as f64 / total as f64;
    if share > RATIO_CAP {
        out.push(Finding {
            line: 1,
            end: usize::MAX,
            rule: "comment-ratio",
            message: format!(
                "{:.0}% of this file is prose ({} of {} lines)",
                share * 100.0,
                scan.prose_lines,
                total
            ),
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::scan::scan;

    fn rules_for(src: &str) -> Vec<Finding> {
        check(&scan(src, Lang::Rust))
    }

    fn has(src: &str, rule: &str) -> bool {
        rules_for(src).iter().any(|f| f.rule == rule)
    }

    #[test]
    fn a_comment_on_an_item_should_be_a_doc_comment() {
        assert!(has(
            "// explains the thing\npub fn a() {}\n",
            "prefer-rustdoc"
        ));
        assert!(has(
            "// explains it\n#[derive(Debug)]\nstruct A;\n",
            "prefer-rustdoc"
        ));
    }

    #[test]
    fn a_heading_inside_a_trait_is_not_a_doc() {
        let src =
            "trait T {\n    // Player transport.\n    fn play(&self);\n    fn stop(&self);\n}\n";
        assert!(!has(src, "prefer-rustdoc"));
    }

    #[test]
    fn a_comment_on_a_constant_is_still_a_doc() {
        let src = "// What this holds.\nconst A: u8 = 1;\nconst B: u8 = 2;\n";
        assert!(has(src, "prefer-rustdoc"));
    }

    #[test]
    fn a_comment_not_on_an_item_is_left_alone() {
        assert!(!has(
            "// a section divider\n\nlet x = 1;\n",
            "prefer-rustdoc"
        ));
        assert!(!has(
            "fn a() {\n    // a note\n    b();\n}\n",
            "prefer-rustdoc"
        ));
    }

    #[test]
    fn wrapping_wider_does_not_beat_the_cap() {
        let long = "word ".repeat(75);
        let src = format!("/// {long}\n/// {long}\npub fn a() {{}}\n");
        assert!(has(&src, "block-too-long"));
    }

    #[test]
    fn hedging_is_flagged() {
        assert!(has(
            "// this should work for most cases\nlet x = 1;\n",
            "hedging"
        ));
        assert!(has(
            "/// Hopefully the host answers.\npub fn a() {}\n",
            "hedging"
        ));
    }

    #[test]
    fn plan_vocabulary_is_flagged() {
        assert!(has(
            "// Wired up in Slice 2.
let x = 1;
",
            "planning-leftover"
        ));
        assert!(has(
            "/// Finalized pre-cutover.
pub fn a() {}
",
            "planning-leftover"
        ));
        assert!(has(
            "// Lands in Phase 3.
let x = 1;
",
            "planning-leftover"
        ));
    }

    fn repo() -> Vec<String> {
        vec!["packages/plugin/Ffi/NativeBridge.cs".to_string()]
    }

    #[test]
    fn a_comment_naming_a_file_that_moved_is_flagged() {
        let s = scan(
            "/// Mirrors `plugin/Services/NativeBridge.cs`.\npub fn a() {}\n",
            Lang::Rust,
        );
        let found = check_paths(&s, &repo());
        assert_eq!(found.len(), 1);
        assert_eq!(found[0].rule, "stale-path");
    }

    #[test]
    fn a_comment_naming_a_file_that_exists_is_left_alone() {
        let s = scan(
            "/// Mirrors `plugin/Ffi/NativeBridge.cs`.\npub fn a() {}\n",
            Lang::Rust,
        );
        assert!(check_paths(&s, &repo()).is_empty());
    }

    #[test]
    fn a_relative_reference_resolves_the_same_way() {
        let s = scan(
            "/// Written to `../plugin/Ffi/NativeBridge.cs`.\npub fn a() {}\n",
            Lang::Rust,
        );
        assert!(check_paths(&s, &repo()).is_empty());
    }

    #[test]
    fn a_runtime_path_is_not_a_source_reference() {
        let s = scan(
            "/// Imports `cache/state.json` once.\npub fn a() {}\n",
            Lang::Rust,
        );
        assert!(check_paths(&s, &repo()).is_empty());
    }

    #[test]
    fn a_staging_directory_is_not_plan_vocabulary() {
        assert!(!has(
            "/// Removes the staged bundle from the staging directory.
pub fn a() {}
",
            "planning-leftover"
        ));
    }

    #[test]
    fn deferral_markers_are_flagged() {
        assert!(has(
            "// TODO: handle the error\nlet x = 1;\n",
            "placeholder"
        ));
        assert!(has("// FIXME later\nlet x = 1;\n", "placeholder"));
    }

    #[test]
    fn a_lowercase_xxx_inside_a_format_is_not_a_marker() {
        let src = "/// Matches mm:ss.xxx and nothing else.\npub fn a() {}\n";
        assert!(!has(src, "placeholder"));
    }

    #[test]
    fn a_long_item_doc_is_flagged() {
        let src = (0..10).map(|_| "/// x\n").collect::<String>();
        assert!(has(&src, "block-too-long"));
    }

    #[test]
    fn a_required_safety_section_does_not_count_against_the_cap() {
        let mut src = String::from(
            "/// Summary.
///
",
        );
        src.push_str(
            "/// # Safety
",
        );
        src.push_str(
            &(0..10)
                .map(|_| {
                    "/// caller obligations
"
                })
                .collect::<String>(),
        );
        assert!(!has(&src, "block-too-long"));
    }

    #[test]
    fn prose_inside_a_function_is_held_to_a_tighter_budget() {
        let src = "fn a() {\n    // one\n    // two\n    // three\n    b();\n}\n";
        assert!(has(src, "mid-function-prose"));
        assert!(!has(src, "block-too-long"));
    }

    #[test]
    fn a_csharp_remarks_section_is_not_summary_prose() {
        let mut src = String::from(
            "/// <summary>Does a thing.</summary>
/// <remarks>
",
        );
        src.push_str(
            &"/// detail
"
            .repeat(20),
        );
        src.push_str(
            "public void A() {}
",
        );
        assert!(!has(&src, "block-too-long"));
    }

    #[test]
    fn a_safety_justification_is_not_prose() {
        let src = "fn a() {
    // SAFETY: one
    // two
    // three
    b();
}
";
        assert!(!has(src, "mid-function-prose"));
    }

    #[test]
    fn prose_before_a_safety_justification_still_counts() {
        let src = "fn a() {
    // one
    // two
    // three
    // SAFETY: x
    b();
}
";
        assert!(has(src, "mid-function-prose"));
    }

    #[test]
    fn many_small_comments_still_add_up() {
        let mut src = String::from("fn a() {\n");
        for i in 0..5 {
            src.push_str(&format!("    // note {i}\n    // more {i}\n    b();\n"));
        }
        src.push_str("}\n");
        assert!(has(&src, "comment-heavy-function"));
        assert!(!has(&src, "mid-function-prose"));
    }

    #[test]
    fn safety_justifications_do_not_make_a_function_heavy() {
        let mut src = String::from("fn a() {\n");
        for _ in 0..5 {
            src.push_str("    // SAFETY: sound because of the check above.\n    b();\n");
        }
        src.push_str("}\n");
        assert!(!has(&src, "comment-heavy-function"));
    }

    #[test]
    fn a_short_note_inside_a_function_is_fine() {
        let src = "fn a() {\n    // one\n    // two\n    b();\n}\n";
        assert!(rules_for(src).is_empty());
    }

    #[test]
    fn a_comment_between_items_is_not_mid_function() {
        let src = "// one\n// two\n// three\nstruct A;\n";
        assert!(!has(src, "mid-function-prose"));
    }

    #[test]
    fn a_comment_in_an_impl_but_outside_a_method_is_not_mid_function() {
        let src = "impl A {\n    // one\n    // two\n    // three\n    fn b() {}\n}\n";
        assert!(!has(src, "mid-function-prose"));
    }

    #[test]
    fn a_multi_line_signature_still_marks_its_body() {
        let src = "fn a(\n    x: u8,\n) -> u8 {\n    // one\n    // two\n    // three\n    x\n}\n";
        assert!(has(src, "mid-function-prose"));
    }

    #[test]
    fn a_module_doc_gets_a_larger_budget() {
        let src = (0..10).map(|_| "//! x\n").collect::<String>();
        assert!(!has(&src, "block-too-long"));
    }

    #[test]
    fn an_allow_directive_silences_only_its_rule() {
        let mut src = String::from("// mbrc-lint: allow block-too-long\n");
        src.push_str(&(0..10).map(|_| "/// x\n").collect::<String>());
        assert!(!has(&src, "block-too-long"));
    }

    #[test]
    fn em_dashes_are_flagged_wherever_they_sit() {
        assert!(has("/// a \u{2014} b\n", "em-dash"));
        assert!(has("let x = 1; // a \u{2014} b\n", "em-dash"));
    }

    #[test]
    fn an_em_dash_inside_a_string_is_not_a_comment() {
        assert!(!has("let s = \"a \u{2014} b\";\n", "em-dash"));
    }

    #[test]
    fn filler_is_flagged() {
        assert!(has("/// It is worth noting that x.\n", "banned-phrase"));
    }

    #[test]
    fn history_narration_is_flagged() {
        assert!(has("/// This was renamed from y.\n", "history-narration"));
        assert!(has("// It used to return null.\n", "history-narration"));
    }

    #[test]
    fn used_to_meaning_employed_to_is_not_history() {
        assert!(!has(
            "/// The mod time used to decide whether a cover is stale.\n",
            "history-narration"
        ));
    }

    #[test]
    fn dates_and_measurements_are_flagged() {
        assert!(has(
            "/// Observed twice on a real install.\n",
            "dated-anecdote"
        ));
        assert!(has(
            "// Measured 2026-08-30 across sixteen minutes.\n",
            "dated-anecdote"
        ));
    }

    #[test]
    fn a_date_in_a_test_is_flagged_like_anywhere_else() {
        let src =
            "#[cfg(test)]\nmod tests {\n    fn t() {\n        // 2026-08-22T13:00:00Z\n    }\n}\n";
        assert!(has(src, "dated-anecdote"));
    }

    #[test]
    fn previously_meaning_earlier_at_runtime_is_not_history() {
        assert!(!has(
            "/// Free a string previously returned to C#.\n",
            "history-narration"
        ));
    }

    #[test]
    fn a_version_number_is_not_a_date() {
        assert!(!has(
            "/// Pinned to 1.5.0 for the shipped client.\n",
            "dated-anecdote"
        ));
    }

    #[test]
    fn overused_words_warn_once_past_the_cap() {
        let src = (0..5).map(|_| "// deliberately\n\n").collect::<String>();
        let found: Vec<_> = rules_for(&src)
            .into_iter()
            .filter(|f| f.rule == "overused-word")
            .collect();
        assert_eq!(found.len(), 1);
    }

    #[test]
    fn a_clean_file_reports_nothing() {
        let src = "/// Reserves a slot.\n///\n/// Loopback is never capped.\nfn admit() {}\n";
        assert!(rules_for(src).is_empty());
    }
}
