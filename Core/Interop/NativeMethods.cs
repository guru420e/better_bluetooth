using System;
using System.Runtime.InteropServices;

namespace BluetoothRectifier.Core.Interop;

/// <summary>
/// Win32 P/Invoke declarations for window styling, hotkey, and console redirection.
/// </summary>
public static class NativeMethods
{
    // ── Global Hotkey ──────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT   = 0x0004;
    public const int VK_B        = 0x42;
    public const int WM_HOTKEY   = 0x0312;
    public const int HOTKEY_ID   = 0xBEEF;

    // ── DWM Windows 11 Rounded Corners ─────────────────────────────────────
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND                   = 2;

    // ── GDI Icon Cleanup ───────────────────────────────────────────────────
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── Console Attachment for CLI Mode ────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    public const int ATTACH_PARENT_PROCESS = -1;
    public const int STD_OUTPUT_HANDLE     = -11;
    public const int STD_ERROR_HANDLE      = -12;
    public const int STD_INPUT_HANDLE      = -10;
    public const uint GENERIC_READ         = 0x80000000;
    public const uint GENERIC_WRITE        = 0x40000000;
    public const uint FILE_SHARE_READ      = 0x00000001;
    public const uint FILE_SHARE_WRITE     = 0x00000002;
    public const uint OPEN_EXISTING        = 3;
}
