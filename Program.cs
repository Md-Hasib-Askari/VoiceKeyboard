using System;
using System.Diagnostics;
using System.Net.Sockets;
using Avalonia;
using VoiceKeyboard.Services;

namespace VoiceKeyboard;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--toggle-visibility")
        {
            TrySendToggle();
            return;
        }

        // If another instance is already running, send toggle and exit
        if (TrySendToggle())
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool TrySendToggle()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(GlobalHotkeyService.SocketPath));
            socket.Send("toggle\n"u8);
            return true;
        }
        catch
        {
            // Stale socket file? Remove it.
            try { System.IO.File.Delete(GlobalHotkeyService.SocketPath); } catch { }
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
