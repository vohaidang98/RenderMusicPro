using System.Drawing.Drawing2D;

namespace RandomVideoDangVH;

internal static class StudioDrawing
{
    public static readonly Color Background = Color.FromArgb(12, 15, 20);
    public static readonly Color Surface = Color.FromArgb(22, 27, 35);
    public static readonly Color Input = Color.FromArgb(15, 19, 26);
    public static readonly Color Border = Color.FromArgb(43, 52, 65);
    public static readonly Color Accent = Color.FromArgb(89, 220, 163);

    public static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class CardPanel : Panel
{
    public int Radius { get; set; } = 12;
    public Color FillColor { get; set; } = StudioDrawing.Surface;
    public Color BorderColor { get; set; } = StudioDrawing.Border;
    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = StudioDrawing.Surface;
        Padding = new Padding(1);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(StudioDrawing.Background);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = StudioDrawing.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), Radius * DeviceDpi / 96F);
        using var fill = new SolidBrush(FillColor);
        using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal enum StudioSymbol { None, Play, Stop, Save, Folder, Network, Video, Clean }

internal sealed class RoundedButton : Button
{
    public Color Color1 { get; set; } = Color.FromArgb(64, 112, 238);
    public Color Color2 { get; set; } = Color.FromArgb(85, 111, 229);
    public Color HoverColor1 { get; set; } = Color.FromArgb(85, 135, 255);
    public Color HoverColor2 { get; set; } = Color.FromArgb(111, 135, 244);
    public bool FolderIcon { get; set; }
    public StudioSymbol Symbol { get; set; }
    private bool _hover;
    private bool _pressed;
    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false; BackColor = StudioDrawing.Surface;
        Cursor = Cursors.Hand; TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = StudioDrawing.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), (FolderIcon ? 6 : 8) * DeviceDpi / 96F);
        Color first = Enabled ? (_hover ? HoverColor1 : Color1) : Color.FromArgb(30, 40, 55);
        Color second = Enabled ? (_hover ? HoverColor2 : Color2) : first;
        if (_pressed) { first = ControlPaint.Dark(first, .1F); second = ControlPaint.Dark(second, .1F); }
        using var brush = new LinearGradientBrush(rect, first, second, 0F);
        e.Graphics.FillPath(brush, path);
        Color ink = Enabled ? ForeColor : Color.FromArgb(115, 130, 151);
        if (FolderIcon)
        {
            float scale = DeviceDpi / 96F;
            float x = (Width - 18 * scale) / 2, y = (Height - 14 * scale) / 2;
            using var pen = new Pen(ink, 1.5F * scale) { LineJoin = LineJoin.Round };
            PointF P(float dx, float dy) => new(x + dx * scale, y + dy * scale);
            e.Graphics.DrawLines(pen, new[] { P(0, 12), P(0, 1), P(6, 1), P(8, 4), P(17, 4), P(17, 12), P(0, 12) });
            e.Graphics.DrawLine(pen, P(0, 6), P(17, 6));
        }
        else
        {
            var textBounds = rect;
            if (Symbol != StudioSymbol.None)
            {
                float scale = DeviceDpi / 96F;
                int textWidth = TextRenderer.MeasureText(Text, Font).Width;
                float x = Math.Max(10 * scale, (Width - textWidth - 24 * scale) / 2F);
                float y = (Height - 16 * scale) / 2F;
                StudioIcons.Draw(e.Graphics, Symbol, ink, x, y, scale);
                textBounds = new Rectangle((int)(x + 24 * scale), 0, Math.Max(0, Width - (int)(x + 24 * scale) - 8), Height);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ink,
                (Symbol == StudioSymbol.None ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(StudioDrawing.Accent, 1.5F);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

// Preserve NumericUpDown's value validation, keyboard editing and accessibility.
// Its native spinner is replaced by two dark buttons in the surrounding field.
internal sealed class StudioNumericUpDown : NumericUpDown
{
    private bool _arranging;
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_arranging) return;
        _arranging = true;
        try
        {
            foreach (Control child in Controls)
            {
                if (child is TextBox edit) edit.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
                else { child.Visible = false; child.SetBounds(-100, -100, 1, 1); }
            }
        }
        finally { _arranging = false; }
        // The editor keeps a gap where the native spinner was; clip it away so no white block shows.
        foreach (Control child in Controls)
        {
            if (child is not TextBox edit || edit.Width <= 0 || edit.Width >= Width) continue;
            var old = Region;
            Region = new Region(new Rectangle(0, 0, edit.Width, Height));
            old?.Dispose();
        }
    }
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is Control spin && spin is not TextBox) { spin.VisibleChanged += (s, _) => { if (s is Control c && c.Visible) c.Visible = false; }; spin.Visible = false; }
    }
}

