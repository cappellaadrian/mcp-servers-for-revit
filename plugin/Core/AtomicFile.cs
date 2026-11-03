// Revara fork. MIT License. See LICENSE and NOTICE.

using System;
using System.IO;
using System.Text;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Writes a small text file so a reader never sees a partial file, and two writers never share a temporary file.
    /// </summary>
    internal static class AtomicFile
    {
        /// <summary>
        /// Writes text as UTF-8 without BOM to a uniquely named temporary file in the folder of path, then moves it
        /// over path. Creates the folder. Any failure throws, after the temporary file is deleted.
        /// </summary>
        internal static void WriteUtf8NoBom(string path, string text)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, new UTF8Encoding(false).GetBytes(text));

#if NET
                File.Move(tmp, path, true);
#else
                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
#endif
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch (Exception)
                {
                    // Nothing more to do: the original failure is the one that matters.
                }
                throw;
            }
        }
    }
}
