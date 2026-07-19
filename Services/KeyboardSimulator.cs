using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace VoiceKeyboard.Services;

public static class KeyboardSimulator
{
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

    public static string? TypeTextWayland(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        string? lastError = null;

        if (CommandExists("wtype"))
        {
            Console.WriteLine("[Keyboard] Trying wtype...");
            var err = TryTypeWithWtype(text);
            if (err == null)
            {
                Console.WriteLine("[Keyboard] wtype succeeded");
                return null;
            }
            Console.WriteLine($"[Keyboard] wtype failed: {err}");
            lastError = err;
        }

        if (CommandExists("ydotool"))
        {
            Console.WriteLine("[Keyboard] Trying ydotool...");
            var err = TryTypeWithYdotool(text);
            if (err == null)
            {
                Console.WriteLine("[Keyboard] ydotool succeeded");
                return null;
            }
            Console.WriteLine($"[Keyboard] ydotool failed: {err}");
            lastError = err;
        }

        if (CommandExists("xdotool"))
        {
            Console.WriteLine("[Keyboard] Trying xdotool...");
            var err = RunProcess("xdotool", new[] { "type", "--", text });
            if (err == null)
            {
                Console.WriteLine("[Keyboard] xdotool succeeded");
                return null;
            }
            Console.WriteLine($"[Keyboard] xdotool failed: {err}");
            lastError = err;
        }

        return lastError ?? "No typing tool found. Install one:\n" +
               "  sudo apt install xdotool   (works via XWayland)\n" +
               "  sudo apt install wtype     (wlroots Wayland)";
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
            RemoveStaleSockets();

            if (!StartYdotoold())
            {
                if (!CanAccessUinput() && !IsInInputGroup())
                    return "Not in 'input' group. Log out and back in.";
                return "ydotoold could not start.";
            }

            System.Threading.Thread.Sleep(400);
        }

        if (!IsYdotooldRunning())
            return "ydotoold is not running.";

        var socket = FindYdotoolSocket();
        if (string.IsNullOrEmpty(socket))
            return "ydotoold socket not found.";

        var err = RunProcess("ydotool", new[] { "type", text }, ydotoolSocket: socket);
        if (err != null)
            return $"ydotool failed: {err}";
        return null;
    }

    private static bool CanAccessUinput()
    {
        try
        {
            using var fs = new FileStream("/dev/uinput", FileMode.Open, FileAccess.Write);
            return true;
        }
        catch
        {
            return false;
        }
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

    private static bool IsInInputGroup()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "groups",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(1000);
            var output = process.StandardOutput.ReadToEnd();
            return output.Contains("input");
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveStaleSockets()
    {
        var candidates = new[]
        {
            Path.Combine("/tmp", ".ydotool_socket"),
        };

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtimeDir))
        {
            candidates = candidates.Append(Path.Combine(runtimeDir, ".ydotool_socket")).ToArray();
        }

        foreach (var candidate in candidates)
        {
            try { File.Delete(candidate); } catch { }
        }
    }

    private static string? FindYdotoolSocket()
    {
        var envSocket = Environment.GetEnvironmentVariable("YDOTOOL_SOCKET");
        if (!string.IsNullOrEmpty(envSocket) && File.Exists(envSocket))
            return envSocket;

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
        // Try direct launch first
        if (LaunchYdotooldDirect("ydotoold"))
            return true;

        Console.WriteLine("[Keyboard] ydotoold direct failed — trying via sg input...");

        // Try with sg to pick up the input group
        if (LaunchYdotooldViaSg())
            return true;

        return false;
    }

    private static bool LaunchYdotooldDirect(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            System.Threading.Thread.Sleep(300);
            if (process.HasExited)
            {
                var err = process.StandardError.ReadToEnd();
                Console.WriteLine($"[Keyboard] {command} exited: {err}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Keyboard] {command} launch failed: {ex.Message}");
            return false;
        }
    }

    private static bool LaunchYdotooldViaSg()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"sg input -c 'ydotoold &>/dev/null &'\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            process.WaitForExit(5000);

            System.Threading.Thread.Sleep(500);
            var running = IsYdotooldRunning();
            Console.WriteLine($"[Keyboard] sg input launch: ydotoold running={running}");
            return running;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Keyboard] sg input launch failed: {ex.Message}");
            return false;
        }
    }
}
