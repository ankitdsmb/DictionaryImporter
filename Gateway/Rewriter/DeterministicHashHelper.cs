using System.Security.Cryptography;

namespace DictionaryImporter.Gateway.Rewriter
{
    internal static class DeterministicHashHelper
    {
        public static string Sha256Hex(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(input.Trim());

                using var sha = SHA256.Create();
                var hashBytes = sha.ComputeHash(bytes);

                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (var b in hashBytes)
                    sb.Append(b.ToString("x2"));

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}