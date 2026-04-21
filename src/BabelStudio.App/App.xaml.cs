using BabelStudio.App.Views;
using Microsoft.UI.Xaml;

namespace BabelStudio.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
