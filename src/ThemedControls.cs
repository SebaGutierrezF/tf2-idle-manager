using System.Drawing.Drawing2D;

namespace TF2IdleManager;

// Native WinForms disabled controls may ignore ForeColor on dark backgrounds.
// Paint the disabled/checked state ourselves; keep Enabled unchanged so commands
// can never be edited while a managed TF2 session is running.
public sealed class ThemedCheckBox : CheckBox
{
    private bool _darkAppearance;
    public bool DarkAppearance
    {
        get => _darkAppearance;
        set { if (_darkAppearance == value) return; _darkAppearance = value; Invalidate(); }
    }

    public ThemedCheckBox()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!DarkAppearance) { base.OnPaint(e); return; }

        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        int side = Math.Max(14, (int)Math.Round(15 * scale));
        int pad = Math.Max(2, (int)Math.Round(3 * scale));
        var square = new Rectangle(pad, (Height - side) / 2, side, side);
        bool selected = CheckState != CheckState.Unchecked;
        var frame = Enabled ? Color.FromArgb(127, 155, 185) : Color.FromArgb(114, 128, 146);
        var fill = selected
            ? (Enabled ? Color.FromArgb(54, 130, 206) : Color.FromArgb(77, 103, 129))
            : Color.FromArgb(40, 52, 66);
        using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, square);
        using (var border = new Pen(frame, 1f)) e.Graphics.DrawRectangle(border, square);
        if (selected)
        {
            using var mark = new Pen(Enabled ? Color.White : Color.FromArgb(225, 234, 243), Math.Max(1.6f, 2f * scale));
            mark.StartCap = LineCap.Round;
            mark.EndCap = LineCap.Round;
            if (CheckState == CheckState.Indeterminate)
                e.Graphics.DrawLine(mark, square.Left + side * .24f, square.Top + side * .50f,
                    square.Left + side * .77f, square.Top + side * .50f);
            else
                e.Graphics.DrawLines(mark, new[] {
                    new PointF(square.Left + side * .20f, square.Top + side * .50f),
                    new PointF(square.Left + side * .43f, square.Top + side * .73f),
                    new PointF(square.Left + side * .82f, square.Top + side * .27f)
                });
        }
        int textX = square.Right + Math.Max(5, (int)Math.Round(6 * scale));
        var textRect = new Rectangle(textX, 0, Math.Max(0, Width - textX - 2), Height);
        var textColor = Enabled ? ForeColor : Color.FromArgb(174, 190, 205);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues && Enabled)
        {
            var focusRect = Rectangle.Inflate(textRect, -1, -3);
            ControlPaint.DrawFocusRectangle(e.Graphics, focusRect, ForeColor, BackColor);
        }
    }
}

public sealed class ThemedButton : Button
{
    private bool _darkAppearance;
    public bool DarkAppearance
    {
        get => _darkAppearance;
        set { if (_darkAppearance == value) return; _darkAppearance = value; Invalidate(); }
    }

    public ThemedButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!DarkAppearance) { base.OnPaint(e); return; }
        var background = BackColor;
        // We deliberately paint disabled captions with a readable slate tone,
        // rather than the dark system-disabled text color WinForms chooses.
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, ClientRectangle);
        var borderColor = Enabled ? Color.FromArgb(77, 98, 119) : Color.FromArgb(65, 80, 98);
        if (FlatAppearance.BorderSize != 0)
        {
            using var pen = new Pen(borderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
        var foreground = Enabled ? ForeColor : Color.FromArgb(188, 201, 215);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft)
            flags = (flags & ~TextFormatFlags.HorizontalCenter) | TextFormatFlags.Left;
        else if (TextAlign is ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight)
            flags = (flags & ~TextFormatFlags.HorizontalCenter) | TextFormatFlags.Right;
        var textArea = Rectangle.Inflate(ClientRectangle, -7, -2);
        TextRenderer.DrawText(e.Graphics, Text, Font, textArea, foreground, flags);
        if (Focused && ShowFocusCues && Enabled)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), foreground, background);
    }
}
