using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using avalonia_terminal.ViewModels;
using avalonia_terminal.Views;

namespace avalonia_terminal;

public partial class App : Application
{
    public override void Initialize() { AvaloniaXamlLoader.Load(this); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
