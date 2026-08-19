using System.Runtime.InteropServices;
using System.Threading;

namespace totem;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static void Main()
    {
        // Only one Totem window at a time: two instances writing to the same
        // autosave cache file would race and could overwrite each other's data.
        // Held for the app's whole lifetime (App.Run blocks until exit).
        using var singleInstance = new Mutex(true, "aec.totem.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox(IntPtr.Zero, "O Totem já está em execução.", "Totem", 0x40 /* MB_ICONINFORMATION */);
            return;
        }

        var app = new App(); // App's constructor already calls InitializeComponent()
        app.Run();
    }
}
