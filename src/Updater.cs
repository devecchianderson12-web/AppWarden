using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace CandadoApp
{
    // Consulta el release "latest" y avisa si su commit es distinto al de esta compilación (hay versión nueva).
    internal static class Updater
    {
        public static string CheckForUpdate()
        {
            try
            {
                if (string.IsNullOrEmpty(BuildInfo.Repo) || string.IsNullOrEmpty(BuildInfo.Commit))
                    return null; // compilado en local, sin repositorio: no hay a quién consultar

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + BuildInfo.Repo + "/releases/tags/latest");
                request.UserAgent = Program.Name;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 15000;

                string json;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    json = reader.ReadToEnd();

                // El workflow pone el commit del release en el cuerpo; si no coincide con el nuestro, hay actualización.
                Match body = Regex.Match(json, "\"body\"\\s*:\\s*\"([^\"]*)\"");
                string remote = body.Success ? body.Groups[1].Value.Trim() : "";
                if (remote.Length == 0 || remote.Equals(BuildInfo.Commit, StringComparison.OrdinalIgnoreCase))
                    return null;

                Match html = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                return html.Success ? html.Groups[1].Value : "https://github.com/" + BuildInfo.Repo + "/releases/latest";
            }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
        }
    }
}
