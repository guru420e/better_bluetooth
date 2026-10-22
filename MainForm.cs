using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Enumeration;
using Panel = System.Windows.Forms.Panel;

namespace BluetoothRectifier;

/// <summary>
/// 440×600 borderless overlay window wired live to BluetoothService and AutoForgetStore.
/// Features custom-drawn dark scrollbar (no OS white scrollbars), ellipsis text truncation,
/// dynamic device cards, and smooth mouse-wheel navigation.
/// </summary>
public sealed class MainForm : Form
{
    // ── Public events ──────────────────────────────────────────────────────
    public event Action? HotkeyPressed;

    // ── Dependencies ───────────────────────────────────────────────────────
    private readonly BluetoothService _service;
    private readonly AutoForgetStore  _store;
    private readonly AutoForgetEngine _engine;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _countdownRemaining = new(StringComparer.OrdinalIgnoreCase);

    // ── Design tokens ──────────────────────────────────────────────────────
    private static readonly Color C_Bg        = Color.FromArgb(0x14, 0x16, 0x1A);
    private static readonly Color C_CardBg    = Color.FromArgb(0x1C, 0x1F, 0x26);
    private static readonly Color C_Border    = Color.FromArgb(0x2A, 0x2E, 0x37);
    private static readonly Color C_Teal      = Color.FromArgb(0x3D, 0xD6, 0xB0);
    private static readonly Color C_TealDark  = Color.FromArgb(0x26, 0x9E, 0x82);
    private static readonly Color C_TealText  = Color.FromArgb(0x0E, 0x10, 0x14);
    private static readonly Color C_Danger    = Color.FromArgb(0xFF, 0x8A, 0x8A);
    private static readonly Color C_TextPrim  = Color.FromArgb(0xF0, 0xF2, 0xF5);
    private static readonly Color C_TextSec   = Color.FromArgb(0x8A, 0x8F, 0x9E);
    private static readonly Color C_SecLabel  = Color.FromArgb(0x5A, 0x5F, 0x6E);
    private static readonly Color C_Connected = Color.FromArgb(0x3D, 0xD6, 0xB0);
    private static readonly Color C_ChipBg    = Color.FromArgb(0x1C, 0x1F, 0x26);
    private static readonly Color C_BtnGray   = Color.FromArgb(0x2A, 0x2E, 0x37);

    // ── Fonts ──────────────────────────────────────────────────────────────
    private readonly Font _fTitle   = new("Segoe UI", 17f, FontStyle.Bold);
    private readonly Font _fStatus  = new("Segoe UI",  8.5f);
    private readonly Font _fSection = new("Segoe UI",  7.5f, FontStyle.Bold);
    private readonly Font _fDevName = new("Segoe UI", 10f,  FontStyle.Bold);
    private readonly Font _fDevSub  = new("Segoe UI",  8.5f);
    private readonly Font _fBtn     = new("Segoe UI",  8.5f, FontStyle.Bold);
    private readonly Font _fIcon    = new("Segoe UI Emoji", 14f);
    private readonly Font _fChip    = new("Segoe UI",  8f,  FontStyle.Bold);
    private readonly Font _fFooter  = new("Segoe UI",  9f);

    // ── UI Controls ────────────────────────────────────────────────────────
    private Label _statusLabel = null!;
    private Button _scanBtn = null!;
    private Panel _contentPanel = null!;
    private FlowLayoutPanel _innerLayout = null!;
    private FlowLayoutPanel _nearbyFlow = null!;
    private FlowLayoutPanel _myDevicesFlow = null!;
    private Panel _chipPanel = null!;
    private Label _footerStatusLabel = null!;
    private CheckBox _autoFixCheckBox = null!;
    private Panel _darkScrollBar = null!;

    // ── Custom Dark Scrolling State ────────────────────────────────────────
    private int _scrollOffset = 0;
    private bool _isDraggingScrollbar = false;
    private int _scrollDragStartY;
    private int _scrollDragStartOffset;

    // ── Throttled UI refresh timer (smooth, 0 CPU thrash) ───────────────────
    private readonly System.Windows.Forms.Timer _throttleTimer;
    private bool _needsRefresh;

