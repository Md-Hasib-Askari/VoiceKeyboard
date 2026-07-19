#!/bin/bash
# Install Voice Keyboard as a desktop application
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_DIR="$SCRIPT_DIR/bin/Release/net10.0"
ICON_DIR="$HOME/.local/share/icons"
APP_APPS_DIR="$HOME/.local/share/applications"

echo "Installing Voice Keyboard..."

# Verify binary exists
if [ ! -f "$APP_DIR/VoiceKeyboard" ]; then
  echo "❌ Binary not found at $APP_DIR/VoiceKeyboard"
  echo "   Run: cd VoiceKeyboard && dotnet build -c Release"
  exit 1
fi

# Create icon (SVG)
mkdir -p "$ICON_DIR"
cat >"$ICON_DIR/voice-keyboard.svg" <<'EOF'
<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128">
  <rect width="128" height="128" rx="20" fill="#1e1e2e"/>
  <rect x="20" y="75" width="88" height="35" rx="6" fill="#313244"/>
  <rect x="28" y="82" width="12" height="8" rx="2" fill="#89b4fa"/>
  <rect x="44" y="82" width="12" height="8" rx="2" fill="#89b4fa"/>
  <rect x="60" y="82" width="12" height="8" rx="2" fill="#89b4fa"/>
  <rect x="76" y="82" width="12" height="8" rx="2" fill="#89b4fa"/>
  <rect x="92" y="82" width="12" height="8" rx="2" fill="#89b4fa"/>
  <rect x="36" y="94" width="12" height="8" rx="2" fill="#89b4fa"/>
  <rect x="52" y="94" width="28" height="8" rx="2" fill="#89b4fa"/>
  <rect x="84" y="94" width="12" height="8" rx="2" fill="#89b4fa"/>
  <circle cx="64" cy="40" r="22" fill="none" stroke="#a6e3a1" stroke-width="4"/>
  <circle cx="64" cy="40" r="8" fill="#a6e3a1"/>
  <path d="M 54 28 A 18 18 0 0 1 54 52" fill="none" stroke="#a6e3a1" stroke-width="3" stroke-linecap="round"/>
  <path d="M 46 22 A 28 28 0 0 1 46 58" fill="none" stroke="#a6e3a1" stroke-width="3" stroke-linecap="round" opacity="0.5"/>
</svg>
EOF

# Create .desktop file
mkdir -p "$APP_APPS_DIR"
cat >"$APP_APPS_DIR/voice-keyboard.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Voice Keyboard
Comment=Speech-to-text voice keyboard using Whisper
Exec=$APP_DIR/VoiceKeyboard
Icon=voice-keyboard
Terminal=false
Categories=Utility;Accessibility;
Keywords=voice;keyboard;speech;whisper;transcription;
StartupNotify=true
EOF

chmod +x "$APP_APPS_DIR/voice-keyboard.desktop"

# Copy to desktop
if [ -d "$HOME/Desktop" ]; then
  cp "$APP_APPS_DIR/voice-keyboard.desktop" "$HOME/Desktop/"
  chmod +x "$HOME/Desktop/voice-keyboard.desktop"
  echo "✅ Desktop shortcut created"
fi

# Update desktop database
update-desktop-database "$APP_APPS_DIR" 2>/dev/null || true

# --- Register global keyboard shortcut ---
echo ""
echo "Registering global hotkey Super+F9..."

if command -v gsettings &>/dev/null && gsettings get org.gnome.settings-daemon.plugins.media-keys &>/dev/null 2>&1; then
  KEYBINDING_PATH="/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/"
  gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:"$KEYBINDING_PATH" name "Voice Keyboard"
  gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:"$KEYBINDING_PATH" command "$APP_DIR/VoiceKeyboard --toggle-visibility"
  gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:"$KEYBINDING_PATH" binding '<Super>F9'

  current=$(gsettings get org.gnome.settings-daemon.plugins.media-keys custom-keybindings)
  if echo "$current" | grep -q "@as \[\]"; then
    gsettings set org.gnome.settings-daemon.plugins.media-keys custom-keybindings "['$KEYBINDING_PATH']"
  elif ! echo "$current" | grep -q "custom0/"; then
    gsettings set org.gnome.settings-daemon.plugins.media-keys custom-keybindings "['$KEYBINDING_PATH', ${current:1}"
  fi
  echo "✅ Registered Super+F9 (GNOME)"
elif command -v kwriteconfig5 &>/dev/null; then
  kwriteconfig5 --file kglobalshortcutsrc --group "Voice Keyboard" --key "_launch" "Meta+F9,none,Launch Voice Keyboard"
  kwriteconfig5 --file kglobalshortcutsrc --group "Voice Keyboard" --key "_k_friendly_name" "Voice Keyboard"
  kglobalaccel5 --replace &>/dev/null || true
  echo "✅ Registered Meta+F9 (KDE)"
else
  echo "⚠️  Could not auto-register global hotkey for your desktop environment."
  echo "   To use the global hotkey, manually bind Super+F9 to:"
  echo "     $APP_DIR/VoiceKeyboard --toggle-visibility"
  echo ""
  echo "   GNOME: Settings → Keyboard → Keyboard Shortcuts → Custom Shortcuts"
  echo "   KDE:   System Settings → Shortcuts → Custom Shortcuts"
  echo "   Sway:  bindsym Mod4+F9 exec $APP_DIR/VoiceKeyboard --toggle-visibility"
  echo "   Hyprland: bind = SUPER, F9, exec, $APP_DIR/VoiceKeyboard --toggle-visibility"
fi

echo ""
echo "✅ Voice Keyboard installed!"
echo ""
echo "  🎤 Super+F9 — Launch / toggle Voice Keyboard from anywhere"
echo "  🔍 App launcher → search 'Voice Keyboard'"
echo ""
echo "To uninstall:"
echo "  rm ~/.local/share/applications/voice-keyboard.desktop"
echo "  rm ~/.local/share/icons/voice-keyboard.svg"
echo "  rm ~/Desktop/voice-keyboard.desktop"
