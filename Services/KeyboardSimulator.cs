using System;
using System.Diagnostics;

namespace VoiceKeyboard.Services;

public static class KeyboardSimulator
{
    /// <summary>
    /// Types the given text using xdotool (Linux X11).
    /// Works reliably on most X11-based Linux desktops.
    /// </summary>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdotool",
                Arguments = $"type -- \"{EscapeForShell(text)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(2000);

            if (process?.ExitCode != 0)
            {
                var err = process?.StandardError.ReadToEnd();
                Console.WriteLine($"[Keyboard] xdotool error: {err}");
            }
            else
            {
                Console.WriteLine($"[Keyboard] Typed: '{text}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Keyboard] Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ydotool alternative — works on Wayland.
    /// Install: sudo apt install ydotool
    /// </summary>
    public static void TypeTextWayland(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"type -- \"{EscapeForShell(text)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(2000);
            Console.WriteLine($"[Keyboard] Typed (Wayland): '{text}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Keyboard] Wayland failed: {ex.Message}");
        }
    }

    private static string EscapeForShell(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
}
