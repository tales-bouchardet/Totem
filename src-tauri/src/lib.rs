use std::fs;
use serde_json::Value;
use std::path::Path;

#[tauri::command]
fn get_json() -> Value {
    let user_folder = std::env::var("USERPROFILE").unwrap_or_default();
    let path = format!("{}/documents/Totem/totemdata.json", user_folder);

    // Se não existir, cria com objeto vazio
    if !Path::new(&path).exists() {
        fs::create_dir_all(format!("{}/documents/Totem", user_folder)).ok();
        fs::write(&path, "{}").ok();
    }

    let read = fs::read_to_string(&path).unwrap_or_default();
    serde_json::from_str(&read).unwrap_or_default()
}

#[tauri::command]
fn set_json(data: Value) {
    let user_folder = std::env::var("USERPROFILE").unwrap_or_default();
    let path = format!("{}/documents/Totem/totemdata.json", user_folder);

    fs::create_dir_all(format!("{}/documents/Totem", user_folder)).ok();
    fs::write(&path, serde_json::to_string(&data).unwrap()).unwrap();
    std::env::set_var("EDIT_MODE", "false");
}

#[tauri::command]
fn edit_mode_set(value: bool) {
    std::env::set_var("EDIT_MODE", value.to_string());
}

#[tauri::command]
fn edit_mode_get() -> String {
    let a = std::env::var("EDIT_MODE").unwrap_or_default();
    if a.is_empty() {
        "false".to_string()
    } else {
        a
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let user_folder = std::env::var("USERPROFILE").unwrap_or_default();
    let path = format!("{}/documents/Totem/totemdata.json", user_folder);

    // Cria o arquivo JSON se não existir ao iniciar
    if !Path::new(&path).exists() {
        fs::create_dir_all(format!("{}/documents/Totem", user_folder)).ok();
        fs::write(&path, "{}").ok();
    }

    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_prevent_default::init())
        .invoke_handler(tauri::generate_handler![get_json, set_json, edit_mode_set, edit_mode_get])
        .run(tauri::generate_context!())
        .unwrap();
}
