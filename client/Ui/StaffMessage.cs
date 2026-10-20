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

class StaffMessage : Window
{
    static int _n;
    public StaffMessage(string text)
    {
        Title = "Message from staff"; Width = 340; SizeToContent = SizeToContent.Height;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (SystemParameters.WorkArea.Width - 340) / 2 + (_n % 5) * 30;
        Top = 120 + (_n % 5) * 30; _n++;
        var ok = new Button { Content = "OK", Margin = new Thickness(0, 12, 0, 0), Height = 28 };
        ok.Click += (_, _) => Close();
        Content = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, ok } };
    }
}

