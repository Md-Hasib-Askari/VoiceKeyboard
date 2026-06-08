```markdown
# Project Overview

Voice Keyboard is a real-time speech-to-text application for Linux that acts as a voice keyboard — transcribing speech and typing the result into whichever window has focus. The frontend is **C# / .NET 10 / Avalonia UI** using the **MVVM pattern** with CommunityToolkit.Mvvm. The ML pipeline (VAD + transcription) runs in a **Python subprocess** (`faster-whisper` + `webrtcvad`), communicating with C# via stdin/stdout pipes with near-zero IPC overhead (~0.002ms/frame). Audio capture uses Linux ALSA (`arecord`). Auto-typing uses `xdotool` (X11) or `ydotool` (Wayland).

# Architecture

```

arecord (ALSA) → C# AudioCapture → pipe → Python Server → pipe → C# Avalonia UI → xdotool/ydotool

```

**Data flow:**
1. `arecord` captures 16kHz 16-bit mono PCM in 960-byte frames (30ms each)
2. `AudioCapture.cs` reads frames and fires `OnAudioFrame` events
3. `RealtimeEngine.cs` forwards frames to the Python server via `WhisperTranscriber.SendAudioFrame()`
4. Python `transcribe_server.py` runs WebRTC VAD on each frame, buffers speech, and transcribes with `faster-whisper` after 300ms silence
5. Python outputs line-delimited events to stdout: `SPEECH_START`, `SPEECH_END`, `RESULT<tab><text>`, `NO_SPEECH`, `ERROR<tab><msg>`
6. `WhisperTranscriber.cs` reads events on a background thread and fires C# events
7. `MainViewModel.cs` handles events: updates UI, calls `KeyboardSimulator` for auto-typing

**Model switching:** Changing the model dropdown in the UI kills the Python server and restarts it with the new model name as a CLI argument. Listening state is preserved across model changes.

**Key components:**
- `AudioCapture.cs` — Manages `arecord` subprocess, reads raw PCM frames via stdout
- `WhisperTranscriber.cs` — Manages Python server lifecycle (start/stop/restart), streams audio in, reads events out
- `RealtimeEngine.cs` — Glues AudioCapture + WhisperTranscriber together; exposes high-level Start/Stop/Pause/ChangeModel
- `KeyboardSimulator.cs` — Static helper that shells out to `xdotool type` or `ydotool type`
- `transcribe_server.py` — Long-running Python process; reads 960-byte PCM frames from stdin, runs WebRTC VAD + faster-whisper transcription, outputs events to stdout

# Build / Test / Run

```bash
# Build (Release)
dotnet build -c Release

# Build (Debug)
dotnet build

# Run
dotnet run
# Or after building:
./bin/Release/net10.0/VoiceKeyboard

# Install as desktop app (creates .desktop entry, icon, desktop shortcut)
chmod +x install-desktop.sh
./install-desktop.sh

# Run benchmark suite (records 5s audio, tests direct vs server transcription, IPC overhead, VAD responsiveness)
chmod +x benchmark.sh
./benchmark.sh
```

**Runtime dependencies:** `python3` with `faster-whisper` and `webrtcvad`, `arecord` (alsa-utils), `xdotool` (X11) or `ydotool` (Wayland), NVIDIA CUDA drivers (optional, for GPU acceleration).

**No automated test suite exists.** Testing is manual via the UI or the benchmark script.

# Conventions

- **MVVM pattern:** Views in `Views/`, ViewModels in `ViewModels/`, services in `Services/`. No code-behind logic except hotkey handling in `MainWindow.axaml.cs`.
- **CommunityToolkit.Mvvm source generators:** Use `[ObservableProperty]` for bindable properties (generates `PropertyName` from `_propertyName` field), `[RelayCommand]` for ICommand methods, `partial void OnXxxChanged()` for property change callbacks. Never reference the backing field directly in code — use the generated property.
- **Avalonia compiled bindings:** `AvaloniaUseCompiledBindingsByDefault=true` in csproj. All XAML bindings are compile-time checked via `x:DataType`.
- **Async initialization:** Services use `async Task InitializeAsync()` pattern. Python server startup is async with a polling wait for `READY` on stdout.
- **Event-driven communication:** C# ↔ Python uses line-delimited text protocol over stdin/stdout. Python reads binary PCM frames from stdin (960 bytes each), writes text events to stdout.
- **Disposal:** Services implement `IAsyncDisposable`. Python process is killed and disposed on shutdown or model change.
- **Naming:** PascalCase for properties/methods, `_camelCase` for private fields, `On<Event>` for event handlers and events.
- **Console logging:** Debug/diagnostic output uses `Console.WriteLine` with `[Component]` prefixes (e.g., `[Whisper]`, `[VAD]`, `[Engine]`, `[AudioCapture]`).

# Key Directories

- `Scripts/` — Python transcription server (`transcribe_server.py`)
- `Services/` — C# service layer: audio capture, Whisper server management, real-time engine, keyboard simulation
- `ViewModels/` — MVVM ViewModels with CommunityToolkit.Mvvm source generators
- `Views/` — Avalonia XAML views and code-behind (hotkeys only)

```
