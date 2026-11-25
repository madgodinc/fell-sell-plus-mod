using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace FellSellPlus.Setup;

// One copy only: a second launch brings the open window to the front and exits.
public partial class App : Application
{
    static Mutex _one;
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    protected override void OnStartup(StartupEventArgs e)
    {
        // Elevated relaunches of the setup (--game=...) replace the first copy, which closes itself.
        bool relaunch = e.Args.Any(a => a.StartsWith("--game="));
        _one = new Mutex(true, "MGI.FellSellPlus.Setup", out bool first);
        if (!first && !relaunch)
        {
            var me = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(me.ProcessName).FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
            if (other != null) { ShowWindow(other.MainWindowHandle, 9); SetForegroundWindow(other.MainWindowHandle); }
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
