//! Pulling comment blocks out of Rust and C# source.
//!
//! Not a parser: a line scanner that knows enough about strings and block
//! comments not to mistake a URL in a literal for a comment. Comments are what
//! the rules act on, and neither `rustc` nor Roslyn hands them over cheaply.

/// Which language a file is, since a doc comment is only a Rust idea here.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Lang {
    Rust,
    CSharp,
}

/// C# doc tags that open a section after the summary, the way a markdown
/// heading does in rustdoc.
const SECTION_TAGS: &[&str] = &[
    "<remarks>",
    "<returns>",
    "<param",
    "<exception",
    "<example",
    "<seealso",
];

/// Lines a `SAFETY:` justification may take before the rest counts as prose.
/// The longest in the tree is three, so this is the observed ceiling rather
/// than an invitation.
pub const SAFETY_ALLOWANCE: usize = 3;

/// What a run of comment lines is attached to.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Kind {
    /// `//!` inner doc, the module header.
    Module,
    /// `///` item doc, Rust or a C# XML doc.
    Doc,
    /// Ordinary `//` in a function body.
    Line,
}

impl Kind {
    /// Line budget before the block is too long to be read at a glance.
    ///
    /// Item and body caps sit at the p95 of what the tree already holds. The
    /// module budget is larger because a module doc is the one place a design
    /// rationale belongs, and it is read once rather than between statements.
    pub fn cap(self) -> usize {
        match self {
            Kind::Module => 20,
            Kind::Doc => 8,
            Kind::Line => 4,
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Kind::Module => "module doc",
            Kind::Doc => "item doc",
            Kind::Line => "comment",
        }
    }
}

/// One comment line: its 1-based number and the text after the marker.
#[derive(Debug, Clone)]
pub struct Line {
    pub number: usize,
    pub text: String,
    pub kind: Kind,
    /// Inside a function body, where prose is the last resort rather than the
    /// first. A comment between items is not this.
    pub in_fn: bool,
    /// Which function body, so density can be judged per function.
    pub fn_id: Option<usize>,
    /// Inside a `trait` body.
    pub in_trait: bool,
}

/// A run of full-line comments of one kind on consecutive lines.
#[derive(Debug, Clone)]
pub struct Block {
    pub kind: Kind,
    pub lines: Vec<Line>,
    /// The next line of code, so a block can be seen to document an item.
    pub follows: Option<String>,
    /// The function body this block sits in, if any.
    pub fn_id: Option<usize>,
    /// Set when the item is a trait method, where a comment is nearly always a
    /// heading over a run of them rather than a doc for one.
    pub in_trait: bool,
}

impl Block {
    /// Lines the cap applies to, with required documentation discounted.
    ///
    /// A doc's `# Safety` or `# Errors` section ends the measurement, as does a
    /// C# `<remarks>` or `<returns>`: those are the documented places for detail
    /// after the summary. A `SAFETY:` justification is discounted
    /// [`SAFETY_ALLOWANCE`] lines and no more, so prose cannot hide behind one.
    pub fn measured_len(&self) -> usize {
        let mut counted = 0;
        let mut i = 0;
        while i < self.lines.len() {
            let t = self.lines[i].text.trim_start();
            if t.starts_with("# ") || t.starts_with("## ") || t.starts_with("### ") {
                break;
            }
            if SECTION_TAGS.iter().any(|tag| t.starts_with(tag)) {
                break;
            }
            if t.starts_with("SAFETY:") {
                i += SAFETY_ALLOWANCE;
                continue;
            }
            counted += 1;
            i += 1;
        }
        counted
    }

    /// True when the block sits inside a function body.
    pub fn in_fn(&self) -> bool {
        self.lines.first().is_some_and(|l| l.in_fn)
    }
}

impl Block {
    pub fn start(&self) -> usize {
        self.lines.first().map_or(0, |l| l.number)
    }

    /// Words in the measured prose, so a block cannot dodge the line cap by
    /// wrapping wider than everything around it.
    pub fn words(&self) -> usize {
        self.lines[..self.measured_len()]
            .iter()
            .map(|l| l.text.split_whitespace().count())
            .sum()
    }

    /// True when this block sits directly on top of one item it could document.
    ///
    /// Comments inside a trait body are excluded: there they group the methods
    /// into sections, so they document a run rather than the one item below.
    pub fn documents_an_item(&self) -> bool {
        !self.in_trait && self.follows.as_deref().is_some_and(declares_item)
    }

