using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterBluetooth.Core.Interop;
using BetterBluetooth.Core.Models;
using BetterBluetooth.Core.Services;
using BetterBluetooth.UI.Components;
using BetterBluetooth.UI.Theme;
using Panel = System.Windows.Forms.Panel;

namespace BetterBluetooth.UI.Forms;

/// <summary>
/// 440×600 borderless overlay window wired live to BluetoothService and AutoForgetStore.
/// Features custom-drawn dark scrollbar, full 510px viewport, ellipsis text truncation,
/// fine-grained card invalidation, and smooth mouse-wheel navigation.
/// </summary>
public sealed class MainForm : Form
{
    // ── Public events ──────────────────────────────────────────────────────
    public event Action? HotkeyPressed;

    // ── Dependencies ───────────────────────────────────────────────────────
    private readonly BluetoothService _service;
    private readonly AutoForgetStore  _store;
    private readonly AutoForgetEngine _engine;
    private readonly ConcurrentDictionary<string, int> _countdownRemaining = new(StringComparer.OrdinalIgnoreCase);

    // ── UI Controls ────────────────────────────────────────────────────────
    private HeaderPanel _header = null!;
    private DarkScrollBar _darkScrollBar = null!;
    private Panel _contentPanel = null!;
    private FlowLayoutPanel _innerLayout = null!;
    private FlowLayoutPanel _nearbyFlow = null!;
    private FlowLayoutPanel _myDevicesFlow = null!;
    private Panel _chipPanel = null!;

    // ── Throttled UI refresh timer (smooth, 0 CPU thrash) ───────────────────
    private readonly System.Windows.Forms.Timer _throttleTimer;
    private bool _needsRefresh;
    private bool _forceClose;

