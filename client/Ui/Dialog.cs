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

static class Dialog
{
    public static bool IsOpen;
    public static string[]? Ask(Window? owner, string title, params (string label, bool secret)[] fields)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var boxes = new List<Control>();
        foreach (var f in fields)
        {
            panel.Children.Add(new TextBlock { Text = f.label, Margin = new Thickness(0, 6, 0, 2) });
            Control c = f.secret ? new PasswordBox() : new TextBox();
            boxes.Add(c); panel.Children.Add(c);
        }
        var ok = new Button { Content = "OK", IsDefault = true, Height = 30, Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(ok);
        var w = new Window
        {
            Title = title, Width = 320, SizeToContent = SizeToContent.Height, Topmost = true, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Content = panel,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        if (owner != null) w.Owner = owner;
        string[]? result = null;
        ok.Click += (_, _) => { result = boxes.Select(b => b is PasswordBox p ? p.Password : ((TextBox)b).Text).ToArray(); w.Close(); };
        w.Loaded += (_, _) => boxes[0].Focus();
        IsOpen = true; w.ShowDialog(); IsOpen = false;
        return result;
    }
}

