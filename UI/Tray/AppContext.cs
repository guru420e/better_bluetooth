using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using BluetoothRectifier.Core.Services;
using BluetoothRectifier.UI.Forms;
using BluetoothRectifier.UI.Theme;

namespace BluetoothRectifier.UI.Tray;

/// <summary>
/// Custom ApplicationContext so the app lives in the system tray even when
/// the overlay window is hidden. The app only exits when the user
/// chooses "Exit" from the tray menu.
/// </summary>
public sealed class AppContext : ApplicationContext
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
        _engine.DeviceAutoForgotten += (dev, _) =>
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
        _tray.Visible = false; // remove tray icon before exit so it doesn't ghost
        _form.ForceClose();
        _engine.Dispose();
        _service.Dispose();
        Application.Exit();
    }

    // ── Tray icon ──────────────────────────────────────────────────────────

    private static Icon BuildTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(Color.FromArgb(240, 20, 22, 26));
            g.FillEllipse(brush, 1, 1, size - 2, size - 2);

            using var pen = new Pen(AppTheme.Teal, 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap   = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            float cx = size / 2f;
            float top = 7f;
            float bot = 25f;
            float wing = 7f;

            // Vertical spine
            g.DrawLine(pen, cx, top, cx, bot);
            // Upper diamond: center-low -> right-high -> spine-top
            g.DrawLine(pen, cx, 16f, cx + wing, 11.5f);
            g.DrawLine(pen, cx + wing, 11.5f, cx, top);
            // Lower diamond: spine-top -> right-low -> spine-bot
            g.DrawLine(pen, cx, top, cx + wing, 20.5f);
            g.DrawLine(pen, cx + wing, 20.5f, cx, bot);
            // Lower return to center-high
            g.DrawLine(pen, cx, bot, cx + wing, 11.5f);
            // Cross to left wings
            g.DrawLine(pen, cx, 16f, cx - wing * 0.65f, 11.5f);
            g.DrawLine(pen, cx, 16f, cx - wing * 0.65f, 20.5f);
        }

        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        return icon;
    }

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
