using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// Lightweight annotator that opens with a fresh snip. Tools: pencil,
/// marker (semi-transparent highlight), arrow, rectangle. After every
/// committed stroke the clipboard is refreshed so the user can paste
/// immediately into the next app. The form auto-closes when it loses
/// focus so it doesn't linger on screen.
/// </summary>
public sealed class AnnotatorForm : Form
{
    private enum Tool { Pencil, Marker, Arrow, Rect }

    private Bitmap _composite;            // base image + all committed strokes
    private readonly Bitmap _baseline;    // pristine copy of input for Reset
    private Bitmap? _preview;             // in-progress shape (arrow/rect)
    private readonly PictureBox _canvas;
    private readonly FlowLayoutPanel _toolbar;

    private Tool _tool = Tool.Pencil;
    private Color _color = Color.FromArgb(255, 235, 54, 54);   // red
    private int _brushSize = 4;

    private bool _drawing;
    private Point _strokeStart;
    private Point _lastPoint;

    private readonly List<ToolStripItem> _toolButtons = new();
    private readonly Dictionary<Color, Panel> _colorSwatches = new();
    private readonly Dictionary<int, Panel> _sizeSwatches = new();

    public AnnotatorForm(Bitmap image)
    {
        _composite = (Bitmap)image.Clone();
        _baseline = (Bitmap)image.Clone();

        Text = "GroqVoice — Annotate";
        Icon = null;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(36, 36, 36);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;

        // --- toolbar ---
        _toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(28, 28, 28),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 6, 6, 6),
        };
        BuildToolbar();
        Controls.Add(_toolbar);

        // --- canvas in a scrollable container ---
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(20, 20, 20) };
        _canvas = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.AutoSize,
            Image = _composite,
            Cursor = Cursors.Cross,
        };
        _canvas.MouseDown += OnCanvasMouseDown;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseUp   += OnCanvasMouseUp;
        scroll.Controls.Add(_canvas);
        Controls.Add(scroll);

        // Size form to fit image, capped to screen real estate
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        var w = Math.Min(image.Width + 40, screen.Width  - 40);
        var h = Math.Min(image.Height + _toolbar.Height + 40, screen.Height - 80);
        ClientSize = new Size(Math.Max(680, w), Math.Max(280, h));

        KeyDown += OnKeyDown;
        Deactivate += (_, _) => Close();   // disappears when focus moves elsewhere
    }

    // ---------------- toolbar construction ----------------

    private void BuildToolbar()
    {
        void AddDivider()
        {
            _toolbar.Controls.Add(new Panel
            {
                Width = 1, Height = 30, Margin = new Padding(6, 1, 6, 1),
                BackColor = Color.FromArgb(70, 70, 70),
            });
        }

        // Tools
        foreach (var (label, tool) in new[]
        {
            ("Pencil",  Tool.Pencil),
            ("Marker",  Tool.Marker),
            ("Arrow",   Tool.Arrow),
            ("Rect",    Tool.Rect),
        })
        {
            var btn = MakeToolButton(label);
            btn.Click += (_, _) => { _tool = tool; RefreshSelection(); };
            btn.Tag = tool;
            _toolbar.Controls.Add(btn);
        }
        AddDivider();

        // Colors
        foreach (var c in new[]
        {
            Color.FromArgb(235, 54, 54),    // red
            Color.FromArgb(255, 200, 0),    // yellow
            Color.FromArgb(40, 200, 90),    // green
            Color.FromArgb(50, 140, 240),   // blue
            Color.FromArgb(245, 245, 245),  // white
            Color.FromArgb(20, 20, 20),     // black
        })
        {
            var sw = new Panel
            {
                Width = 26, Height = 26,
                BackColor = c,
                Margin = new Padding(3, 4, 3, 4),
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.None,
            };
            sw.Click += (_, _) => { _color = c; RefreshSelection(); };
            _colorSwatches[c] = sw;
            _toolbar.Controls.Add(sw);
        }
        AddDivider();

        // Brush sizes
        foreach (var sz in new[] { 3, 6, 12 })
        {
            var sw = new Panel
            {
                Width = 30, Height = 30,
                Margin = new Padding(2, 2, 2, 2),
                BackColor = Color.FromArgb(45, 45, 45),
                Cursor = Cursors.Hand,
            };
            int captured = sz;
            sw.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(sw.BackColor);
                using var b = new SolidBrush(Color.White);
                var d = Math.Min(captured + 2, 22);
                e.Graphics.FillEllipse(b, (sw.Width - d) / 2, (sw.Height - d) / 2, d, d);
            };
            sw.Click += (_, _) => { _brushSize = captured; RefreshSelection(); };
            _sizeSwatches[sz] = sw;
            _toolbar.Controls.Add(sw);
        }
        AddDivider();

        // Reset + Close
        var reset = MakeToolButton("Reset");
        reset.Click += (_, _) => DoReset();
        _toolbar.Controls.Add(reset);

        var done = MakeToolButton("Done (Esc)");
        done.Click += (_, _) => Close();
        _toolbar.Controls.Add(done);

        RefreshSelection();
    }

    private static Button MakeToolButton(string text)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(45, 45, 45),
            Height = 30,
            AutoSize = true,
            Margin = new Padding(2, 4, 2, 4),
            Padding = new Padding(8, 0, 8, 0),
            UseVisualStyleBackColor = false,
            TabStop = false,
        };
    }

    private void RefreshSelection()
    {
        // Highlight active tool button
        foreach (var ctl in _toolbar.Controls)
        {
            if (ctl is Button b && b.Tag is Tool t)
                b.BackColor = (t == _tool)
                    ? Color.FromArgb(80, 130, 200)
                    : Color.FromArgb(45, 45, 45);
        }
        // Highlight active color and size with a white ring
        foreach (var kv in _colorSwatches)
            kv.Value.BorderStyle = (kv.Key.ToArgb() == _color.ToArgb())
                ? BorderStyle.FixedSingle : BorderStyle.None;
        foreach (var kv in _sizeSwatches)
            kv.Value.BorderStyle = (kv.Key == _brushSize)
                ? BorderStyle.FixedSingle : BorderStyle.None;
    }

    // ---------------- drawing ----------------

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _drawing = true;
        _strokeStart = e.Location;
        _lastPoint = e.Location;
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_drawing) return;

        if (_tool == Tool.Pencil || _tool == Tool.Marker)
        {
            // Free-hand strokes commit to _composite as we go
            using var g = Graphics.FromImage(_composite);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var col = _tool == Tool.Marker ? Color.FromArgb(85, _color) : _color;
            var width = _tool == Tool.Marker ? _brushSize * 3 : _brushSize;
            using var pen = new Pen(col, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, _lastPoint, e.Location);
            _lastPoint = e.Location;
            _canvas.Invalidate();
        }
        else
        {
            // Shape tools draw onto a fresh preview overlay each move
            _preview?.Dispose();
            _preview = (Bitmap)_composite.Clone();
            using var g = Graphics.FromImage(_preview);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawShape(g, _strokeStart, e.Location);
            _canvas.Image = _preview;
        }
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;

        if (_tool == Tool.Pencil || _tool == Tool.Marker)
        {
            _canvas.Image = _composite;
        }
        else
        {
            using var g = Graphics.FromImage(_composite);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawShape(g, _strokeStart, e.Location);
            _preview?.Dispose();
            _preview = null;
            _canvas.Image = _composite;
        }

        // Push to clipboard so the next paste anywhere uses the annotated image
        try { ClipboardImage.Set(_composite); }
        catch (Exception ex) { Log.Warn($"annotator clipboard set failed: {ex.Message}"); }
    }

    private void DrawShape(Graphics g, Point from, Point to)
    {
        using var pen = new Pen(_color, _brushSize);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        if (_tool == Tool.Arrow)
        {
            pen.CustomEndCap = new AdjustableArrowCap(5, 5, true);
            g.DrawLine(pen, from, to);
        }
        else if (_tool == Tool.Rect)
        {
            int x = Math.Min(from.X, to.X), y = Math.Min(from.Y, to.Y);
            int w = Math.Abs(to.X - from.X), h = Math.Abs(to.Y - from.Y);
            g.DrawRectangle(pen, x, y, w, h);
        }
    }

    private void DoReset()
    {
        var old = _composite;
        _composite = (Bitmap)_baseline.Clone();
        _canvas.Image = _composite;
        old.Dispose();
        try { ClipboardImage.Set(_composite); } catch { }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        BringToFront();
        Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview?.Dispose();
            _composite.Dispose();
            _baseline.Dispose();
        }
        base.Dispose(disposing);
    }
}