    /// The block's prose as one string, for phrase matching.
    pub fn text(&self) -> String {
        self.lines
            .iter()
            .map(|l| l.text.trim())
            .collect::<Vec<_>>()
            .join(" ")
    }
}

/// A `mbrc-lint: allow` directive and what it covers.
#[derive(Debug, Clone)]
pub struct Allow {
    pub number: usize,
    pub rules: Vec<String>,
    pub whole_file: bool,
}

/// Everything the rules need from one file.
#[derive(Debug)]
pub struct Scan {
    pub lang: Lang,
    /// Where each function body starts, keyed by the id its comments carry.
    pub fn_starts: std::collections::BTreeMap<usize, usize>,
    pub blocks: Vec<Block>,
    pub trailing: Vec<Line>,
    pub allows: Vec<Allow>,
    pub code_lines: usize,
    pub comment_lines: usize,
    /// Plain `//` lines only. Documentation is not density to complain about.
    pub prose_lines: usize,
}

impl Scan {
    /// True when `rule` is suppressed for a block starting at `start`.
    pub fn allowed(&self, rule: &str, start: usize) -> bool {
        self.allows
            .iter()
            .any(|a| (a.whole_file || a.number + 1 == start) && a.rules.iter().any(|r| r == rule))
    }
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum StringKind {
    /// `"..."`, escapes honoured.
    Normal,
    /// `r"..."` / `r#"..."#` in Rust, `@"..."` in C#. `usize` is the hash count.
    Raw(usize),
}

impl Default for Scan {
    fn default() -> Self {
        Self {
            lang: Lang::Rust,
            fn_starts: std::collections::BTreeMap::new(),
            blocks: Vec::new(),
            trailing: Vec::new(),
            allows: Vec::new(),
            code_lines: 0,
            comment_lines: 0,
            prose_lines: 0,
        }
    }
}

/// The brace, string and comment state carried from one line to the next.
struct Cursor {
    in_string: Option<StringKind>,
    block_depth: usize,
    /// One entry per open brace: the id of the function that opened it, if a
    /// function did. A closure inside a function keeps the enclosing id, which
    /// is what "mid-function" should mean.
    fn_braces: Vec<Option<usize>>,
    trait_braces: Vec<bool>,
    pending_fn: bool,
    next_fn_id: usize,
}

impl Cursor {
    fn new() -> Self {
        Self {
            in_string: None,
            block_depth: 0,
            fn_braces: Vec::new(),
            trait_braces: Vec::new(),
            pending_fn: false,
            next_fn_id: 0,
        }
    }

    /// The id of the innermost function body the cursor is inside.
    fn fn_id(&self) -> Option<usize> {
        self.fn_braces.iter().rev().find_map(|f| *f)
    }

    fn in_trait(&self) -> bool {
        self.trait_braces.iter().any(|t| *t)
    }

