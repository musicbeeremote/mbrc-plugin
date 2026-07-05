//! Per-endpoint schema extraction, for diffing two captures.
//!
//! Every frame is filed under its `(dir, context)` endpoint (e.g.
//! `("s2c", "playerstatus")`). For each endpoint we accumulate a flat map of
//! `data` field paths to their JSON type, merged across all frames seen. Two
//! captures can then be compared endpoint-by-endpoint to surface schema drift -
//! the shape-parity check between, say, the C# plugin and the Rust core.

use std::collections::{BTreeMap, BTreeSet};

use serde_json::Value;

use crate::{parse_line, Record};

/// Field path (dotted, `[]` for array element) -> JSON type. When a path shows
/// more than one type across frames the types are unioned as `"a|b"`.
pub type FieldMap = BTreeMap<String, String>;

/// `(dir, context)` -> field schema.
pub type EndpointSchemas = BTreeMap<(String, String), FieldMap>;

/// Extract per-endpoint schemas from an `mbrc-capture/2` trace (frames only;
/// meta lines are ignored).
pub fn endpoint_schemas(contents: &str) -> EndpointSchemas {
    let mut out: EndpointSchemas = BTreeMap::new();
    for line in contents.lines() {
        let Some(Record::Frame(f)) = parse_line(line) else {
            continue;
        };
        let key = (f.dir.clone(), f.context().unwrap_or("RAW").to_string());
        let entry = out.entry(key).or_default();
        if let Some(data) = f.frame.as_ref().and_then(|fr| fr.get("data")) {
            collect_fields(data, String::new(), entry);
        }
    }
    out
}

/// `(dir, context)` -> the distinct `data` values seen (canonical JSON, with
/// `ignore` fields stripped anywhere they appear). For value-level parity diffs.
pub type EndpointValues = BTreeMap<(String, String), BTreeSet<String>>;

/// Extract distinct per-endpoint `data` values, dropping `ignore` fields (e.g.
/// volatile ones like playback position or timestamps) before canonicalizing.
pub fn endpoint_values(contents: &str, ignore: &[String]) -> EndpointValues {
    let mut out: EndpointValues = BTreeMap::new();
    for line in contents.lines() {
        let Some(Record::Frame(f)) = parse_line(line) else {
            continue;
        };
        let key = (f.dir.clone(), f.context().unwrap_or("RAW").to_string());
        let mut data = f
            .frame
            .as_ref()
            .and_then(|fr| fr.get("data"))
            .cloned()
            .unwrap_or(Value::Null);
        strip_keys(&mut data, ignore);
        out.entry(key)
            .or_default()
            .insert(serde_json::to_string(&data).unwrap_or_default());
    }
    out
}

/// Remove every `ignore` key at any depth.
fn strip_keys(v: &mut Value, ignore: &[String]) {
    match v {
        Value::Object(o) => {
            for k in ignore {
                o.remove(k);
            }
            for val in o.values_mut() {
                strip_keys(val, ignore);
            }
        }
        Value::Array(a) => {
            for item in a {
                strip_keys(item, ignore);
            }
        }
        _ => {}
    }
}

fn type_name(v: &Value) -> &'static str {
    match v {
        Value::Null => "null",
        Value::Bool(_) => "bool",
        Value::Number(_) => "num",
        Value::String(_) => "str",
        Value::Array(_) => "arr",
        Value::Object(_) => "obj",
    }
}

fn collect_fields(v: &Value, path: String, out: &mut FieldMap) {
    match v {
        Value::Object(o) => {
            if o.is_empty() {
                merge_type(out, &path, "obj");
            }
            for (k, val) in o {
                let p = if path.is_empty() {
                    k.clone()
                } else {
                    format!("{path}.{k}")
                };
                collect_fields(val, p, out);
            }
        }
        Value::Array(a) => {
            merge_type(out, &path, "arr");
            if let Some(first) = a.first() {
                collect_fields(first, format!("{path}[]"), out);
            }
        }
        _ => merge_type(out, &path, type_name(v)),
    }
}

fn merge_type(out: &mut FieldMap, path: &str, ty: &str) {
    out.entry(path.to_string())
        .and_modify(|existing| {
            if !existing.split('|').any(|t| t == ty) {
                existing.push('|');
                existing.push_str(ty);
            }
        })
        .or_insert_with(|| ty.to_string());
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::Frame;

    fn line(dir: &str, context: &str, data: Value) -> String {
        let raw =
            serde_json::to_string(&serde_json::json!({"context": context, "data": data})).unwrap();
        serde_json::to_string(&Frame::new(0, 0, dir, 0, raw.as_bytes())).unwrap()
    }

    #[test]
    fn flattens_field_types_by_endpoint() {
        let contents = line(
            "s2c",
            "playerstatus",
            serde_json::json!({"playervolume": "81", "playermute": false}),
        );
        let s = endpoint_schemas(&contents);
        let fields = &s[&("s2c".into(), "playerstatus".into())];
        assert_eq!(fields.get("playervolume").map(String::as_str), Some("str"));
        assert_eq!(fields.get("playermute").map(String::as_str), Some("bool"));
    }

    #[test]
    fn unions_conflicting_types_and_recurses_arrays() {
        let contents = [
            line("s2c", "x", serde_json::json!({"v": "1"})),
            line("s2c", "x", serde_json::json!({"v": 1})),
            line("s2c", "list", serde_json::json!([{"id": 1}, {"id": 2}])),
        ]
        .join("\n");
        let s = endpoint_schemas(&contents);
        let x = &s[&("s2c".into(), "x".into())];
        let v = x.get("v").unwrap();
        assert!(v == "str|num" || v == "num|str", "got {v}");
        let list = &s[&("s2c".into(), "list".into())];
        assert_eq!(list.get("").map(String::as_str), Some("arr"));
        assert_eq!(list.get("[].id").map(String::as_str), Some("num"));
    }

    #[test]
    fn endpoint_values_dedups_and_strips_ignored() {
        let contents = [
            line(
                "s2c",
                "pos",
                serde_json::json!({"current": 10, "total": 200}),
            ),
            line(
                "s2c",
                "pos",
                serde_json::json!({"current": 50, "total": 200}),
            ),
        ]
        .join("\n");
        // Without ignore: two distinct values.
        let v = endpoint_values(&contents, &[]);
        assert_eq!(v[&("s2c".into(), "pos".into())].len(), 2);
        // Ignoring the volatile `current` collapses them to one.
        let v = endpoint_values(&contents, &["current".to_string()]);
        assert_eq!(v[&("s2c".into(), "pos".into())].len(), 1);
    }
}