    public MainForm(BluetoothService service, AutoForgetStore store, AutoForgetEngine engine)
    {
        _service = service;
        _store   = store;
        _engine  = engine;

        FormBorderStyle = FormBorderStyle.None;
        BackColor       = AppTheme.Bg;
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
            string norm = AutoForgetStore.NormalizeMac(mac);
            _countdownRemaining[norm] = remaining;

            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() => InvalidateCardForMac(norm));
            }
        };

        _engine.GraceCountdownCancelled += mac =>
        {
            string norm = AutoForgetStore.NormalizeMac(mac);
            _countdownRemaining.TryRemove(norm, out _);

            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() => InvalidateCardForMac(norm));
            }
        };

        _engine.DeviceAutoForgotten += (dev, reason) =>
        {
            _countdownRemaining.TryRemove(AutoForgetStore.NormalizeMac(dev.MacFormatted), out _);
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    _header.SetStatusText($"Auto-forgot {dev.Name}. Hold sync to pair next time.");
                    ScheduleRefresh();
                });
            }
        };

        _engine.DeviceAutoPaired += dev =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    _header.SetStatusText($"Auto-paired with {dev.Name}!");
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

    private void InvalidateCardForMac(string normMac)
    {
        foreach (Control ctrl in _myDevicesFlow.Controls)
        {
            if (ctrl.Tag is BluetoothDeviceInfo dev &&
                string.Equals(AutoForgetStore.NormalizeMac(dev.MacFormatted), normMac, StringComparison.OrdinalIgnoreCase))
            {
                ctrl.Invalidate();
                return;
            }
        }
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
            short delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            _darkScrollBar.ApplyWheelDelta(delta);
            return;
        }

        if (m.Msg == NativeMethods.WM_HOTKEY &&
            m.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
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

        _header = new HeaderPanel();
        _header.ScanClicked     += ToggleScan;
        _header.SettingsClicked += ShowAutoForgetListDialog;
        _header.DragWindowMoved += delta => Location = new Point(Location.X + delta.X, Location.Y + delta.Y);
        Controls.Add(_header);

        // ── Scrollable middle content viewport (expanded to 510px without footer) ─
        _contentPanel = new Panel
        {
            Location   = new Point(0, 84),
            Size       = new Size(432, 510),
            BackColor  = AppTheme.Bg,
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
            BackColor     = AppTheme.Bg,
            Margin        = Padding.Empty,
            Padding       = new Padding(16, 6, 0, 14),
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

        AppTheme.EnableDoubleBuffer(_contentPanel);
        AppTheme.EnableDoubleBuffer(_innerLayout);
        AppTheme.EnableDoubleBuffer(_nearbyFlow);
        AppTheme.EnableDoubleBuffer(_myDevicesFlow);

        // ── Custom Dark Scrollbar (expanded to 502px) ───────────────────────
        _darkScrollBar = new DarkScrollBar
        {
            Location = new Point(433, 88),
            Size     = new Size(5, 502),
        };
        _darkScrollBar.ScrollChanged += offset =>
        {
            _innerLayout.Location = new Point(0, -offset);
        };
        Controls.Add(_darkScrollBar);
        _darkScrollBar.BringToFront();

        ResumeLayout(false);
    }

    private void ToggleScan()
    {
        if (_service.IsScanning)
        {
            _service.StopScan();
            _header.SetScanning(false);
        }
        else
        {
            _service.StartScan();
            _header.SetScanning(true);
        }
    }

    private static Label MakeSectionLabel(string text) => new()
    {
        Text      = text,
        Font      = AppTheme.FontSection,
        ForeColor = AppTheme.SecLabel,
        BackColor = AppTheme.Bg,
        AutoSize  = true,
        Margin    = new Padding(0, 4, 0, 4),
    };

    private Panel BuildMyDevicesSectionRow()
    {
        var row = new Panel
        {
            Size      = new Size(398, 30),
            BackColor = AppTheme.Bg,
            Margin    = new Padding(0, 4, 0, 4),
        };

        var lbl = new Label
        {
            Text      = "MY DEVICES",
            Font      = AppTheme.FontSection,
            ForeColor = AppTheme.SecLabel,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(0, 6),
        };

        _chipPanel = new Panel
        {
            Location  = new Point(236, 2),
            Size      = new Size(162, 26),
            BackColor = AppTheme.ChipBg,
            Cursor    = Cursors.Hand,
        };
        AppTheme.EnableDoubleBuffer(_chipPanel);

        _chipPanel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = AppTheme.RoundedPath(new Rectangle(0, 0, _chipPanel.Width - 1, _chipPanel.Height - 1), 12);
            using var pen  = new Pen(AppTheme.Border, 1f);
            g.DrawPath(pen, path);

            string text = $"⚙  Rules · {_store.Count}";
            TextRenderer.DrawText(g, text, AppTheme.FontChip, _chipPanel.ClientRectangle, AppTheme.Teal,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        _chipPanel.Click += (_, _) => ShowAutoForgetListDialog();

        row.Controls.Add(lbl);
        row.Controls.Add(_chipPanel);
        return row;
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

        ReconcileNearby(nearby);
        ReconcileMyDevices(paired);

        _darkScrollBar.UpdateMetrics(_contentPanel.Height, _innerLayout.Height);
    }

    private void ReconcileNearby(List<BluetoothDeviceInfo> nearby)
    {
        if (nearby.Count == 0)
        {
            if (_nearbyFlow.Controls.Count != 1 || !(_nearbyFlow.Controls[0] is Label))
            {
                _nearbyFlow.SuspendLayout();
                _nearbyFlow.Controls.Clear();
                _nearbyFlow.Controls.Add(new Label
                {
                    Text      = _service.IsScanning ? "Searching for discoverable devices nearby..." : "No devices found. Click Scan.",
                    Font      = AppTheme.FontDevSub,
                    ForeColor = AppTheme.SecLabel,
                    AutoSize  = true,
                    Margin    = new Padding(4, 4, 0, 4),
                });
                _nearbyFlow.ResumeLayout(true);
            }
            return;
        }

        var currentCards = _nearbyFlow.Controls.OfType<Control>()
            .Where(c => c.Tag is BluetoothDeviceInfo)
            .ToList();

        bool needsRebuild = currentCards.Count != nearby.Count;
        if (!needsRebuild)
        {
            for (int i = 0; i < nearby.Count; i++)
            {
                var cardDev = currentCards[i].Tag as BluetoothDeviceInfo;
                if (cardDev == null || cardDev.Id != nearby[i].Id)
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            _nearbyFlow.SuspendLayout();
            _nearbyFlow.Controls.Clear();
            foreach (var dev in nearby)
            {
                _nearbyFlow.Controls.Add(DeviceCard.CreateNearbyCard(dev, _service.PairDeviceAsync));
            }
            _nearbyFlow.ResumeLayout(true);
        }
        else
        {
            for (int i = 0; i < nearby.Count; i++)
            {
                currentCards[i].Tag = nearby[i];
                currentCards[i].Invalidate();
            }
        }
    }

    private void ReconcileMyDevices(List<BluetoothDeviceInfo> paired)
    {
        if (paired.Count == 0)
        {
            if (_myDevicesFlow.Controls.Count != 1 || !(_myDevicesFlow.Controls[0] is Label))
            {
                _myDevicesFlow.SuspendLayout();
                _myDevicesFlow.Controls.Clear();
                _myDevicesFlow.Controls.Add(new Label
                {
                    Text      = "No paired devices found.",
                    Font      = AppTheme.FontDevSub,
                    ForeColor = AppTheme.SecLabel,
                    AutoSize  = true,
                    Margin    = new Padding(4, 4, 0, 4),
                });
                _myDevicesFlow.ResumeLayout(true);
            }
            return;
        }

        var currentCards = _myDevicesFlow.Controls.OfType<Control>()
            .Where(c => c.Tag is BluetoothDeviceInfo)
            .ToList();

        bool needsRebuild = currentCards.Count != paired.Count;
        if (!needsRebuild)
        {
            for (int i = 0; i < paired.Count; i++)
            {
                var cardDev = currentCards[i].Tag as BluetoothDeviceInfo;
                if (cardDev == null || cardDev.Id != paired[i].Id || cardDev.IsConnected != paired[i].IsConnected)
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            _myDevicesFlow.SuspendLayout();
            _myDevicesFlow.Controls.Clear();
            foreach (var dev in paired)
            {
                _myDevicesFlow.Controls.Add(DeviceCard.CreateMyDeviceCard(
                    dev,
                    _store,
                    _service.UnpairDeviceAsync,
                    _service.PairDeviceAsync,
                    mac => _countdownRemaining.TryGetValue(mac, out int s) ? s : null,
                    ShowAutoForgetListDialog));
            }
            _myDevicesFlow.ResumeLayout(true);
        }
        else
        {
            for (int i = 0; i < paired.Count; i++)
            {
                currentCards[i].Tag = paired[i];
                currentCards[i].Invalidate();
            }
        }
    }

    private void ShowAutoForgetListDialog()
    {
        using var dlg = new RulesManagerDialog(_store, _service);
        dlg.ShowDialog(this);
    }

    // ── Service Event Handlers ─────────────────────────────────────────────

    private void OnEnumerationCompleted()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(OnEnumerationCompleted));
            return;
        }

        _header.SetStatusText("Live discovery active");
    }

    private void OnStatusMessage(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnStatusMessage(msg)));
            return;
        }

        _header.SetStatusText(msg);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _throttleTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
