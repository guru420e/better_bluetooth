using System;
using System.Drawing;
using System.Windows.Forms;
using BetterBluetooth.UI.Theme;

namespace BetterBluetooth.UI.Components;

/// <summary>
/// Top header panel (440x84) containing the title, status label, rules gear button,
/// scan toggle button, and borderless window dragging support.
/// </summary>
public sealed class HeaderPanel : Panel
{
    private readonly Label  _titleLabel;
    private readonly Label  _statusLabel;
    private readonly Button _rulesBtn;
    private readonly Button _scanBtn;

    public event Action? ScanClicked;
    public event Action? SettingsClicked;
    public event Action<Point>? DragWindowMoved;

    private bool  _dragging;
    private Point _dragOriginScreen;

    public HeaderPanel()
    {
        Location  = Point.Empty;
        Size      = new Size(440, 84);
        BackColor = AppTheme.Bg;

        _titleLabel = new Label
        {
            Text      = "Better Bluetooth",
            Font      = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(16, 14),
            Cursor    = Cursors.SizeAll,
        };

        _statusLabel = new Label
        {
            Text      = "Radio on  ·  Idle",
            Font      = AppTheme.FontStatus,
            ForeColor = AppTheme.TextSec,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(18, 50),
        };

        _rulesBtn = AppTheme.CreateIconButton("⚙", AppTheme.TextSec, w: 36, h: 36);
        _rulesBtn.Location = new Point(292, 24);
        _rulesBtn.Click   += (_, _) => SettingsClicked?.Invoke();

        _scanBtn = AppTheme.CreateButton("↺  Scan", AppTheme.Teal, AppTheme.TealText, w: 88, h: 36);
        _scanBtn.Location = new Point(336, 24);
        _scanBtn.Click   += (_, _) => ScanClicked?.Invoke();

        Controls.AddRange(new Control[] { _titleLabel, _statusLabel, _rulesBtn, _scanBtn });

        WireDrag(this);
        WireDrag(_titleLabel);
        WireDrag(_statusLabel);
    }

    public void SetScanning(bool isScanning)
    {
        if (isScanning)
        {
            _scanBtn.Text      = "⏹  Stop";
            _scanBtn.BackColor = AppTheme.BtnGray;
            _scanBtn.ForeColor = AppTheme.TextPrim;
            _statusLabel.Text  = "Scanning for devices...";
        }
        else
        {
            _scanBtn.Text      = "↺  Scan";
            _scanBtn.BackColor = AppTheme.Teal;
            _scanBtn.ForeColor = AppTheme.TealText;
            _statusLabel.Text  = "Radio on  ·  Idle";
        }
    }

    public void SetStatusText(string text)
    {
        _statusLabel.Text = text;
    }

    private void WireDrag(Control ctrl)
    {
        ctrl.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOriginScreen = ((Control)s!).PointToScreen(e.Location);
            }
        };

        ctrl.MouseMove += (s, e) =>
        {
            if (_dragging)
            {
                var now = ((Control)s!).PointToScreen(e.Location);
                int dx = now.X - _dragOriginScreen.X;
                int dy = now.Y - _dragOriginScreen.Y;
                _dragOriginScreen = now;
                DragWindowMoved?.Invoke(new Point(dx, dy));
            }
        };

        ctrl.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _dragging = false;
        };
    }
}
