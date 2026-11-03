// Revara fork. Adapted from mcp-servers-for-revit PR #60 (commit a4ada37), MIT License. See LICENSE and NOTICE.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// The file where the plugin publishes the port it bound and this session's token, so the client can find both.
    /// </summary>
    public static class ConnectionInfoFile
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Revara",
                    "connection.json");
            }
        }

        /// <summary>
        /// Writes {"port":..,"token":".."} as UTF-8 without BOM to a uniquely named temporary file next to path, then moves it over
        /// path, so a reader never sees a partial file. Any failure throws.
        /// </summary>
        public static void Write(string path, int port, string token)
        {
            var obj = new JObject();
            obj["port"] = port;
            obj["token"] = token;
            string json = obj.ToString(Newtonsoft.Json.Formatting.None);

            AtomicFile.WriteUtf8NoBom(path, json);
        }

        /// <summary>
        /// Deletes the file only if it parses and holds this token (another Revit may have replaced it). Never throws.
        /// </summary>
        public static void DeleteIfOwned(string path, string token)
        {
            try
            {
                if (token == null || !File.Exists(path)) return;

                JObject obj = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                string found = (obj["token"] as JValue)?.Value as string;
                if (found != null && found == token)
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Unreadable or not ours: leave it.
            }
        }
    }
}
