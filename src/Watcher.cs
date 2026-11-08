using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

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

        // Aplicaciones instaladas (accesos del menú Inicio + rutas del registro) más las abiertas: para el buscador por nombre.
        public static List<BlockedApp> InstalledAndRunning()
        {
            Dictionary<string, BlockedApp> found = new Dictionary<string, BlockedApp>(StringComparer.OrdinalIgnoreCase);

            object shell = CreateShell();
            try
            {
                foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
                {
                    string root = Environment.GetFolderPath(folder);
                    if (root.Length == 0)
                        continue;
                    foreach (string lnk in FindShortcuts(root))
                    {
                        try
                        {
                            string target = ResolveShortcut(shell, lnk);
                            if (target == null || !target.ToLowerInvariant().EndsWith(".exe") || !File.Exists(target))
                                continue;
                            string file = Path.GetFileName(target).ToLowerInvariant();
                            if (file.Length == 0 || found.ContainsKey(file))
                                continue;
                            found[file] = new BlockedApp(target, Path.GetFileNameWithoutExtension(lnk));
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                if (shell != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }

            // Rutas registradas en "App Paths" (programas sin acceso directo en Inicio).
            AddAppPaths(found, Registry.LocalMachine);
            AddAppPaths(found, Registry.CurrentUser);

            // Juegos instalados de Epic y Steam (no aparecen en el menú Inicio).
            AddEpicGames(found);
            AddSteamGames(found);

            foreach (BlockedApp app in RunningWithWindow())
                if (!found.ContainsKey(app.FileName))
                    found[app.FileName] = app;

            return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // Recorre las carpetas del menú Inicio sin abortar por una subcarpeta sin permisos.
        private static List<string> FindShortcuts(string root)
        {
            List<string> result = new List<string>();
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try { result.AddRange(Directory.GetFiles(dir, "*.lnk")); }
                catch { }
                try { foreach (string sub in Directory.GetDirectories(dir)) stack.Push(sub); }
                catch { }
            }
            return result;
        }

        private static void AddAppPaths(Dictionary<string, BlockedApp> found, RegistryKey hive)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    if (key == null)
                        return;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        if (!name.ToLowerInvariant().EndsWith(".exe"))
                            continue;
                        try
                        {
                            using (RegistryKey sub = key.OpenSubKey(name))
                            {
                                string path = sub != null ? sub.GetValue(null) as string : null;
                                if (string.IsNullOrEmpty(path))
                                    continue;
                                path = path.Trim('"');
                                if (!File.Exists(path))
                                    continue;
                                string file = Path.GetFileName(path).ToLowerInvariant();
                                if (file.Length == 0 || found.ContainsKey(file))
                                    continue;
                                found[file] = new BlockedApp(path, Path.GetFileNameWithoutExtension(path));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // Juegos de Epic: cada manifiesto .item trae la carpeta, el ejecutable y el nombre visible.
        private static void AddEpicGames(Dictionary<string, BlockedApp> found)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Epic\EpicGamesLauncher\Data\Manifests");
                if (!Directory.Exists(dir))
                    return;
                foreach (string item in Directory.GetFiles(dir, "*.item"))
                {
                    try
                    {
                        string json = File.ReadAllText(item);
                        string install = JsonField(json, "InstallLocation");
                        string exe = JsonField(json, "LaunchExecutable");
                        string name = JsonField(json, "DisplayName");
                        if (install.Length == 0 || exe.Length == 0)
                            continue;
                        string path = Path.Combine(install, exe);
                        if (!File.Exists(path))
                            continue;
                        string file = Path.GetFileName(path).ToLowerInvariant();
                        if (file.Length == 0 || found.ContainsKey(file))
                            continue;
                        found[file] = new BlockedApp(path, name.Length > 0 ? name : Path.GetFileNameWithoutExtension(path));
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Juegos de Steam: se recorren las bibliotecas y se listan los .exe de cada juego.
        private static void AddSteamGames(Dictionary<string, BlockedApp> found)
        {
            try
            {
                string steam = SteamPath();
                if (steam.Length == 0)
                    return;

                List<string> libraries = new List<string> { steam };
                string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p))
                            libraries.Add(p);
                    }
                }

                foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string common = Path.Combine(library, @"steamapps\common");
                    if (!Directory.Exists(common))
                        continue;
                    string[] games;
                    try { games = Directory.GetDirectories(common); }
                    catch { continue; }

                    foreach (string game in games)
                    {
                        string gameName = Path.GetFileName(game);
                        string norm = Normalize(gameName);

                        List<string> candidates = ShallowExes(game)
                            .Where(e => !IsHelperExe(Path.GetFileName(e).ToLowerInvariant())).ToList();

                        // Preferir el .exe cuyo nombre se parece al del juego; si no hay, los de la carpeta raíz.
                        List<string> matches = candidates.Where(e =>
                        {
                            string n = Normalize(Path.GetFileNameWithoutExtension(e));
                            return n.Length > 2 && (norm.Contains(n) || n.Contains(norm));
                        }).ToList();

                        List<string> chosen = matches.Count > 0
                            ? matches
                            : candidates.Where(e => string.Equals(Path.GetDirectoryName(e), game, StringComparison.OrdinalIgnoreCase)).ToList();

                        foreach (string exe in chosen)
                        {
                            string file = Path.GetFileName(exe).ToLowerInvariant();
                            if (found.ContainsKey(file))
                                continue;
                            found[file] = new BlockedApp(exe, gameName);
                        }
                    }
                }
            }
            catch { }
        }

        // .exe en la carpeta del juego y en sus subcarpetas directas (muchos juegos lo tienen en \bin, \Binaries, etc.).
        private static IEnumerable<string> ShallowExes(string root)
        {
            List<string> list = new List<string>();
            try { list.AddRange(Directory.GetFiles(root, "*.exe")); }
            catch { }
            try
            {
                foreach (string sub in Directory.GetDirectories(root))
                {
                    try { list.AddRange(Directory.GetFiles(sub, "*.exe")); }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static string Normalize(string text)
        {
            char[] chars = text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private static bool IsHelperExe(string file)
        {
            return file.Contains("unins") || file.Contains("crash") || file.Contains("redist")
                || file.Contains("setup") || file.Contains("vcredist") || file.Contains("dxsetup")
                || file.Contains("install") || file.Contains("launcher_helper") || file.Contains("notification_helper");
        }

        private static string SteamPath()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string p = k != null ? k.GetValue("SteamPath") as string : null;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                        return p;
                }
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    string p = k != null ? k.GetValue("InstallPath") as string : null;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                        return p;
                }
            }
            catch { }
            return "";
        }

        private static string JsonField(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success)
                return "";
            return m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/");
        }

        private static object CreateShell()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                return type != null ? Activator.CreateInstance(type) : null;
            }
            catch { return null; }
        }

        // Resuelve el destino de un acceso directo .lnk reutilizando un único objeto WScript.Shell.
        private static string ResolveShortcut(object shell, string lnkPath)
        {
            if (shell == null)
                return null;
            try
            {
                object shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                object target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                return target as string;
            }
            catch { return null; }
        }
    }
}
