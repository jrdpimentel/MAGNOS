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

// Blocks Win key, Alt+Tab, Alt+Esc, Alt+F4, Alt+Space, Ctrl+Esc (and Ctrl+Shift+Esc) while alive.
class KeyBlocker : IDisposable
{
    delegate IntPtr Proc(int n, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KBD { public int vk, scan, flags, time; public IntPtr extra; }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    readonly Proc _proc;
    IntPtr _h;

    public KeyBlocker()
    {
        _proc = Callback;
        _h = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0);
    }

    IntPtr Callback(int n, IntPtr w, IntPtr l)
    {
        if (n >= 0)
        {
            int msg = (int)w;
            if (msg == 0x100 || msg == 0x104)
            {
                var k = Marshal.PtrToStructure<KBD>(l);
                bool alt = (k.flags & 0x20) != 0;
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool block = k.vk == 0x5B || k.vk == 0x5C || k.vk == 0x5D
                    || (alt && (k.vk == 0x09 || k.vk == 0x1B || k.vk == 0x73 || k.vk == 0x20))
                    || (ctrl && k.vk == 0x1B);
                if (block) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_h, n, w, l);
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) { UnhookWindowsHookEx(_h); _h = IntPtr.Zero; }
    }
}

