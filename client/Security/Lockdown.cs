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

// Disables Task Manager for the current Windows user while the client runs (real mode only).
static class Lockdown
{
    const string P = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public static void Apply()
    {
        try { using var k = Registry.CurrentUser.CreateSubKey(P); k.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord); } catch { }
    }
    public static void Restore()
    {
        try { using var k = Registry.CurrentUser.CreateSubKey(P); k.DeleteValue("DisableTaskMgr", false); } catch { }
    }
}

