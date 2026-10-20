using System.IO;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;

namespace MAGNOS.Client;

static class Fg
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    public static (string app, string title) Get()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return ("", "");
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        GetWindowThreadProcessId(h, out var pid);
        var app = "";
        try { app = Process.GetProcessById((int)pid).ProcessName; } catch { }
        return (app, sb.ToString());
    }
}

