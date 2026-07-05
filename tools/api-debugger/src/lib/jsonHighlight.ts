// Zero-dependency JSON syntax highlighter. Offline/CSP-safe (no CDN, no lib):
// escape the input, then a single regex classifies keys, strings, numbers,
// booleans, and null into themed <span>s. Non-JSON input degrades gracefully -
// it comes back escaped (safe to render), just uncolored.

/** Escape HTML-significant chars so raw payloads can't inject markup. */
export function escapeHtml(s: string): string {
  return s.replace(/[&<>]/g, (c) => (c === "&" ? "&amp;" : c === "<" ? "&lt;" : "&gt;"));
}

const TOKEN =
  // string (optionally a key when trailed by a colon) | boolean | null | number
  /"(?:\\.|[^"\\])*"(?:\s*:)?|\b(?:true|false)\b|\bnull\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g;

/** Highlight a (pretty-printed) JSON string into span-wrapped HTML. */
export function highlightJson(text: string): string {
  return escapeHtml(text).replace(TOKEN, (match) => {
    let cls: string;
    if (match[0] === '"') {
      cls = match.endsWith(":") ? "tok-key" : "tok-str";
    } else if (match === "true" || match === "false") {
      cls = "tok-bool";
    } else if (match === "null") {
      cls = "tok-null";
    } else {
      cls = "tok-num";
    }
    return `<span class="${cls}">${match}</span>`;
  });
}
