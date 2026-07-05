mod connection;
mod discovery;
mod proxy;
mod sessions;

use connection::ConnectionState;
use proxy::ProxyState;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(ConnectionState::default())
        .manage(ProxyState::default())
        .invoke_handler(tauri::generate_handler![
            connection::connect,
            connection::send_command,
            connection::disconnect,
            proxy::start_proxy,
            proxy::stop_proxy,
            sessions::list_sessions,
            sessions::save_session,
            sessions::read_session,
            sessions::delete_session,
            sessions::import_session,
            discovery::discover,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