    /// Walks one line, updating brace, string and block-comment state.
    ///
    /// Returns the `//` comment it ran into, as the rest of the line and
    /// whether code preceded it on the same line.
    fn walk(
        &mut self,
        line: &str,
        number: usize,
        fn_starts: &mut std::collections::BTreeMap<usize, usize>,
    ) -> Option<(String, bool)> {
        let bytes: Vec<char> = line.chars().collect();
        let mut i = 0usize;
        let mut saw_code = false;
        let mut comment = None;
        // `fn` before the first brace on this line, so a multi-line signature
        // still marks the body it eventually opens.
        let opens_fn = declares_fn(line);
        let opens_trait = declares_trait(line);
        let mut first_brace = true;

        while i < bytes.len() {
            if let Some(kind) = self.in_string {
                i += consume_string(&bytes, i, kind, &mut self.in_string);
                continue;
            }
            if self.block_depth > 0 {
                if bytes[i] == '*' && bytes.get(i + 1) == Some(&'/') {
                    self.block_depth -= 1;
                    i += 2;
                } else if bytes[i] == '/' && bytes.get(i + 1) == Some(&'*') {
                    self.block_depth += 1;
                    i += 2;
                } else {
                    i += 1;
                }
                continue;
            }
            match bytes[i] {
                '/' if bytes.get(i + 1) == Some(&'/') => {
                    comment = Some((bytes[i..].iter().collect::<String>(), saw_code));
                    break;
                }
                '/' if bytes.get(i + 1) == Some(&'*') => {
                    self.block_depth += 1;
                    saw_code = true;
                    i += 2;
                }
                '"' => {
                    self.in_string = Some(match raw_prefix(&bytes, i) {
                        Some(h) => StringKind::Raw(h),
                        None => StringKind::Normal,
                    });
                    saw_code = true;
                    i += 1;
                }
                '{' => {
                    let id = if first_brace && (opens_fn || self.pending_fn) {
                        self.next_fn_id += 1;
                        fn_starts.insert(self.next_fn_id, number);
                        Some(self.next_fn_id)
                    } else {
                        None
                    };
                    self.fn_braces.push(id);
                    self.trait_braces.push(first_brace && opens_trait);
                    first_brace = false;
                    self.pending_fn = false;
                    saw_code = true;
                    i += 1;
                }
                '}' => {
                    self.fn_braces.pop();
                    self.trait_braces.pop();
                    saw_code = true;
                    i += 1;
                }
                '\'' => {
                    saw_code = true;
                    i += char_literal_len(&bytes, i);
                }
                c if !c.is_whitespace() => {
                    saw_code = true;
                    i += 1;
                }
                _ => i += 1,
            }
        }

        if opens_fn && first_brace {
            self.pending_fn = true;
        }
        comment
    }
}

/// Scans one source file into comment blocks.
pub fn scan(source: &str, lang: Lang) -> Scan {
    let mut out = Scan {
        lang,
        ..Scan::default()
    };
    let mut cursor = Cursor::new();
    let mut pending: Vec<Line> = Vec::new();

    for (idx, raw) in source.split('\n').enumerate() {
        let number = idx + 1;
        let line = raw.strip_suffix('\r').unwrap_or(raw);
        let comment = cursor.walk(line, number, &mut out.fn_starts);

        let Some((rest, trailing)) = comment else {
            flush(&mut pending, &mut out);
            if !line.trim().is_empty() {
                out.code_lines += 1;
            }
            continue;
        };

        let (kind, text) = classify(&rest);
        out.comment_lines += 1;
        if kind == Kind::Line {
            out.prose_lines += 1;
        }
        if trailing {
            out.code_lines += 1;
        }
        if let Some(allow) = parse_allow(&text, number) {
            flush(&mut pending, &mut out);
            out.allows.push(allow);
            continue;
        }

        let fn_id = cursor.fn_id();
        let entry = Line {
            number,
            text,
            kind,
            in_fn: fn_id.is_some(),
            fn_id,
            in_trait: cursor.in_trait(),
        };
        if trailing {
            flush(&mut pending, &mut out);
            out.trailing.push(entry);
            continue;
        }

        let breaks = pending
            .last()
            .is_some_and(|p| p.kind != kind || p.number + 1 != number);
        if breaks {
            flush(&mut pending, &mut out);
        }
        pending.push(entry);
    }
    flush(&mut pending, &mut out);
    attach_followers(&mut out, source);
    out
}

/// Records the first real line after each block, skipping attributes so a doc
/// comment above `#[derive(...)]` still counts as documenting the item below.
fn attach_followers(out: &mut Scan, source: &str) {
    let lines: Vec<&str> = source.split('\n').collect();
    for block in &mut out.blocks {
        let Some(last) = block.lines.last() else {
            continue;
        };
        // Attributes sit between a doc comment and its item; a blank line means
        // the comment stands alone and documents nothing.
        let mut k = last.number;
        while k < lines.len() {
            let t = lines[k].trim();
            if t.starts_with("#[") {
                k += 1;
                continue;
            }
            if !t.is_empty() && !t.starts_with("//") {
                block.follows = Some(t.to_string());
            }
            break;
        }
    }
}

/// Whether a line opens a `trait` body.
fn declares_trait(line: &str) -> bool {
    let code = line.split("//").next().unwrap_or(line);
    code.split(|c: char| !c.is_alphanumeric() && c != '_')
        .any(|w| w == "trait")
}

/// Whether a line declares an item a doc comment could attach to.
fn declares_item(line: &str) -> bool {
    let mut t = line.trim_start();
    if let Some(rest) = t.strip_prefix("pub") {
        t = rest
            .trim_start_matches(|c: char| c == '(' || c == ')' || c == ':' || c.is_alphanumeric())
            .trim_start();
    }
    const KINDS: &[&str] = &[
        "fn ",
        "async fn ",
        "unsafe fn ",
        "extern ",
        "struct ",
        "enum ",
        "trait ",
        "union ",
        "const ",
        "static ",
        "type ",
        "impl ",
        "mod ",
        "macro_rules! ",
    ];
    KINDS.iter().any(|k| t.starts_with(k))
}

/// Length of a char literal at `i`, or 1 when the quote is a Rust lifetime.
///
/// `'"'` must not be mistaken for the start of a string; `'a` must not swallow
/// the rest of the line looking for a close.
fn char_literal_len(bytes: &[char], i: usize) -> usize {
    let escaped = bytes.get(i + 1) == Some(&'\\');
    let close = if escaped { i + 3 } else { i + 2 };
    if bytes.get(close) == Some(&'\'') {
        close - i + 1
    } else {
        1
    }
}

/// Whether a line opens a function, so the brace it reaches marks a body.
fn declares_fn(line: &str) -> bool {
    let code = line.split("//").next().unwrap_or(line);
    code.split(|c: char| !c.is_alphanumeric() && c != '_')
        .any(|w| w == "fn")
}

fn flush(pending: &mut Vec<Line>, out: &mut Scan) {
    if pending.is_empty() {
        return;
    }
    let kind = pending[0].kind;
    let in_trait = pending.first().is_some_and(|l| l.in_trait);
    let fn_id = pending.first().and_then(|l| l.fn_id);
    out.blocks.push(Block {
        kind,
        lines: std::mem::take(pending),
        follows: None,
        fn_id,
        in_trait,
    });
}

/// Marker and text of a comment, given the slice starting at `//`.
fn classify(rest: &str) -> (Kind, String) {
    let after = &rest[2..];
    if let Some(t) = after.strip_prefix('!') {
        return (Kind::Module, t.trim_start().to_string());
    }
    // `////` is a plain comment in Rust, not a doc comment.
    if after.starts_with('/') && !after.starts_with("//") {
        return (Kind::Doc, after[1..].trim_start().to_string());
    }
    (Kind::Line, after.trim_start().to_string())
}

/// `mbrc-lint: allow <rule>[, <rule>]`, or `allow-file` for the whole file.
fn parse_allow(text: &str, number: usize) -> Option<Allow> {
    let at = text.find("mbrc-lint:")?;
    let rest = text[at + "mbrc-lint:".len()..].trim();
    let (whole_file, list) = if let Some(r) = rest.strip_prefix("allow-file") {
        (true, r)
    } else {
        (false, rest.strip_prefix("allow")?)
    };
    // Take rule ids until the first word that is not one: the rest is a reason.
    let rules: Vec<String> = list
        .split(|c: char| c == ',' || c.is_whitespace())
        .filter(|s| !s.is_empty())
        .take_while(|s| crate::rules::RULES.contains(s))
        .map(|s| (*s).to_string())
        .collect();
    if rules.is_empty() {
        return None;
    }
    Some(Allow {
        number,
        rules,
        whole_file,
    })
}

/// Hashes count when the quote at `i` opens a raw or verbatim string.
fn raw_prefix(bytes: &[char], i: usize) -> Option<usize> {
    let mut j = i;
    let mut hashes = 0usize;
    while j > 0 && bytes[j - 1] == '#' {
        hashes += 1;
        j -= 1;
    }
    if j > 0 && (bytes[j - 1] == 'r' || bytes[j - 1] == '@') {
        return Some(hashes);
    }
    None
}

/// Advances through string contents, clearing `state` at the terminator.
fn consume_string(
    bytes: &[char],
    i: usize,
    kind: StringKind,
    state: &mut Option<StringKind>,
) -> usize {
    match kind {
        StringKind::Normal => {
            if bytes[i] == '\\' {
                return 2;
            }
            if bytes[i] == '"' {
                *state = None;
            }
            1
        }
        StringKind::Raw(hashes) => {
            if bytes[i] == '"' {
                let closing = (1..=hashes).all(|k| bytes.get(i + k) == Some(&'#'));
                // A C# verbatim string escapes a quote by doubling it.
                if closing && bytes.get(i + 1) != Some(&'"') {
                    *state = None;
                    return hashes + 1;
                }
                if bytes.get(i + 1) == Some(&'"') {
                    return 2;
                }
            }
            1
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rs(source: &str) -> Scan {
        scan(source, Lang::Rust)
    }

    #[test]
    fn groups_consecutive_lines_of_one_kind() {
        let s = rs("/// a\n/// b\nfn x() {}\n// c\n");
        assert_eq!(s.blocks.len(), 2);
        assert_eq!(s.blocks[0].kind, Kind::Doc);
        assert_eq!(s.blocks[0].lines.len(), 2);
        assert_eq!(s.blocks[1].kind, Kind::Line);
    }

    #[test]
    fn a_heading_ends_the_measured_prose() {
        let s = rs("/// Does a thing.
///
/// # Safety
/// Caller must not lie.
");
        assert_eq!(s.blocks[0].lines.len(), 4);
        assert_eq!(s.blocks[0].measured_len(), 2);
    }

    #[test]
    fn prose_after_a_safety_justification_still_counts() {
        let s = rs(
            "fn a() {\n    // SAFETY: one\n    // two\n    // three\n    // four\n    // five\n}\n",
        );
        assert_eq!(s.blocks[0].measured_len(), 2);
    }

    #[test]
    fn comments_are_attributed_to_their_function() {
        let s = rs("fn a() {\n    // one\n}\nfn b() {\n    // two\n}\n");
        assert_ne!(s.blocks[0].fn_id, s.blocks[1].fn_id);
        assert!(s.blocks[0].fn_id.is_some());
    }

    #[test]
    fn a_blank_line_ends_a_block() {
        let s = rs("// a\n\n// b\n");
        assert_eq!(s.blocks.len(), 2);
    }

    #[test]
    fn kinds_do_not_merge() {
        let s = rs("//! module\n/// item\n");
        assert_eq!(s.blocks.len(), 2);
        assert_eq!(s.blocks[0].kind, Kind::Module);
        assert_eq!(s.blocks[1].kind, Kind::Doc);
    }

    #[test]
    fn a_url_in_a_string_is_not_a_comment() {
        let s = rs("let u = \"https://example.com/a\";\n");
        assert!(s.blocks.is_empty());
    }

    #[test]
    fn a_string_literal_spanning_lines_hides_its_contents() {
        let s = rs("let src = \"fn a() {
    // one
    // two
    // three
}
\";
");
        assert!(s.blocks.is_empty());
    }

    #[test]
    fn a_char_literal_holding_a_quote_does_not_open_a_string() {
        let s = rs("let q = '\"';
// a
// b
// c
");
        assert_eq!(s.blocks.len(), 1);
        assert_eq!(s.blocks[0].lines.len(), 3);
    }

    #[test]
    fn a_lifetime_is_not_a_char_literal() {
        let s = rs("fn f<'a>(x: &'a str) {}
// a
// b
// c
");
        assert_eq!(s.blocks.len(), 1);
    }

    #[test]
    fn a_raw_string_holding_slashes_is_not_a_comment() {
        let s = rs("let u = r#\"// not a comment\"#;\n");
        assert!(s.blocks.is_empty());
    }

    #[test]
    fn a_verbatim_csharp_string_is_not_a_comment() {
        let s = rs("var p = @\"C:\\x\\\\ // no\";\n");
        assert!(s.blocks.is_empty());
    }

    #[test]
    fn block_comments_hide_their_contents() {
        let s = rs("/* // not counted\n   still inside */\ncode();\n");
        assert!(s.blocks.is_empty());
    }

    #[test]
    fn trailing_comments_are_kept_apart_from_blocks() {
        let s = rs("let x = 1; // note\n");
        assert!(s.blocks.is_empty());
        assert_eq!(s.trailing.len(), 1);
        assert_eq!(s.trailing[0].text, "note");
    }

    #[test]
    fn four_slashes_is_not_a_doc_comment() {
        let s = rs("//// divider\n");
        assert_eq!(s.blocks[0].kind, Kind::Line);
    }

    #[test]
    fn an_allow_directive_is_not_part_of_the_block() {
        let s = rs("// mbrc-lint: allow block-too-long\n/// a\n/// b\n");
        assert_eq!(s.blocks.len(), 1);
        assert_eq!(s.blocks[0].lines.len(), 2);
        assert!(s.allowed("block-too-long", 2));
        assert!(!s.allowed("em-dash", 2));
    }

    #[test]
    fn test_prose_counts_like_any_other() {
        let s = rs("fn a() {}\n#[cfg(test)]\nmod tests {\n    // one\n    // two\n}\n");
        assert_eq!(s.comment_lines, 2);
    }

    #[test]
    fn a_directive_may_carry_a_reason() {
        let s = rs(
            "// mbrc-lint: allow block-too-long - the rules list is the point
// a
",
        );
        assert!(s.allowed("block-too-long", 2));
    }

    #[test]
    fn a_misspelled_rule_suppresses_nothing() {
        let s = rs("// mbrc-lint: allow block-to-long
// a
");
        assert!(!s.allowed("block-too-long", 2));
    }

    #[test]
    fn a_file_allow_covers_every_block() {
        let s = rs("// mbrc-lint: allow-file em-dash\ncode();\n// a\n");
        assert!(s.allowed("em-dash", 3));
    }
}
