using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace VoiceKeyboard.Services;

public static class KeyboardSimulator
{
    /// <summary>
    /// Types text via xdotool (X11). Falls back to Wayland tools if detected.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        if (IsWayland())
        {
            return TypeTextWayland(text);
        }

        var err = RunProcess("xdotool", new[] { "type", "--", text });
        if (err != null)
            return $"xdotool failed: {err}";
        return null;
    }

    /// <summary>
    /// Types text on Wayland via wtype (preferred) or ydotool.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? TypeTextWayland(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        string? lastError = null;

        if (CommandExists("wtype"))
        {
            var err = TryTypeWithWtype(text);
            if (err == null)
                return null;
            lastError = err;
        }

        if (CommandExists("ydotool"))
        {
            var err = TryTypeWithYdotool(text);
            if (err == null)
                return null;
            lastError = err;
        }

        return lastError ?? "Install a Wayland typing tool:\n" +
               "  sudo apt install wtype\n" +
               "(or) sudo apt install ydotool ydotoold && ydotoold &";
    }

    private static string? TryTypeWithWtype(string text)
    {
        var err = RunProcessWithStdin("wtype", new[] { "-" }, text);
        if (err != null)
            return $"wtype failed: {err}";
        return null;
    }

    private static string? TryTypeWithYdotool(string text)
    {
        if (!IsYdotooldRunning())
        {
            if (!StartYdotoold())
                return "ydotoold could not be started.\nInstall: sudo apt install ydotool ydotoold";

            System.Threading.Thread.Sleep(200);
        }

        var err = RunProcess("ydotool", new[] { "type", text }, ydotoolSocket: FindYdotoolSocket());
        if (err != null)
            return $"ydotool failed: {err}";
        return null;
    }

    private static string? RunProcess(string fileName, string[] args, string? ydotoolSocket = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            if (!string.IsNullOrEmpty(ydotoolSocket))
                psi.EnvironmentVariables["YDOTOOL_SOCKET"] = ydotoolSocket;

            using var process = Process.Start(psi);
            if (process == null)
                return $"Could not start {fileName}";

            process.WaitForExit(3000);

            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd().Trim();
                if (string.IsNullOrEmpty(err))
                    err = $"exit code {process.ExitCode}";
                return err;
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? RunProcessWithStdin(string fileName, string[] args, string stdinText)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
                return $"Could not start {fileName}";

            process.StandardInput.Write(stdinText);
            process.StandardInput.Close();
            process.WaitForExit(3000);

            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd().Trim();
                if (string.IsNullOrEmpty(err))
                    err = $"exit code {process.ExitCode}";
                return err;
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool IsWayland() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
        || string.Equals(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            "wayland",
            StringComparison.OrdinalIgnoreCase
        );

    private static bool CommandExists(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsYdotooldRunning()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pgrep",
                Arguments = "-x ydotoold",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindYdotoolSocket()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("YDOTOOL_SOCKET")))
            return null;

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var uid = Environment.GetEnvironmentVariable("UID");
        var candidates = new[]
        {
            !string.IsNullOrEmpty(runtimeDir) ? Path.Combine(runtimeDir, ".ydotool_socket") : null,
            !string.IsNullOrEmpty(uid) ? Path.Combine("/run", "user", uid, ".ydotool_socket") : null,
            Path.Combine("/tmp", ".ydotool_socket"),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool StartYdotoold()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ydotoold",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                return false;
            // Let it run for a moment
            System.Threading.Thread.Sleep(150);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
