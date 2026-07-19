using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceKeyboard.Services;

public class WhisperTranscriber : IAsyncDisposable
{
    private Process? _pythonProcess;
    private bool _initialized;
    private string _currentModel = "turbo";
    private string _currentPythonPath = "python3";
    private string _currentLanguage = "en";

    public string CurrentModel => _currentModel;

    public event Action? OnSpeechStart;
    public event Action? OnSpeechEnd;
    public event Action<string>? OnTranscription;
    public event Action? OnNoSpeech;
    public event Action<string>? OnError;
    public event Action<string>? OnDeviceDetected;

    public async Task InitializeAsync(string model = "turbo", string pythonPath = "python3", string? language = null)
    {
        language ??= _currentLanguage;
        if (_initialized && _currentModel == model && _currentPythonPath == pythonPath && _currentLanguage == language)
            return;

        // Stop existing server if running
        if (_initialized)
        {
            await DisposeAsync();
        }

        _currentLanguage = language;
        _currentModel = model;
        _currentPythonPath = pythonPath;

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "transcribe_server.py");

        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-u \"{scriptPath}\" {model} {language}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        _pythonProcess =
            Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Python process");
        Console.WriteLine($"[Whisper] Python PID={_pythonProcess.Id}, model={model}, python={pythonPath}");

        // Read stderr in background
        _ = Task.Run(async () =>
        {
            try
            {
                var buf = new char[256];
                while (true)
                {
                    int read = await _pythonProcess.StandardError.ReadAsync(buf, 0, buf.Length);
                    if (read == 0)
                        break;
                    Console.Write($"[Python:err] {new string(buf, 0, read)}");
                }
            }
            catch { }
        });

        // Read stdout events in background
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_pythonProcess.HasExited)
                {
                    var line = await _pythonProcess.StandardOutput.ReadLineAsync();
                    if (line == null)
                        break;
                    HandleEvent(line);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Whisper] Event reader error: {ex.Message}");
            }
        });

        // Wait for READY
        var sw = Stopwatch.StartNew();
        while (!_initialized && sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(50);
        }

        if (!_initialized)
            throw new Exception("Python server failed to start — no READY signal");
    }

    private void HandleEvent(string line)
    {
        if (line.StartsWith("READY"))
        {
            var parts = line.Split('\t');
            Console.WriteLine($"[Whisper] Server ready: model={parts[1]}, device={parts[2]}");
            var deviceStr = parts[2].Contains("cuda")
                ? $"GPU ({parts[2]})"
                : $"CPU ({parts[2]})";
            OnDeviceDetected?.Invoke(deviceStr);
            _initialized = true;
            return;
        }

        if (line.StartsWith("LOADING"))
        {
            var parts = line.Split('\t');
            Console.WriteLine($"[Whisper] Loading model: {parts[1]}");
            return;
        }

        if (line == "SPEECH_START")
        {
            OnSpeechStart?.Invoke();
            return;
        }

        if (line == "SPEECH_END")
        {
            OnSpeechEnd?.Invoke();
            return;
        }

        if (line.StartsWith("RESULT\t"))
        {
            var text = line[7..];
            Console.WriteLine($"[Whisper] Result: '{text}'");
            OnTranscription?.Invoke(text);
            return;
        }

        if (line == "NO_SPEECH")
        {
            OnNoSpeech?.Invoke();
            return;
        }

        if (line.StartsWith("ERROR\t"))
        {
            var msg = line[6..];
            Console.WriteLine($"[Whisper] Error: {msg}");
            OnError?.Invoke(msg);
            return;
        }
    }

    public void SendAudioFrame(byte[] frame)
    {
        if (!_initialized || _pythonProcess == null || _pythonProcess.HasExited)
            return;

        try
        {
            _pythonProcess.StandardInput.BaseStream.Write(frame, 0, frame.Length);
            _pythonProcess.StandardInput.BaseStream.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Whisper] Send error: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _initialized = false;
        try
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _pythonProcess.StandardInput.Close();
                _pythonProcess.Kill();
                await _pythonProcess.WaitForExitAsync();
            }
        }
        catch { }
        finally
        {
            _pythonProcess?.Dispose();
            _pythonProcess = null;
        }
    }

    public void DisposeSync()
    {
        _initialized = false;
        try
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _pythonProcess.StandardInput.Close();
                _pythonProcess.Kill();
                _pythonProcess.WaitForExit(3000);
            }
        }
        catch { }
        finally
        {
            _pythonProcess?.Dispose();
            _pythonProcess = null;
        }
    }

    public static async Task<string> DetectPythonPathAsync(Action<string>? onStatus = null)
    {
        // Use uv-managed venv — independent of conda/pyenv
        return await PythonEnvironmentService.EnsureEnvironmentAsync(onStatus);
    }

    public static async Task<string?> ProbePythonAsync(string pythonPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-c \"import webrtcvad; import faster_whisper\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? pythonPath : null;
        }
        catch
        {
            return null;
        }
    }
}
