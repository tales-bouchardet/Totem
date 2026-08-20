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
        using var singleInstance = new Mutex(true, "totem.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox(IntPtr.Zero, "O Totem já está em execução.", "Totem", 0x40);
            return;
        }

        var app = new App();
        app.Run();
    }
}
