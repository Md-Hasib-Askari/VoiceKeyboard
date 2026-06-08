using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceKeyboard.Services;

public class RealtimeEngine : IAsyncDisposable
{
    private readonly AudioCapture _capture;
    private readonly WhisperTranscriber _transcriber;

    private volatile bool _isPaused;
    private volatile bool _isListening;

    public event Action<string>? OnTranscription;
    public event Action? OnSpeechStart;
    public event Action? OnSpeechEnd;
    public event Action<string>? OnStatusChanged;

    public string CurrentModel => _transcriber.CurrentModel;
    public string PythonPath { get; set; } = "python3";

    public RealtimeEngine()
    {
        _capture = new AudioCapture();
        _transcriber = new WhisperTranscriber();

        _transcriber.OnSpeechStart += () =>
        {
            if (!_isPaused)
                OnSpeechStart?.Invoke();
        };
        _transcriber.OnSpeechEnd += () =>
        {
            if (!_isPaused)
                OnSpeechEnd?.Invoke();
        };
        _transcriber.OnTranscription += text =>
        {
            if (!_isPaused)
                OnTranscription?.Invoke(text);
        };

        _capture.OnAudioFrame += frame =>
        {
            if (!_isPaused && _isListening)
                _transcriber.SendAudioFrame(frame);
        };
    }

    public async Task InitializeAsync(string model = "small")
    {
        await _transcriber.InitializeAsync(model, PythonPath);
    }

    public async Task ChangeModelAsync(string model)
    {
        var wasListening = _isListening;
        var wasPaused = _isPaused;

        if (wasListening)
        {
            _capture.Stop();
            _isListening = false;
        }

        OnStatusChanged?.Invoke($"🔄 Loading {model}...");
        await _transcriber.InitializeAsync(model, PythonPath);

        if (wasListening)
        {
            _isListening = true;
            _isPaused = wasPaused;
            _capture.Start();
            OnStatusChanged?.Invoke("🔴 Listening...");
        }
        else
        {
            OnStatusChanged?.Invoke($"Ready. Model: {model}");
        }
    }

    public void StartListening()
    {
        _isPaused = false;
        _isListening = true;
        _capture.Start();
        OnStatusChanged?.Invoke("🔴 Listening...");
    }

    public void PauseListening()
    {
        _isPaused = true;
        OnSpeechEnd?.Invoke();
        OnStatusChanged?.Invoke("⏸ Paused");
    }

    public void ResumeListening()
    {
        _isPaused = false;
        OnStatusChanged?.Invoke("🔴 Listening...");
    }

    public void StopListening()
    {
        _isListening = false;
        _isPaused = false;
        _capture.Stop();
        OnSpeechEnd?.Invoke();
        OnStatusChanged?.Invoke("⏹ Stopped");
    }

    public async Task ChangePythonPathAsync(string pythonPath)
    {
        PythonPath = pythonPath;
        var wasListening = _isListening;
        var wasPaused = _isPaused;

        if (wasListening)
        {
            _capture.Stop();
            _isListening = false;
        }

        OnStatusChanged?.Invoke($"🔄 Restarting with Python: {pythonPath}...");
        await _transcriber.InitializeAsync(CurrentModel, PythonPath);

        if (wasListening)
        {
            _isListening = true;
            _isPaused = wasPaused;
            _capture.Start();
            OnStatusChanged?.Invoke("🔴 Listening...");
        }
        else
        {
            OnStatusChanged?.Invoke($"Ready. Model: {CurrentModel}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _capture.Dispose();
        await _transcriber.DisposeAsync();
    }
}
