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

record StartResp(string token, double balanceMinutes, string username);

record StartMsg(string token, string mode, string customer, string tier, string startedAt, int purchasedMinutes);

record EndResp(decimal amount);

class SessionInfo
{
    public string Token = "", Mode = "Open", Customer = "", Tier = "";
    public DateTime StartedAt = DateTime.UtcNow;
    public int Purchased;
    public double Remaining = -1, Elapsed;      // minutes; Remaining NaN = not synced yet
    public decimal Amount;
    public bool Paused;
    public DateTime SyncedAt = DateTime.UtcNow;
}

class InfiniteRetry : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext c) => TimeSpan.FromSeconds(5);
}

