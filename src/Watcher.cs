using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CandadoApp
{
    internal struct BlockedHit
    {
        public int Pid;
        public string Name;
        public string FileName;
        public string Path;
        public bool Suspended;
    }

    // Revisa periódicamente los procesos y detiene los que están en la lista hasta que se escriba la contraseña.
    internal sealed class Watcher
    {
        private readonly Settings settings;
        private readonly HashSet<int> authorized = new HashSet<int>();
        private readonly HashSet<int> pending = new HashSet<int>();
        private readonly int selfPid = Process.GetCurrentProcess().Id;

        public Watcher(Settings settings)
        {
            this.settings = settings;
        }

        public List<BlockedHit> Poll()
        {
            List<BlockedHit> hits = new List<BlockedHit>();
            if (!settings.ProtectionEnabled || !settings.HasCredential || settings.Blocked.Count == 0)
                return hits;

            HashSet<string> names = new HashSet<string>(settings.Blocked.Select(b => b.FileName).Where(n => n.Length > 0));
            if (names.Count == 0)
                return hits;

            Process[] all = Process.GetProcesses();
            try
            {
                HashSet<int> alive = new HashSet<int>(all.Select(p => SafeId(p)));
                authorized.RemoveWhere(id => !alive.Contains(id));
                pending.RemoveWhere(id => !alive.Contains(id));

                foreach (Process p in all)
                {
                    int pid = SafeId(p);
                    if (pid <= 0 || pid == selfPid || authorized.Contains(pid) || pending.Contains(pid))
                        continue;

                    string name;
                    try { name = (p.ProcessName + ".exe").ToLowerInvariant(); }
                    catch { continue; }
                    if (!names.Contains(name))
                        continue;

                    string path = TryPath(p);
                    bool suspended = TrySuspend(p);
                    pending.Add(pid);
                    hits.Add(new BlockedHit { Pid = pid, Name = DisplayName(name), FileName = name, Path = path, Suspended = suspended });
                }
            }
            finally
            {
                foreach (Process p in all)
                    p.Dispose();
            }
            return hits;
        }

        // Una app puede tener varios procesos con el mismo .exe: un desbloqueo (o un cierre) los cubre a todos.
        public void AuthorizeFamily(string fileName)
        {
            ForEachOfFamily(fileName, (p, id) =>
            {
                authorized.Add(id);
                pending.Remove(id);
                try { Native.NtResumeProcess(p.Handle); } catch { }
            });
        }

        public void KillFamily(string fileName)
        {
            ForEachOfFamily(fileName, (p, id) =>
            {
                pending.Remove(id);
                try { Native.NtResumeProcess(p.Handle); p.Kill(); } catch { }
            });
        }

        private void ForEachOfFamily(string fileName, Action<Process, int> action)
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    int id = SafeId(p);
                    if (id > 0 && id != selfPid && (p.ProcessName + ".exe").ToLowerInvariant() == fileName)
                        action(p, id);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        private string DisplayName(string fileName)
        {
            BlockedApp app = settings.Blocked.FirstOrDefault(b => b.FileName == fileName);
            return app != null ? app.Name : fileName;
        }

        private static int SafeId(Process p)
        {
            try { return p.Id; }
            catch { return -1; }
        }

        private static string TryPath(Process p)
        {
            try { return p.MainModule.FileName; }
            catch { return ""; }
        }

        private static bool TrySuspend(Process p)
        {
            try { return Native.NtSuspendProcess(p.Handle) == 0; }
            catch { return false; }
        }

        // Procesos con ventana visible.
        public static List<BlockedApp> RunningWithWindow()
        {
            Dictionary<string, BlockedApp> found = new Dictionary<string, BlockedApp>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0)
                        continue;
                    string file = (p.ProcessName + ".exe").ToLowerInvariant();
                    if (found.ContainsKey(file))
                        continue;
                    string path = TryPath(p);
                    found[file] = new BlockedApp(path.Length > 0 ? path : file, file);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return found.Values.OrderBy(a => a.Name).ToList();
        }

        // Aplicaciones instaladas (accesos del menú Inicio) más las que están abiertas: para el buscador por nombre.
        public static List<BlockedApp> InstalledAndRunning()
        {
            Dictionary<string, BlockedApp> found = new Dictionary<string, BlockedApp>();
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
            {
                string root = Environment.GetFolderPath(folder);
                if (root.Length == 0 || !Directory.Exists(root))
                    continue;
                string[] shortcuts;
                try { shortcuts = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (string lnk in shortcuts)
                {
                    try
                    {
                        string target = ResolveShortcut(lnk);
                        if (target == null || !target.ToLowerInvariant().EndsWith(".exe"))
                            continue;
                        string file = Path.GetFileName(target).ToLowerInvariant();
                        if (file.Length == 0 || found.ContainsKey(file))
                            continue;
                        found[file] = new BlockedApp(target, Path.GetFileNameWithoutExtension(lnk));
                    }
                    catch { }
                }
            }

            foreach (BlockedApp app in RunningWithWindow())
                if (!found.ContainsKey(app.FileName))
                    found[app.FileName] = app;

            return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // Resuelve el destino de un acceso directo .lnk sin dependencias externas (WScript.Shell por reflexión).
        private static string ResolveShortcut(string lnkPath)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
                return null;
            object shell = null;
            try
            {
                shell = Activator.CreateInstance(type);
                object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                object target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                return target as string;
            }
            catch { return null; }
            finally
            {
                if (shell != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
    }
}
