using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BetterBluetooth.UI.Theme;

namespace BetterBluetooth.UI.Components;

/// <summary>
/// Top header panel (440x84) containing the title, status label,
/// Bluetooth adapter power toggle, Auto-Scan (with animated pulse), Manual Scan,
/// and borderless window dragging support.
/// </summary>
public sealed class HeaderPanel : Panel
{
    private readonly Label  _titleLabel;
    private readonly Label  _statusLabel;
    private readonly Button _btPowerBtn;
    private readonly Button _autoScanBtn;
    private readonly Button _manualScanBtn;

    public event Action? BluetoothToggleClicked;
    public event Action? AutoScanClicked;
    public event Action? ManualScanClicked;
    public event Action<Point>? DragWindowMoved;

    private bool  _dragging;
    private Point _dragOriginScreen;

    // State
    private bool  _isRadioOn = true;
    private bool  _isAutoScan = true;
    private bool  _isScanning = false;
    private int   _countdownSeconds = 10;
    private float _pulseAlpha = 1f;

    public HeaderPanel()
    {
        Location  = Point.Empty;
        Size      = new Size(440, 84);
        BackColor = AppTheme.Bg;

        _titleLabel = new Label
        {
            Text      = "Better Bluetooth",
            Font      = new Font("Segoe UI", 15.5f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(14, 16),
            Cursor    = Cursors.SizeAll,
        };

        _statusLabel = new Label
        {
            Text      = "Radio on · Auto-scanning (10s)",
            Font      = AppTheme.FontStatus,
            ForeColor = AppTheme.TextSec,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(16, 52),
        };

        // 1. Bluetooth Adapter Toggle (60x32)
        _btPowerBtn = AppTheme.CreateButton("ᛒ ON", Color.FromArgb(0x1B, 0x3A, 0x4B), Color.FromArgb(0x5A, 0xC8, 0xFA), 60, 32);
        _btPowerBtn.Location = new Point(188, 20);
        _btPowerBtn.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _btPowerBtn.Click += (_, _) => BluetoothToggleClicked?.Invoke();

        // 2. Auto Scan Button (108x32) with animated pulse
        _autoScanBtn = AppTheme.CreateButton("● Auto (10s)", AppTheme.Teal, AppTheme.TealText, 108, 32);
        _autoScanBtn.Location = new Point(254, 20);
        _autoScanBtn.Click += (_, _) => AutoScanClicked?.Invoke();

        // 3. Manual Scan Button (62x32)
        _manualScanBtn = AppTheme.CreateButton("Manual", AppTheme.BtnGray, AppTheme.TextSec, 62, 32);
        _manualScanBtn.Location = new Point(368, 20);
        _manualScanBtn.Click += (_, _) => ManualScanClicked?.Invoke();

        Controls.AddRange(new Control[] { _titleLabel, _statusLabel, _btPowerBtn, _autoScanBtn, _manualScanBtn });

        WireDrag(this);
        WireDrag(_titleLabel);
        WireDrag(_statusLabel);

        UpdateButtonsAppearance();
    }

    public void SetBluetoothState(bool isRadioOn)
    {
        _isRadioOn = isRadioOn;
        if (!_isRadioOn)
        {
            _btPowerBtn.Text = "ᛒ OFF";
            _btPowerBtn.BackColor = Color.FromArgb(0x3B, 0x22, 0x22);
            _btPowerBtn.ForeColor = AppTheme.Danger;
            _statusLabel.Text = "Bluetooth is OFF · App frozen";
            _statusLabel.ForeColor = AppTheme.Danger;

            _autoScanBtn.Enabled = false;
            _manualScanBtn.Enabled = false;
            _autoScanBtn.BackColor = Color.FromArgb(0x1C, 0x1F, 0x26);
            _autoScanBtn.ForeColor = AppTheme.SecLabel;
            _manualScanBtn.BackColor = Color.FromArgb(0x1C, 0x1F, 0x26);
            _manualScanBtn.ForeColor = AppTheme.SecLabel;
        }
        else
        {
            _btPowerBtn.Text = "ᛒ ON";
            _btPowerBtn.BackColor = Color.FromArgb(0x1B, 0x3A, 0x4B);
            _btPowerBtn.ForeColor = Color.FromArgb(0x5A, 0xC8, 0xFA);
            _statusLabel.ForeColor = AppTheme.TextSec;

            _autoScanBtn.Enabled = true;
            _manualScanBtn.Enabled = true;
            UpdateButtonsAppearance();
        }
    }

    public void SetScanMode(bool isAutoScan, bool isScanning)
    {
        _isAutoScan = isAutoScan;
        _isScanning = isScanning;
        UpdateButtonsAppearance();
    }

    public void UpdateHeartbeat(int secondsRemaining, float pulseAlpha, bool isRecycling)
    {
        _countdownSeconds = secondsRemaining;
        _pulseAlpha = Math.Clamp(pulseAlpha, 0.2f, 1f);

        if (!_isRadioOn) return;

        if (_isAutoScan)
        {
            if (isRecycling)
            {
                _autoScanBtn.Text = "↻ Scanning";
            }
            else
            {
                _autoScanBtn.Text = $"● Auto ({_countdownSeconds}s)";
            }
        }
    }

    private void UpdateButtonsAppearance()
    {
        if (!_isRadioOn) return;

        if (_isAutoScan)
        {
            _autoScanBtn.BackColor = AppTheme.Teal;
            _autoScanBtn.ForeColor = AppTheme.TealText;
            _autoScanBtn.Text      = $"● Auto ({_countdownSeconds}s)";

            _manualScanBtn.BackColor = AppTheme.BtnGray;
            _manualScanBtn.ForeColor = AppTheme.TextSec;
            _manualScanBtn.Text      = "Manual";

            _statusLabel.Text = "Radio on · Auto-scanning (10s)";
        }
        else
        {
            _autoScanBtn.BackColor = AppTheme.BtnGray;
            _autoScanBtn.ForeColor = AppTheme.TextSec;
            _autoScanBtn.Text      = "Auto";

            _manualScanBtn.BackColor = _isScanning ? AppTheme.Warning : AppTheme.Teal;
            _manualScanBtn.ForeColor = _isScanning ? Color.FromArgb(0x14, 0x16, 0x1A) : AppTheme.TealText;
            _manualScanBtn.Text      = _isScanning ? "⏹ Stop" : "Scan";

            _statusLabel.Text = _isScanning ? "Manual scanning active..." : "Manual mode · Idle";
        }
    }

    public void SetStatusText(string text)
    {
        if (_isRadioOn)
        {
            _statusLabel.Text = text;
        }
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
