using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace VoiceKeyboard.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly Dictionary<string, Action> _commands;

    private UnixDomainSocketEndPoint? _socketEndPoint;
    private Socket? _listenSocket;

    public static readonly string SocketDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "voice-keyboard");

    public static readonly string SocketPath = Path.Combine(SocketDir, "toggle.sock");

    public GlobalHotkeyService(Dictionary<string, Action> commands)
    {
        _commands = commands;
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
                        var buffer = new byte[32];
                        var len = await client.ReceiveAsync(buffer, SocketFlags.None);
                        var cmd = System.Text.Encoding.UTF8.GetString(buffer, 0, len)
                            .TrimEnd('\0', '\n', '\r');

                        if (_commands.TryGetValue(cmd, out var action))
                        {
                            _ = Dispatcher.UIThread.InvokeAsync(() => action());
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

    public static void SendCommand(string command)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(System.Text.Encoding.UTF8.GetBytes(command + "\n"));
        }
        catch
        {
            throw;
        }
    }
}
