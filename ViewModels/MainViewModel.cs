using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceKeyboard.Services;

namespace VoiceKeyboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RealtimeEngine _engine;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _statusText = "Initializing Whisper model...";

    [ObservableProperty]
    private string _lastTranscription = "No transcription yet.";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private bool _autoType = true;

    [ObservableProperty]
    private bool _useWayland;

    partial void OnUseWaylandChanged(bool value)
    {
        _settings.UseWayland = value;
        ConfigService.Save(_settings);
    }

    [ObservableProperty]
    private string _startButtonText = "Start";

    [ObservableProperty]
    private string _pauseButtonText = "Pause";

    [ObservableProperty]
    private bool _isSpeechDetected;

    [ObservableProperty]
    private string _selectedModel = "turbo";

    [ObservableProperty]
    private string _pythonPath = "python3";

    [ObservableProperty]
    private bool _isDetectingPython;

    [ObservableProperty]
    private string _deviceInfo = "Detecting...";

    public List<string> AvailableModels { get; } =
        new() { "tiny.en", "tiny", "base", "small", "medium", "large-v3", "turbo" };

    public MainViewModel()
    {
        _engine = new RealtimeEngine();
        _settings = ConfigService.Load();
        PythonPath = _settings.PythonPath;
        UseWayland = _settings.UseWayland;
        _engine.PythonPath = PythonPath;
        _engine.OnTranscription += OnTranscription;
        _engine.OnSpeechStart += () =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => IsSpeechDetected = true);
        _engine.OnSpeechEnd += () =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => IsSpeechDetected = false);
        _engine.OnDeviceDetected += device =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => DeviceInfo = device);
        _engine.OnStatusChanged += status =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = status);

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        IsDetectingPython = true;

        try
        {
            StatusText = "🔍 Setting up Python environment...";
            var venvPython = await WhisperTranscriber.DetectPythonPathAsync(status =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = status)
            );
            PythonPath = venvPython;
            _engine.PythonPath = PythonPath;
            _settings.PythonPath = PythonPath;
            ConfigService.Save(_settings);
            Console.WriteLine($"[ViewModel] Python ready: {PythonPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ViewModel] Python environment setup failed: {ex.Message}");
        }
        finally
        {
            IsDetectingPython = false;
        }

        try
        {
            await _engine.InitializeAsync(SelectedModel);
            IsReady = true;
            StatusText = "Ready. Press Start (F9) to listen continuously.";
        }
        catch (Exception ex)
        {
            StatusText = $"Init failed: {ex.Message}";
        }
    }

    partial void OnPythonPathChanged(string value)
    {
        if (_engine.PythonPath == value)
            return;
        _settings.PythonPath = value;
        ConfigService.Save(_settings);
        IsReady = false;
        _ = _engine.ChangePythonPathAsync(value);
        IsReady = true;
    }

    partial void OnSelectedModelChanged(string value)
    {
        if (!IsReady)
            return;
        IsReady = false;
        _ = ChangeModelAsync(value);
    }

    private async System.Threading.Tasks.Task ChangeModelAsync(string model)
    {
        try
        {
            await _engine.ChangeModelAsync(model);
            IsReady = true;
            StatusText = $"Ready. Model: {model}";
        }
        catch (Exception ex)
        {
            StatusText = $"Model change failed: {ex.Message}";
            IsReady = true;
        }
    }

    [RelayCommand]
    private void ToggleStart()
    {
        if (IsListening)
        {
            IsListening = false;
            IsPaused = false;
            IsSpeechDetected = false;
            _engine.StopListening();
            StartButtonText = "Start";
        }
        else
        {
            IsListening = true;
            IsPaused = false;
            _engine.StartListening();
            StartButtonText = "Stop";
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsListening)
            return;

        if (IsPaused)
        {
            IsPaused = false;
            _engine.ResumeListening();
            PauseButtonText = "Pause";
        }
        else
        {
            IsPaused = true;
            IsSpeechDetected = false;
            _engine.PauseListening();
            PauseButtonText = "Resume";
        }
    }

    private void OnTranscription(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            LastTranscription = text;
        });

        if (AutoType)
        {
            string? err;
            if (UseWayland)
                err = KeyboardSimulator.TypeTextWayland(text + " ");
            else
                err = KeyboardSimulator.TypeText(text + " ");

            if (err != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText = $"Typing error: {err}";
                });
            }
        }
    }

    [RelayCommand]
    private async Task AutoDetectPython()
    {
        IsDetectingPython = true;
        try
        {
            var venvPython = await WhisperTranscriber.DetectPythonPathAsync(status =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = status)
            );
            PythonPath = venvPython;
        }
        finally
        {
            IsDetectingPython = false;
        }
    }
}
