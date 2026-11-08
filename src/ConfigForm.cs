using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CandadoApp
{
    internal sealed class ConfigForm : Form
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const int SidebarWidth = 216;
        private const int ContentLeft = 236;
        private const int ContentWidth = 648;
        private const int CardTop = 146;
        private const int CardHeight = 454;
        private const int Inner = 28;
        private const int FieldLeft = 190;
        private const int FieldWidth = 430;
        private const int RowHeight = 54;

        private readonly Settings settings;
        private readonly Banner banner = new Banner();
        private readonly List<NavButton> navButtons = new List<NavButton>();
        private readonly List<Card> pages = new List<Card>();
        private readonly List<string[]> pageTitles = new List<string[]>();
        private readonly AppList appList = new AppList();
        private readonly Label passwordStatus = new Label();
        private readonly ToggleSwitch protectionBox = new ToggleSwitch("Protección activa");
        private readonly Card updatePanel = new Card { Fill = Theme.Field, Radius = 14, Visible = false };
        private readonly RoundButton updateButton = new RoundButton();
        private UpdateInfo updateInfo;
        private Card page;
        private int rowY;
        private int navY;

        public bool AllowClose;

        public event EventHandler SettingsChanged;
        public event EventHandler QuitRequested;

        public ConfigForm(Settings settings)
        {
            this.settings = settings;

            Text = "Abrir " + Program.Name;
            Icon = Program.AppIcon;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(ContentLeft + ContentWidth + 20, CardTop + CardHeight + 20);

            banner.Bounds = new Rectangle(ContentLeft, 42, ContentWidth, 88);
            Controls.Add(banner);
            MakeDraggable(banner);
            MakeDraggable(this);

            IconButton close = new IconButton("");
            close.Bounds = new Rectangle(ClientSize.Width - 44, 8, 32, 28);
            close.Click += delegate { Hide(); };
            IconButton minimize = new IconButton("");
            minimize.Bounds = new Rectangle(ClientSize.Width - 80, 8, 32, 28);
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(close);
            Controls.Add(minimize);

            navY = 92;
            NavSection("Protección");
            AddPage("", "Aplicaciones", "Programas que pedirán la contraseña", BuildApps);
            AddPage("", "Contraseña", "Clave y activación de la protección", BuildPassword);
            AddPage("", "Opciones", "Imagen, cierre y arranque", BuildOptions);

            NavButton quit = new NavButton("", "Salir del programa") { Bounds = new Rectangle(16, ClientSize.Height - 62, SidebarWidth - 32, 44) };
            quit.Click += delegate
            {
                if (QuitRequested != null)
                    QuitRequested(this, EventArgs.Empty);
            };
            Controls.Add(quit);

            // Panel de actualización, abajo en la barra lateral (oculto hasta que haya una versión nueva).
            updatePanel.Bounds = new Rectangle(16, ClientSize.Height - 150, SidebarWidth - 32, 78);
            Label updateTitle = new Label
            {
                Text = "Actualización disponible",
                AutoSize = false,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Font = Theme.UiFont(9.5f, FontStyle.Bold),
                Bounds = new Rectangle(12, 10, SidebarWidth - 56, 20)
            };
            updateButton.Text = "Actualizar";
            updateButton.Font = Theme.UiFont(10, FontStyle.Bold);
            updateButton.Bounds = new Rectangle(12, 36, SidebarWidth - 56, 32);
            updateButton.Click += delegate { StartUpdate(); };
            updatePanel.Controls.Add(updateTitle);
            updatePanel.Controls.Add(updateButton);
            Controls.Add(updatePanel);
            updatePanel.BringToFront();

            ShowPage(0);
        }

        public void ShowUpdateAvailable(UpdateInfo info)
        {
            updateInfo = info;
            updateButton.Enabled = true;
            updateButton.Text = "Actualizar";
            updatePanel.Visible = true;
            updatePanel.BringToFront();
        }

        private void StartUpdate()
        {
            if (updateInfo == null)
                return;
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                OpenReleasePage();
                return;
            }
            updateButton.Enabled = false;
            updateButton.Text = "Descargando…";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = Updater.ApplyUpdate(updateInfo);
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
                            updateButton.Enabled = true;
                            updateButton.Text = "Actualizar";
                            OpenReleasePage();
                        }
                    }));
                }
                catch { }
            });
        }

        private void OpenReleasePage()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updateInfo.PageUrl) { UseShellExecute = true }); }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundWindow(this, true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Prepare(e.Graphics);
            using (Pen line = new Pen(Theme.Line))
                e.Graphics.DrawLine(line, SidebarWidth, 0, SidebarWidth, ClientSize.Height);
            e.Graphics.DrawIcon(Program.AppIcon, new Rectangle(24, 28, 28, 28));
            using (Font logo = Theme.UiFont(13, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Theme.Text))
                e.Graphics.DrawString(Program.Name, logo, brush, 58, 29);
        }

        private void MakeDraggable(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
            };
        }

        private void Changed()
        {
            if (SettingsChanged != null)
                SettingsChanged(this, EventArgs.Empty);
        }

        private void NavSection(string title)
        {
            Label label = new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                Font = Theme.UiFont(8.5f),
                Location = new Point(28, navY)
            };
            Controls.Add(label);
            navY += 28;
        }

        private void AddPage(string glyph, string title, string subtitle, Action build)
        {
            int index = pages.Count;
            NavButton button = new NavButton(glyph, title) { Bounds = new Rectangle(16, navY, SidebarWidth - 32, 44) };
            button.Click += delegate { ShowPage(index); };
            Controls.Add(button);
            navButtons.Add(button);
            navY += 48;

            page = new Card { Bounds = new Rectangle(ContentLeft, CardTop, ContentWidth, CardHeight), Visible = false };
            Controls.Add(page);
            pages.Add(page);
            pageTitles.Add(new[] { title, subtitle });
            rowY = Inner;
            build();
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navButtons[i].Selected = i == index;
            }
            banner.SetTitle(pageTitles[index][0], pageTitles[index][1]);
        }

        private void BuildApps()
        {
            appList.Bounds = new Rectangle(Inner, Inner, ContentWidth - Inner * 2, CardHeight - Inner - 68);
            appList.Removed += delegate(object sender, string fileName)
            {
                settings.Blocked.RemoveAll(b => b.FileName == fileName);
                RefreshApps();
                Changed();
            };
            page.Controls.Add(appList);
            RefreshApps();

            int y = CardHeight - 56;
            RoundButton addExe = new RoundButton { Text = "Agregar aplicación (.exe)", Font = Theme.UiFont(10, FontStyle.Bold), Bounds = new Rectangle(Inner, y, 290, 40) };
            addExe.Click += delegate { AddFromFile(); };
            RoundButton addSearch = new RoundButton { Text = "Buscar aplicación", Fill = Theme.Field, Font = Theme.UiFont(10), Bounds = new Rectangle(Inner + 302, y, 240, 40) };
            addSearch.Click += delegate { AddFromSearch(); };
            page.Controls.Add(addExe);
            page.Controls.Add(addSearch);
        }

        private void RefreshApps()
        {
            appList.Show(settings.Blocked);
        }

        private void AddBlocked(BlockedApp app)
        {
            if (app == null || app.FileName.Length == 0 || settings.IsBlocked(app.FileName))
                return;
            settings.Blocked.Add(app);
            RefreshApps();
            Changed();
        }

        private void AddFromFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Programas|*.exe", Title = "Elegir la aplicación a bloquear" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddBlocked(new BlockedApp(dialog.FileName));
            }
        }

        private void AddFromSearch()
        {
            List<BlockedApp> apps = Watcher.InstalledAndRunning().Where(a => !settings.IsBlocked(a.FileName)).ToList();
            using (AppSearch picker = new AppSearch(apps))
            {
                if (picker.ShowDialog(this) == DialogResult.OK)
                    AddBlocked(picker.Selected);
            }
        }

        private void BuildPassword()
        {
            protectionBox.Checked = settings.ProtectionEnabled && settings.HasCredential;
            protectionBox.CheckedChanged += delegate
            {
                if (protectionBox.Checked && !settings.HasCredential)
                {
                    protectionBox.Checked = false;
                    passwordStatus.ForeColor = Theme.Danger;
                    passwordStatus.Text = "Primero guarda una contraseña o un PIN.";
                    return;
                }
                settings.ProtectionEnabled = protectionBox.Checked;
                Changed();
            };
            FullRow(protectionBox);

            rowY += 6;
            RoundInput password = PasswordInput();
            RoundInput repeat = PasswordInput();
            Row("Nueva contraseña", password);
            Row("Repetir", repeat);

            RoundButton save = new RoundButton { Text = "Guardar contraseña", Font = Theme.UiFont(10, FontStyle.Bold), Bounds = new Rectangle(FieldLeft, rowY, 190, 42) };
            passwordStatus.ForeColor = Theme.Muted;
            passwordStatus.BackColor = Color.Transparent;
            passwordStatus.TextAlign = ContentAlignment.MiddleLeft;
            passwordStatus.Bounds = new Rectangle(FieldLeft + 204, rowY, FieldWidth - 204, 42);
            passwordStatus.Text = settings.HasPassword ? "Contraseña definida." : "Sin contraseña.";
            save.Click += delegate
            {
                string first = password.Box.Text;
                if (first.Length == 0 || first != repeat.Box.Text)
                {
                    passwordStatus.ForeColor = Theme.Danger;
                    passwordStatus.Text = first.Length == 0 ? "Escribe una contraseña." : "No coinciden.";
                    return;
                }
                settings.SetPassword(first);
                password.Box.Clear();
                repeat.Box.Clear();
                passwordStatus.ForeColor = Theme.Muted;
                passwordStatus.Text = "Contraseña guardada.";
                Changed();
            };
            page.Controls.Add(save);
            page.Controls.Add(passwordStatus);

            rowY += RowHeight + 10;
            RoundInput pin = PasswordInput();
            pin.Box.MaxLength = 12;
            Row("PIN (opcional)", pin);

            RoundButton savePin = new RoundButton { Text = "Guardar PIN", Font = Theme.UiFont(10, FontStyle.Bold), Bounds = new Rectangle(FieldLeft, rowY, 190, 42) };
            Label pinStatus = new Label
            {
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(FieldLeft + 204, rowY, FieldWidth - 204, 42),
                Text = settings.HasPin ? "PIN definido." : "Sin PIN (mín. 4 dígitos)."
            };
            savePin.Click += delegate
            {
                string code = pin.Box.Text.Trim();
                if (code.Length == 0)
                {
                    settings.ClearPin();
                    pinStatus.ForeColor = Theme.Muted;
                    pinStatus.Text = "PIN quitado.";
                    Changed();
                    return;
                }
                if (!code.All(char.IsDigit) || code.Length < 4)
                {
                    pinStatus.ForeColor = Theme.Danger;
                    pinStatus.Text = "Solo números, mínimo 4 dígitos.";
                    return;
                }
                settings.SetPin(code);
                pin.Box.Clear();
                pinStatus.ForeColor = Theme.Muted;
                pinStatus.Text = "PIN guardado.";
                Changed();
            };
            page.Controls.Add(savePin);
            page.Controls.Add(pinStatus);
        }

        private void BuildOptions()
        {
            FullRow(Toggle("Mostrar la X para cerrar sin la contraseña", settings.AllowClose, v => { settings.AllowClose = v; Changed(); }));

            Title("Imagen de fondo", rowY);
            RoundButton imageButton = new RoundButton { Text = "Elegir imagen", Fill = Theme.Field, Font = Font, Bounds = new Rectangle(FieldLeft, rowY, 150, 38) };
            Label imageName = new Label
            {
                Text = ImageCaption(),
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(FieldLeft + 164, rowY, FieldWidth - 164, 38)
            };
            imageButton.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;
                    settings.LockImage = dialog.FileName;
                    imageName.Text = ImageCaption();
                    Changed();
                }
            };
            page.Controls.Add(imageButton);
            page.Controls.Add(imageName);
            rowY += RowHeight;

            if (settings.LockImage.Length > 0)
            {
                RoundButton clear = new RoundButton { Text = "Quitar imagen (fondo oscuro)", Fill = Theme.Field, Font = Font, Bounds = new Rectangle(FieldLeft, rowY, 260, 38) };
                clear.Click += delegate
                {
                    settings.LockImage = "";
                    imageName.Text = ImageCaption();
                    Changed();
                };
                page.Controls.Add(clear);
                rowY += RowHeight;
            }

            ToggleSwitch startup = Toggle("Iniciar con Windows", IsStartupEnabled(), SetStartup);
            FullRow(startup);
        }

        private string ImageCaption()
        {
            return settings.LockImage.Length > 0 ? Path.GetFileName(settings.LockImage) : "Sin imagen (fondo oscuro)";
        }

        private static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(Program.Name) != null;
        }

        private static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    key.SetValue(Program.Name, "\"" + Application.ExecutablePath + "\" " + Program.BackgroundArgument);
                else
                    key.DeleteValue(Program.Name, false);
            }
        }

        private void Title(string title, int y, int x = Inner)
        {
            page.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Location = new Point(x, y + 9)
            });
        }

        private void Row(string title, Control control)
        {
            Title(title, rowY);
            control.Bounds = new Rectangle(FieldLeft, rowY, FieldWidth, 38);
            page.Controls.Add(control);
            rowY += RowHeight;
        }

        private void FullRow(Control control)
        {
            control.Bounds = new Rectangle(Inner, rowY, ContentWidth - Inner * 2, 38);
            page.Controls.Add(control);
            rowY += RowHeight - 6;
        }

        private RoundInput PasswordInput()
        {
            RoundInput input = new RoundInput { Font = Font };
            input.Box.PasswordChar = '*';
            return input;
        }

        private static ToggleSwitch Toggle(string title, bool value, Action<bool> apply)
        {
            ToggleSwitch box = new ToggleSwitch(title) { Checked = value };
            box.CheckedChanged += delegate { apply(box.Checked); };
            return box;
        }
    }
}
