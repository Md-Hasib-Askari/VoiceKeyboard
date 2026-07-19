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
    public event Action<float>? OnAudioLevel;

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

                var level = ComputeRms(frame);
                OnAudioLevel?.Invoke(level);

                OnAudioFrame?.Invoke(frame);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioCapture] Error: {ex.Message}");
        }
    }

    private static float ComputeRms(byte[] pcm16)
    {
        long sum = 0;
        for (int i = 0; i < pcm16.Length; i += 2)
        {
            var sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            sum += sample * sample;
        }

        var rms = Math.Sqrt((double)sum / (pcm16.Length / 2));
        return Math.Clamp((float)(rms / 16384.0), 0f, 1f);
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
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
