// Revara fork. MIT License. See LICENSE and NOTICE.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// The user's plugin settings, kept outside the add-in folder (and outside commandRegistry.json, which the
    /// Settings window rewrites) so a reinstall or a save in that window never erases them.
    /// </summary>
    public static class PluginSettingsFile
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Revara",
                    "plugin-settings.json");
            }
        }

        /// <summary>
        /// Whether the service starts by itself. File absent = true. An object without autoStart = true. Every member
        /// named autoStart, whatever its letter case (the old setting was written as AutoStart), must be a boolean
        /// true for the answer to be true. Anything else (unreadable, not JSON, not an object, any autoStart member
        /// not a boolean, any autoStart member false) = false: a user who turned it off must never see it turn back
        /// on by itself. Never throws.
        /// </summary>
        public static bool ReadAutoStart(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;

                JToken root = JToken.Parse(File.ReadAllText(path, Encoding.UTF8));
                JObject obj = root as JObject;
                if (obj == null) return false;

                foreach (JProperty property in obj.Properties())
                {
                    if (!IsAutoStartName(property.Name)) continue;
                    JToken member = property.Value;
                    if (member == null || member.Type != JTokenType.Boolean) return false;
                    if (!(bool)member) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sets autoStart, dropping any member whose name differs from it only by letter case, and keeps every other
        /// member of the existing object (a file that does not parse as an object starts again from {}). Written
        /// atomically. Any write failure throws.
        /// </summary>
        public static void WriteAutoStart(string path, bool value)
        {
            JObject obj = null;
            try
            {
                if (File.Exists(path))
                {
                    obj = JToken.Parse(File.ReadAllText(path, Encoding.UTF8)) as JObject;
                }
            }
            catch (Exception)
            {
                obj = null;
            }
            if (obj == null) obj = new JObject();

            var variants = new System.Collections.Generic.List<string>();
            foreach (JProperty property in obj.Properties())
            {
                if (IsAutoStartName(property.Name)) variants.Add(property.Name);
            }
            foreach (string name in variants) obj.Remove(name);

            obj["autoStart"] = value;
            AtomicFile.WriteUtf8NoBom(path, obj.ToString(Formatting.None));
        }

        private static bool IsAutoStartName(string name)
        {
            return string.Equals(name, "autoStart", StringComparison.OrdinalIgnoreCase);
        }
    }
}
