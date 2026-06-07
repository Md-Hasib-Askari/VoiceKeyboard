using Avalonia.Controls;
using Avalonia.Input;
using VoiceKeyboard.ViewModels;

namespace VoiceKeyboard.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsListening)
                    vm.ToggleStartCommand.Execute(null);
                else
                    vm.ToggleStartCommand.Execute(null);
            }
        }
        else if (e.Key == Key.F10)
        {
            if (DataContext is MainViewModel vm && vm.IsListening)
            {
                vm.TogglePauseCommand.Execute(null);
            }
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        // Could implement push-to-talk here (hold F9 to talk)
    }
}
