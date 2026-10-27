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
        AutoForgetStore store,
        Func<string, Task<DevicePairingResultStatus>> onPairRequested,
        Action<string> onStatusNotification)
    {
        var card = CreateBaseCardPanel();
        card.Tag = dev;
        string icon = AppTheme.GetDeviceEmoji(dev);

        card.Paint += (s, e) =>
        {
            var cur = (BluetoothDeviceInfo)((Control)s!).Tag!;
            string sub = $"{cur.MacFormatted}  ·  {(cur.Rssi.HasValue ? $"{cur.Rssi.Value} dBm" : "Pairing mode")}";
            PaintNearbyText(e.Graphics, icon, cur.Name, sub);
        };

        // 1. Auto-Pair toggle button (X=262, W=34, H=34)
        bool autoPairOn = store.IsAutoPair(dev.Mac);
        var autoPairBtn = new Button
        {
            Text      = "⚡",
            Font      = AppTheme.FontBtn,
            Size      = new Size(34, 34),
            Location  = new Point(262, 17),
            BackColor = autoPairOn ? AppTheme.Teal : AppTheme.CardBg,
            ForeColor = autoPairOn ? AppTheme.TealText : AppTheme.TextSec,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        autoPairBtn.FlatAppearance.BorderSize  = 1;
        autoPairBtn.FlatAppearance.BorderColor = autoPairOn ? AppTheme.Teal : AppTheme.Border;
        autoPairBtn.Click += (_, _) =>
        {
            bool nowOn = store.ToggleAutoPair(dev.Mac);
            autoPairBtn.BackColor = nowOn ? AppTheme.Teal : AppTheme.CardBg;
            autoPairBtn.ForeColor = nowOn ? AppTheme.TealText : AppTheme.TextSec;
            autoPairBtn.FlatAppearance.BorderColor = nowOn ? AppTheme.Teal : AppTheme.Border;

            if (nowOn)
                onStatusNotification($"Auto-pair enabled for {dev.Name}. Will pair automatically when detected!");
            else
                onStatusNotification($"Auto-pair disabled for {dev.Name}.");
        };
        card.Controls.Add(autoPairBtn);

        // 2. Connect button (X=302, W=86, H=34)
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
        Action onStoreChanged,
        Action<string> onStatusNotification)
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

            PaintMyDeviceText(e.Graphics, icon, cur.Name, subText, subColor);
        };

        // 1. Trash / Remove button (X=354, W=34)
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

        // 2. Auto-forget toggle button (X=314, W=34)
        bool autoForgetOn = store.Contains(dev.Mac);
        var toggle = new Button
        {
            Text      = "≡×",
            Font      = AppTheme.FontBtn,
            Size      = new Size(34, 34),
            Location  = new Point(314, 17),
            BackColor = autoForgetOn ? AppTheme.Teal : AppTheme.CardBg,
            ForeColor = autoForgetOn ? AppTheme.TealText : AppTheme.TextSec,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        toggle.FlatAppearance.BorderSize  = 1;
        toggle.FlatAppearance.BorderColor = autoForgetOn ? AppTheme.Teal : AppTheme.Border;
        toggle.Click += (_, _) =>
        {
            bool nowOn = store.Toggle(dev.Mac);
            toggle.BackColor = nowOn ? AppTheme.Teal : AppTheme.CardBg;
            toggle.ForeColor = nowOn ? AppTheme.TealText : AppTheme.TextSec;
            toggle.FlatAppearance.BorderColor = nowOn ? AppTheme.Teal : AppTheme.Border;
            onStoreChanged();

            if (nowOn)
                onStatusNotification($"Auto-fix active for {dev.Name}: will auto-forget on disconnect & auto-pair when discoverable.");
            else
                onStatusNotification($"Auto-fix disabled for {dev.Name}.");
        };
        card.Controls.Add(toggle);

        // 3. Connect / Disconnect button
        if (dev.IsConnected)
        {
            var disc = AppTheme.CreateButton("Disconnect", AppTheme.BtnGray, AppTheme.TextPrim, w: 96, h: 34);
            disc.Location = new Point(210, 17);
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
            conn.Location = new Point(220, 17);
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

    private static void PaintNearbyText(Graphics g, string icon, string name, string sub)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.DrawString(icon, AppTheme.FontIcon, new SolidBrush(AppTheme.TextSec), new PointF(10f, 18f));

        // Auto-pair button starts at 262, so width of 204 leaves 8px gap
        TextRenderer.DrawText(g, name, AppTheme.FontDevName,
            new Rectangle(50, 12, 204, 22), AppTheme.TextPrim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, sub, AppTheme.FontDevSub,
            new Rectangle(50, 36, 204, 20), AppTheme.TextSec,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private static void PaintMyDeviceText(Graphics g, string icon, string name, string sub, Color subColor)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.DrawString(icon, AppTheme.FontIcon, new SolidBrush(AppTheme.TextSec), new PointF(10f, 18f));

        // Buttons start at 210, so width of 152 leaves 8px gap before buttons
        TextRenderer.DrawText(g, name, AppTheme.FontDevName,
            new Rectangle(50, 12, 152, 22), AppTheme.TextPrim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, sub, AppTheme.FontDevSub,
            new Rectangle(50, 36, 152, 20), subColor,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}
