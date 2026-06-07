# 🎤 Voice Keyboard — Speech-to-Text for Linux

A real-time voice keyboard for Linux that transcribes your speech using [faster-whisper](https://github.com/SYSTRAN/faster-whisper) with NVIDIA GPU acceleration and types the result into whatever application has focus.

Built with **C# / Avalonia UI** for the frontend and **Python** for the ML pipeline (VAD + Whisper), communicating via stdin/stdout with zero IPC overhead.

![Architecture](https://img.shields.io/badge/C%23-Avalonia%20UI-blue) ![Python](https://img.shields.io/badge/Python-faster--whisper-green) ![GPU](https://img.shields.io/badge/GPU-CUDA%20cuBLAS-yellow)

## ✨ Features

- **Real-time streaming** — WebRTC VAD detects speech, auto-transcribes on silence (~440ms total latency)
- **GPU Accelerated** — Uses CUDA/cuBLAS via faster-whisper (auto-falls back to CPU)
- **Model Selector** — Switch between tiny.en, tiny, base, small, medium, large-v3 at runtime
- **Auto-Type** — Transcribed text is typed via `xdotool` (X11) or `ydotool` (Wayland)
- **Dark Theme UI** — Catppuccin-inspired Avalonia UI with speech indicator
- **Desktop App** — Install as a desktop application with icon and app launcher entry
- **Hotkeys** — F9 to Start/Stop, F10 to Pause/Resume

## 🏗️ Architecture

```
┌──────────────┐     PCM frames      ┌──────────────────────┐
│   arecord    │────────────────────►│  C# AudioCapture     │
│  (ALSA mic)  │  960 bytes/frame    │  (30ms, 16kHz mono)  │
└──────────────┘                     └──────────┬───────────┘
                                                │ pipe (stdin)
                                                ▼
                                     ┌──────────────────────┐
                                     │  Python Server       │
                                     │  ├─ WebRTC VAD       │
                                     │  └─ faster-whisper   │
                                     │     (CUDA GPU)       │
                                     └──────────┬───────────┘
                                                │ pipe (stdout)
                                                ▼
                                     ┌──────────────────────┐
                                     │  C# Avalonia UI      │
                                     │  ├─ Speech indicator │
                                     │  ├─ Model selector   │
                                     │  └─ xdotool/ydotool  │
                                     └──────────────────────┘
```

**IPC overhead: ~0.002ms per frame** (measured) — effectively zero.

## 📋 Prerequisites

1. **.NET 10 SDK**

   ```bash
   sudo apt-get install dotnet-sdk-10.0
   ```

2. **Python 3.10+** with packages:

   ```bash
   pip install faster-whisper webrtcvad
   ```

3. **NVIDIA GPU** (optional but recommended)
   - CUDA 12.x drivers installed
   - faster-whisper auto-detects and uses GPU

4. **xdotool** (X11 auto-typing)

   ```bash
   sudo apt install xdotool
   ```

5. **ydotool** (Wayland auto-typing — check the UI checkbox)

   ```bash
   sudo apt install ydotool
   sudo systemctl enable --now ydotool
   ```

6. **ALSA** (audio capture)

   ```bash
   sudo apt install alsa-utils
   ```

## 🚀 Build & Run

```bash
# Clone and build
dotnet build -c Release

# Run
./bin/Release/net10.0/VoiceKeyboard

# Or run directly
dotnet run
```

On first run, the Whisper model will be downloaded automatically (~500MB for `small`).

## 🖥️ Install as Desktop App

```bash
chmod +x install-desktop.sh
./install-desktop.sh
```

This creates:

- Desktop shortcut on your Desktop
- App launcher entry (search "Voice Keyboard")
- Custom SVG icon

## 🎮 Usage

1. Launch Voice Keyboard
2. Select a model from the dropdown (default: `small`)
3. Click **Start** or press **F9** to begin listening
4. Speak — the speech indicator turns 🔴 red when voice is detected
5. Pause for ~300ms — audio is automatically transcribed and typed
6. Click **Pause** or press **F10** to pause (mic stays open)
7. Click **Stop** or press **F9** to stop completely

| Control | Action |
|---------|--------|
| **Start/Stop** button or **F9** | Toggle continuous listening |
| **Pause/Resume** button or **F10** | Pause/resume listening |
| **Model dropdown** | Switch Whisper model (reloads server) |
| **Auto-type** checkbox | Toggle xdotool/ydotool typing |
| **Wayland** checkbox | Switch to ydotool for Wayland |

## 🧠 Models

| Model | Size | GPU Speed | Accuracy | Best For |
|-------|------|-----------|----------|----------|
| `tiny.en` | ~75 MB | Fastest | Basic | Quick commands, fastest response |
| `tiny` | ~75 MB | Fastest | Basic | Multilingual quick commands |
| `base` | ~150 MB | Fast | Good | General use |
| `small` | ~500 MB | Fast | **Better** | **Recommended default** |
| `medium` | ~1.5 GB | Moderate | Great | Better accuracy, slower |
| `large-v3` | ~3 GB | Slowest | Best | Maximum accuracy |

Models are downloaded automatically on first use. You can switch models at runtime — the Python server restarts with the new model.

## 📊 Benchmark

Run the benchmark suite:

```bash
chmod +x benchmark.sh
./benchmark.sh
```

Sample results (NVIDIA GPU, `small` model):

| Metric | Result |
|--------|--------|
| IPC overhead | 0.002ms/frame |
| Transcription (1s audio) | ~139ms |
| Transcription (5s audio) | ~153ms |
| Real-time factor | 32.7x faster than real-time |
| VAD silence timeout | 300ms |
| **Total after you stop speaking** | **~440ms** |

## 📁 Project Structure

```
VoiceKeyboard/
├── Program.cs                        # Entry point
├── App.axaml / App.axaml.cs          # Avalonia app config
├── VoiceKeyboard.csproj              # Project file (Avalonia + MVVM Toolkit)
├── .gitignore
├── install-desktop.sh                # Desktop app installer
├── benchmark.sh                      # Benchmark suite
│
├── Scripts/
│   └── transcribe_server.py          # Python server: WebRTC VAD + faster-whisper
│
├── Services/
│   ├── AudioCapture.cs               # arecord PCM streaming (16kHz mono)
│   ├── WhisperTranscriber.cs         # Python server manager (stdin/stdout IPC)
│   ├── RealtimeEngine.cs             # Audio pipeline: capture → Python server
│   └── KeyboardSimulator.cs          # xdotool (X11) / ydotool (Wayland)
│
├── ViewModels/
│   └── MainViewModel.cs              # MVVM: state, commands, model selector
│
└── Views/
    ├── MainWindow.axaml              # UI layout (Catppuccin dark theme)
    └── MainWindow.axaml.cs           # F9/F10 hotkey handlers
```

## 🔧 How It Works

1. **Audio Capture**: `arecord` (ALSA) captures 16kHz 16-bit mono audio in 30ms frames
2. **Streaming**: C# streams each frame to the Python server via stdin pipe
3. **VAD**: Python uses WebRTC VAD (aggressiveness=3) to detect speech start/end
4. **Buffering**: Speech frames are buffered; trailing silence is included for natural boundaries
5. **Transcription**: After 300ms of silence, buffered audio is transcribed with faster-whisper
6. **Typing**: Result text (+ trailing space) is typed via xdotool/ydotool into the focused window
7. **UI Update**: Transcription appears in the Avalonia UI and speech indicator updates in real-time

## ❓ Troubleshooting

- **No GPU detected**: Falls back to CPU automatically. Ensure CUDA drivers are installed.
- **xdotool not working**: You must be on X11. On Wayland, use the "Wayland (ydotool)" checkbox.
- **Microphone not found**: Check `arecord -l` to verify your mic is visible to ALSA.
- **Model download fails**: Manually download from [HuggingFace](https://huggingface.co/SYSTRAN) and place in the app directory.
- **Words merging**: Trailing spaces are auto-added. If still merging, check that xdotool is working.
- **Python server won't start**: Ensure `faster-whisper` and `webrtcvad` are installed: `pip install faster-whisper webrtcvad`

## 📜 License

MIT
