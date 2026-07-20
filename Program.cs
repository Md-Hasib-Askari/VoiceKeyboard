using System;
using System.Net.Sockets;
using Avalonia;
using VoiceKeyboard.Services;

namespace VoiceKeyboard;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            var cmd = args[0] switch
            {
                "--toggle-visibility" => "start-stop",
                "--start-stop" => "start-stop",
                _ => null
            };

            if (cmd != null)
            {
                if (TrySendCommand(cmd))
                    return;

                LaunchApp();
                return;
            }
        }

        if (TrySendCommand("toggle"))
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void LaunchApp()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location,
                UseShellExecute = true,
                CreateNoWindow = false,
            });
        }
        catch { }
    }

    private static bool TrySendCommand(string command)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(GlobalHotkeyService.SocketPath));
            socket.Send(System.Text.Encoding.UTF8.GetBytes(command + "\n"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
