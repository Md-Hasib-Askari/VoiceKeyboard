using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VoiceKeyboard.Services;

/// <summary>
/// Manages a self-contained Python virtual environment using uv.
/// Creates a venv at ~/.local/share/voice-keyboard/venv with the required
/// packages (faster-whisper, webrtcvad), independent of conda/pyenv.
/// </summary>
public static class PythonEnvironmentService
{
    private static readonly string DataDir =
        Environment.GetEnvironmentVariable("XDG_DATA_HOME")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "voice-keyboard"
        );

    private static readonly string VenvDir = Path.Combine(DataDir, "venv");
    private static readonly string VenvPython = Path.Combine(VenvDir, "bin", "python3");
    private static readonly string RequirementsPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "requirements.txt"
    );

    public static string VenvPythonPath => VenvPython;
    public static bool VenvExists => File.Exists(VenvPython);

    public static async Task<string> EnsureEnvironmentAsync(Action<string>? onStatus = null)
    {
        // Step 1: Ensure uv is available
        var uvPath = await EnsureUvInstalledAsync(onStatus);

        // Step 2: Create venv if missing
        if (!VenvExists)
        {
            onStatus?.Invoke("📦 Creating Python virtual environment...");
            Console.WriteLine("[PythonEnv] Creating venv...");
            await RunProcessAsync(uvPath, $"venv \"{VenvDir}\" --python 3.10", 60);
            Console.WriteLine("[PythonEnv] Venv created");
        }

        // Step 3: Install packages (skip if already present)
        bool torchInstalled = await CheckPackageInstalledAsync(uvPath, VenvPython, "torch");
        bool whisperInstalled = await CheckPackageInstalledAsync(uvPath, VenvPython, "faster-whisper");
        bool vadInstalled = await CheckPackageInstalledAsync(uvPath, VenvPython, "webrtcvad");

        if (!torchInstalled)
        {
            onStatus?.Invoke("📦 Installing PyTorch with CUDA (large download, ~2.5GB)...");
            Console.WriteLine("[PythonEnv] Installing torch (CUDA)...");
            await RunProcessAsync(
                uvPath,
                $"pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu124 --python \"{VenvPython}\"",
                600
            );
        }
        else
        {
            Console.WriteLine("[PythonEnv] torch already installed, skipping");
        }

        if (!whisperInstalled || !vadInstalled)
        {
            onStatus?.Invoke("📦 Installing Python packages...");
            Console.WriteLine("[PythonEnv] Installing packages...");
            await RunProcessAsync(
                uvPath,
                $"pip install -r \"{RequirementsPath}\" --python \"{VenvPython}\"",
                120
            );
            Console.WriteLine("[PythonEnv] Packages installed");
        }
        else
        {
            Console.WriteLine("[PythonEnv] packages already installed, skipping");
            onStatus?.Invoke("✅ Python environment ready");
        }

        return VenvPython;
    }

    public static async Task<string> EnsureUvInstalledAsync(Action<string>? onStatus = null)
    {
        // Try to find uv in PATH
        var uvPath = await WhichAsync("uv");
        if (uvPath != null)
        {
            Console.WriteLine($"[PythonEnv] Found uv: {uvPath}");
            return uvPath;
        }

        // Try common locations
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "bin",
                "uv"
            ),
            "/usr/local/bin/uv",
            "/usr/bin/uv",
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                Console.WriteLine($"[PythonEnv] Found uv: {candidate}");
                return candidate;
            }
        }

        // Install uv via official installer
        onStatus?.Invoke("📥 Installing uv package manager...");
        Console.WriteLine("[PythonEnv] Installing uv...");

        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = "-lc \"curl -LsSf https://astral.sh/uv/install.sh | sh\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(psi) ?? throw new Exception("Failed to start uv installer");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new Exception($"uv installation failed: {err}");
        }

        // Find newly installed uv
        uvPath =
            await WhichAsync("uv")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "bin",
                "uv"
            );

        if (!File.Exists(uvPath))
            throw new Exception("uv was installed but cannot be found");

        Console.WriteLine($"[PythonEnv] uv installed: {uvPath}");
        return uvPath;
    }

    /// <summary>
    /// Recreates the venv from scratch (useful for troubleshooting).
    /// </summary>
    public static async Task<string> ResetEnvironmentAsync(Action<string>? onStatus = null)
    {
        if (Directory.Exists(VenvDir))
        {
            try
            {
                Directory.Delete(VenvDir, recursive: true);
            }
            catch { }
        }

        return await EnsureEnvironmentAsync(onStatus);
    }

    private static async Task<string?> WhichAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-lc \"which {command}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null)
                return null;
            var output = await process.StandardOutput.ReadLineAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var path = output.Trim();
                if (File.Exists(path))
                    return path;
            }
        }
        catch { }
        return null;
    }

    private static async Task<bool> CheckPackageInstalledAsync(
        string uvPath,
        string venvPython,
        string package
    )
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = uvPath,
                Arguments = $"pip show {package} --python \"{venvPython}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunProcessAsync(string fileName, string arguments, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(psi) ?? throw new Exception($"Failed to start: {fileName} {arguments}");

        // Read stderr in background for logging
        _ = Task.Run(async () =>
        {
            try
            {
                var buf = new char[512];
                while (true)
                {
                    int read = await process.StandardError.ReadAsync(buf, 0, buf.Length);
                    if (read == 0)
                        break;
                    Console.Write($"[PythonEnv:err] {new string(buf, 0, read)}");
                }
            }
            catch { }
        });

        // Read stdout in background for logging
        _ = Task.Run(async () =>
        {
            try
            {
                var buf = new char[512];
                while (true)
                {
                    int read = await process.StandardOutput.ReadAsync(buf, 0, buf.Length);
                    if (read == 0)
                        break;
                    Console.Write($"[PythonEnv:out] {new string(buf, 0, read)}");
                }
            }
            catch { }
        });

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"Process failed (exit {process.ExitCode}): {fileName} {arguments}"
            );
        }
    }
}
