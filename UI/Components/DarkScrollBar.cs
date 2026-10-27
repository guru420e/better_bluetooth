using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BetterBluetooth.UI.Theme;

namespace BetterBluetooth.UI.Components;

/// <summary>
/// A slim, custom-drawn 5px dark scrollbar control that blends seamlessly into
/// the dark UI without ever spawning native Windows white scrollbars.
/// </summary>
public sealed class DarkScrollBar : Panel
{
    private int _scrollOffset;
    private int _viewportHeight;
    private int _contentHeight;

    private bool _isDragging;
    private int  _dragStartY;
    private int  _dragStartOffset;

    public event Action<int>? ScrollChanged;

    public DarkScrollBar()
    {
        Size      = new Size(5, 428);
        BackColor = AppTheme.Bg;
        Cursor    = Cursors.Hand;
        AppTheme.EnableDoubleBuffer(this);
    }

    public int ScrollOffset
    {
        get => _scrollOffset;
        set
        {
            int clamped = Math.Clamp(value, 0, MaxScroll);
            if (_scrollOffset != clamped)
            {
                _scrollOffset = clamped;
                Invalidate();
                ScrollChanged?.Invoke(_scrollOffset);
            }
        }
    }

    public int MaxScroll => Math.Max(0, _contentHeight - _viewportHeight);

    public void UpdateMetrics(int viewportHeight, int contentHeight)
    {
        _viewportHeight = viewportHeight;
        _contentHeight  = contentHeight;

        int max = MaxScroll;
        if (_scrollOffset > max)
        {
            _scrollOffset = max;
            ScrollChanged?.Invoke(_scrollOffset);
        }
        Invalidate();
    }

    public void ApplyWheelDelta(int delta)
    {
        int max = MaxScroll;
        if (max <= 0)
        {
            ScrollOffset = 0;
            return;
        }

        int step = (delta / 120) * 44;
        ScrollOffset = Math.Clamp(_scrollOffset - step, 0, max);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        int max = MaxScroll;
        if (max <= 0 || _contentHeight <= 0)
            return; // Content fits, thumb not drawn

        int trackH = Height;
        float ratio = (float)_viewportHeight / _contentHeight;
        int thumbH = Math.Clamp((int)(trackH * ratio), 28, trackH);
        int maxThumbY = trackH - thumbH;
        int thumbY = max > 0 ? (int)((float)_scrollOffset / max * maxThumbY) : 0;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(_isDragging ? AppTheme.Teal : AppTheme.ScrollThumb);
        using var path  = AppTheme.RoundedPath(new Rectangle(0, thumbY, Width, thumbH), 2);
        g.FillPath(brush, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && MaxScroll > 0)
        {
            _isDragging = true;
            _dragStartY = e.Y;
            _dragStartOffset = _scrollOffset;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging && MaxScroll > 0)
        {
            int trackH = Height;
            float ratio = (float)_viewportHeight / _contentHeight;
            int thumbH = Math.Clamp((int)(trackH * ratio), 28, trackH);
            int maxThumbY = trackH - thumbH;

            if (maxThumbY > 0)
            {
                int deltaY = e.Y - _dragStartY;
                int newOffset = _dragStartOffset + (int)((float)deltaY / maxThumbY * MaxScroll);
                ScrollOffset = newOffset;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            Invalidate();
        }
    }
}
