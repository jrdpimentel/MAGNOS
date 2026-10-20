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

class ChatWindow : Window
{
    public event Func<string, Task>? SendRequested;
    readonly StackPanel _list = new();
    readonly ScrollViewer _sv;
    readonly TextBox _box = new() { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };

    public ChatWindow()
    {
        Title = "Chat with staff"; Width = 320; Height = 380; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - 340; Top = 200;
        _sv = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var send = new Button { Content = "Send", Width = 64, Margin = new Thickness(6, 0, 0, 0) };
        send.Click += async (_, _) => await Send();
        _box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Send(); };
        var bottom = new DockPanel { Margin = new Thickness(6) };
        DockPanel.SetDock(send, Dock.Right);
        bottom.Children.Add(send); bottom.Children.Add(_box);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bottom, 1);
        root.Children.Add(_sv); root.Children.Add(bottom);
        Content = root;
        Loaded += (_, _) => _box.Focus();
    }

    async Task Send()
    {
        var t = _box.Text.Trim();
        if (t.Length == 0) return;
        _box.Clear(); Add("You", t);
        if (SendRequested != null) await SendRequested(t);
    }

    public void Add(string who, string text)
    {
        _list.Children.Add(new TextBlock { Text = who + ": " + text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 4) });
        _sv.ScrollToEnd();
    }

    public void Reset() { _list.Children.Clear(); Hide(); }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { e.Cancel = true; Hide(); }
}

