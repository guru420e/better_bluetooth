using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BluetoothRectifier;

/// <summary>
/// Custom ApplicationContext so the app lives in the tray even when
/// the overlay window is hidden. The app only exits when the user
/// chooses "Exit" from the tray menu.
/// </summary>
sealed class AppContext : ApplicationContext
{
    private readonly NotifyIcon       _tray;
    private readonly MainForm         _form;
    private readonly BluetoothService _service;
    private readonly AutoForgetStore  _store;
    private readonly AutoForgetEngine _engine;

    public AppContext()
    {
        _service = new BluetoothService();
        _store   = new AutoForgetStore();
        _engine  = new AutoForgetEngine(_service, _store);
        _form    = new MainForm(_service, _store, _engine);

        _tray = new NotifyIcon
        {
            Icon    = BuildTrayIcon(),
            Text    = "Bluetooth Rectifier",
            Visible = true,
        };

        _tray.ContextMenuStrip = BuildTrayMenu();

        // Left-click on the tray icon toggles the window
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleWindow();
        };

        // The MainForm fires this event when the registered hotkey is pressed
        _form.HotkeyPressed += ToggleWindow;

        // Auto-forget notification via tray balloon tip
        _engine.DeviceAutoForgotten += (dev, reason) =>
        {
            try
            {
                _tray.ShowBalloonTip(4000, "Device Auto-Forgotten",
                    $"Auto-forgot \"{dev.Name}\". Hold sync button to pair next time.",
                    ToolTipIcon.Info);
            }
            catch { }
        };
    }

    // ── Toggle ─────────────────────────────────────────────────────────────

    private void ToggleWindow()
    {
        if (_form.Visible)
            _form.HideWindow();
        else
            _form.ShowAtTray();
    }

    // ── Tray menu ──────────────────────────────────────────────────────────

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open Bluetooth Rectifier");
        openItem.Font = new Font(openItem.Font, FontStyle.Bold); // bold = default action
        openItem.Click += (_, _) => _form.ShowAtTray();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ExitApp()
    {
        _tray.Visible = false;  // remove tray icon before exit so it doesn't ghost
        _form.ForceClose();
        _engine.Dispose();
        _service.Dispose();
        Application.Exit();
    }

    // ── Tray icon ──────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a 32×32 teal circle with a dark "B" in the centre.
    /// No external .ico file needed.
    /// </summary>
    private static Icon BuildTrayIcon()
    {
        const int sz = 32;
        using var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Teal circle
        using (var brush = new SolidBrush(Color.FromArgb(0x3D, 0xD6, 0xB0)))
            g.FillEllipse(brush, 1, 1, sz - 2, sz - 2);

        // Dark "B" centred
        using var font  = new Font("Segoe UI", sz * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush2 = new SolidBrush(Color.FromArgb(0x0E, 0x10, 0x14));
        var size = g.MeasureString("B", font);
        g.DrawString("B", font, brush2,
            (sz - size.Width)  / 2f - 0.5f,
            (sz - size.Height) / 2f);

        // Clone the icon so we can safely call DestroyIcon on the HICON
        var hIcon = bmp.GetHicon();
        var icon  = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    // ── Cleanup ────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _form.Dispose();
            _engine.Dispose();
            _service.Dispose();
        }
        base.Dispose(disposing);
    }
}
