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
        // Compara el commit al que apunta la etiqueta "latest" (fuente canónica) con el de esta compilación.
        public static UpdateInfo CheckForUpdate()
        {
            try
            {
                if (string.IsNullOrEmpty(BuildInfo.Repo) || string.IsNullOrEmpty(BuildInfo.Commit))
                    return null;

                string refJson = Get("https://api.github.com/repos/" + BuildInfo.Repo + "/git/ref/tags/latest");
                if (refJson == null)
                    return null; // sin red, repo privado o sin release: no molestar

                Match sha = Regex.Match(refJson, "\"sha\"\\s*:\\s*\"([0-9a-fA-F]{40})\"");
                if (!sha.Success || sha.Groups[1].Value.Equals(BuildInfo.Commit, StringComparison.OrdinalIgnoreCase))
                    return null; // ya es la última versión

                string release = Get("https://api.github.com/repos/" + BuildInfo.Repo + "/releases/tags/latest");
                UpdateInfo info = new UpdateInfo();
                if (release != null)
                {
                    Match asset = Regex.Match(release, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                    if (asset.Success)
                        info.DownloadUrl = asset.Groups[1].Value;
                    Match html = Regex.Match(release, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                    if (html.Success)
                        info.PageUrl = html.Groups[1].Value;
                }
                if (info.PageUrl.Length == 0)
                    info.PageUrl = "https://github.com/" + BuildInfo.Repo + "/releases/latest";
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
                Process.Start(exe);          // arranca la versión nueva
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
