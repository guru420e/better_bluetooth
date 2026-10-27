using System;
using System.Drawing;
using System.Windows.Forms;
using BluetoothRectifier.UI.Theme;

namespace BluetoothRectifier.UI.Components;

/// <summary>
/// Bottom footer panel (440x80) displaying the top divider, auto-fix checkbox,
/// and live status label.
/// </summary>
public sealed class FooterPanel : Panel
{
    private readonly CheckBox _autoFixCheckBox;
    private readonly Label    _footerStatusLabel;

    public event Action<bool>? AutoFixChanged;

    public FooterPanel()
    {
        Location  = new Point(0, 520);
        Size      = new Size(440, 80);
        BackColor = AppTheme.Bg;

        _autoFixCheckBox = new CheckBox
        {
            Text      = "Fix stale pairings automatically",
            Font      = AppTheme.FontFooter,
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(14, 28),
            Checked   = false,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };
        _autoFixCheckBox.CheckedChanged += (_, _) => AutoFixChanged?.Invoke(_autoFixCheckBox.Checked);

        _footerStatusLabel = new Label
        {
            Text      = "Idle",
            Font      = AppTheme.FontStatus,
            ForeColor = AppTheme.TextSec,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(380, 32),
        };

        Controls.AddRange(new Control[] { _autoFixCheckBox, _footerStatusLabel });
    }

    public bool AutoFixEnabled
    {
        get => _autoFixCheckBox.Checked;
        set => _autoFixCheckBox.Checked = value;
    }

    public void SetStatusText(string text)
    {
        _footerStatusLabel.Text = text.Length > 25 ? text.Substring(0, 22) + "..." : text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AppTheme.Border, 1f);
        e.Graphics.DrawLine(pen, 16, 0, 424, 0);
    }
}
