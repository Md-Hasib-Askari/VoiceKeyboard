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
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            if (DataContext is MainViewModel vm)
                vm.ToggleStartCommand.Execute(null);
            e.Handled = true;
        }
    }

    public void ShowAndActivate()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (!IsVisible)
            Show();
        Activate();
        Focus();
    }
}
