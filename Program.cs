using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GgpkColorizer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, "Local\\PoEModifierColorizer", out bool firstInstance);
        if (!firstInstance)
        {
            if (!ShowExistingWindow())
                MessageBox.Show("PoE Modifier Colorizer is already open. / 詞綴上色器已開啟。", "PoE Modifier Colorizer");
            return;
        }
        try
        {
            Application.Run(new MainForm(new AppController()));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "PoE Modifier Colorizer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool ShowExistingWindow()
    {
        using Process current = Process.GetCurrentProcess();
        foreach (Process candidate in Process.GetProcessesByName(current.ProcessName))
        {
            using (candidate)
            {
                try
                {
                    if (candidate.Id == current.Id || candidate.SessionId != current.SessionId)
                        continue;
                    IntPtr window = candidate.MainWindowHandle;
                    if (window == IntPtr.Zero)
                        continue;
                    bool restored = !IsIconic(window) || ShowWindowAsync(window, 9); // SW_RESTORE
                    if (restored && SetForegroundWindow(window))
                        return true;
                    var flash = new FlashWindowInfo
                    {
                        Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                        Window = window,
                        Flags = 14, // FLASHW_TRAY | FLASHW_TIMERNOFG
                    };
                    FlashWindowEx(ref flash);
                    return true;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    // The other process may have exited while locating its window.
                }
            }
        }
        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
