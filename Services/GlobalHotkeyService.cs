using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace VoiceKeyboard.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly Action _onToggle;

    private UnixDomainSocketEndPoint? _socketEndPoint;
    private Socket? _listenSocket;

    public static readonly string SocketDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "voice-keyboard");

    public static readonly string SocketPath = Path.Combine(SocketDir, "toggle.sock");

    public GlobalHotkeyService(Action onToggle)
    {
        _onToggle = onToggle;
    }

    public void Start()
    {
        try
        {
            Directory.CreateDirectory(SocketDir);

            try { File.Delete(SocketPath); } catch { }

            _socketEndPoint = new UnixDomainSocketEndPoint(SocketPath);
            _listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listenSocket.Bind(_socketEndPoint);
            _listenSocket.Listen(1);

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using var client = await _listenSocket.AcceptAsync();
                        var buffer = new byte[16];
                        await client.ReceiveAsync(buffer, SocketFlags.None);
                        var cmd = System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0', '\n', '\r');
                        if (cmd == "toggle")
                        {
                            _ = Dispatcher.UIThread.InvokeAsync(() => _onToggle());
                        }
                    }
                    catch { }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GlobalHotkey] Socket listener failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _listenSocket?.Close();
        try { File.Delete(SocketPath); } catch { }
    }

    public static void SendToggleSignal()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send("toggle\n"u8);
        }
        catch
        {
            Console.WriteLine("Voice Keyboard is not running, launching...");
            throw;
        }
    }
}
