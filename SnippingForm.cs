using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// Fullscreen overlay across all monitors. Captures the entire virtual screen
/// up-front (so the form itself isn't in the picture), dims it, and lets the
/// user marquee-select a rectangle. On mouse-up the cropped bitmap is copied
/// to the clipboard. Esc cancels.
/// </summary>
public sealed class SnippingForm : Form
{
    private const int PenWidth = 2;
    // generous reserve for the "W × H" size hint, which sits just outside the
    // selection and flips above it near the bottom edge
    private const int LabelReserveW = 150;
    private const int LabelReserveH = 36;

    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmed;
    private Point _start;
    private Rectangle _sel;
    private bool _selecting;
    private readonly Rectangle _virtualBounds;

    // paint resources live for the life of the form — allocating a Font and three
    // brushes per frame was showing up in the drag
    private readonly Pen _borderPen = new(Color.FromArgb(255, 100, 200, 255), PenWidth);
    private readonly SolidBrush _labelBg = new(Color.FromArgb(180, 0, 0, 0));
    private readonly SolidBrush _labelFg = new(Color.White);
    private readonly SolidBrush _hintFg = new(Color.FromArgb(220, 255, 255, 255));
    private readonly SolidBrush _hintBg = new(Color.FromArgb(160, 0, 0, 0));
    private readonly Font _labelFont = new("Segoe UI", 9f, FontStyle.Regular);
    private readonly Font _hintFont = new("Segoe UI", 11f, FontStyle.Regular);

    public Bitmap? Result { get; private set; }

    public SnippingForm()
    {
        _virtualBounds = SystemInformation.VirtualScreen;

        // Snapshot the entire virtual screen before the form appears. PArgb is the
        // format GDI+ blits fastest — no per-pixel premultiply on every draw.
        _screenshot = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_screenshot))
            g.CopyFromScreen(_virtualBounds.Location, Point.Empty, _virtualBounds.Size);

        // Bake the dim once instead of alpha-blending the whole virtual screen on
        // every mouse move. Painting then becomes two straight copies.
        _dimmed = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_dimmed))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_screenshot, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, _dimmed.Width, _dimmed.Height);
        }

        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = _virtualBounds;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        BackColor = Color.Black;
        Text = "GroqVoice — snipping";

        SetStyle(ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint      // no WM_ERASEBKGND round trip
               | ControlStyles.OptimizedDoubleBuffer, true);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp   += OnMouseUp;
        KeyDown   += OnKeyDown;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            // WS_EX_TOOLWINDOW: don't appear in Alt+Tab
            p.ExStyle |= 0x00000080;
            return p;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
    }

    // Everything visible is opaque and painted below, so the default erase — a
    // full-virtual-screen fill — is pure waste.
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;      // keeps the 1:1 copies pixel-exact

        // Only ever touch the invalidated area. During a drag that is a band around
        // the selection rather than the whole desktop.
        var clip = Rectangle.Intersect(Rectangle.Ceiling(g.VisibleClipBounds), ClientRectangle);
        if (clip.Width <= 0 || clip.Height <= 0) return;

        // 1) Dimmed backdrop — already blended, so a straight copy
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        // 2) Punch the selection back through at full brightness
        if (_sel.Width > 0 && _sel.Height > 0)
        {
            var bright = Rectangle.Intersect(_sel, clip);
            if (bright.Width > 0 && bright.Height > 0)
                g.DrawImage(_screenshot, bright, bright, GraphicsUnit.Pixel);
        }

        g.CompositingMode = CompositingMode.SourceOver;

        if (_sel.Width > 0 && _sel.Height > 0)
        {
            g.DrawRectangle(_borderPen, _sel);

            var sizeText = $"{_sel.Width} × {_sel.Height}";
            var sz = g.MeasureString(sizeText, _labelFont);
            var labelRect = new RectangleF(_sel.Right - sz.Width - 8, _sel.Bottom + 4, sz.Width + 8, sz.Height + 2);
            if (labelRect.Bottom > ClientRectangle.Bottom) labelRect.Y = _sel.Top - sz.Height - 6;
            if (labelRect.X < 0) labelRect.X = 0;
            g.FillRectangle(_labelBg, labelRect);
            g.DrawString(sizeText, _labelFont, _labelFg, labelRect.X + 4, labelRect.Y + 1);
        }

        // Hint text bottom-center
        if (!_selecting && _sel.Width == 0)
        {
            string hint = "Drag to select region  •  Esc to cancel";
            var sz = g.MeasureString(hint, _hintFont);
            var x = (ClientRectangle.Width - sz.Width) / 2;
            var y = ClientRectangle.Height - sz.Height - 30;
            g.FillRectangle(_hintBg, x - 12, y - 6, sz.Width + 24, sz.Height + 12);
            g.DrawString(hint, _hintFont, _hintFg, x, y);
        }
    }

    /// <summary>Selection rect plus its border and size hint — the area a repaint must cover.</summary>
    private static Rectangle ChromeBounds(Rectangle sel)
    {
        if (sel.Width <= 0 || sel.Height <= 0) return Rectangle.Empty;
        var r = Rectangle.Inflate(sel, PenWidth + 1, PenWidth + 1);
        var label = new Rectangle(sel.Right - LabelReserveW, sel.Top - LabelReserveH,
                                  LabelReserveW, sel.Height + 2 * LabelReserveH);
        return Rectangle.Union(r, label);
    }

    private void InvalidateSelection(Rectangle before, Rectangle after)
    {
        var a = ChromeBounds(before);
        var b = ChromeBounds(after);
        var area = a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);
        if (area.IsEmpty) return;
        Invalidate(Rectangle.Intersect(area, ClientRectangle));
    }

    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _selecting = true;
        _start = e.Location;
        _sel = new Rectangle(_start, Size.Empty);
        Invalidate();   // once, to clear the hint line
    }

    private void OnMouseMove(object? s, MouseEventArgs e)
    {
        if (!_selecting) return;
        int x = Math.Min(_start.X, e.X);
        int y = Math.Min(_start.Y, e.Y);
        int w = Math.Abs(e.X - _start.X);
        int h = Math.Abs(e.Y - _start.Y);

        var next = new Rectangle(x, y, w, h);
        if (next == _sel) return;       // mouse moved within the same pixel

        var prev = _sel;
        _sel = next;
        InvalidateSelection(prev, next);
    }

    private void OnMouseUp(object? s, MouseEventArgs e)
    {
        if (!_selecting) return;
        _selecting = false;

        if (_sel.Width >= 4 && _sel.Height >= 4)
        {
            // Clamp to screenshot bounds (defensive)
            var clamped = Rectangle.Intersect(_sel, new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
            if (clamped.Width >= 4 && clamped.Height >= 4)
            {
                // 24bpp RGB (no alpha) — legacy editors (Photoshop, Krita, Paint, MS Office)
                // mishandle ARGB pastes because CF_DIB has no agreed alpha convention.
                var crop = new Bitmap(clamped.Width, clamped.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(crop))
                {
                    g.DrawImage(_screenshot,
                        new Rectangle(0, 0, clamped.Width, clamped.Height),
                        clamped,
                        GraphicsUnit.Pixel);
                }
                Result = crop;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
        }
        // too small — just stay open and let user try again
        _sel = Rectangle.Empty;
        Invalidate();
    }

    private void OnKeyDown(object? s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshot.Dispose();
            _dimmed.Dispose();
            _borderPen.Dispose();
            _labelBg.Dispose();
            _labelFg.Dispose();
            _hintFg.Dispose();
            _hintBg.Dispose();
            _labelFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
