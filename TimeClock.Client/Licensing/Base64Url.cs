using System;

namespace TimeClock.Client.Licensing
{
    internal static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static byte[] Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<byte>();
            }

            string base64 = value
                .Replace('-', '+')
                .Replace('_', '/');

            int padding = 4 - (base64.Length % 4);
            if (padding < 4)
            {
                base64 = base64.PadRight(base64.Length + padding, '=');
            }

            return Convert.FromBase64String(base64);
        }
    }
}
