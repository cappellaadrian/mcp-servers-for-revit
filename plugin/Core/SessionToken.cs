// Revara fork. The token design (32 random bytes, uppercase hex, constant-time comparison) is adapted from
// mcp-servers-for-revit PR #60 (commit a4ada37), MIT License. See LICENSE and NOTICE.

using System;
using System.Security.Cryptography;
using System.Text;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// The per-session secret a client must present in the "auth" member of every request.
    /// </summary>
    public static class SessionToken
    {
        /// <summary>
        /// 32 bytes from a cryptographic random generator, as 64 uppercase hex characters.
        /// </summary>
        public static string Generate()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// True when both values are present, have the same length and are equal. The comparison takes the same
        /// time wherever the first difference is.
        /// </summary>
        public static bool Matches(string expected, string presented)
        {
            if (expected == null || presented == null) return false;
            if (expected.Length != presented.Length) return false;

#if NET
            byte[] a = Encoding.UTF8.GetBytes(expected);
            byte[] b = Encoding.UTF8.GetBytes(presented);
            return CryptographicOperations.FixedTimeEquals(a, b);
#else
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                diff |= expected[i] ^ presented[i];
            }
            return diff == 0;
#endif
        }
    }
}