internal sealed class StudioInputHost : Panel
{
    private readonly Control _input;
    private readonly Control? _trailing;
    private readonly RoundedButton? _minus;
    private readonly RoundedButton? _plus;
    private readonly ToolTip _tip = new();
    private bool _arranging;
    private float _scale = 1F;

    public StudioInputHost(Control input, Control? trailing = null)
    {
        _input = input; _trailing = trailing;
        BackColor = StudioDrawing.Surface;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        input.Dock = DockStyle.None; input.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        input.BackColor = StudioDrawing.Input;
        if (input is TextBox tb) { tb.AutoSize = true; tb.BorderStyle = BorderStyle.None; }
        if (input is NumericUpDown numeric)
        {
            numeric.AutoSize = true; numeric.BorderStyle = BorderStyle.None;
            _minus = StepButton("−", "Giảm giá trị"); _plus = StepButton("+", "Tăng giá trị");
            _minus.Click += (_, _) => numeric.DownButton(); _plus.Click += (_, _) => numeric.UpButton();
            Controls.Add(_minus); Controls.Add(_plus);
            numeric.ValueChanged += (_, _) => SyncState();
        }
        Controls.Add(input);
        if (trailing != null) Controls.Add(trailing);
        input.Enter += (_, _) => Invalidate(); input.Leave += (_, _) => Invalidate();
        input.EnabledChanged += (_, _) => { SyncState(); Invalidate(); };
        input.FontChanged += (_, _) => PerformLayout();
        input.TextChanged += (_, _) => _tip.SetToolTip(input, input.Text);
        SyncState();
    }

    private RoundedButton StepButton(string text, string name) => new()
    {
        Text = text, AccessibleName = name, Font = new Font("Segoe UI", 10F),
        Color1 = Color.FromArgb(26, 38, 55), Color2 = Color.FromArgb(26, 38, 55),
        HoverColor1 = Color.FromArgb(42, 61, 81), HoverColor2 = Color.FromArgb(42, 61, 81),
        ForeColor = Color.FromArgb(186, 204, 226), BackColor = StudioDrawing.Input, TabStop = true
    };
    private void SyncState()
    {
        if (_input is NumericUpDown number && _minus != null && _plus != null)
        { _minus.Enabled = number.Enabled && number.Value > number.Minimum; _plus.Enabled = number.Enabled && number.Value < number.Maximum; }
        if (_trailing != null) _trailing.Enabled = _input.Enabled;
    }
    public void RefreshState()
    {
        SyncState();
        Invalidate();
    }
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        _scale *= factor.Height;
        base.ScaleControl(factor, specified);
        PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_input == null || _arranging) return;
        _arranging = true;
        try
        {
            int P(float value) => (int)Math.Round(value * Math.Max(_scale, DeviceDpi / 96F));
            int height = Math.Min(ClientSize.Height, P(40));
            int top = Math.Max(0, (ClientSize.Height - height) / 2);
            int right = P(10);
            if (_trailing != null)
            {
                _trailing.SetBounds(Math.Max(0, ClientSize.Width - P(39)), top + P(5), P(34), Math.Max(0, height - P(10)));
                right = P(44);
            }
            if (_minus != null && _plus != null)
            {
                _minus.SetBounds(Math.Max(0, ClientSize.Width - P(65)), top + P(6), P(28), Math.Max(0, height - P(12)));
                _plus.SetBounds(Math.Max(0, ClientSize.Width - P(34)), top + P(6), P(28), Math.Max(0, height - P(12)));
                right = P(70);
            }
            int nativeHeight = _input switch
            {
                TextBox tb => tb.PreferredHeight,
                NumericUpDown numeric => numeric.PreferredHeight,
                ComboBox combo => combo.PreferredHeight,
                _ => _input.PreferredSize.Height
            };
            _input.SetBounds(P(11), top + Math.Max(0, (height - nativeHeight) / 2), Math.Max(1, ClientSize.Width - P(12) - right), nativeHeight);
            Invalidate();
        }
        finally { _arranging = false; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 3 || Height < 3) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Max(_scale, DeviceDpi / 96F);
        float height = Math.Min(Height, 40 * scale);
        var bounds = new RectangleF(.5F, (Height - height) / 2 + .5F, Width - 1, height - 1);
        using var shape = StudioDrawing.Round(bounds, 8 * scale);
        using var fill = new SolidBrush(StudioDrawing.Input);
        e.Graphics.FillPath(fill, shape);
        using var border = new Pen(_input.ContainsFocus ? StudioDrawing.Accent : _input.Enabled ? StudioDrawing.Border : Color.FromArgb(32, 43, 60));
        e.Graphics.DrawPath(border, shape);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
}

