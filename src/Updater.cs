using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace CandadoApp
{
    internal sealed class UpdateInfo
    {
        public string DownloadUrl = "";
        public string PageUrl = "";
    }

    // Avisa de versiones nuevas y, con un clic, descarga la nueva y reemplaza la vieja.
    internal static class Updater
    {
        // El workflow pone el commit exacto en el cuerpo del release; solo avisa si NO coincide con el de esta build.
        public static UpdateInfo CheckForUpdate()
        {
            try
            {
                if (string.IsNullOrEmpty(BuildInfo.Repo) || string.IsNullOrEmpty(BuildInfo.Commit))
                    return null;

                string release = Get("https://api.github.com/repos/" + BuildInfo.Repo + "/releases/tags/latest");
                if (release == null)
                    return null; // sin red, repo privado o sin release: no molestar

                Match body = Regex.Match(release, "\"body\"\\s*:\\s*\"([^\"]*)\"");
                string remote = body.Success ? body.Groups[1].Value.Trim() : "";
                if (remote.Length == 0 || remote.Equals(BuildInfo.Commit, StringComparison.OrdinalIgnoreCase))
                    return null; // ya es la última versión

                UpdateInfo info = new UpdateInfo();
                Match asset = Regex.Match(release, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                if (asset.Success)
                    info.DownloadUrl = asset.Groups[1].Value;
                Match html = Regex.Match(release, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                info.PageUrl = html.Success ? html.Groups[1].Value : "https://github.com/" + BuildInfo.Repo + "/releases/latest";
                return info;
            }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
        }

        // Descarga el nuevo .exe, deja el actual como ".old", pone el nuevo en su lugar y lo inicia.
        public static bool ApplyUpdate(UpdateInfo info)
        {
            if (info == null || info.DownloadUrl.Length == 0)
                return false;
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string newPath = exe + ".new";
                string oldPath = exe + ".old";

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, Program.Name);
                    client.DownloadFile(info.DownloadUrl, newPath);
                }

                if (File.Exists(oldPath))
                    File.Delete(oldPath);
                File.Move(exe, oldPath);     // se puede renombrar un .exe en ejecución
                File.Move(newPath, exe);
                // Arranca la versión nueva pidiéndole que espere a que esta cierre (para no chocar con la instancia única).
                Process.Start(new ProcessStartInfo(exe) { Arguments = Program.UpdateRestartArgument, UseShellExecute = false });
                return true;
            }
            catch (Exception e)
            {
                if (e is WebException || e is IOException || e is UnauthorizedAccessException)
                    return false;
                throw;
            }
        }

        // Borra el ejecutable viejo que dejó la actualización anterior.
        public static void CleanupOld()
        {
            try
            {
                string old = Process.GetCurrentProcess().MainModule.FileName + ".old";
                if (File.Exists(old))
                    File.Delete(old);
            }
            catch { }
        }

        private static string Get(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = Program.Name;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 15000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd();
            }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
        }
    }
}
