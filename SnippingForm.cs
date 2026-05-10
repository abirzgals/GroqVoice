using System.Drawing;
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
    private readonly Bitmap _screenshot;
    private Point _start;
    private Rectangle _sel;
    private bool _selecting;
    private readonly Rectangle _virtualBounds;

    public Bitmap? Result { get; private set; }

    public SnippingForm()
    {
        _virtualBounds = SystemInformation.VirtualScreen;

        // Snapshot the entire virtual screen before the form appears.
        _screenshot = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_screenshot))
            g.CopyFromScreen(_virtualBounds.Location, Point.Empty, _virtualBounds.Size);

        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = _virtualBounds;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;
        Text = "GroqVoice — snipping";

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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        // 1) Full screenshot underneath
        g.DrawImage(_screenshot, 0, 0, _screenshot.Width, _screenshot.Height);
        // 2) Dim overlay
        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillRectangle(dim, ClientRectangle);
        // 3) "Punch through" the selection so the original screenshot shows there
        if (_sel.Width > 0 && _sel.Height > 0)
        {
            g.DrawImage(_screenshot, _sel, _sel, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(255, 100, 200, 255), 2);
            g.DrawRectangle(pen, _sel);

            // Size hint
            var sizeText = $"{_sel.Width} × {_sel.Height}";
            using var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            using var fg = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 9f, FontStyle.Regular);
            var sz = g.MeasureString(sizeText, font);
            var labelRect = new RectangleF(_sel.Right - sz.Width - 8, _sel.Bottom + 4, sz.Width + 8, sz.Height + 2);
            if (labelRect.Bottom > ClientRectangle.Bottom) labelRect.Y = _sel.Top - sz.Height - 6;
            if (labelRect.X < 0) labelRect.X = 0;
            g.FillRectangle(bg, labelRect);
            g.DrawString(sizeText, font, fg, labelRect.X + 4, labelRect.Y + 1);
        }

        // Hint text bottom-center
        if (!_selecting && _sel.Width == 0)
        {
            using var font = new Font("Segoe UI", 11f, FontStyle.Regular);
            using var fg = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            using var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            string hint = "Drag to select region  •  Esc to cancel";
            var sz = g.MeasureString(hint, font);
            var x = (ClientRectangle.Width - sz.Width) / 2;
            var y = ClientRectangle.Height - sz.Height - 30;
            g.FillRectangle(bg, x - 12, y - 6, sz.Width + 24, sz.Height + 12);
            g.DrawString(hint, font, fg, x, y);
        }
    }

    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _selecting = true;
        _start = e.Location;
        _sel = new Rectangle(_start, Size.Empty);
        Invalidate();
    }

    private void OnMouseMove(object? s, MouseEventArgs e)
    {
        if (!_selecting) return;
        int x = Math.Min(_start.X, e.X);
        int y = Math.Min(_start.Y, e.Y);
        int w = Math.Abs(e.X - _start.X);
        int h = Math.Abs(e.Y - _start.Y);
        _sel = new Rectangle(x, y, w, h);
        Invalidate();
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
                Result = _screenshot.Clone(clamped, _screenshot.PixelFormat);
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
        if (disposing) _screenshot.Dispose();
        base.Dispose(disposing);
    }
}
