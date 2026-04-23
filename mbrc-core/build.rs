// Generates C# P/Invoke bindings for the FFI surface into
// ../plugin/Services/Generated/NativeBridge.Generated.cs.
//
// Rust is the source of truth: every `extern "C"` fn in lib.rs and every
// `#[repr(...)]` type in ffi/types.rs referenced from those signatures is
// emitted as C# automatically. CI diffs the generated file to catch drift.

fn main() {
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/ffi/types.rs");
    println!("cargo:rerun-if-changed=src/ffi/mod.rs");
    println!("cargo:rerun-if-changed=build.rs");

    let out = std::path::PathBuf::from("../plugin/Services/Generated/NativeBridge.Generated.cs");

    if let Some(parent) = out.parent() {
        std::fs::create_dir_all(parent).expect("create generated output dir");
    }

    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/ffi/types.rs")
        .csharp_dll_name("mbrc_core")
        .csharp_namespace("MusicBeePlugin.Services.Generated")
        .csharp_class_name("NativeMethods")
        .csharp_entry_point_prefix("")
        .csharp_method_prefix("")
        .csharp_use_function_pointer(false)
        .generate_csharp_file(&out)
        .expect("csbindgen generate_csharp_file");
}
