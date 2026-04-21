using BabelStudio.App.Views;
using Microsoft.UI.Xaml;

namespace BabelStudio.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (window is not null && e.Exception is Exception exception)
        {
            window.ReportUnhandledException(exception, "Unhandled UI exception");
            e.Handled = true;
        }
    }
}
