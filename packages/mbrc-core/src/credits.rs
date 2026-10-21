//! Artist credits: who is on a track and in what role (#139).
//!
//! MusicBee keeps them in `MultiArtist` as one string: NUL separated entries,
//! each led by a control character naming its role, the way the Multiple
//! Artist Splitter writes them. No prefix is the artist, `\u{1}` a guest,
//! `\u{2}` a performer (`Name (instrument)`) and `\u{4}` a remixer. Writing the
//! same encoding back to `MultiArtist` sets every role field the way the
//! splitter does and leaves the display artist alone.

use serde_json::{Value, json};

/// What a credited name did on the track.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Role {
    Artist,
    Guest,
    Performer,
    Remixer,
}

impl Role {
    fn from_prefix(c: char) -> Option<Self> {
        match c {
            '\u{1}' => Some(Self::Guest),
            '\u{2}' => Some(Self::Performer),
            '\u{4}' => Some(Self::Remixer),
            _ => None,
        }
    }

    fn prefix(self) -> Option<char> {
        match self {
            Self::Artist => None,
            Self::Guest => Some('\u{1}'),
            Self::Performer => Some('\u{2}'),
            Self::Remixer => Some('\u{4}'),
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Artist => "artist",
            Self::Guest => "guest",
            Self::Performer => "performer",
            Self::Remixer => "remixer",
        }
    }

    pub fn parse(name: &str) -> Option<Self> {
        [Self::Artist, Self::Guest, Self::Performer, Self::Remixer]
            .into_iter()
            .find(|r| r.as_str() == name)
    }

    /// Whether the display artist names this role, which makes it someone a
    /// track is browsed under.
    pub fn is_lead(self) -> bool {
        matches!(self, Self::Artist | Self::Guest)
    }
}

/// One credited name.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Credit {
    pub name: String,
    pub role: Role,
    /// A performer's instrument or part, from `Name (instrument)`.
    pub detail: Option<String>,
}

impl Credit {
    pub fn new(name: &str, role: Role) -> Self {
        Self {
            name: name.to_owned(),
            role,
            detail: None,
        }
    }

    pub fn to_json(&self) -> Value {
        let mut obj = json!({ "name": self.name, "role": self.role.as_str() });
        if let Some(detail) = &self.detail {
            obj["detail"] = json!(detail);
        }
        obj
    }
}

/// The credits in a `MultiArtist` value, in MusicBee's order.
///
/// An entry with a role prefix this module does not know is skipped rather than
/// shown under the wrong role. MusicBee pads some imported names with a byte
/// order mark, which is dropped.
pub fn parse(raw: &str) -> Vec<Credit> {
    raw.split('\0').filter_map(parse_entry).collect()
}

fn parse_entry(entry: &str) -> Option<Credit> {
    let mut chars = entry.chars();
    let (role, rest) = match chars.next()? {
        c if c.is_control() => (Role::from_prefix(c)?, chars.as_str()),
        _ => (Role::Artist, entry),
    };
    let rest = clean(rest);
    if rest.is_empty() {
        return None;
    }
    let (name, detail) = match role {
        Role::Performer => split_detail(&rest),
        _ => (rest.clone(), None),
    };
    Some(Credit { name, role, detail })
}

fn clean(s: &str) -> String {
    s.replace('\u{feff}', "").trim().to_owned()
}

/// `Name (instrument)` as its two parts; a bare entry is all name.
fn split_detail(entry: &str) -> (String, Option<String>) {
    match entry.strip_suffix(')').and_then(|s| s.rsplit_once(" (")) {
        Some((name, detail)) if !name.trim().is_empty() => {
            (name.trim().to_owned(), Some(detail.trim().to_owned()))
        }
        _ => (entry.to_owned(), None),
    }
}

/// The `MultiArtist` value MusicBee stores for these credits.
pub fn encode(credits: &[Credit]) -> String {
    let mut out = String::new();
    for credit in credits {
        if let Some(prefix) = credit.role.prefix() {
            out.push(prefix);
        }
        out.push_str(credit.name.trim());
        if let Some(detail) = credit.detail.as_deref().filter(|d| !d.is_empty()) {
            out.push_str(&format!(" ({detail})"));
        }
        out.push('\0');
    }
    out
}