    // ── Window drag state ──────────────────────────────────────────────────
    private bool  _dragging;
    private Point _dragOriginScreen;
    private bool  _forceClose;

    public MainForm(BluetoothService service, AutoForgetStore store, AutoForgetEngine engine)
    {
        _service = service;
        _store   = store;
        _engine  = engine;

        FormBorderStyle = FormBorderStyle.None;
        BackColor       = C_Bg;
        Size            = new Size(440, 600);
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        TopMost         = true;
        DoubleBuffered  = true;

        BuildUI();

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) HideWindow(); };

        _service.DeviceDiscovered     += _ => ScheduleRefresh();
        _service.DeviceUpdated        += _ => ScheduleRefresh();
        _service.DeviceRemoved        += _ => ScheduleRefresh();
        _service.EnumerationCompleted += OnEnumerationCompleted;
        _service.StatusMessage        += OnStatusMessage;
        _store.Changed                += () => { _chipPanel?.Invalidate(); ScheduleRefresh(); };

        // ── Auto-Forget Engine Events ───────────────────────────────────────────
        _engine.GraceCountdownTick += (mac, remaining) =>
        {
            _countdownRemaining[AutoForgetStore.NormalizeMac(mac)] = remaining;
            ScheduleRefresh();
        };

        _engine.GraceCountdownCancelled += mac =>
        {
            _countdownRemaining.TryRemove(AutoForgetStore.NormalizeMac(mac), out _);
            ScheduleRefresh();
        };

        _engine.DeviceAutoForgotten += (dev, reason) =>
        {
            _countdownRemaining.TryRemove(AutoForgetStore.NormalizeMac(dev.MacFormatted), out _);
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    _statusLabel.Text = $"Auto-forgot {dev.Name}. Hold sync to pair next time.";
                    ScheduleRefresh();
                });
            }
        };

        _throttleTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _throttleTimer.Tick += (_, _) =>
        {
            if (_needsRefresh)
            {
                _needsRefresh = false;
                RenderDeviceLists();
            }
        };
        _throttleTimer.Start();
    }

    private void ScheduleRefresh()
    {
        _needsRefresh = true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Win32 hooks & Mouse Wheel
    // ══════════════════════════════════════════════════════════════════════
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        int pref = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref pref, sizeof(int));

        NativeMethods.RegisterHotKey(
            Handle, NativeMethods.HOTKEY_ID,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
            NativeMethods.VK_B);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEWHEEL = 0x020A;
        if (m.Msg == WM_MOUSEWHEEL)
        {
            // Intercept mouse wheel everywhere over the window
            short delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            HandleScrollDelta(delta);
            return;
        }

        if (m.Msg == NativeMethods.WM_HOTKEY &&
            m.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
    }

    private void HandleScrollDelta(int delta)
    {
        int maxScroll = Math.Max(0, _innerLayout.Height - _contentPanel.Height);
        if (maxScroll <= 0)
        {
            _scrollOffset = 0;
            _innerLayout.Location = new Point(0, 0);
            _darkScrollBar.Invalidate();
            return;
        }

        int step = (delta / 120) * 44;
        _scrollOffset = Math.Clamp(_scrollOffset - step, 0, maxScroll);
        _innerLayout.Location = new Point(0, -_scrollOffset);
        _darkScrollBar.Invalidate();
    }

    public void ShowAtTray()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
        Show();
        Activate();

        if (!_service.IsScanning)
        {
            ToggleScan();
        }
    }

    public void HideWindow() => Hide();

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _throttleTimer.Stop();
        base.OnFormClosing(e);
    }

    // ══════════════════════════════════════════════════════════════════════
    // UI Construction
    // ══════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        SuspendLayout();

        Controls.Add(BuildHeader());

        // ── Scrollable middle content viewport ──────────────────────────────
        // AutoScroll is strictly FALSE so Windows never displays white scrollbars
        _contentPanel = new Panel
        {
            Location   = new Point(0, 84),
            Size       = new Size(432, 436),
            BackColor  = C_Bg,
            AutoScroll = false,
        };

        _innerLayout = new FlowLayoutPanel
        {
            Location      = Point.Empty,
            Width         = 426,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            BackColor     = C_Bg,
            Margin        = Padding.Empty,
            Padding       = new Padding(16, 6, 0, 10),
        };

        // 1. NEARBY SECTION
        _innerLayout.Controls.Add(MakeSectionLabel("NEARBY  ·  READY TO PAIR"));
        _nearbyFlow = new FlowLayoutPanel
        {
            Width         = 398,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            Margin        = new Padding(0, 4, 0, 14),
        };
        _innerLayout.Controls.Add(_nearbyFlow);

        // 2. MY DEVICES SECTION
        _innerLayout.Controls.Add(BuildMyDevicesSectionRow());
        _myDevicesFlow = new FlowLayoutPanel
        {
            Width         = 398,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            Margin        = new Padding(0, 4, 0, 10),
        };
        _innerLayout.Controls.Add(_myDevicesFlow);

        _contentPanel.Controls.Add(_innerLayout);
        Controls.Add(_contentPanel);

        // ── Slim Dark Scrollbar (blends 100% with dark theme) ───────────────
        _darkScrollBar = new Panel
        {
            Location  = new Point(433, 88),
            Size      = new Size(5, 428),
            BackColor = C_Bg,
            Cursor    = Cursors.Hand,
        };
        EnableDoubleBuffer(_darkScrollBar);
        _darkScrollBar.Paint += (_, e) => PaintDarkScrollBar(e.Graphics);

        _darkScrollBar.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDraggingScrollbar = true;
                _scrollDragStartY = e.Y;
                _scrollDragStartOffset = _scrollOffset;
                _darkScrollBar.Invalidate();
            }
        };

        _darkScrollBar.MouseMove += (s, e) =>
        {
            if (_isDraggingScrollbar)
            {
                int maxScroll = Math.Max(0, _innerLayout.Height - _contentPanel.Height);
                if (maxScroll <= 0) return;

                int trackH = _darkScrollBar.Height;
                float ratio = (float)_contentPanel.Height / _innerLayout.Height;
                int thumbH = Math.Clamp((int)(trackH * ratio), 28, trackH);
                int maxThumbY = trackH - thumbH;
                if (maxThumbY > 0)
                {
                    int deltaY = e.Y - _scrollDragStartY;
                    int newOffset = _scrollDragStartOffset + (int)((float)deltaY / maxThumbY * maxScroll);
                    _scrollOffset = Math.Clamp(newOffset, 0, maxScroll);
                    _innerLayout.Location = new Point(0, -_scrollOffset);
                    _darkScrollBar.Invalidate();
                }
            }
        };

        _darkScrollBar.MouseUp += (_, _) =>
        {
            _isDraggingScrollbar = false;
            _darkScrollBar.Invalidate();
        };

        Controls.Add(_darkScrollBar);
        _darkScrollBar.BringToFront();

        Controls.Add(BuildFooter());

        ResumeLayout(false);
    }

    private void PaintDarkScrollBar(Graphics g)
    {
        int maxScroll = Math.Max(0, _innerLayout.Height - _contentPanel.Height);
        if (maxScroll <= 0) return; // Content fits, no scrollbar needed

        int trackH = _darkScrollBar.Height;
        float ratio = (float)_contentPanel.Height / _innerLayout.Height;
        int thumbH = Math.Clamp((int)(trackH * ratio), 28, trackH);
        int maxThumbY = trackH - thumbH;
        int thumbY = maxScroll > 0 ? (int)((float)_scrollOffset / maxScroll * maxThumbY) : 0;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_isDraggingScrollbar ? C_Teal : Color.FromArgb(0x35, 0x3B, 0x47));
        using var path = RoundedPath(new Rectangle(0, thumbY, _darkScrollBar.Width, thumbH), 2);
        g.FillPath(brush, path);
    }

    // ── Header ─────────────────────────────────────────────────────────────

    private Panel BuildHeader()
    {
        var hdr = new Panel
        {
            Location  = Point.Empty,
            Size      = new Size(440, 84),
            BackColor = C_Bg,
        };
        EnableDrag(hdr);

        var title = new Label
        {
            Text      = "Bluetooth",
            Font      = _fTitle,
            ForeColor = C_TextPrim,
            BackColor = C_Bg,
            AutoSize  = true,
            Location  = new Point(16, 14),
            Cursor    = Cursors.SizeAll,
        };
        EnableDrag(title);

        _statusLabel = new Label
        {
            Text      = "Radio on  ·  Idle",
            Font      = _fStatus,
            ForeColor = C_TextSec,
            BackColor = C_Bg,
            AutoSize  = true,
            Location  = new Point(18, 50),
        };
        EnableDrag(_statusLabel);

        _scanBtn = MakeButton("↺  Scan", C_Teal, C_TealText, w: 88, h: 36);
        _scanBtn.Location = new Point(336, 24);
        _scanBtn.Click   += (_, _) => ToggleScan();

        hdr.Controls.AddRange(new Control[] { title, _statusLabel, _scanBtn });
        return hdr;
    }

    private void ToggleScan()
    {
        if (_service.IsScanning)
        {
            _service.StopScan();
            _scanBtn.Text      = "↺  Scan";
            _scanBtn.BackColor = C_Teal;
            _scanBtn.ForeColor = C_TealText;
            _statusLabel.Text  = "Radio on  ·  Idle";
            _footerStatusLabel.Text = "Idle";
        }
        else
        {
            _service.StartScan();
            _scanBtn.Text      = "⏹  Stop";
            _scanBtn.BackColor = C_BtnGray;
            _scanBtn.ForeColor = C_TextPrim;
            _statusLabel.Text  = "Scanning for devices...";
            _footerStatusLabel.Text = "Scanning...";
        }
    }

    // ── Section Rows ───────────────────────────────────────────────────────

    private Label MakeSectionLabel(string text) => new()
    {
        Text      = text,
        Font      = _fSection,
        ForeColor = C_SecLabel,
        BackColor = C_Bg,
        AutoSize  = true,
        Margin    = new Padding(0, 4, 0, 4),
    };

    private Panel BuildMyDevicesSectionRow()
    {
        var row = new Panel
        {
            Size      = new Size(398, 30),
            BackColor = C_Bg,
            Margin    = new Padding(0, 4, 0, 4),
        };

        var lbl = new Label
        {
            Text      = "MY DEVICES",
            Font      = _fSection,
            ForeColor = C_SecLabel,
            BackColor = C_Bg,
            AutoSize  = true,
            Location  = new Point(0, 6),
        };

        _chipPanel = new Panel
        {
            Location  = new Point(236, 2),
            Size      = new Size(162, 26),
            BackColor = C_ChipBg,
            Cursor    = Cursors.Hand,
        };
        EnableDoubleBuffer(_chipPanel);

        _chipPanel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = RoundedPath(new Rectangle(0, 0, _chipPanel.Width - 1, _chipPanel.Height - 1), 12);
            using var pen  = new Pen(C_Border, 1f);
            g.DrawPath(pen, path);

            string text = $"≡×  Auto-forget list · {_store.Count}";
            TextRenderer.DrawText(g, text, _fChip, _chipPanel.ClientRectangle, C_Teal,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        _chipPanel.Click += (_, _) => ShowAutoForgetListDialog();

        row.Controls.Add(lbl);
        row.Controls.Add(_chipPanel);
        return row;
    }

    // ── Footer ─────────────────────────────────────────────────────────────

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Location  = new Point(0, 520),
            Size      = new Size(440, 80),
            BackColor = C_Bg,
        };

        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(C_Border, 1f);
            e.Graphics.DrawLine(pen, 16, 0, 424, 0);
        };

        _autoFixCheckBox = new CheckBox
        {
            Text      = "Fix stale pairings automatically",
            Font      = _fFooter,
            ForeColor = C_TextPrim,
            BackColor = C_Bg,
            AutoSize  = true,
            Location  = new Point(14, 28),
            Checked   = false,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };

        _footerStatusLabel = new Label
        {
            Text      = "Idle",
            Font      = _fStatus,
            ForeColor = C_TextSec,
            BackColor = C_Bg,
            AutoSize  = true,
            Location  = new Point(380, 32),
        };

        footer.Controls.AddRange(new Control[] { _autoFixCheckBox, _footerStatusLabel });
        return footer;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Dynamic Device Rendering
    // ══════════════════════════════════════════════════════════════════════

    private void RenderDeviceLists()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RenderDeviceLists));
            return;
        }

        var all = _service.Devices;
        var nearby = all.Where(d => !d.IsPaired).OrderByDescending(d => d.Rssi ?? -999).ToList();
        var paired = all.Where(d => d.IsPaired).OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name).ToList();

        // ── Render Nearby Flow ─────────────────────────────────────────────
        _nearbyFlow.SuspendLayout();
        _nearbyFlow.Controls.Clear();

        if (nearby.Count == 0)
        {
            var emptyLbl = new Label
            {
                Text      = _service.IsScanning ? "Searching for discoverable devices nearby..." : "No devices found. Click Scan.",
                Font      = _fDevSub,
                ForeColor = C_SecLabel,
                AutoSize  = true,
                Margin    = new Padding(4, 4, 0, 4),
            };
            _nearbyFlow.Controls.Add(emptyLbl);
        }
        else
        {
            foreach (var dev in nearby)
            {
                _nearbyFlow.Controls.Add(CreateNearbyCard(dev));
            }
        }
        _nearbyFlow.ResumeLayout(true);

        // ── Render My Devices Flow ─────────────────────────────────────────
        _myDevicesFlow.SuspendLayout();
        _myDevicesFlow.Controls.Clear();

        if (paired.Count == 0)
        {
            var emptyLbl = new Label
            {
                Text      = "No paired devices found.",
                Font      = _fDevSub,
                ForeColor = C_SecLabel,
                AutoSize  = true,
                Margin    = new Padding(4, 4, 0, 4),
            };
            _myDevicesFlow.Controls.Add(emptyLbl);
        }
        else
        {
            foreach (var dev in paired)
            {
                _myDevicesFlow.Controls.Add(CreateMyDeviceCard(dev));
            }
        }
        _myDevicesFlow.ResumeLayout(true);

        // Update scroll bounds
        int maxScroll = Math.Max(0, _innerLayout.Height - _contentPanel.Height);
        if (_scrollOffset > maxScroll)
        {
            _scrollOffset = maxScroll;
            _innerLayout.Location = new Point(0, -_scrollOffset);
        }
        _darkScrollBar.Invalidate();
    }

    private Control CreateNearbyCard(BluetoothDeviceInfo dev)
    {
        var card = MakeCardPanel();
        string icon = GetDeviceEmoji(dev);
        string sub = $"{dev.MacFormatted}  ·  {(dev.Rssi.HasValue ? $"{dev.Rssi.Value} dBm" : "Pairing mode")}";

        card.Paint += (_, e) =>
        {
            gPaintNearbyCardText(e.Graphics, icon, dev.Name, sub);
        };

        var btn = MakeButton("Connect", C_Teal, C_TealText, w: 86, h: 34);
        btn.Location = new Point(302, 17);
        btn.Click += async (_, _) =>
        {
            btn.Enabled = false;
            btn.Text = "Pairing...";
            var result = await _service.PairDeviceAsync(dev.Id);
            if (result != DevicePairingResultStatus.Paired)
            {
                MessageBox.Show(this, $"Pairing failed with status: {result}", "Pairing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btn.Text = "Connect";
                btn.Enabled = true;
            }
        };

        card.Controls.Add(btn);
        return card;
    }

    private Control CreateMyDeviceCard(BluetoothDeviceInfo dev)
    {
        var card = MakeCardPanel();
        string icon = GetDeviceEmoji(dev);

        string normMac = AutoForgetStore.NormalizeMac(dev.MacFormatted);
        string subText;
        Color subColor;

        if (_countdownRemaining.TryGetValue(normMac, out int secondsLeft))
        {
            subText = $"⏳ Forgetting in {secondsLeft}s...";
            subColor = Color.FromArgb(0xFF, 0xB8, 0x6C); // Vibrant amber warning
        }
        else if (dev.IsConnected)
        {
            subText = "● Connected";
            subColor = C_Connected;
        }
        else if (dev.Rssi.HasValue)
        {
            subText = $"Paired · Not connected ({dev.Rssi.Value} dBm)";
            subColor = C_TextSec;
        }
        else
        {
            subText = "Paired · Out of range";
            subColor = C_SecLabel;
        }

        card.Paint += (_, e) =>
        {
            gPaintMyDeviceCardText(e.Graphics, icon, dev.Name, subText, subColor);
        };

        // 1. Trash / Remove button (X=354, W=34)
        var trash = MakeIconButton(text: "🗑", fg: C_Danger, w: 34, h: 34);
        trash.Location = new Point(354, 17);
        trash.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this, $"Remove \"{dev.Name}\" ({dev.MacFormatted})?", "Remove Device",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                trash.Enabled = false;
                await _service.UnpairDeviceAsync(dev.Id);
            }
        };
        card.Controls.Add(trash);

        // 2. Auto-forget toggle button (X=314, W=34)
        bool autoForgetOn = _store.Contains(dev.Mac);
        var toggle = new Button
        {
            Text      = "≡×",
            Font      = _fBtn,
            Size      = new Size(34, 34),
            Location  = new Point(314, 17),
            BackColor = autoForgetOn ? C_Teal : C_CardBg,
            ForeColor = autoForgetOn ? C_TealText : C_TextSec,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        toggle.FlatAppearance.BorderSize  = 1;
        toggle.FlatAppearance.BorderColor = autoForgetOn ? C_Teal : C_Border;
        toggle.Click += (_, _) =>
        {
            bool nowOn = _store.Toggle(dev.Mac);
            toggle.BackColor = nowOn ? C_Teal : C_CardBg;
            toggle.ForeColor = nowOn ? C_TealText : C_TextSec;
            toggle.FlatAppearance.BorderColor = nowOn ? C_Teal : C_Border;
            _chipPanel.Invalidate();

            if (nowOn)
            {
                _statusLabel.Text = "Note: Auto-forget means you must pair again next time.";
            }
            else
            {
                _statusLabel.Text = $"Auto-forget disabled for {dev.Name}.";
            }
        };
        card.Controls.Add(toggle);

        // 3. Connect / Disconnect button
        if (dev.IsConnected)
        {
            var disc = MakeButton("Disconnect", C_BtnGray, C_TextPrim, w: 96, h: 34);
            disc.Location = new Point(210, 17);
            disc.Click += (_, _) =>
            {
                MessageBox.Show(this,
                    $"To disconnect {dev.Name}, power off the device or turn off Bluetooth.",
                    "Disconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            card.Controls.Add(disc);
        }
        else
        {
            var conn = MakeButton("Connect", C_Teal, C_TealText, w: 86, h: 34);
            conn.Location = new Point(220, 17);
            conn.Click += async (_, _) =>
            {
                if (dev.CodMajor == 5) // Gamepad / Controller
                {
                    var dlg = MessageBox.Show(this,
                        $"{dev.Name} is a Bluetooth HID controller.\n\n" +
                        "Controllers only reconnect when you wake them up: press the Home/Guide button.\n\n" +
                        "Would you like to unpair and re-pair it fresh instead?",
                        "Controller Reconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (dlg == DialogResult.Yes)
                    {
                        conn.Enabled = false;
                        conn.Text = "Re-pairing...";
                        await _service.UnpairDeviceAsync(dev.Id);
                        MessageBox.Show(this, "Device removed. Put the controller in pairing mode (hold sync button), then click Connect from NEARBY.", "Ready to Pair", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    conn.Enabled = false;
                    conn.Text = "Connecting...";
                    await _service.PairDeviceAsync(dev.Id);
                    conn.Text = "Connect";
                    conn.Enabled = true;
                }
            };
            card.Controls.Add(conn);
        }

        return card;
    }

    private static string GetDeviceEmoji(BluetoothDeviceInfo dev)
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

    private Panel MakeCardPanel()
    {
        var card = new Panel
        {
            Size      = new Size(398, 68),
            BackColor = C_CardBg,
            Margin    = new Padding(0, 0, 0, 8),
        };
        EnableDoubleBuffer(card);
        card.Region = new Region(RoundedPath(new Rectangle(0, 0, card.Width, card.Height), 10));

        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedPath(new Rectangle(1, 1, card.Width - 2, card.Height - 2), 9);
            using var pen  = new Pen(C_Border, 1f);
            g.DrawPath(pen, path);
        };

        return card;
    }

    private void gPaintNearbyCardText(Graphics g, string icon, string name, string sub)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.DrawString(icon, _fIcon, new SolidBrush(C_TextSec), new PointF(10f, 18f));

        // Connect button starts at 302, so width of 244 leaves 8px gap
        TextRenderer.DrawText(g, name, _fDevName,
            new Rectangle(50, 12, 244, 22), C_TextPrim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, sub, _fDevSub,
            new Rectangle(50, 36, 244, 20), C_TextSec,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private void gPaintMyDeviceCardText(Graphics g, string icon, string name, string sub, Color subColor)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.DrawString(icon, _fIcon, new SolidBrush(C_TextSec), new PointF(10f, 18f));

        // Buttons start at 210, so width of 152 leaves 8px gap before buttons
        TextRenderer.DrawText(g, name, _fDevName,
            new Rectangle(50, 12, 152, 22), C_TextPrim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, sub, _fDevSub,
            new Rectangle(50, 36, 152, 20), subColor,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private void ShowAutoForgetListDialog()
    {
        var list = _store.MacAddresses;
        string msg;
        if (list.Count == 0)
        {
            msg = "The auto-forget list is currently empty.\n\n" +
                  "Toggle the (≡×) button on any paired device to automatically unpair it when it disconnects.";
        }
        else
        {
            msg = $"Devices on the Auto-forget list ({list.Count}):\n\n" +
                  string.Join("\n", list.Select(mac =>
                  {
                      var dev = _service.FindByMac(mac);
                      return dev != null ? $"• {dev.Name} ({mac})" : $"• {mac}";
                  })) +
                  "\n\nNote: Devices with auto-forget enabled will be forgotten after disconnect and grace period.";
        }

        MessageBox.Show(this, msg, "Auto-forget List", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Service Event Handlers ─────────────────────────────────────────────

    private void OnEnumerationCompleted()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(OnEnumerationCompleted));
            return;
        }

        _statusLabel.Text       = "Live discovery active";
        _footerStatusLabel.Text = "Scanning (live)";
    }

    private void OnStatusMessage(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnStatusMessage(msg)));
            return;
        }

        _footerStatusLabel.Text = msg.Length > 25 ? msg.Substring(0, 22) + "..." : msg;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private Button MakeButton(string text, Color bg, Color fg, int w, int h)
    {
        var btn = new Button
        {
            Text      = text,
            Font      = _fBtn,
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

    private Button MakeIconButton(string text, Color fg, int w, int h)
    {
        var btn = new Button
        {
            Text      = text,
            Font      = _fBtn,
            Size      = new Size(w, h),
            BackColor = C_CardBg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        btn.FlatAppearance.BorderSize        = 1;
        btn.FlatAppearance.BorderColor       = C_Border;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x22, 0x26, 0x2F);
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x28, 0x2C, 0x36);
        return btn;
    }

    private void EnableDrag(params Control[] controls)
    {
        foreach (var ctrl in controls)
        {
            ctrl.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragOriginScreen = ((Control)s!).PointToScreen(e.Location); } };
            ctrl.MouseMove += (s, e) => { if (_dragging) { var now = ((Control)s!).PointToScreen(e.Location); Location = new Point(Location.X + now.X - _dragOriginScreen.X, Location.Y + now.Y - _dragOriginScreen.Y); _dragOriginScreen = now; } };
            ctrl.MouseUp   += (_, e) => { if (e.Button == MouseButtons.Left) _dragging = false; };
        }
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
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

    private static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t),
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    private static void EnableDoubleBuffer(Panel p) =>
        typeof(Panel).GetProperty(
                "DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(p, true);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _throttleTimer.Dispose();
            _fTitle.Dispose(); _fStatus.Dispose(); _fSection.Dispose();
            _fDevName.Dispose(); _fDevSub.Dispose(); _fBtn.Dispose();
            _fIcon.Dispose(); _fChip.Dispose(); _fFooter.Dispose();
        }
        base.Dispose(disposing);
    }
}
