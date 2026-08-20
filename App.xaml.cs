using System.Windows;

namespace totem;

public partial class App : Application
{
    public static new MainWindow? MainWindow { get; private set; }
    public static Wpf.Ui.Controls.ContentDialogHost? DialogHost { get; set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ImageClipboard.PurgeLeftovers();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
