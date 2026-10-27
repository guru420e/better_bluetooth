using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using BluetoothRectifier.Core.Models;

namespace BluetoothRectifier.UI.Theme;

/// <summary>
/// Central theme colors, fonts, drawing utilities, and button factories
/// for the dark overlay interface.
/// </summary>
public static class AppTheme
{
    // ── Color Tokens ───────────────────────────────────────────────────────
    public static readonly Color Bg          = Color.FromArgb(0x14, 0x16, 0x1A);
    public static readonly Color CardBg      = Color.FromArgb(0x1C, 0x1F, 0x26);
    public static readonly Color Border      = Color.FromArgb(0x2A, 0x2E, 0x37);
    public static readonly Color Teal        = Color.FromArgb(0x3D, 0xD6, 0xB0);
    public static readonly Color TealDark    = Color.FromArgb(0x26, 0x9E, 0x82);
    public static readonly Color TealText    = Color.FromArgb(0x0E, 0x10, 0x14);
    public static readonly Color Danger      = Color.FromArgb(0xFF, 0x8A, 0x8A);
    public static readonly Color Warning     = Color.FromArgb(0xFF, 0xB8, 0x6C);
    public static readonly Color TextPrim    = Color.FromArgb(0xF0, 0xF2, 0xF5);
    public static readonly Color TextSec     = Color.FromArgb(0x8A, 0x8F, 0x9E);
    public static readonly Color SecLabel    = Color.FromArgb(0x5A, 0x5F, 0x6E);
    public static readonly Color Connected   = Color.FromArgb(0x3D, 0xD6, 0xB0);
    public static readonly Color ChipBg      = Color.FromArgb(0x1C, 0x1F, 0x26);
    public static readonly Color BtnGray     = Color.FromArgb(0x2A, 0x2E, 0x37);
    public static readonly Color ScrollThumb = Color.FromArgb(0x35, 0x3B, 0x47);

    // ── Fonts ──────────────────────────────────────────────────────────────
    public static readonly Font FontTitle   = new("Segoe UI", 17f, FontStyle.Bold);
    public static readonly Font FontStatus  = new("Segoe UI",  8.5f);
    public static readonly Font FontSection = new("Segoe UI",  7.5f, FontStyle.Bold);
    public static readonly Font FontDevName = new("Segoe UI", 10f,  FontStyle.Bold);
    public static readonly Font FontDevSub  = new("Segoe UI",  8.5f);
    public static readonly Font FontBtn     = new("Segoe UI",  8.5f, FontStyle.Bold);
    public static readonly Font FontIcon    = new("Segoe UI Emoji", 14f);
    public static readonly Font FontChip    = new("Segoe UI",  8f,  FontStyle.Bold);
    public static readonly Font FontFooter  = new("Segoe UI",  9f);

    // ── Drawing Utilities ──────────────────────────────────────────────────
    public static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X,         r.Y,          d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        path.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t),
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    public static void EnableDoubleBuffer(Control c) =>
        typeof(Control).GetProperty(
                "DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance)?
            .SetValue(c, true);

    public static string GetDeviceEmoji(BluetoothDeviceInfo dev)
    {
        if (dev.CodMajor == 5 || dev.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
            dev.Name.Contains("Gamepad", StringComparison.OrdinalIgnoreCase))
            return "🎮";

        if (dev.CodMajor == 4 || dev.Name.Contains("Buds", StringComparison.OrdinalIgnoreCase) ||
            dev.Name.Contains("Pods", StringComparison.OrdinalIgnoreCase) ||
            dev.Name.Contains("Drum", StringComparison.OrdinalIgnoreCase) ||
            dev.Name.Contains("Rockerz", StringComparison.OrdinalIgnoreCase))
            return "🎧";

        return "📶";
    }

    // ── Button Factories ───────────────────────────────────────────────────
    public static Button CreateButton(string text, Color bg, Color fg, int w, int h)
    {
        var btn = new Button
        {
            Text      = text,
            Font      = FontBtn,
            Size      = new Size(w, h),
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        btn.FlatAppearance.BorderSize        = 0;
        btn.FlatAppearance.MouseOverBackColor = Lerp(bg, Color.White, 0.15f);
        btn.FlatAppearance.MouseDownBackColor = Lerp(bg, Color.Black, 0.20f);
        return btn;
    }

    public static Button CreateIconButton(string text, Color fg, int w, int h)
    {
        var btn = new Button
        {
            Text      = text,
            Font      = FontBtn,
            Size      = new Size(w, h),
            BackColor = CardBg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        btn.FlatAppearance.BorderSize        = 1;
        btn.FlatAppearance.BorderColor       = Border;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x22, 0x26, 0x2F);
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x28, 0x2C, 0x36);
        return btn;
    }
}
