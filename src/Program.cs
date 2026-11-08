using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CandadoApp
{
    internal static class Program
    {
        public const string Name = "AppWarden";
        public const string BackgroundArgument = "--background";

        private static Icon appIcon;

        public static Icon AppIcon
        {
            get { return appIcon ?? (appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)); }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, Name + ".Instancia", out created))
            using (EventWaitHandle openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Abrir"))
            {
                if (!created)
                {
                    Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                    openSignal.Set();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(openSignal, !args.Contains(BackgroundArgument)));
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Settings settings = Settings.Load();
        private readonly Watcher watcher;
        private readonly ConfigForm configForm;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem protectionItem = new ToolStripMenuItem();
        private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 400 };
        private readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        private readonly Queue<BlockedHit> queue = new Queue<BlockedHit>();
        private readonly RegisteredWaitHandle openWait;
        private LockSession lockSession;
        private bool updateShown;

        public TrayApp(EventWaitHandle openSignal, bool openConfig)
        {
            watcher = new Watcher(settings);
            configForm = new ConfigForm(settings);

            ToolStripMenuItem openItem = new ToolStripMenuItem("Abrir " + Program.Name, null, delegate { OpenConfig(); });
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
            protectionItem.Click += delegate { ToggleProtection(); };
            menu.Items.Add(openItem);
            menu.Items.Add(protectionItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, delegate { Quit(); });
            Theme.StyleMenu(menu);

            tray.Icon = Program.AppIcon;
            tray.Text = Program.Name;
            tray.ContextMenuStrip = menu;
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    OpenConfig();
            };
            tray.Visible = true;

            configForm.SettingsChanged += delegate { settings.Save(); UpdateProtectionItem(); };
            configForm.QuitRequested += delegate { Quit(); };

            IntPtr handle = configForm.Handle; // necesario para BeginInvoke antes de mostrar la ventana
            openWait = ThreadPool.RegisterWaitForSingleObject(openSignal, delegate
            {
                configForm.BeginInvoke(new Action(OpenConfig));
            }, null, Timeout.Infinite, false);

            poll.Tick += delegate { Scan(); };
            poll.Start();

            updateTimer.Tick += delegate { CheckUpdate(); };
            updateTimer.Start();
            CheckUpdate();

            UpdateProtectionItem();
            WarnIfIncomplete();
            if (openConfig)
                OpenConfig();
        }

        private void Scan()
        {
            foreach (BlockedHit hit in watcher.Poll())
                queue.Enqueue(hit);
            PumpQueue();
        }

        private void PumpQueue()
        {
            if (lockSession != null || queue.Count == 0)
                return;

            BlockedHit hit = queue.Dequeue();
            string family = hit.FileName;

            lockSession = new LockSession(settings, hit.Name);
            lockSession.Unlocked += delegate
            {
                watcher.AuthorizeFamily(family); // reanuda todas las instancias abiertas de esa app
                DrainFamily(family);
                EndLock();
            };
            lockSession.Cancelled += delegate
            {
                watcher.KillFamily(family); // cierra todas las instancias de esa app
                DrainFamily(family);
                EndLock();
            };
            lockSession.Show();
        }

        // Descarta los avisos en cola de esa misma app, ya resueltos por la familia.
        private void DrainFamily(string family)
        {
            BlockedHit[] rest = queue.Where(h => h.FileName != family).ToArray();
            queue.Clear();
            foreach (BlockedHit h in rest)
                queue.Enqueue(h);
        }

        private void EndLock()
        {
            if (lockSession != null)
            {
                lockSession.Dispose();
                lockSession = null;
            }
            PumpQueue();
        }

        private void ToggleProtection()
        {
            if (!settings.ProtectionEnabled && !settings.HasPassword)
            {
                OpenConfig();
                return;
            }
            settings.ProtectionEnabled = !settings.ProtectionEnabled;
            settings.Save();
            UpdateProtectionItem();
        }

        private void UpdateProtectionItem()
        {
            protectionItem.Text = settings.ProtectionEnabled ? "Desactivar protección" : "Activar protección";
        }

        private void WarnIfIncomplete()
        {
            if (!settings.HasPassword || settings.Blocked.Count == 0)
                tray.ShowBalloonTip(5000, Program.Name,
                    "Abre la ventana para poner una contraseña y elegir las aplicaciones a bloquear.", ToolTipIcon.Info);
        }

        // Consulta el release en segundo plano y, si hay uno más nuevo, muestra el aviso una sola vez.
        private void CheckUpdate()
        {
            if (updateShown)
                return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string url = Updater.CheckForUpdate();
                if (url == null)
                    return;
                try { configForm.BeginInvoke(new Action(delegate { ShowUpdate(url); })); }
                catch { }
            });
        }

        private void ShowUpdate(string url)
        {
            if (updateShown)
                return;
            updateShown = true;
            new UpdateToast(url).Show();
        }

        private void OpenConfig()
        {
            if (lockSession != null)
                return;

            IntPtr previous = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            configForm.Show();
            Native.SetThreadDpiAwarenessContext(previous);
            if (configForm.WindowState == FormWindowState.Minimized)
                configForm.WindowState = FormWindowState.Normal;
            configForm.Activate();
        }

        private void Quit()
        {
            poll.Stop();
            updateTimer.Stop();
            openWait.Unregister(null);
            if (lockSession != null)
                lockSession.Dispose();
            tray.Visible = false;
            tray.Dispose();
            configForm.AllowClose = true;
            configForm.Close();
            ExitThread();
        }
    }
}
