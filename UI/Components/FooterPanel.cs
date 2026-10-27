using System;
using System.Drawing;
using System.Windows.Forms;
using BetterBluetooth.UI.Theme;

namespace BetterBluetooth.UI.Components;

/// <summary>
/// Bottom footer panel (440x80) displaying the top divider, master automation
/// checkboxes (Auto-unpair & Auto-pair), and live status label.
/// </summary>
public sealed class FooterPanel : Panel
{
    private readonly CheckBox _autoForgetCheckBox;
    private readonly CheckBox _autoPairCheckBox;
    private readonly Label    _footerStatusLabel;

    public event Action<bool>? AutoForgetChanged;
    public event Action<bool>? AutoPairChanged;

    public FooterPanel()
    {
        Location  = new Point(0, 520);
        Size      = new Size(440, 80);
        BackColor = AppTheme.Bg;

        _autoForgetCheckBox = new CheckBox
        {
            Text      = "Auto-unpair",
            Font      = AppTheme.FontFooter,
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(14, 28),
            Checked   = true,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };
        _autoForgetCheckBox.CheckedChanged += (_, _) => AutoForgetChanged?.Invoke(_autoForgetCheckBox.Checked);

        _autoPairCheckBox = new CheckBox
        {
            Text      = "Auto-pair",
            Font      = AppTheme.FontFooter,
            ForeColor = AppTheme.TextPrim,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(135, 28),
            Checked   = true,
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
        };
        _autoPairCheckBox.CheckedChanged += (_, _) => AutoPairChanged?.Invoke(_autoPairCheckBox.Checked);

        _footerStatusLabel = new Label
        {
            Text      = "Idle",
            Font      = AppTheme.FontStatus,
            ForeColor = AppTheme.TextSec,
            BackColor = AppTheme.Bg,
            AutoSize  = true,
            Location  = new Point(360, 32),
        };

        Controls.AddRange(new Control[] { _autoForgetCheckBox, _autoPairCheckBox, _footerStatusLabel });
    }

    public bool AutoForgetEnabled
    {
        get => _autoForgetCheckBox.Checked;
        set => _autoForgetCheckBox.Checked = value;
    }

    public bool AutoPairEnabled
    {
        get => _autoPairCheckBox.Checked;
        set => _autoPairCheckBox.Checked = value;
    }

    public bool AutoFixEnabled
    {
        get => AutoForgetEnabled;
        set => AutoForgetEnabled = value;
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
