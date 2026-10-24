//! The Windows version resource and application manifest a binary carries.
//!
//! Explorer's Details tab, Task Manager, the UAC prompt and antivirus heuristics
//! all read these, so a binary without them shows up as an anonymous file with
//! no version. The strings come from the same `Directory.Build.props` that
//! stamps `mb_remote.dll`, so the three shipped files describe one product.

use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use crate::{element, find_props, resolve_version};

/// The MSBuild expression `<Copyright>` uses for the current year.
const YEAR_EXPRESSION: &str = "$([System.DateTime]::Now.Year)";

/// Which kind of image the resource is linked into.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind {
    /// An executable; the resource links into the crate's bins.
    Exe,
    /// A `cdylib`; the resource links into the DLL and not its tests.
    Dll,
}

/// What a binary says about itself.
#[derive(Clone, Copy, Debug)]
pub struct Binary<'a> {
    /// `FileDescription`: the name Task Manager and the UAC prompt show.
    pub description: &'a str,
    /// `OriginalFilename`, which lets a renamed copy still be identified.
    pub file_name: &'a str,
    pub kind: Kind,
    /// An application manifest, relative to the crate. Only meaningful for an exe.
    pub manifest: Option<&'a str>,
}

/// The product-wide strings read from the props file.
#[derive(Debug)]
struct Product {
    company: String,
    name: String,
    copyright: String,
}

/// Compiles and links the version resource (and manifest, if any) for `binary`.
///
/// A build for Windows that cannot compile the resource fails rather than
/// shipping a binary without it.
pub fn embed_resources(binary: &Binary<'_>) {
    let version = resolve_version();
    let props = find_props();
    let xml = std::fs::read_to_string(&props)
        .unwrap_or_else(|e| panic!("cannot read {}: {e}", props.display()));
    let product = read_product(&xml).unwrap_or_else(|e| panic!("{}: {e}", props.display()));

    let manifest = binary.manifest.map(|relative| {
        println!("cargo:rerun-if-changed={relative}");
        Path::new(&std::env::var("CARGO_MANIFEST_DIR").expect("set by cargo")).join(relative)
    });
    let debug = std::env::var("PROFILE").is_ok_and(|p| p == "debug");
    let comments = std::env::var("CARGO_PKG_DESCRIPTION").unwrap_or_default();

    let rc = render_rc(
        binary,
        &product,
        &version,
        &comments,
        debug,
        manifest.as_deref(),
    );
    let out = PathBuf::from(std::env::var("OUT_DIR").expect("set by cargo")).join("mbrc.rc");
    std::fs::write(&out, rc).unwrap_or_else(|e| panic!("cannot write {}: {e}", out.display()));

    let result = match binary.kind {
        Kind::Exe => embed_resource::compile(&out, embed_resource::NONE),
        Kind::Dll => embed_resource::compile_for_cdylib(&out, embed_resource::NONE),
    };
    if let Err(e) = result.manifest_required() {
        panic!("{} has no version resource: {e}", binary.file_name);
    }
}

fn read_product(xml: &str) -> Result<Product, String> {
    let copyright = element(xml, "Copyright")?.replace(YEAR_EXPRESSION, &build_year().to_string());
    Ok(Product {
        company: element(xml, "Company")?.to_owned(),
        name: element(xml, "Product")?.to_owned(),
        copyright,
    })
}

/// The year the build happened, or the one `SOURCE_DATE_EPOCH` pins.
fn build_year() -> i64 {
    println!("cargo:rerun-if-env-changed=SOURCE_DATE_EPOCH");
    let seconds = std::env::var("SOURCE_DATE_EPOCH")
        .ok()
        .and_then(|s| s.trim().parse::<i64>().ok())
        .unwrap_or_else(|| {
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .map_or(0, |d| i64::try_from(d.as_secs()).unwrap_or(0))
        });
    year_from_unix_days(seconds.div_euclid(86_400))
}

