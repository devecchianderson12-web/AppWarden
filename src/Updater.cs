using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace CandadoApp
{
    // Consulta el release "latest" del repositorio y avisa si se publicó uno más nuevo que esta compilación.
    internal static class Updater
    {
        public static string CheckForUpdate()
        {
            try
            {
                if (string.IsNullOrEmpty(BuildInfo.Repo) || string.IsNullOrEmpty(BuildInfo.BuildTimeUtc))
                    return null; // compilado en local, sin repositorio: no hay a quién consultar

                DateTime built = ParseUtc(BuildInfo.BuildTimeUtc);
                if (built == DateTime.MinValue)
                    return null;

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + BuildInfo.Repo + "/releases/tags/latest");
                request.UserAgent = Program.Name;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 15000;

                string json;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    json = reader.ReadToEnd();

                Match published = Regex.Match(json, "\"published_at\"\\s*:\\s*\"([^\"]+)\"");
                if (!published.Success)
                    return null;

                DateTime releaseTime = ParseUtc(published.Groups[1].Value);
                if (releaseTime <= built.AddSeconds(120)) // margen: el release propio se publica segundos después de compilar
                    return null;

                Match html = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                return html.Success ? html.Groups[1].Value : "https://github.com/" + BuildInfo.Repo + "/releases/latest";
            }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
            catch (FormatException) { return null; }
        }

        private static DateTime ParseUtc(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return parsed;
            return DateTime.MinValue;
        }
    }
}
