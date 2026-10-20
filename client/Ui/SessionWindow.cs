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

class SessionWindow : Window
{
    public event Func<Task>? EndRequested;
    public event Func<Task>? ChangePasswordRequested;
    public event Func<Task>? ChatRequested;
    readonly SessionInfo _s;
    readonly TextBlock _label = new() { Foreground = new SolidColorBrush(Color.FromRgb(79, 70, 229)), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 2) };
    readonly TextBlock _big = new() { FontFamily = new FontFamily("Consolas"), FontSize = 32, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(55, 48, 163)), HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _started = new(), _plan = new(), _amount = new() { FontWeight = FontWeights.Bold };
    readonly TextBlock _sync = new() { FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };
    readonly StackPanel _details = new();
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _allow;

    public SessionWindow(SessionInfo s, string pc, bool canChangePw)
    {
        _s = s;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        Width = 280; SizeToContent = SizeToContent.Height;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 255));
        Left = SystemParameters.WorkArea.Right - 300; Top = 12;

        var head = new Grid { Background = new SolidColorBrush(Color.FromRgb(79, 70, 229)) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var who = new TextBlock { Text = s.Customer, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 6, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        var pcTxt = new TextBlock { Text = pc, Foreground = Brushes.White, Margin = new Thickness(6, 8, 6, 8) };
        var min = new Button { Content = "\u2013", Width = 26, Margin = new Thickness(2), Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        min.Click += (_, _) => _details.Visibility = _details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(pcTxt, 1); Grid.SetColumn(min, 2);
        head.Children.Add(who); head.Children.Add(pcTxt); head.Children.Add(min);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var info = new StackPanel { Margin = new Thickness(8) };
        info.Children.Add(_started); info.Children.Add(_plan); info.Children.Add(_amount);
        var box = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(10, 8, 10, 6), Background = Brushes.White, Child = info };

        _details.Children.Add(_label); _details.Children.Add(_big); _details.Children.Add(box); _details.Children.Add(_sync);
        _details.Children.Add(new TextBlock { Text = "Activity monitoring is on.", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
        if (canChangePw)
        {
            var pw = new Button { Content = "Change password", Height = 28, Margin = new Thickness(10, 4, 10, 0) };
            pw.Click += async (_, _) => { if (ChangePasswordRequested != null) await ChangePasswordRequested(); };
            _details.Children.Add(pw);
        }
        var chatBtn = new Button { Content = "Chat with staff", Height = 28, Margin = new Thickness(10, 4, 10, 0) };
        chatBtn.Click += async (_, _) => { if (ChatRequested != null) await ChatRequested(); };
        _details.Children.Add(chatBtn);
        var end = new Button { Content = "End Session / Logout", Height = 34, Margin = new Thickness(10, 6, 10, 10), Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)) };
        end.Click += async (_, _) => { if (EndRequested != null) await EndRequested(); };
        _details.Children.Add(end);

        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(_details);
        Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(79, 70, 229)), BorderThickness = new Thickness(1), Child = root };
        _t.Tick += (_, _) => Refresh(); _t.Start(); Refresh();
    }

    static string Fmt(double minutes)
    {
        var t = TimeSpan.FromMinutes(Math.Max(0, minutes));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    void Refresh()
    {
        var d = _s.Paused ? 0 : (DateTime.UtcNow - _s.SyncedAt).TotalMinutes;
        if (_s.Mode == "Open") { _label.Text = _s.Paused ? "PAUSED" : "OPEN TIME SESSION"; _big.Text = Fmt(_s.Elapsed + d); }
        else if (double.IsNaN(_s.Remaining)) { _label.Text = "SYNCING..."; _big.Text = "--:--:--"; }
        else { _label.Text = _s.Paused ? "PAUSED" : "TIME LEFT"; _big.Text = Fmt(_s.Remaining - d); }
        _started.Text = "Started at: " + _s.StartedAt.ToLocalTime().ToString("hh:mm tt");
        _plan.Text = _s.Mode == "Open" ? "Plan: Pay-as-you-go" : _s.Mode == "Fixed" ? "Plan: Prepaid time" : "Plan: Wallet (" + _s.Tier + ")";
        _amount.Text = "Total so far: \u20B1" + _s.Amount.ToString("0.00");
        _sync.Text = "Synced " + _s.SyncedAt.ToLocalTime().ToString("hh:mm:ss tt") + (_s.Paused ? " (paused)" : "");
    }

    public void AllowClose() { _t.Stop(); _allow = true; Close(); }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_allow) e.Cancel = true; }
}

