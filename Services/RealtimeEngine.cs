using System;
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
    public event Action<float>? OnAudioLevel;
    public event Action<string>? OnDeviceDetected;
    public event Action<string>? OnStatusChanged;

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
        _transcriber.OnDeviceDetected += device => OnDeviceDetected?.Invoke(device);

        _capture.OnAudioLevel += level =>
        {
            if (_isListening && !_isPaused)
                OnAudioLevel?.Invoke(level);
            else
                OnAudioLevel?.Invoke(0f);
        };

        _capture.OnAudioFrame += frame =>
        {
            if (!_isPaused && _isListening)
                _transcriber.SendAudioFrame(frame);
        };
    }

    public async Task InitializeAsync()
    {
        await _transcriber.InitializeAsync("turbo", PythonPath);
    }

    public void StartListening()
    {
        _isPaused = false;
        _isListening = true;
        _capture.Start();
        OnStatusChanged?.Invoke("Listening");
    }

    public void PauseListening()
    {
        _isPaused = true;
        OnSpeechEnd?.Invoke();
        OnStatusChanged?.Invoke("Paused");
    }

    public void ResumeListening()
    {
        _isPaused = false;
        OnStatusChanged?.Invoke("Listening");
    }

    public void StopListening()
    {
        _isListening = false;
        _isPaused = false;
        _capture.Stop();
        OnSpeechEnd?.Invoke();
        OnStatusChanged?.Invoke("Idle");
    }

    public void DisposeSync()
    {
        _isListening = false;
        _capture.Dispose();
        _transcriber.DisposeSync();
    }

    public async ValueTask DisposeAsync()
    {
        _capture.Dispose();
        await _transcriber.DisposeAsync();
    }
}
