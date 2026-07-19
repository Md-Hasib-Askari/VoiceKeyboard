using System;
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
    private string _statusText = "Initializing...";

    [ObservableProperty]
    private string _lastTranscription = "";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private bool _autoType = true;

    [ObservableProperty]
    private string _startButtonText = "Start";

    [ObservableProperty]
    private bool _isSpeechDetected;

    [ObservableProperty]
    private string _pythonPath = "python3";

    [ObservableProperty]
    private bool _isDetectingPython;

    [ObservableProperty]
    private string _deviceInfo = "Detecting...";

    [ObservableProperty]
    private float _audioLevel;

    partial void OnPythonPathChanged(string value)
    {
        if (_engine.PythonPath == value)
            return;
        _settings.PythonPath = value;
        ConfigService.Save(_settings);
        _engine.PythonPath = value;
    }

    public MainViewModel()
    {
        _engine = new RealtimeEngine();
        _settings = ConfigService.Load();
        PythonPath = _settings.PythonPath;
        _engine.PythonPath = PythonPath;

        _engine.OnTranscription += OnTranscription;
        _engine.OnSpeechStart += () =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => IsSpeechDetected = true);
        _engine.OnSpeechEnd += () =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => IsSpeechDetected = false);
        _engine.OnAudioLevel += level =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AudioLevel = level);
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
            StatusText = "Setting up Python environment...";
            var venvPython = await WhisperTranscriber.DetectPythonPathAsync(status =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = status)
            );
            PythonPath = venvPython;
            _engine.PythonPath = PythonPath;
            _settings.PythonPath = PythonPath;
            ConfigService.Save(_settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ViewModel] Python setup failed: {ex.Message}");
        }
        finally
        {
            IsDetectingPython = false;
        }

        try
        {
            await _engine.InitializeAsync();
            IsReady = true;
            StatusText = "Idle";
        }
        catch (Exception ex)
        {
            StatusText = $"Init failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleStart()
    {
        if (IsListening)
        {
            IsListening = false;
            IsSpeechDetected = false;
            _engine.StopListening();
            StartButtonText = "Start";
        }
        else
        {
            IsListening = true;
            _engine.StartListening();
            StartButtonText = "Stop";
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
            Console.WriteLine($"[ViewModel] AutoType=true, IsWayland={KeyboardSimulator.IsWayland()}, text='{text}'");

            string? err;
            if (KeyboardSimulator.IsWayland())
                err = KeyboardSimulator.TypeTextWayland(text + " ");
            else
                err = KeyboardSimulator.TypeText(text + " ");

            if (err != null)
            {
                Console.WriteLine($"[ViewModel] Typing error: {err}");
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText = $"! {err}";
                });
            }
            else
            {
                Console.WriteLine($"[ViewModel] Typed successfully: '{text}'");
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

    public void DisposeSync()
    {
        _engine.StopListening();
        _engine.DisposeSync();
    }
}
