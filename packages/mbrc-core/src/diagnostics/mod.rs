//! Diagnostics capture: the supported way for a user to report a bug.
//!
//! A user presses Start in the Configure panel, reproduces the problem, presses
//! Stop, and gets one zip they can attach to an issue. Before this, a useful
//! report meant knowing that logs exist, that the default level records no wire
//! frames, where the storage folder is, and which of half a dozen version
//! numbers a maintainer would ask for.
//!
//! - [`capture`] owns the session (the log-level override and its lifetime).
//! - [`report`] assembles the environment and core state into `report.json`.
//! - [`redact`] is the bundle's own redaction policy - secrets out, everything
//!   else readable.
//! - [`bundle`] writes the zip.
//!
//! Nothing here uploads anything. The bundle lands in a folder the host names,
//! and what happens to it next is the user's decision.

pub mod bundle;
pub mod capture;
pub mod redact;
pub mod report;
