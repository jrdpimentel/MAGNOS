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

class Controller
{
    static string Cfg(string k) => ConfigurationManager.AppSettings[k] ?? "";
    static bool Flag(string k) => string.Equals(Cfg(k), "True", StringComparison.OrdinalIgnoreCase);
    static void UI(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

    string _server = Cfg("ServerUrl").TrimEnd('/');
    readonly string _key = Cfg("ClientKey");
    readonly int _idleShutdown = int.TryParse(Cfg("AutoShutDownInterval"), out var n) ? n : 0;
    readonly bool _canRegister = !string.Equals(Cfg("AccountCreationAllowed"), "False", StringComparison.OrdinalIgnoreCase);
    readonly bool _test = Flag("TestMode");
    readonly bool _trial = Flag("LockdownTrial");   // real lock-down, but the client quits itself after 2 minutes
    HttpClient _http = new();
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(1) };
    string _pcName = "";
    int _idleSeconds;
    SessionInfo? _s;
    LockWindow? _lock;
    SessionWindow? _win;
    HubConnection? _hub;
    ChatWindow? _chat;

    public void Start()
    {
        _pcName = ResolveName();
        if (string.IsNullOrWhiteSpace(_server)) _server = "http://127.0.0.1:5000";
        _http = NewHttp(_server);
        if (!_test) Lockdown.Apply();
        if (_trial)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
            t.Tick += (_, _) => { Lockdown.Restore(); Environment.Exit(0); };
            t.Start();
        }
        _idle.Tick += (_, _) =>
        {
            if (_s != null || _trial || _test || _idleShutdown <= 0) { _lock?.SetCountdown(""); return; }
            _idleSeconds++;
            var left = Math.Max(0, _idleShutdown - _idleSeconds);
            _lock?.SetCountdown($"Auto-shutdown in {left / 60:00}:{left % 60:00}");
            if (left == 0) Run("shutdown", "/s /t 0");
        };
        _idle.Start();
        var act = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        act.Tick += async (_, _) => await ReportActivity();
        act.Start();
        ShowLock();
        _ = ConnectHub();
    }

    static string ResolveName()
    {
        var n = Cfg("ClientName").Trim();
        if (n.Length > 0) return n.Replace(' ', '-');
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(i => i.OperationalStatus == OperationalStatus.Up))
            foreach (var ua in ni.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 127 || b[0] == 169) continue;
                return $"PC-{Math.Max(1, b[3] - 100):D2}";
            }
        return Environment.MachineName;
    }

    HttpClient NewHttp(string url)
    {
        var h = new HttpClient { BaseAddress = new Uri(url) };
        h.DefaultRequestHeaders.Add("X-Client-Key", _key);
        return h;
    }

    void SetServer(string url) { _server = url; _http = NewHttp(url); }

    // Looks for the server on the local network (UDP broadcast). Returns e.g. http://192.168.1.6:5000
    static async Task<string?> Discover()
    {
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            var msg = Encoding.ASCII.GetBytes("MAGNOS?");
            await udp.SendAsync(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, 5001));
            var task = udp.ReceiveAsync();
            if (await Task.WhenAny(task, Task.Delay(2500)) != task) return null;
            var r = task.Result;
            var text = Encoding.ASCII.GetString(r.Buffer);
            return text.StartsWith("MAGNOS|") ? $"http://{r.RemoteEndPoint.Address}:{text[9..]}" : null;
        }
        catch { return null; }
    }

    // ----- wallpaper -----
    static string WallpaperPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MAGNOS", "wallpaper.img");

    async Task SyncWallpaper()
    {
        try
        {
            var r = await _http.GetAsync("/api/wallpaper");
            Directory.CreateDirectory(Path.GetDirectoryName(WallpaperPath)!);
            if (r.IsSuccessStatusCode) await File.WriteAllBytesAsync(WallpaperPath, await r.Content.ReadAsByteArrayAsync());
            else if (r.StatusCode == HttpStatusCode.NotFound && File.Exists(WallpaperPath)) File.Delete(WallpaperPath);
            else return;
            UI(() => _lock?.SetWallpaper(File.Exists(WallpaperPath) ? WallpaperPath : null));
        }
        catch { }
    }

    void BuildHub()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl($"{_server}/hub?pc={Uri.EscapeDataString(_pcName)}&key={Uri.EscapeDataString(_key)}")
            .WithAutomaticReconnect(new InfiniteRetry()).Build();
        _hub.On<string, string>("Command", (cmd, text) => UI(() => OnCommand(cmd, text)));
        _hub.On<StartMsg>("StartSession", m => UI(() => BeginSession(m)));
        _hub.On<double, double, double, bool>("State", (rem, el, amt, paused) => UI(() => ApplyState(rem, el, amt, paused)));
        _hub.On("Wallpaper", () => { _ = SyncWallpaper(); });
        _hub.On<string>("Chat", text => UI(() => ReceiveChat(text)));
        _hub.Reconnecting += ex => { _ = WatchReconnect(); return Task.CompletedTask; };
    }

    // If the connection stays down, the server may have a new address: look for it.
    async Task WatchReconnect()
    {
        await Task.Delay(20000);
        if (_hub == null || _hub.State == HubConnectionState.Connected) return;
        var url = await Discover();
        if (url != null && url != _server)
        {
            try { await _hub.StopAsync(); } catch { }
            SetServer(url);
            await ConnectHub();
        }
    }

    async Task ConnectHub()
    {
        if (string.IsNullOrWhiteSpace(Cfg("ServerUrl")))
        {
            var u = await Discover();
            if (u != null) SetServer(u);
        }
        int fails = 0;
        while (true)
        {
            try { BuildHub(); await _hub!.StartAsync(); _ = SyncWallpaper(); return; }
            catch
            {
                if (++fails >= 3)
                {
                    fails = 0;
                    var u = await Discover();
                    if (u != null && u != _server) SetServer(u);
                }
                await Task.Delay(5000);
            }
        }
    }

    void OnCommand(string cmd, string text)
    {
        switch (cmd)
        {
            case "Lock": EndLocal(); if (text.Length > 0) _lock?.SetStatus(text); break;
            case "Shutdown": Run("shutdown", "/s /t 0"); break;
            case "Restart": Run("shutdown", "/r /t 0"); break;
            case "Message": new StaffMessage(text).Show(); break;
        }
    }

    static void Run(string file, string args) => Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false });

    // ----- sessions -----
    void BeginSession(StartMsg m)
    {
        var started = DateTime.TryParse(m.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;
        var s = new SessionInfo { Token = m.token, Mode = m.mode, Customer = m.customer, Tier = m.tier, StartedAt = started, Purchased = m.purchasedMinutes };
        s.Elapsed = Math.Max(0, (DateTime.UtcNow - started).TotalMinutes);
        s.Remaining = m.mode == "Open" ? -1 : m.mode == "Fixed" ? Math.Max(0, m.purchasedMinutes - s.Elapsed) : double.NaN;
        OpenWidget(s);
    }

    void ApplyState(double rem, double el, double amt, bool paused)
    {
        if (_s == null) return;
        _s.Elapsed = el; _s.Amount = (decimal)amt; _s.Paused = paused; _s.SyncedAt = DateTime.UtcNow;
        _s.Remaining = _s.Mode == "Open" ? -1 : rem;
    }

    void OpenWidget(SessionInfo s)
    {
        _s = s;
        _lock?.AllowClose(); _lock = null;
        _win?.AllowClose();
        _win = new SessionWindow(s, _pcName, s.Mode == "Wallet");
        _win.EndRequested += EndSession;
        _win.ChangePasswordRequested += ChangePasswordFromWidget;
        _win.ChatRequested += OpenChat;
        _win.Show();
    }

    async Task EndSession()
    {
        var t = _s?.Token; decimal? amt = null;
        if (t != null)
        {
            try
            {
                var r = await _http.PostAsJsonAsync("/api/session/end", new { token = t });
                if (r.IsSuccessStatusCode) amt = (await r.Content.ReadFromJsonAsync<EndResp>())?.amount ?? 0m;
            }
            catch { }
        }
        EndLocal();
        if (amt != null) _lock?.SetStatus("Session ended. Total: \u20B1" + amt.Value.ToString("0.00"));
    }

    void EndLocal()
    {
        _s = null;
        _win?.AllowClose(); _win = null;
        _chat?.Reset();
        if (_lock == null) ShowLock();
    }

    // ----- chat -----
    ChatWindow EnsureChat()
    {
        if (_chat == null)
        {
            _chat = new ChatWindow();
            _chat.SendRequested += async text =>
            {
                try { await _http.PostAsJsonAsync("/api/chat", new { pcName = _pcName, text }); }
                catch { _chat?.Add("System", "Could not send. Check the connection."); }
            };
        }
        return _chat;
    }

    void ReceiveChat(string text)
    {
        if (_s == null) return;          // chat is only shown during a session
        var c = EnsureChat(); c.Add("Staff", text); c.Show(); c.Activate();
    }

    Task OpenChat() { var c = EnsureChat(); c.Show(); c.Activate(); return Task.CompletedTask; }

    // ----- activity (active program + window title, only while a session is running) -----
    async Task ReportActivity()
    {
        if (_s == null) return;
        try
        {
            var (app, title) = Fg.Get();
            if (app.StartsWith("MAGNOS", StringComparison.OrdinalIgnoreCase)) return;
            await _http.PostAsJsonAsync("/api/activity", new { pcName = _pcName, app, title });
        }
        catch { }
    }

    // ----- lock screen -----
    void ShowLock()
    {
        _idleSeconds = 0;
        _lock = new LockWindow(_pcName, _canRegister, _test, File.Exists(WallpaperPath) ? WallpaperPath : null);
        _lock.LoginRequested += Login;
        _lock.RegisterRequested += Register;
        _lock.ChangePasswordRequested += ChangePassword;
        _lock.ExitRequested += TryExit;
        _lock.Activity += () => _idleSeconds = 0;
        _lock.Show();
    }

    // Staff exit: Ctrl+Shift+F12 on the lock screen, then the admin key.
    async Task<bool> TryExit(string adminKey)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/pcs");
            req.Headers.Add("X-Admin-Key", adminKey);
            var r = await _http.SendAsync(req);
            if (!r.IsSuccessStatusCode) return false;
            Lockdown.Restore();
            Environment.Exit(0);
            return true;
        }
        catch { return false; }
    }

    async Task Register(string fullName, string u, string p)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("/api/register", new { username = u, password = p, fullName });
            _lock?.SetStatus(r.IsSuccessStatusCode ? "Account created. You can log in now." : await r.Content.ReadAsStringAsync());
            if (r.IsSuccessStatusCode) _lock?.ShowLogin(u);
        }
        catch { _lock?.SetStatus("Cannot reach server."); }
    }

    async Task Login(string u, string p)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("/api/session/start", new { username = u, password = p, pcName = _pcName });
            if (!r.IsSuccessStatusCode) { _lock?.SetStatus(await r.Content.ReadAsStringAsync()); return; }
            var s = (await r.Content.ReadFromJsonAsync<StartResp>())!;
            OpenWidget(new SessionInfo { Token = s.token, Mode = "Wallet", Customer = s.username, Tier = "Member", Remaining = s.balanceMinutes });
        }
        catch { _lock?.SetStatus("Cannot reach server."); }
    }

    async Task<string> ChangePassword(string user, string oldPw, string newPw)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("/api/change-password", new { username = user, oldPassword = oldPw, newPassword = newPw });
            return r.IsSuccessStatusCode ? "Password changed." : await r.Content.ReadAsStringAsync();
        }
        catch { return "Cannot reach server."; }
    }

    async Task ChangePasswordFromWidget()
    {
        if (_s == null) return;
        var v = Dialog.Ask(_win, "Change password", ("Old password", true), ("New password", true));
        if (v == null) return;
        MessageBox.Show(await ChangePassword(_s.Customer, v[0], v[1]), "Change password");
    }
}

