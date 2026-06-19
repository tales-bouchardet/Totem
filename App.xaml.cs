using Microsoft.UI.Xaml;

namespace totem;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ImageClipboard.PurgeLeftovers(); // remove temporários de sessões anteriores
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
