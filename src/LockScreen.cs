using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CandadoApp
{
    // Ventana a pantalla completa (una por monitor) que bloquea el paso mientras el programa está suspendido.
    internal sealed class LockSession : IDisposable
    {
        private readonly Settings settings;
        private readonly string appName;
        private readonly List<LockForm> forms = new List<LockForm>();
        private readonly Timer guard = new Timer { Interval = 150 };
        private readonly Native.LowLevelKeyboardProc keyboardProc;
        private readonly Image image;
        private LockForm mainForm;
        private IntPtr keyboardHook;
        private bool closed;

        public event EventHandler Unlocked;
        public event EventHandler Cancelled;

        public LockSession(Settings settings, string appName)
        {
            this.settings = settings;
            this.appName = appName;
            image = LoadImage(settings.LockImage);
            keyboardProc = OnKeyboard;
            guard.Tick += delegate { KeepOnTop(); };
        }

        public void Show()
        {
            IntPtr previous = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            foreach (Screen screen in Screen.AllScreens)
            {
                LockForm form = new LockForm(screen, image, screen.Primary, settings, appName);
                form.Unlocked += delegate { Finish(Unlocked); };
                form.Cancelled += delegate { Finish(Cancelled); };
                forms.Add(form);
                if (screen.Primary)
                    mainForm = form;
            }
            foreach (LockForm form in forms)
                form.Show();
            Native.SetThreadDpiAwarenessContext(previous);

            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, Native.GetModuleHandle(null), 0);
            KeepOnTop();
            guard.Start();
        }

        private void Finish(EventHandler handler)
        {
            if (closed)
                return;
            closed = true;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static Image LoadImage(string path)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(path)))
                using (Image source = Image.FromStream(stream))
                    return new Bitmap(source);
            }
            catch (Exception e)
            {
                if (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                    return null;
                throw;
            }
        }

        private void KeepOnTop()
        {
            foreach (LockForm form in forms)
                Native.SetWindowPos(form.Handle, Native.HWND_TOPMOST, form.Target.X, form.Target.Y, form.Target.Width, form.Target.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

            IntPtr foreground = Native.GetForegroundWindow();
            if (mainForm == null || foreground == mainForm.Handle)
                return;

            uint process;
            uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out process);
            uint thisThread = Native.GetCurrentThreadId();
            Native.AttachThreadInput(foregroundThread, thisThread, true);
            Native.SetForegroundWindow(mainForm.Handle);
            Native.AttachThreadInput(foregroundThread, thisThread, false);
            mainForm.FocusPassword();
        }

        private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int key = System.Runtime.InteropServices.Marshal.ReadInt32(data);
                int flags = System.Runtime.InteropServices.Marshal.ReadInt32(data, 8);
                if (IsBlocked(key, (flags & Native.LLKHF_ALTDOWN) != 0, (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0))
                    return new IntPtr(1);
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private static bool IsBlocked(int key, bool alt, bool control)
        {
            Keys k = (Keys)key;
            if (k == Keys.LWin || k == Keys.RWin)
                return true;
            if (k == Keys.Escape)
                return alt || control;
            return alt && (k == Keys.Tab || k == Keys.F4 || k == Keys.Space);
        }

        public void Dispose()
        {
            guard.Stop();
            guard.Dispose();
            if (keyboardHook != IntPtr.Zero)
                Native.UnhookWindowsHookEx(keyboardHook);
            foreach (LockForm form in forms)
            {
                form.AllowForceClose = true;
                form.Close();
            }
            if (image != null)
                image.Dispose();
        }
    }

    internal sealed class LockForm : Form
    {
        private readonly Image image;
        private readonly Settings settings;
        private readonly string appName;
        private readonly float scale;
        private readonly RoundInput passwordInput = new RoundInput();
        private readonly Label errorLabel = new Label();
        private readonly Card panel = new Card { Fill = Theme.Window, Radius = 24 };
        private Bitmap background;

        public bool AllowForceClose;
        public Rectangle Target { get; private set; }

        public event EventHandler Unlocked;
        public event EventHandler Cancelled;

        public LockForm(Screen screen, Image image, bool withControls, Settings settings, string appName)
        {
            Target = screen.Bounds;
            this.image = image;
            this.settings = settings;
            this.appName = appName;
            scale = Native.DpiScaleAt(new Point(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2));

            Text = Program.Name;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            BackColor = Color.Black;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            if (withControls)
                BuildControls();
        }

        private int Px(int value)
        {
            return (int)Math.Round(value * scale);
        }

        private Font Scaled(float pixels, FontStyle style = FontStyle.Regular)
        {
            return new Font(style == FontStyle.Bold ? "Segoe UI Semibold" : "Segoe UI", pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void BuildControls()
        {
            panel.Size = new Size(Px(400), Px(248));

            Label glyph = new Label
            {
                Text = "",
                Font = new Font(Theme.IconFont, 30f * scale, GraphicsUnit.Pixel),
                ForeColor = Theme.AccentLight,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(Px(28), Px(22), Px(344), Px(40))
            };
            Label title = new Label
            {
                Text = "Aplicación bloqueada",
                Font = Scaled(20, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(Px(28), Px(66), Px(344), Px(30))
            };
            Label which = new Label
            {
                Text = appName,
                Font = Scaled(12),
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(Px(28), Px(96), Px(344), Px(22))
            };

            passwordInput.Box.PasswordChar = '*';
            passwordInput.Font = Scaled(16);
            passwordInput.Bounds = new Rectangle(Px(28), Px(128), Px(344), Px(44));

            errorLabel.ForeColor = Theme.Danger;
            errorLabel.BackColor = Color.Transparent;
            errorLabel.Font = Scaled(12);
            errorLabel.TextAlign = ContentAlignment.MiddleCenter;
            errorLabel.Bounds = new Rectangle(Px(28), Px(174), Px(344), Px(18));

            RoundButton unlock = new RoundButton { Text = "Desbloquear", Font = Scaled(15, FontStyle.Bold), Radius = Px(12) };
            unlock.Bounds = new Rectangle(Px(28), Px(196), Px(344), Px(44));
            unlock.Click += delegate { TryUnlock(); };
            AcceptButton = unlock;

            panel.Controls.AddRange(new Control[] { glyph, title, which, passwordInput, errorLabel, unlock });
            Controls.Add(panel);

            if (settings.AllowClose)
            {
                IconButton close = new IconButton("", 11f * scale) { Fill = Theme.Card, Size = new Size(Px(44), Px(40)) };
                close.Click += delegate { Raise(Cancelled); };
                close.Location = new Point(ClientSize.Width - close.Width - Px(24), Px(24));
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                Controls.Add(close);
            }
        }

        private void TryUnlock()
        {
            bool valid = settings.Check(passwordInput.Box.Text);
            passwordInput.Box.Clear();
            if (valid)
            {
                Raise(Unlocked);
                return;
            }
            errorLabel.Text = "Contraseña incorrecta";
            passwordInput.Box.Focus();
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void FocusPassword()
        {
            passwordInput.Box.Focus();
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            passwordInput.Box.Focus();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            panel.Location = new Point((ClientSize.Width - panel.Width) / 2, (ClientSize.Height - panel.Height) / 2);
        }

        // Los controles redondeados repintan el fondo tras sus esquinas: se reutiliza una copia ya escalada.
        private Bitmap ScaledBackground()
        {
            if (background != null && background.Size == ClientSize)
                return background;
            if (background != null)
                background.Dispose();

            background = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (Graphics g = Graphics.FromImage(background))
            {
                if (image != null)
                {
                    float ratio = Math.Max((float)ClientSize.Width / image.Width, (float)ClientSize.Height / image.Height);
                    float width = image.Width * ratio;
                    float height = image.Height * ratio;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, (ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
                }
                else
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(24, 25, 33), Color.FromArgb(44, 40, 66), 60f))
                        g.FillRectangle(brush, ClientRectangle);
                }
            }
            return background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
                e.Graphics.DrawImageUnscaled(ScaledBackground(), 0, 0);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowForceClose && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && background != null)
                background.Dispose();
            base.Dispose(disposing);
        }
    }
}
