using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceKeyboard.Services;

public class AudioCapture : IDisposable
{
    private Process? _arecordProcess;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public event Action<byte[]>? OnAudioFrame;

    // 480 samples * 2 bytes (16-bit) = 960 bytes = 30ms at 16kHz
    private const int FrameSize = 960;

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isRunning)
            return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        StartArecord();
    }

    private void StartArecord()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "arecord",
            Arguments = "-f S16_LE -r 16000 -c 1 -t raw",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _arecordProcess =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start arecord");

        _ = CaptureLoopAsync(_cts!.Token);
        Console.WriteLine("[AudioCapture] arecord started");
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[FrameSize];
        var stream = _arecordProcess!.StandardOutput.BaseStream;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int totalRead = 0;
                while (totalRead < FrameSize)
                {
                    int read = await stream.ReadAsync(
                        buffer.AsMemory(totalRead, FrameSize - totalRead),
                        ct
                    );
                    if (read == 0)
                        return;
                    totalRead += read;
                }

                var frame = buffer.ToArray();
                OnAudioFrame?.Invoke(frame);
                if (DateTime.Now.Millisecond < 100) // ~1 log/sec
                    Console.WriteLine($"[AudioCapture] Frame sent, {frame.Length} bytes");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioCapture] Error: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_isRunning)
            return;
        _isRunning = false;
        _cts?.Cancel();

        try
        {
            _arecordProcess?.Kill();
        }
        catch { }
        _arecordProcess?.WaitForExit(1000);
        _arecordProcess?.Dispose();
        _arecordProcess = null;

        Console.WriteLine("[AudioCapture] Stopped");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
