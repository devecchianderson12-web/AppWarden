using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CandadoApp
{
    internal static class Theme
    {
        public static readonly Color Window = Color.FromArgb(31, 32, 41);
        public static readonly Color Card = Color.FromArgb(42, 43, 54);
        public static readonly Color Field = Color.FromArgb(53, 54, 66);
        public static readonly Color FieldHover = Color.FromArgb(63, 64, 78);
        public static readonly Color Line = Color.FromArgb(46, 47, 58);
        public static readonly Color Accent = Color.FromArgb(122, 107, 216);
        public static readonly Color AccentLight = Color.FromArgb(160, 146, 240);
        public static readonly Color Text = Color.FromArgb(244, 244, 248);
        public static readonly Color Muted = Color.FromArgb(138, 139, 153);
        public static readonly Color Danger = Color.FromArgb(255, 127, 80);

        public const string IconFont = "Segoe MDL2 Assets";

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(style == FontStyle.Bold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
        {
            using (GraphicsPath path = Round(r, radius))
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static Color Mix(Color a, Color b, float amount)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }

        // Windows 11 redondea la ventana con su propio borde suavizado; en Windows 10 se recorta.
        public static void RoundWindow(Control window, bool clipIfUnsupported)
        {
            int round = Native.DWMWCP_ROUND;
            int result = -1;
            try
            {
                result = Native.DwmSetWindowAttribute(window.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch (DllNotFoundException) { }

            if (result != 0 && clipIfUnsupported)
            {
                using (GraphicsPath path = Round(new RectangleF(0, 0, window.Width, window.Height), 16))
                    window.Region = new Region(path);
            }
        }

        public static void StyleMenu(ContextMenuStrip menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Renderer = new MenuRenderer();
            menu.BackColor = Card;
            menu.ForeColor = Text;
            menu.Font = UiFont(10);
            menu.Padding = new Padding(6);
            menu.HandleCreated += delegate { RoundWindow(menu, false); };
        }
    }

    // Extrae y guarda en caché el icono de cada programa (por ruta del .exe) para mostrarlo en las listas.
    internal static class IconCache
    {
        private static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static Image Get(string path, int size)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            Image image;
            if (cache.TryGetValue(path, out image))
                return image;
            image = Load(path, size);
            cache[path] = image; // se guarda también null para no reintentar
            return image;
        }

        private static Image Load(string path, int size)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null)
                        return null;
                    Bitmap bitmap = new Bitmap(size, size);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    using (Bitmap source = icon.ToBitmap())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(source, new Rectangle(0, 0, size, size));
                    }
                    return bitmap;
                }
            }
            catch { return null; }
        }
    }

    internal sealed class MenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Theme.Card))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
                return;
            Theme.Prepare(e.Graphics);
            Theme.FillRound(e.Graphics, Theme.Accent, new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2), 8);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Field))
                e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
    }

    internal class Card : Panel
    {
        public Color Fill = Theme.Card;
        public int Radius = 20;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Theme.FillRound(e.Graphics, Fill, ClientRectangle, Radius);
        }
    }

    internal sealed class Banner : Card
    {
        private string title = "";
        private string subtitle = "";

        public void SetTitle(string title, string subtitle)
        {
            this.title = title;
            this.subtitle = subtitle;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (GraphicsPath path = Theme.Round(ClientRectangle, Radius))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, Theme.Accent, Theme.AccentLight, 0f))
                    g.FillPath(brush, path);

                g.SetClip(path);
                using (SolidBrush soft = new SolidBrush(Color.FromArgb(28, Color.White)))
                {
                    g.FillEllipse(soft, Width - Height * 1.5f, Height * 0.25f, Height * 1.6f, Height * 1.6f);
                    g.FillEllipse(soft, Width - Height * 0.8f, -Height * 0.7f, Height * 1.3f, Height * 1.3f);
                }
                g.ResetClip();
            }

            using (Font big = Theme.UiFont(21, FontStyle.Bold))
            using (Font small = Theme.UiFont(9.5f))
            using (Font glyph = new Font(Theme.IconFont, 30f))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(205, Color.White)))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(title, big, white, Height * 0.26f, Height * 0.17f);
                g.DrawString(subtitle, small, soft, Height * 0.28f, Height * 0.62f);
                g.DrawString("", glyph, soft, new RectangleF(0, 0, Width - Height * 0.3f, Height), right);
            }
        }
    }

    internal class RoundButton : Button
    {
        private bool hover;
        private bool pressed;

        public Color Fill = Theme.Accent;
        public int Radius = 12;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.Opaque, false); // ButtonBase lo activa y dejaría las esquinas sin pintar
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        protected Color CurrentFill
        {
            get
            {
                if (!Enabled)
                    return Theme.Mix(Fill, Theme.Card, 0.6f);
                if (pressed)
                    return Theme.Mix(Fill, Color.Black, 0.12f);
                return hover ? Theme.Mix(Fill, Color.White, 0.1f) : Fill;
            }
        }

        protected bool Hovered
        {
            get { return hover; }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Theme.FillRound(e.Graphics, CurrentFill, ClientRectangle, Radius);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class IconButton : RoundButton
    {
        public IconButton(string glyph, float size = 9f)
        {
            Text = glyph;
            Fill = Theme.Window;
            Font = new Font(Theme.IconFont, size);
            ForeColor = Theme.Muted;
        }
    }

    internal sealed class NavButton : RoundButton
    {
        private readonly string glyph;
        private bool selected;

        public NavButton(string glyph, string title)
        {
            this.glyph = glyph;
            Text = title;
            Fill = Theme.Accent;
            Font = Theme.UiFont(10);
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            if (selected || Hovered)
                Theme.FillRound(e.Graphics, selected ? Fill : Theme.Card, ClientRectangle, Radius);

            Color color = selected ? Color.White : Theme.Muted;
            using (Font icon = new Font(Theme.IconFont, 11f))
                TextRenderer.DrawText(e.Graphics, glyph, icon, new Rectangle(14, 0, 24, Height), color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(46, 0, Width - 50, Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        public ToggleSwitch(string title)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Opaque, false);
            Text = title;
            AutoSize = false;
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - 50, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            float height = 22;
            float width = 42;
            RectangleF track = new RectangleF(Width - width - 1, (Height - height) / 2, width, height);
            Theme.FillRound(e.Graphics, Checked ? Theme.Accent : Theme.Field, track, height / 2);
            float knob = height - 6;
            float x = Checked ? track.Right - knob - 3 : track.X + 3;
            using (SolidBrush brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, x, track.Y + 3, knob, knob);
        }
    }

    internal sealed class RoundInput : Control
    {
        public readonly TextBox Box = new TextBox();

        public RoundInput()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Field;
            Box.ForeColor = Theme.Text;
            Box.ShortcutsEnabled = false;
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Box.Font = Font;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Box.Width = Width - 28;
            Box.Location = new Point(14, (Height - Box.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Theme.FillRound(e.Graphics, Theme.Field, ClientRectangle, 12);
            if (!Box.Focused)
                return;
            using (GraphicsPath path = Theme.Round(new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f), 12))
            using (Pen pen = new Pen(Theme.Accent, 1.5f))
                e.Graphics.DrawPath(pen, path);
        }
    }

    // Lista desplazable de aplicaciones bloqueadas; cada fila trae su botón para quitarla.
    internal sealed class AppList : Panel
    {
        public event EventHandler<string> Removed;

        public AppList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Window;
            AutoScroll = true;
            BorderStyle = BorderStyle.None;
        }

        public void Show(IList<BlockedApp> apps)
        {
            SuspendLayout();
            Controls.Clear();
            if (apps.Count == 0)
            {
                Controls.Add(new Label
                {
                    Text = "Todavía no hay aplicaciones en la lista.",
                    ForeColor = Theme.Muted,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(6, 10)
                });
            }
            int y = 2;
            int width = Width - 20;
            foreach (BlockedApp app in apps)
            {
                Controls.Add(Row(app, y, width));
                y += 56;
            }
            ResumeLayout();
        }

        private Card Row(BlockedApp app, int y, int width)
        {
            Card card = new Card { Bounds = new Rectangle(2, y, width, 48), Radius = 12 };

            Control glyph;
            Image ico = IconCache.Get(app.Path, 24);
            if (ico != null)
                glyph = new PictureBox { Image = ico, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Bounds = new Rectangle(10, 12, 24, 24) };
            else
                glyph = new Label
                {
                    Text = "",
                    Font = new Font(Theme.IconFont, 12f),
                    ForeColor = Theme.AccentLight,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Bounds = new Rectangle(8, 4, 34, 40)
                };
            Label name = new Label
            {
                Text = app.Name,
                Font = Theme.UiFont(10, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Bounds = new Rectangle(46, 6, width - 110, 20)
            };
            Label path = new Label
            {
                Text = app.Path.Length > 0 ? app.Path : "coincidencia por nombre",
                Font = Theme.UiFont(8.5f),
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Bounds = new Rectangle(46, 25, width - 110, 18)
            };
            IconButton remove = new IconButton("") { Fill = Theme.Field, Bounds = new Rectangle(width - 52, 7, 40, 34) };
            remove.Click += delegate
            {
                if (Removed != null)
                    Removed(this, app.FileName);
            };
            card.Controls.Add(glyph);
            card.Controls.Add(name);
            card.Controls.Add(path);
            card.Controls.Add(remove);
            return card;
        }
    }

    // Buscador de aplicaciones por nombre: se escribe y la lista se filtra en vivo; al elegir se agrega igual que a mano.
    internal sealed class AppSearch : Form
    {
        private readonly ListBox list = new ListBox();
        private readonly RoundInput search = new RoundInput();
        private readonly List<BlockedApp> all;
        private List<BlockedApp> shown = new List<BlockedApp>();

        public BlockedApp Selected { get; private set; }

        public AppSearch(IEnumerable<BlockedApp> apps)
        {
            all = new List<BlockedApp>(apps);
            Text = "Buscar aplicación";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10);
            ClientSize = new Size(440, 460);
            Padding = new Padding(1);

            Label title = new Label { Text = "Buscar aplicación", Font = Theme.UiFont(13, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = true, Location = new Point(18, 16) };
            Controls.Add(title);

            search.Font = Font;
            search.Bounds = new Rectangle(14, 50, 412, 40);
            search.Box.KeyDown += OnSearchKey;
            search.Box.TextChanged += delegate { Refilter(); };
            Controls.Add(search);

            Label hint = new Label { Text = "Escribe el nombre (por ejemplo: roblox)", Font = Theme.UiFont(8.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent, AutoSize = true, Location = new Point(16, 96) };
            Controls.Add(hint);

            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Window;
            list.ForeColor = Theme.Text;
            list.Font = Font;
            list.ItemHeight = 36;
            list.IntegralHeight = false;
            list.Bounds = new Rectangle(14, 120, 412, 288);
            list.DrawItem += DrawItem;
            list.DoubleClick += delegate { Choose(); };
            Controls.Add(list);

            RoundButton add = new RoundButton { Text = "Agregar", Font = Theme.UiFont(10, FontStyle.Bold), Bounds = new Rectangle(246, 414, 180, 40) };
            add.Click += delegate { Choose(); };
            RoundButton cancel = new RoundButton { Text = "Cancelar", Fill = Theme.Field, Font = Font, Bounds = new Rectangle(14, 414, 180, 40) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(add);
            Controls.Add(cancel);

            Refilter();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundWindow(this, true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            search.Box.Focus();
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Choose();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && list.Items.Count > 0)
            {
                list.Focus();
                e.Handled = true;
            }
        }

        private void Refilter()
        {
            string query = search.Box.Text.Trim().ToLowerInvariant();
            shown = query.Length == 0
                ? all
                : all.Where(a => a.Name.ToLowerInvariant().Contains(query) || a.FileName.Contains(query)).ToList();

            list.BeginUpdate();
            list.Items.Clear();
            foreach (BlockedApp app in shown)
                list.Items.Add(app.Name + "   ·   " + app.FileName);
            if (list.Items.Count > 0)
                list.SelectedIndex = 0;
            list.EndUpdate();
        }

        private void Choose()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count)
                return;
            Selected = shown[list.SelectedIndex];
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count)
                return;
            BlockedApp app = shown[e.Index];
            using (SolidBrush background = new SolidBrush(Theme.Window))
                e.Graphics.FillRectangle(background, e.Bounds);
            Theme.Prepare(e.Graphics);
            if ((e.State & DrawItemState.Selected) != 0)
                Theme.FillRound(e.Graphics, Theme.Accent, new RectangleF(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width, e.Bounds.Height - 2), 8);

            int iconSize = 24;
            int textLeft = e.Bounds.X + 12;
            Image icon = IconCache.Get(app.Path, iconSize);
            if (icon != null)
            {
                e.Graphics.DrawImage(icon, e.Bounds.X + 10, e.Bounds.Y + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
                textLeft = e.Bounds.X + 44;
            }

            string text = string.Equals(app.Name, app.FileName, StringComparison.OrdinalIgnoreCase)
                ? app.Name
                : app.Name + "   ·   " + app.FileName;
            Rectangle bounds = new Rectangle(textLeft, e.Bounds.Y, e.Bounds.Right - textLeft - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, list.Font, bounds,
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Aviso no invasivo, abajo a la derecha, con bordes redondeados y botón "Actualizar".
    internal sealed class UpdateToast : Form
    {
        private readonly UpdateInfo info;

        public UpdateToast(UpdateInfo info)
        {
            this.info = info;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            Font = Theme.UiFont(10);
            ClientSize = new Size(360, 120);

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);

            IconButton close = new IconButton("", 8f) { Fill = Theme.Card, Bounds = new Rectangle(Width - 36, 8, 26, 24) };
            close.Click += delegate { Close(); };
            Controls.Add(close);

            RoundButton update = new RoundButton { Text = "Actualizar", Font = Theme.UiFont(10, FontStyle.Bold), Bounds = new Rectangle(66, 74, 150, 34) };
            update.Click += delegate { StartUpdate(update); };
            Controls.Add(update);
        }

        // Descarga la versión nueva y reinicia; si no hay archivo para bajar, abre la página del release.
        private void StartUpdate(RoundButton button)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                OpenPage();
                Close();
                return;
            }
            button.Enabled = false;
            button.Text = "Descargando…";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = Updater.ApplyUpdate(info);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (ok)
                        {
                            Application.Exit();
                        }
                        else
                        {
                            button.Enabled = true;
                            button.Text = "Actualizar";
                            OpenPage();
                            Close();
                        }
                    }));
                }
                catch { }
            });
        }

        private void OpenPage()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.PageUrl) { UseShellExecute = true }); }
            catch { }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000008 /* WS_EX_TOPMOST */;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundWindow(this, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Theme.FillRound(e.Graphics, Theme.Card, ClientRectangle, 16);
            using (GraphicsPath border = Theme.Round(new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f), 16))
            using (Pen pen = new Pen(Theme.Line))
                e.Graphics.DrawPath(pen, border);

            e.Graphics.DrawIcon(Program.AppIcon, new Rectangle(18, 20, 36, 36));
            using (Font title = Theme.UiFont(11.5f, FontStyle.Bold))
            using (SolidBrush text = new SolidBrush(Theme.Text))
            using (SolidBrush muted = new SolidBrush(Theme.Muted))
            {
                e.Graphics.DrawString("Actualización disponible", title, text, 64, 18);
                e.Graphics.DrawString("Hay una nueva versión de " + Program.Name + ".", Font, muted, 66, 46);
            }
        }
    }
}
