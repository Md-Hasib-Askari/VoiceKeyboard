using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VoiceKeyboard.Services;
using VoiceKeyboard.ViewModels;
using VoiceKeyboard.Views;

namespace VoiceKeyboard;

public class App : Application
{
    private MainViewModel? _viewModel;
    private GlobalHotkeyService? _hotkeyService;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainViewModel();

            var mainWindow = new MainWindow { DataContext = _viewModel };
            desktop.MainWindow = mainWindow;

            _hotkeyService = new GlobalHotkeyService(new Dictionary<string, Action>
            {
                ["toggle"] = () => mainWindow.ShowAndActivate(),
                ["start-stop"] = () => _viewModel.ToggleStartCommand.Execute(null),
            });
            _hotkeyService.Start();

            desktop.Exit += (_, _) => Cleanup();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Quit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void Cleanup()
    {
        _hotkeyService?.Dispose();
        _viewModel?.DisposeSync();

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pkill",
                Arguments = "-x ydotoold",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(2000);
        }
        catch { }

        try { File.Delete(GlobalHotkeyService.SocketPath); } catch { }
        try { File.Delete("/tmp/.ydotool_socket"); } catch { }
    }
}
