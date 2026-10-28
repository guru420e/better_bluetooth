using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Enumeration;
using BetterBluetooth.Core.Models;
using BetterBluetooth.Core.Services;
using BetterBluetooth.UI.Theme;
using Panel = System.Windows.Forms.Panel;

namespace BetterBluetooth.UI.Components;

/// <summary>
/// Reusable card component for rendering Bluetooth devices in the Nearby and My Devices lists.
/// Built with custom double-buffered painting, GDI+ rounded clipping, and ellipsis truncation.
/// </summary>
public static class DeviceCard
{
    public static Panel CreateNearbyCard(
        BluetoothDeviceInfo dev,
        Func<string, Task<DevicePairingResultStatus>> onPairRequested)
    {
        var card = CreateBaseCardPanel();
        card.Tag = dev;
        string icon = AppTheme.GetDeviceEmoji(dev);

        card.Paint += (s, e) =>
        {
            var cur = (BluetoothDeviceInfo)((Control)s!).Tag!;
            PaintNearbyText(e.Graphics, icon, cur.Name, cur);
        };

        var btn = AppTheme.CreateButton("Connect", AppTheme.Teal, AppTheme.TealText, w: 86, h: 34);
        btn.Location = new Point(302, 17);
        btn.Click += async (_, _) =>
        {
            btn.Enabled = false;
            btn.Text = "Pairing...";
            var result = await onPairRequested(dev.Id);
            if (result != DevicePairingResultStatus.Paired)
            {
                MessageBox.Show(card.FindForm(), $"Pairing failed with status: {result}", "Pairing",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btn.Text = "Connect";
                btn.Enabled = true;
            }
        };

        card.Controls.Add(btn);
        return card;
    }

    public static Panel CreateMyDeviceCard(
        BluetoothDeviceInfo dev,
        AutoForgetStore store,
        Func<string, Task> onUnpairRequested,
        Func<string, Task<DevicePairingResultStatus>> onPairRequested,
        Func<string, int?> getCountdownRemaining,
        Action onOpenRulesRequested)
    {
        var card = CreateBaseCardPanel();
        card.Tag = dev;
        string icon = AppTheme.GetDeviceEmoji(dev);

        card.Paint += (s, e) =>
        {
            var cur = (BluetoothDeviceInfo)((Control)s!).Tag!;
            string normMac = AutoForgetStore.NormalizeMac(cur.MacFormatted);
            string subText;
            Color subColor;

            int? secondsLeft = getCountdownRemaining(normMac);
            if (secondsLeft.HasValue)
            {
                subText = $"⏳ Forgetting in {secondsLeft.Value}s...";
                subColor = AppTheme.Warning;
            }
            else if (cur.IsConnected)
            {
                subText = "● Connected";
                subColor = AppTheme.Connected;
            }
            else if (cur.Rssi.HasValue)
            {
                subText = $"Paired · Not connected ({cur.Rssi.Value} dBm)";
                subColor = AppTheme.TextSec;
            }
            else
            {
                subText = "Paired · Out of range";
                subColor = AppTheme.SecLabel;
            }

            bool hasRules = store.Contains(normMac) || store.IsAutoPair(normMac);
            PaintMyDeviceText(e.Graphics, icon, cur.Name, subText, subColor, hasRules);
        };

        // Clicking the card opens the rules manager for this device
        card.DoubleClick += (_, _) => onOpenRulesRequested();

        // 1. Trash / Remove button (X=354, W=34, H=34)
        var trash = AppTheme.CreateIconButton(text: "🗑", fg: AppTheme.Danger, w: 34, h: 34);
        trash.Location = new Point(354, 17);
        trash.Click += async (_, _) =>
        {
            var form = card.FindForm();
            var res = MessageBox.Show(form, $"Remove \"{dev.Name}\" ({dev.MacFormatted})?", "Remove Device",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                trash.Enabled = false;
                await onUnpairRequested(dev.Id);
            }
        };
        card.Controls.Add(trash);

        // 2. Connect / Disconnect button (X=260, W=86, H=34)
        if (dev.IsConnected)
        {
            var disc = AppTheme.CreateButton("Disconnect", AppTheme.BtnGray, AppTheme.TextPrim, w: 86, h: 34);
            disc.Location = new Point(260, 17);
            disc.Click += (_, _) =>
            {
                MessageBox.Show(card.FindForm(),
                    $"To disconnect {dev.Name}, power off the device or turn off Bluetooth.",
                    "Disconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            card.Controls.Add(disc);
        }
        else
        {
            var conn = AppTheme.CreateButton("Connect", AppTheme.Teal, AppTheme.TealText, w: 86, h: 34);
            conn.Location = new Point(260, 17);
            conn.Click += async (_, _) =>
            {
                var form = card.FindForm();
                if (dev.CodMajor == 5) // Gamepad / Controller
                {
                    var dlg = MessageBox.Show(form,
                        $"{dev.Name} is a Bluetooth HID controller.\n\n" +
                        "Controllers only reconnect when you wake them up: press the Home/Guide button.\n\n" +
                        "Would you like to unpair and re-pair it fresh instead?",
                        "Controller Reconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (dlg == DialogResult.Yes)
                    {
                        conn.Enabled = false;
                        conn.Text = "Re-pairing...";
                        await onUnpairRequested(dev.Id);
                        MessageBox.Show(form, "Device removed. Put the controller in pairing mode (hold sync button), then click Connect from NEARBY.",
                            "Ready to Pair", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    conn.Enabled = false;
                    conn.Text = "Connecting...";
                    await onPairRequested(dev.Id);
                    conn.Text = "Connect";
                    conn.Enabled = true;
                }
            };
            card.Controls.Add(conn);
        }

        return card;
    }

    public static Panel CreateRuleCard(
        string mac,
        string name,
        BluetoothDeviceInfo? dev,
        AutoForgetStore store,
        Action onStoreChanged,
        Action<string> onStatusNotification)
    {
        var card = new Panel
        {
            Size      = new Size(398, 68),
            BackColor = AppTheme.CardBg,
            Margin    = new Padding(0, 0, 0, 8),
        };
        AppTheme.EnableDoubleBuffer(card);
        card.Region = new Region(AppTheme.RoundedPath(new Rectangle(0, 0, card.Width, card.Height), 10));

        string icon = dev != null ? AppTheme.GetDeviceEmoji(dev) : "🎮";

        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using var path = AppTheme.RoundedPath(new Rectangle(1, 1, card.Width - 2, card.Height - 2), 9);
            using var pen  = new Pen(AppTheme.Border, 1f);
            g.DrawPath(pen, path);

            // Icon
            g.DrawString(icon, AppTheme.FontIcon, new SolidBrush(AppTheme.TextSec), new PointF(10f, 18f));

            // Device Name
            TextRenderer.DrawText(g, name, AppTheme.FontDevName,
                new Rectangle(50, 14, 246, 22), AppTheme.TextPrim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

            // MAC Address
            TextRenderer.DrawText(g, mac, AppTheme.FontDevSub,
                new Rectangle(50, 36, 246, 20), AppTheme.SecLabel,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        };

        bool autoForget = store.Contains(mac);
        bool autoPair   = store.IsAutoPair(mac);

        // 1. Auto-Unpair button (⏏) with ToolTip (X=304, Y=17, W=38, H=34)
        var unpairBtn = new Button
        {
            Text      = "⏏",
            Font      = new Font("Segoe UI", 11f, FontStyle.Bold),
            Size      = new Size(38, 34),
            Location  = new Point(304, 17),
            BackColor = autoForget ? AppTheme.Teal : AppTheme.CardBg,
            ForeColor = autoForget ? AppTheme.TealText : AppTheme.TextSec,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        unpairBtn.FlatAppearance.BorderSize  = 1;
        unpairBtn.FlatAppearance.BorderColor = autoForget ? AppTheme.Teal : AppTheme.Border;

        // 2. Auto-Pair button (🔗) with ToolTip (X=348, Y=17, W=38, H=34)
        var pairBtn = new Button
        {
            Text      = "🔗",
            Font      = new Font("Segoe UI Emoji", 10.5f),
            Size      = new Size(38, 34),
            Location  = new Point(348, 17),
            BackColor = autoPair ? AppTheme.Teal : AppTheme.CardBg,
            ForeColor = autoPair ? AppTheme.TealText : AppTheme.TextSec,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        pairBtn.FlatAppearance.BorderSize  = 1;
        pairBtn.FlatAppearance.BorderColor = autoPair ? AppTheme.Teal : AppTheme.Border;

        unpairBtn.Click += (_, _) =>
        {
            bool nowForget = !store.Contains(mac);
            bool curPair   = store.IsAutoPair(mac);
            store.SetRule(mac, name, autoForget: nowForget, autoPair: curPair);

            unpairBtn.BackColor = nowForget ? AppTheme.Teal : AppTheme.CardBg;
            unpairBtn.ForeColor = nowForget ? AppTheme.TealText : AppTheme.TextSec;
            unpairBtn.FlatAppearance.BorderColor = nowForget ? AppTheme.Teal : AppTheme.Border;

            onStoreChanged();
            onStatusNotification(nowForget
                ? $"Auto-unpair enabled for {name}."
                : $"Auto-unpair disabled for {name}.");
        };

        pairBtn.Click += (_, _) =>
        {
            bool curForget = store.Contains(mac);
            bool nowPair   = !store.IsAutoPair(mac);
            store.SetRule(mac, name, autoForget: curForget, autoPair: nowPair);

            pairBtn.BackColor = nowPair ? AppTheme.Teal : AppTheme.CardBg;
            pairBtn.ForeColor = nowPair ? AppTheme.TealText : AppTheme.TextSec;
            pairBtn.FlatAppearance.BorderColor = nowPair ? AppTheme.Teal : AppTheme.Border;

            onStoreChanged();
            onStatusNotification(nowPair
                ? $"Auto-pair enabled for {name}."
                : $"Auto-pair disabled for {name}.");
        };

        var tip = new ToolTip
        {
            InitialDelay = 150,
            ReshowDelay  = 100,
            AutoPopDelay = 4000,
            ShowAlways   = true,
        };
        tip.SetToolTip(unpairBtn, "Auto-unpair on disconnect");
        tip.SetToolTip(pairBtn, "Auto-pair on discovery");

        card.Controls.AddRange(new Control[] { unpairBtn, pairBtn });
        return card;
    }

    private static Panel CreateBaseCardPanel()
    {
        var card = new Panel
        {
            Size      = new Size(398, 68),
            BackColor = AppTheme.CardBg,
            Margin    = new Padding(0, 0, 0, 8),
        };
        AppTheme.EnableDoubleBuffer(card);
        card.Region = new Region(AppTheme.RoundedPath(new Rectangle(0, 0, card.Width, card.Height), 10));

        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = AppTheme.RoundedPath(new Rectangle(1, 1, card.Width - 2, card.Height - 2), 9);
            using var pen  = new Pen(AppTheme.Border, 1f);
            g.DrawPath(pen, path);
        };

        return card;
    }

    private static void PaintNearbyText(Graphics g, string icon, string name, BluetoothDeviceInfo cur)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        bool isFresh = cur.IsFresh;
        g.DrawString(icon, AppTheme.FontIcon, new SolidBrush(isFresh ? AppTheme.TextPrim : AppTheme.SecLabel), new PointF(10f, 18f));

        // Connect button starts at 302, so width of 244 leaves 8px gap
        TextRenderer.DrawText(g, name, AppTheme.FontDevName,
            new Rectangle(50, 12, 244, 22), isFresh ? AppTheme.TextPrim : AppTheme.TextSec,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        if (isFresh)
        {
            // Fresh / Live Device: Draw glowing green dot and "Just now" badge
            using var pulseBrush = new SolidBrush(Color.FromArgb(220, AppTheme.Connected));
            g.FillEllipse(pulseBrush, 50, 42, 6, 6);

            string rssiText = cur.Rssi.HasValue ? $"{cur.Rssi.Value} dBm" : "Pairing mode";
            string sub = $"Just now  ·  {cur.MacFormatted}  ·  {rssiText}";
            TextRenderer.DrawText(g, sub, AppTheme.FontDevSub,
                new Rectangle(60, 36, 234, 20), AppTheme.Connected,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
        else
        {
            // Inactive / Stale Device
            string sub = $"○ Inactive  ·  {cur.MacFormatted}";
            TextRenderer.DrawText(g, sub, AppTheme.FontDevSub,
                new Rectangle(50, 36, 244, 20), AppTheme.SecLabel,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
    }

    private static void PaintMyDeviceText(Graphics g, string icon, string name, string sub, Color subColor, bool hasRules)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.DrawString(icon, AppTheme.FontIcon, new SolidBrush(AppTheme.TextSec), new PointF(10f, 18f));

        // Name
        int nameWidth = hasRules ? 144 : 200;
        TextRenderer.DrawText(g, name, AppTheme.FontDevName,
            new Rectangle(50, 12, nameWidth, 22), AppTheme.TextPrim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        // If rules are configured, show a subtle teal badge [⚡ Auto]
        if (hasRules)
        {
            var badgeRect = new Rectangle(198, 14, 54, 18);
            using var badgePath = AppTheme.RoundedPath(badgeRect, 4);
            using var badgeBrush = new SolidBrush(Color.FromArgb(32, AppTheme.Teal));
            using var badgePen   = new Pen(Color.FromArgb(120, AppTheme.Teal), 1f);
            g.FillPath(badgeBrush, badgePath);
            g.DrawPath(badgePen, badgePath);

            TextRenderer.DrawText(g, "⚡ Auto", new Font("Segoe UI", 7.5f, FontStyle.Bold),
                badgeRect, AppTheme.Teal,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // Subtext (● Connected, ⏳ Forgetting in 12s..., etc.)
        TextRenderer.DrawText(g, sub, AppTheme.FontDevSub,
            new Rectangle(50, 36, 202, 20), subColor,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}