internal sealed class StudioComboBox : ComboBox
{
    public StudioComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat;
        BackColor = StudioDrawing.Input; ForeColor = Color.FromArgb(235, 242, 250);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using var brush = new SolidBrush(highlighted ? Color.FromArgb(36, 60, 83) : BackColor);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    protected override void OnFontChanged(EventArgs e)
    { base.OnFontChanged(e); ItemHeight = Font.Height + 4; }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        bool print = m.Msg is 0x317 or 0x318;
        if ((m.Msg != 0xF && !print) || !IsHandleCreated || Width < 20) return;
        if (print && m.WParam == IntPtr.Zero) return;
        using var graphics = print ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle);
        using var brush = new SolidBrush(BackColor);
        int width = (int)Math.Round(20 * DeviceDpi / 96F);
        graphics.FillRectangle(brush, ClientRectangle);
        string text = (SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : Text) ?? string.Empty;
        TextRenderer.DrawText(graphics, text, Font, new Rectangle(0, 0, Math.Max(1, Width - width - 4), Height),
            Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var arrow = new Pen(Enabled ? ForeColor : SystemColors.GrayText, 1.5F);
        float x = Width - width / 2F, y = Height / 2F;
        graphics.DrawLines(arrow, new[] { new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2) });
    }
}

internal sealed class StudioProgressBar : Control
{
    private int _value;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public int Value { get => _value; set { _value = Math.Clamp(value, Minimum, Maximum); Invalidate(); } }
    public StudioProgressBar()
    { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); BackColor = StudioDrawing.Surface; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = StudioDrawing.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F);
        using var track = new SolidBrush(Color.FromArgb(34, 47, 65));
        e.Graphics.FillPath(track, path);
        if (Value <= Minimum) return;
        float width = (Width - 1) * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
        using var fillPath = StudioDrawing.Round(new RectangleF(0, 0, Math.Max(1, width), Height - 1), Height / 2F);
        using var fill = new SolidBrush(StudioDrawing.Accent);
        e.Graphics.FillPath(fill, fillPath);
    }
}

internal static class StudioIcons
{
    public static void Draw(Graphics g, StudioSymbol symbol, Color color, float x, float y, float scale)
    {
        using var pen = new Pen(color, 1.5F * scale) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new SolidBrush(color);
        PointF P(float a, float b) => new(x + a * scale, y + b * scale);
        void Line(float a, float b, float c, float d) => g.DrawLine(pen, P(a,b), P(c,d));
        void Box(float a, float b, float w, float h) => g.DrawRectangle(pen, x+a*scale, y+b*scale, w*scale, h*scale);
        switch(symbol)
        {
            case StudioSymbol.Play: g.FillPolygon(fill, new[] { P(4,2), P(14,8), P(4,14) }); break;
            case StudioSymbol.Stop: g.FillRectangle(fill, x+4*scale,y+4*scale,8*scale,8*scale); break;
            case StudioSymbol.Save: Box(2,2,12,12); Box(5,2,6,4); Box(5,10,6,4); break;
            case StudioSymbol.Folder:
                g.DrawLines(pen, new[] { P(1,13), P(1,3), P(6,3), P(8,5), P(15,5), P(15,13), P(1,13) }); break;
            case StudioSymbol.Network: Box(6,1,4,4); Box(1,11,4,4); Box(11,11,4,4); Line(8,5,8,8); Line(3,8,13,8); Line(3,8,3,11); Line(13,8,13,11); break;
            case StudioSymbol.Video: Box(1,3,10,10); g.DrawLines(pen,new[] { P(11,6), P(15,4), P(15,12), P(11,10) }); break;
            case StudioSymbol.Clean: Line(10,1,6,8); g.DrawPolygon(pen,new[] { P(5,7), P(11,10), P(8,15), P(1,12) }); break;
        }
    }
}

internal sealed class StudioBrandMark : Control
{
    public StudioBrandMark() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(StudioDrawing.Background);
        using var path = StudioDrawing.Round(new RectangleF(0,0,Width-1,Height-1), 14 * DeviceDpi/96F);
        using var fill = new SolidBrush(Color.FromArgb(26,58,46)); e.Graphics.FillPath(fill,path);
        using var pen = new Pen(StudioDrawing.Accent, 3F * DeviceDpi/96F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var bars = new[] { .2F, .44F, .68F, .36F, .54F };
        for(int i=0;i<bars.Length;i++)
        {
            float x = Width * (.25F + i*.125F), height=Height*bars[i];
            e.Graphics.DrawLine(pen,x,(Height-height)/2,x,(Height+height)/2);
        }
    }
}

internal sealed class StudioStepBadge : Control
{
    public StudioStepBadge() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Font = new Font("Segoe UI Semibold", 10F); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(StudioDrawing.Surface);
        using var path = StudioDrawing.Round(new RectangleF(0,0,Width-1,Height-1),8*DeviceDpi/96F);
        using var fill = new SolidBrush(Color.FromArgb(28,49,43)); e.Graphics.FillPath(fill,path);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,StudioDrawing.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
}