/// The proleptic Gregorian year containing `days` since the Unix epoch.
///
/// Howard Hinnant's `civil_from_days`, reduced to the year.
fn year_from_unix_days(days: i64) -> i64 {
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let march_based_month = (5 * doy + 2) / 153;
    let year = yoe + era * 400;
    if march_based_month >= 10 {
        year + 1
    } else {
        year
    }
}

/// The four numeric fields of `FILEVERSION`; a prerelease suffix is dropped.
fn numeric_version(version: &str) -> [u16; 4] {
    let core = version.split(['-', '+']).next().unwrap_or_default();
    let mut fields = [0; 4];
    for (field, part) in fields.iter_mut().zip(core.split('.')) {
        *field = part.parse().unwrap_or(0);
    }
    fields
}

/// Quotes `value` as an RC string literal.
fn rc_string(value: &str) -> String {
    format!("\"{}\"", value.replace('\\', "\\\\").replace('"', "\"\""))
}

fn render_rc(
    binary: &Binary<'_>,
    product: &Product,
    version: &str,
    comments: &str,
    debug: bool,
    manifest: Option<&Path>,
) -> String {
    const VS_FF_DEBUG: u32 = 0x1;
    const VS_FF_PRERELEASE: u32 = 0x2;
    const VOS_NT_WINDOWS32: u32 = 0x4_0004;
    const VFT_APP: u32 = 0x1;
    const VFT_DLL: u32 = 0x2;

    let [a, b, c, d] = numeric_version(version);
    let mut flags = 0;
    if version.contains('-') {
        flags |= VS_FF_PRERELEASE;
    }
    if debug {
        flags |= VS_FF_DEBUG;
    }
    let file_type = match binary.kind {
        Kind::Exe => VFT_APP,
        Kind::Dll => VFT_DLL,
    };
    let internal_name = binary
        .file_name
        .rsplit_once('.')
        .map_or(binary.file_name, |(stem, _)| stem);

    let strings = [
        ("CompanyName", product.company.as_str()),
        ("FileDescription", binary.description),
        ("FileVersion", version),
        ("InternalName", internal_name),
        ("LegalCopyright", product.copyright.as_str()),
        ("OriginalFilename", binary.file_name),
        ("ProductName", product.name.as_str()),
        ("ProductVersion", version),
        ("Comments", comments),
    ];
    let values: String = strings
        .iter()
        .filter(|(_, value)| !value.is_empty())
        .map(|(key, value)| {
            format!(
                "            VALUE {}, {}\r\n",
                rc_string(key),
                rc_string(value)
            )
        })
        .collect();

    let mut rc = format!(
        "#pragma code_page(65001)\r\n\
         1 VERSIONINFO\r\n\
         FILEVERSION {a},{b},{c},{d}\r\n\
         PRODUCTVERSION {a},{b},{c},{d}\r\n\
         FILEFLAGSMASK 0x3F\r\n\
         FILEFLAGS {flags:#X}\r\n\
         FILEOS {VOS_NT_WINDOWS32:#X}\r\n\
         FILETYPE {file_type:#X}\r\n\
         FILESUBTYPE 0x0\r\n\
         BEGIN\r\n\
         \x20   BLOCK \"StringFileInfo\"\r\n\
         \x20   BEGIN\r\n\
         \x20       BLOCK \"040904B0\"\r\n\
         \x20       BEGIN\r\n\
         {values}\
         \x20       END\r\n\
         \x20   END\r\n\
         \x20   BLOCK \"VarFileInfo\"\r\n\
         \x20   BEGIN\r\n\
         \x20       VALUE \"Translation\", 0x409, 1200\r\n\
         \x20   END\r\n\
         END\r\n"
    );
    if let Some(manifest) = manifest {
        // RT_MANIFEST (24), ID 1: the process manifest of an exe.
        rc.push_str(&format!(
            "1 24 {}\r\n",
            rc_string(&manifest.to_string_lossy())
        ));
    }
    rc
}

#[cfg(test)]
mod tests {
    use super::*;

