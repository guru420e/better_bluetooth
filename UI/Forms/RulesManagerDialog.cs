using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using BetterBluetooth.Core.Interop;
using BetterBluetooth.Core.Models;
using BetterBluetooth.Core.Services;
using BetterBluetooth.UI.Theme;
using Panel = System.Windows.Forms.Panel;

namespace BetterBluetooth.UI.Forms;

/// <summary>
/// Dark modal dialog for managing per-device Auto-Forget and Auto-Pair rules,
/// backed by LiteDB with true ACID atomicity.
/// </summary>
public sealed class RulesManagerDialog : Form
{
    private readonly AutoForgetStore  _store;
    private readonly BluetoothService _service;

    private FlowLayoutPanel _listFlow = null!;
    private CheckBox _masterForgetCheck = null!;
    private CheckBox _masterPairCheck = null!;

    private bool  _dragging;
    private Point _dragOriginScreen;

    public RulesManagerDialog(AutoForgetStore store, BluetoothService service)
    {
        _store   = store;
        _service = service;

        FormBorderStyle = FormBorderStyle.None;
        BackColor       = AppTheme.Bg;
        Size            = new Size(410, 520);
        StartPosition   = FormStartPosition.CenterParent;
        ShowInTaskbar   = false;
        TopMost         = true;
        DoubleBuffered  = true;

        BuildUI();

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int pref = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref pref, sizeof(int));
    }

    private void BuildUI()
    {
        SuspendLayout();

        // ── 1. Header (410x74) ──────────────────────────────────────────────
        var header = new Panel
        {
            Location  = Point.Empty,
            Size      = new Size(410, 74),
            BackColor = AppTheme.Bg,
        };
        WireDrag(header);

        var title = new Label
        {
            Text      = "Automation Rules",
            Font      = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(18, 14),
        };
        WireDrag(title);

        var sub = new Label
        {
            Text      = "Configure Auto-Forget & Auto-Pair per device",
            Font      = AppTheme.FontDevSub,
            ForeColor = AppTheme.TextSec,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(19, 44),
        };
        WireDrag(sub);

        var closeBtn = new Button
        {
            Text      = "✕",
            Font      = AppTheme.FontBtn,
            Size      = new Size(32, 32),
            Location  = new Point(362, 16),
            BackColor = AppTheme.CardBg,
            ForeColor = AppTheme.TextSec,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        closeBtn.FlatAppearance.BorderSize  = 1;
        closeBtn.FlatAppearance.BorderColor = AppTheme.Border;
        closeBtn.Click += (_, _) => Close();

        header.Controls.AddRange(new Control[] { title, sub, closeBtn });
        Controls.Add(header);

        // ── 2. Middle Scrollable Flow ──────────────────────────────────────
        _listFlow = new FlowLayoutPanel
        {
            Location      = new Point(12, 78),
            Size          = new Size(386, 330),
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
            BackColor     = AppTheme.Bg,
            Margin        = Padding.Empty,
            Padding       = new Padding(0, 4, 0, 4),
        };
        AppTheme.EnableDoubleBuffer(_listFlow);
        Controls.Add(_listFlow);

        // ── 3. Footer (410x108) ────────────────────────────────────────────
        var footer = new Panel
        {
            Location  = new Point(0, 412),
            Size      = new Size(410, 108),
            BackColor = AppTheme.Bg,
        };

        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border, 1f);
            e.Graphics.DrawLine(pen, 16, 0, 394, 0);
        };

        _masterForgetCheck = new CheckBox
        {
            Text      = "Enable Auto-Unpair globally",
            Font      = AppTheme.FontFooter,
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(18, 14),
            Checked   = _store.MasterAutoForget,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };
        _masterForgetCheck.CheckedChanged += (_, _) =>
        {
            _store.MasterAutoForget = _masterForgetCheck.Checked;
        };

        _masterPairCheck = new CheckBox
        {
            Text      = "Enable Auto-Pair globally",
            Font      = AppTheme.FontFooter,
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(18, 40),
            Checked   = _store.MasterAutoPair,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };
        _masterPairCheck.CheckedChanged += (_, _) =>
        {
            _store.MasterAutoPair = _masterPairCheck.Checked;
        };

        var doneBtn = AppTheme.CreateButton("Done", AppTheme.Teal, AppTheme.TealText, w: 76, h: 32);
        doneBtn.Location = new Point(316, 68);
        doneBtn.Click   += (_, _) => Close();

        footer.Controls.AddRange(new Control[] { _masterForgetCheck, _masterPairCheck, doneBtn });
        Controls.Add(footer);

        ResumeLayout(false);

        PopulateRules();
    }

    private void PopulateRules()
    {
        _listFlow.SuspendLayout();
        _listFlow.Controls.Clear();

        // Collect all paired devices + any saved rules in DB
        var paired = _service.Devices.Where(d => d.IsPaired).ToList();
        var rules  = _store.GetAllRules();

        var deviceMap = paired.ToDictionary(
            d => AutoForgetStore.NormalizeMac(d.MacFormatted),
            d => d,
            StringComparer.OrdinalIgnoreCase);

        var ruleMap = rules.ToDictionary(
            r => AutoForgetStore.NormalizeMac(r.Mac),
            r => r,
            StringComparer.OrdinalIgnoreCase);

        var allMacs = deviceMap.Keys.Union(ruleMap.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        if (allMacs.Count == 0)
        {
            _listFlow.Controls.Add(new Label
            {
                Text      = "No paired or configured devices found.\nPair a device first to configure automation rules.",
                Font      = AppTheme.FontDevSub,
                ForeColor = AppTheme.SecLabel,
                AutoSize  = true,
                Margin    = new Padding(12, 20, 0, 0),
            });
            _listFlow.ResumeLayout(true);
            return;
        }

        foreach (var mac in allMacs)
        {
            deviceMap.TryGetValue(mac, out var dev);
            ruleMap.TryGetValue(mac, out var rule);

            string name = dev?.Name ?? (rule?.Name ?? mac);
            bool autoForget = rule?.AutoForget ?? false;
            bool autoPair   = rule?.AutoPair ?? false;

            _listFlow.Controls.Add(CreateRuleCard(mac, name, dev, autoForget, autoPair));
        }

        _listFlow.ResumeLayout(true);
    }

    private Panel CreateRuleCard(string mac, string name, BluetoothDeviceInfo? dev, bool autoForget, bool autoPair)
    {
        var card = new Panel
        {
            Size      = new Size(364, 94),
            BackColor = AppTheme.CardBg,
            Margin    = new Padding(0, 0, 0, 8),
        };
        AppTheme.EnableDoubleBuffer(card);
        card.Region = new Region(AppTheme.RoundedPath(new Rectangle(0, 0, card.Width, card.Height), 8));

        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = AppTheme.RoundedPath(new Rectangle(1, 1, card.Width - 2, card.Height - 2), 7);
            using var pen  = new Pen(AppTheme.Border, 1f);
            g.DrawPath(pen, path);
        };

        string icon = dev != null ? AppTheme.GetDeviceEmoji(dev) : "📶";

        var lblIcon = new Label
        {
            Text      = icon,
            Font      = AppTheme.FontIcon,
            ForeColor = AppTheme.TextSec,
            Location  = new Point(10, 10),
            AutoSize  = true,
        };

        var lblName = new Label
        {
            Text      = name,
            Font      = AppTheme.FontDevName,
            ForeColor = AppTheme.TextPrim,
            Location  = new Point(40, 10),
            Size      = new Size(260, 20),
            AutoEllipsis = true,
        };

        var lblMac = new Label
        {
            Text      = mac,
            Font      = new Font("Segoe UI", 7.5f),
            ForeColor = AppTheme.TextSec,
            Location  = new Point(42, 32),
            AutoSize  = true,
        };

        var trashBtn = AppTheme.CreateIconButton("🗑", AppTheme.Danger, w: 28, h: 28);
        trashBtn.Location = new Point(324, 10);
        trashBtn.Click += (_, _) =>
        {
            _store.Remove(mac);
            PopulateRules();
        };

        var chkForget = new CheckBox
        {
            Text      = "Auto-unpair on disconnect",
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.CardBg,
            AutoSize  = true,
            Location  = new Point(42, 54),
            Checked   = autoForget,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };

        var chkPair = new CheckBox
        {
            Text      = "Auto-pair on discovery",
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.CardBg,
            AutoSize  = true,
            Location  = new Point(208, 54),
            Checked   = autoPair,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };

        chkForget.CheckedChanged += (_, _) =>
        {
            _store.SetRule(mac, name, chkForget.Checked, chkPair.Checked);
        };

        chkPair.CheckedChanged += (_, _) =>
        {
            _store.SetRule(mac, name, chkForget.Checked, chkPair.Checked);
        };

        card.Controls.AddRange(new Control[] { lblIcon, lblName, lblMac, trashBtn, chkForget, chkPair });
        return card;
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
                Location = new Point(Location.X + now.X - _dragOriginScreen.X, Location.Y + now.Y - _dragOriginScreen.Y);
                _dragOriginScreen = now;
            }
        };

        ctrl.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _dragging = false;
        };
    }
}
