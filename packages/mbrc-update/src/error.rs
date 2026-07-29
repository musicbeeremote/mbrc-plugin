use std::fmt;

/// Everything that can go wrong parsing or verifying a release manifest.
#[derive(Debug)]
pub enum UpdateError {
    /// The manifest was not valid JSON, or did not match the schema.
    Parse(String),
    /// The manifest parsed but failed a structural rule (see [`crate::manifest`]).
    Invalid(String),
    /// No public key was compiled in, so nothing can ever verify. Fails closed.
    NoTrustedKeys,
    /// A trusted key was compiled in but is not a valid minisign public key.
    MalformedKey { name: String, reason: String },
    /// The detached signature is not a valid minisign signature.
    MalformedSignature(String),
    /// The signature did not verify against any trusted key.
    UntrustedSignature,
    /// A file's SHA512 did not match the manifest.
    HashMismatch {
        path: String,
        expected: String,
        actual: String,
    },
}

impl fmt::Display for UpdateError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Parse(m) => write!(f, "manifest is not valid: {m}"),
            Self::Invalid(m) => write!(f, "manifest failed validation: {m}"),
            Self::NoTrustedKeys => write!(
                f,
                "no release public keys were compiled in; refusing to trust any manifest"
            ),
            Self::MalformedKey { name, reason } => {
                write!(f, "trusted key {name} is malformed: {reason}")
            }
            Self::MalformedSignature(m) => write!(f, "signature is malformed: {m}"),
            Self::UntrustedSignature => {
                write!(
                    f,
                    "signature did not verify against any trusted release key"
                )
            }
            Self::HashMismatch {
                path,
                expected,
                actual,
            } => write!(
                f,
                "sha512 mismatch for {path}: manifest says {expected}, file is {actual}"
            ),
        }
    }
}

impl std::error::Error for UpdateError {}

pub type Result<T> = std::result::Result<T, UpdateError>;