    const HELPER: Binary<'static> = Binary {
        description: "MusicBee Remote Helper",
        file_name: "mbrc-helper.exe",
        kind: Kind::Exe,
        manifest: Some("mbrc-helper.exe.manifest"),
    };

    fn product() -> Product {
        Product {
            company: "Kelsos".into(),
            name: "MusicBee Remote".into(),
            copyright: "Copyright (C) 2011-2026 Konstantinos Paparas (kelsos)".into(),
        }
    }

    #[test]
    fn a_prerelease_suffix_leaves_the_numeric_fields() {
        assert_eq!(numeric_version("1.5.0"), [1, 5, 0, 0]);
        assert_eq!(numeric_version("1.5.0-beta.2"), [1, 5, 0, 0]);
        assert_eq!(numeric_version("2.0.1-nightly.20261024+abc"), [2, 0, 1, 0]);
        assert_eq!(numeric_version("1.6"), [1, 6, 0, 0]);
    }

    #[test]
    fn years_turn_over_on_the_first_of_january() {
        assert_eq!(year_from_unix_days(0), 1970);
        assert_eq!(year_from_unix_days(20_453), 2025);
        assert_eq!(year_from_unix_days(20_454), 2026);
        assert_eq!(year_from_unix_days(-1), 1969);
    }

    #[test]
    fn rc_strings_escape_quotes_and_backslashes() {
        assert_eq!(rc_string(r#"a "b" C:\x"#), r#""a ""b"" C:\\x""#);
    }

    #[test]
    fn a_prerelease_build_is_flagged_and_keeps_its_full_version_string() {
        let rc = render_rc(&HELPER, &product(), "1.5.0-beta.2", "", false, None);
        assert!(rc.contains("FILEVERSION 1,5,0,0\r\n"));
        assert!(rc.contains("FILEFLAGS 0x2\r\n"));
        assert!(rc.contains(r#"VALUE "ProductVersion", "1.5.0-beta.2""#));
    }

    #[test]
    fn an_exe_carries_its_identity_and_manifest() {
        let rc = render_rc(
            &HELPER,
            &product(),
            "1.5.0",
            "Elevated helper",
            false,
            Some(Path::new(r"C:\src\mbrc-helper.exe.manifest")),
        );
        for expected in [
            "FILETYPE 0x1\r\n",
            "FILEFLAGS 0x0\r\n",
            r#"VALUE "CompanyName", "Kelsos""#,
            r#"VALUE "FileDescription", "MusicBee Remote Helper""#,
            r#"VALUE "InternalName", "mbrc-helper""#,
            r#"VALUE "OriginalFilename", "mbrc-helper.exe""#,
            r#"VALUE "ProductName", "MusicBee Remote""#,
            r#"VALUE "Comments", "Elevated helper""#,
            r#"1 24 "C:\\src\\mbrc-helper.exe.manifest""#,
        ] {
            assert!(rc.contains(expected), "missing {expected:?} in\n{rc}");
        }
    }

    #[test]
    fn a_dll_has_no_manifest_and_skips_empty_comments() {
        let core = Binary {
            description: "MusicBee Remote Core",
            file_name: "mbrc_core.dll",
            kind: Kind::Dll,
            manifest: None,
        };
        let rc = render_rc(&core, &product(), "1.5.0", "", true, None);
        assert!(rc.contains("FILETYPE 0x2\r\n"));
        assert!(rc.contains("FILEFLAGS 0x1\r\n"));
        assert!(!rc.contains("Comments"));
        assert!(!rc.contains(" 24 "));
    }

    /// The real file, so a props edit that drops a field fails here.
    #[test]
    fn the_repository_props_file_names_the_product() {
        let xml = std::fs::read_to_string(find_props()).unwrap();
        let product = read_product(&xml).unwrap_or_else(|e| panic!("{}: {e}", crate::PROPS_FILE));
        assert_eq!(product.name, "MusicBee Remote");
        assert!(!product.copyright.contains("$("), "{}", product.copyright);
    }
}
