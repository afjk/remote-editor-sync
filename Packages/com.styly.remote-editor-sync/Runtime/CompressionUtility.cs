using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RemoteEditorSync
{
    /// <summary>
    /// Tiny helper for gzipping payloads that need to cross the wire frequently.
    /// </summary>
    public static class CompressionUtility
    {
        public static string CompressToBase64(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input ?? string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(input);
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(bytes, 0, bytes.Length);
                }

                return Convert.ToBase64String(memory.ToArray());
            }
        }

        public static bool TryDecompressFromBase64(string input, out string output)
        {
            output = string.Empty;
            if (string.IsNullOrEmpty(input))
            {
                return true;
            }

            try
            {
                var bytes = Convert.FromBase64String(input);
                using (var memory = new MemoryStream(bytes))
                using (var gzip = new GZipStream(memory, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    output = reader.ReadToEnd();
                    return true;
                }
            }
            catch
            {
                output = string.Empty;
                return false;
            }
        }
    }
}