/// The credits of a track, or its display artist as its one artist when
/// MusicBee lists none: a file nobody split, a stream, a queued file outside
/// the library.
pub fn or_display(raw: &str, display: &str) -> Vec<Credit> {
    let credits = parse(raw);
    if credits.is_empty() && !display.trim().is_empty() {
        return vec![Credit::new(display.trim(), Role::Artist)];
    }
    credits
}

/// The names a track is browsed under: its artists and guests.
pub fn lead_names(credits: &[Credit]) -> Vec<&str> {
    credits
        .iter()
        .filter(|c| c.role.is_lead())
        .map(|c| c.name.as_str())
        .collect()
}

/// `credits` with its artists and guests replaced by `names`, every other role
/// kept.
///
/// A name that was a guest stays a guest, so a client that edits the flat
/// list of names never turns a featured artist into a lead one. A new name is
/// an artist.
pub fn with_lead_names(credits: &[Credit], names: &[&str]) -> Vec<Credit> {
    let was_guest = |name: &str| {
        credits
            .iter()
            .any(|c| c.role == Role::Guest && c.name.eq_ignore_ascii_case(name))
    };
    let mut out: Vec<Credit> = names
        .iter()
        .map(|n| n.trim())
        .filter(|n| !n.is_empty())
        .map(|n| {
            Credit::new(
                n,
                if was_guest(n) {
                    Role::Guest
                } else {
                    Role::Artist
                },
            )
        })
        .collect();
    out.extend(credits.iter().filter(|c| !c.role.is_lead()).cloned());
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    const SPLIT: &str = "Tico \"El Inmigrante\"\0\u{1}Wisin & Yandel\0\u{1}Gadiel\0";

    #[test]
    fn a_split_track_reads_as_its_artist_and_guests() {
        let credits = parse(SPLIT);
        assert_eq!(
            credits,
            vec![
                Credit::new("Tico \"El Inmigrante\"", Role::Artist),
                Credit::new("Wisin & Yandel", Role::Guest),
                Credit::new("Gadiel", Role::Guest),
            ]
        );
    }

    #[test]
    fn a_performer_credit_carries_its_instrument() {
        let credits = parse("Eluveitie\0\u{2}\u{feff}Tommy Vetterli (engineer)\0\u{2}producer\0");
        assert_eq!(credits[1].name, "Tommy Vetterli");
        assert_eq!(credits[1].detail.as_deref(), Some("engineer"));
        assert_eq!(credits[2].name, "producer");
        assert_eq!(credits[2].detail, None);
    }

    #[test]
    fn performers_and_remixers_are_not_browsed_under() {
        let credits = parse("A\0\u{1}B\0\u{2}C (drums)\0\u{4}D\0");
        assert_eq!(lead_names(&credits), vec!["A", "B"]);
    }

    #[test]
    fn an_unknown_role_is_skipped() {
        assert_eq!(parse("A\0\u{8}B\0"), vec![Credit::new("A", Role::Artist)]);
    }

    #[test]
    fn encoding_is_what_musicbee_reads_back() {
        assert_eq!(encode(&parse(SPLIT)), SPLIT);
        let performer = "A\0\u{2}Tommy Vetterli (engineer)\0";
        assert_eq!(encode(&parse(performer)), performer);
    }

    #[test]
    fn an_unsplit_track_is_its_display_artist() {
        assert_eq!(
            or_display("", "Gadiel feat. Tico"),
            vec![Credit::new("Gadiel feat. Tico", Role::Artist)]
        );
        assert!(or_display("", " ").is_empty());
    }

    #[test]
    fn editing_the_names_keeps_guests_and_other_roles() {
        let credits = parse("A\0\u{1}B\0\u{2}C (drums)\0");
        let edited = with_lead_names(&credits, &["A", "b", "E"]);
        assert_eq!(
            encode(&edited),
            "A\0\u{1}b\0E\0\u{2}C (drums)\0",
            "B stays a guest, E is new, the drummer is kept"
        );
    }
}
