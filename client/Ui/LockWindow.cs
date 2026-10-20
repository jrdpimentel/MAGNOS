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

class LockWindow : Window
{
    public event Func<string, string, Task>? LoginRequested;
    public event Func<string, string, string, Task>? RegisterRequested;            // full name, username, password
    public event Func<string, string, string, Task<string>>? ChangePasswordRequested;
    public event Func<string, Task<bool>>? ExitRequested;
    public event Action? Activity;

    readonly TextBox _u = new(), _rn = new(), _ru = new();
    readonly PasswordBox _p = new(), _rp = new(), _rc = new();
    readonly TextBlock _status = new() { Foreground = Brushes.Orange, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    readonly TextBlock _count = new() { Foreground = Brushes.White, FontSize = 16, Margin = new Thickness(0, 22, 24, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    readonly StackPanel _loginPanel = new(), _regPanel = new();
    bool _allow;
    KeyBlocker? _blocker;

    public LockWindow(string pcName, bool canRegister, bool test = false, string? wallpaper = null)
    {
        SetWallpaper(wallpaper);
        if (test)
        {
            Title = "MAGNOS client (TEST MODE)"; Width = 560; Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            KeyDown += (_, e) => { if (e.Key == Key.Q && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) Environment.Exit(0); };
        }
        else
        {
            WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false;
            Deactivated += (_, _) => { if (!Dialog.IsOpen) { Topmost = true; Activate(); } };
            Loaded += (_, _) => _blocker = new KeyBlocker();
            PreviewKeyDown += async (_, e) =>
            {
                if (e.Key == Key.F12 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    var v = Dialog.Ask(this, "Staff exit", ("Admin key:", true));
                    if (v != null && ExitRequested != null && !await ExitRequested(v[0])) SetStatus("Wrong admin key, or server unreachable.");
                }
            };
        }
        PreviewMouseMove += (_, _) => Activity?.Invoke();
        PreviewKeyDown += (_, e) =>
        {
            Activity?.Invoke();
            if (e.Key == Key.Enter) { if (_regPanel.Visibility == Visibility.Visible) DoRegister(); else DoLogin(); e.Handled = true; }
        };

        // login panel
        AddField(_loginPanel, "Username", _u); AddField(_loginPanel, "Password", _p);
        _loginPanel.Children.Add(PrimaryButton("Log in", (_, _) => DoLogin()));
        if (canRegister) _loginPanel.Children.Add(Link("Create account", (_, _) => ShowRegister()));
        _loginPanel.Children.Add(Link("Change password", (_, _) => DoChangePassword()));

        // register panel
        AddField(_regPanel, "Full name", _rn); AddField(_regPanel, "Username", _ru);
        AddField(_regPanel, "Password", _rp); AddField(_regPanel, "Confirm password", _rc);
        _regPanel.Children.Add(PrimaryButton("Register", (_, _) => DoRegister()));
        _regPanel.Children.Add(Link("Already have an account? Log in", (_, _) => ShowLogin()));

        var stack = new StackPanel { Width = 300 };
        stack.Children.Add(_title); stack.Children.Add(_loginPanel); stack.Children.Add(_regPanel); stack.Children.Add(_status);
        stack.Children.Add(new TextBlock { Text = "While you use this PC, staff can see the active program and window title.", Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 9, 14, 30)), CornerRadius = new CornerRadius(14), Padding = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = stack
        };
        var root = new Grid();
        root.Children.Add(new TextBlock { Text = pcName, Foreground = Brushes.White, FontSize = 44, FontWeight = FontWeights.Bold, Margin = new Thickness(24, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        root.Children.Add(_count);
        root.Children.Add(card);
        Content = root;
        ShowLogin();
        Loaded += (_, _) => _u.Focus();
    }

    static void AddField(Panel p, string label, Control c)
    {
        p.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 3) });
        c.Height = 34; c.FontSize = 16; c.Padding = new Thickness(6, 3, 6, 3);
        p.Children.Add(c);
    }

    static Button PrimaryButton(string text, RoutedEventHandler h)
    {
        var b = new Button { Content = text, Height = 38, Margin = new Thickness(0, 16, 0, 0), Background = new SolidColorBrush(Color.FromRgb(47, 109, 246)), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
        b.Click += h; return b;
    }

    static Button Link(string text, RoutedEventHandler h)
    {
        var b = new Button { Content = text, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)), Cursor = Cursors.Hand, Margin = new Thickness(0, 8, 0, 0) };
        b.Click += h; return b;
    }

    public void ShowLogin(string? username = null)
    {
        _title.Text = "Member Login"; _regPanel.Visibility = Visibility.Collapsed; _loginPanel.Visibility = Visibility.Visible;
        if (username != null) _u.Text = username;
        _p.Clear(); _u.Focus();
    }

    void ShowRegister()
    {
        _title.Text = "Register Account"; _loginPanel.Visibility = Visibility.Collapsed; _regPanel.Visibility = Visibility.Visible;
        _status.Text = ""; _rn.Focus();
    }

    async void DoLogin()
    {
        if (LoginRequested == null) return;
        await LoginRequested(_u.Text, _p.Password); _p.Clear();
    }

    async void DoRegister()
    {
        if (RegisterRequested == null) return;
        if (_rp.Password != _rc.Password) { SetStatus("Passwords do not match."); return; }
        await RegisterRequested(_rn.Text, _ru.Text, _rp.Password);
        _rp.Clear(); _rc.Clear();
    }

    async void DoChangePassword()
    {
        var v = Dialog.Ask(this, "Change password", ("Username", false), ("Old password", true), ("New password", true));
        if (v == null || ChangePasswordRequested == null) return;
        SetStatus(await ChangePasswordRequested(v[0], v[1], v[2]));
    }

    public void SetWallpaper(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path!); bmp.EndInit(); bmp.Freeze();
                Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                return;
            }
        }
        catch { }
        Background = new LinearGradientBrush(Color.FromRgb(11, 18, 40), Color.FromRgb(58, 28, 110), 45);
    }

    public void SetStatus(string s) => _status.Text = s;
    public void SetCountdown(string s) => _count.Text = s;
    public void AllowClose() { _allow = true; Close(); }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_allow) e.Cancel = true; }
    protected override void OnClosed(EventArgs e) { _blocker?.Dispose(); base.OnClosed(e); }
}

