using System;
using System.Collections.Generic;
using System.Text;

namespace TimeClock.Core.Services
{
    /// <summary>
    /// Utility minimale per parsing/serializzazione CSV (RFC4180-style).
    /// Gestisce campi quotati, virgole, doppi apici e campi vuoti.
    /// </summary>
    public static class CsvCodec
    {
        public static string[] ParseLine(string line)
        {
            if (line == null)
                return Array.Empty<string>();

            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        // Escape doppio apice ""
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                else
                {
                    if (ch == ',')
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                    }
                    else if (ch == '"')
                    {
                        inQuotes = true;
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }

        public static string BuildLine(IEnumerable<string?> fields)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var raw in fields)
            {
                if (!first)
                    sb.Append(',');

                string value = raw ?? string.Empty;
                bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

                if (mustQuote)
                {
                    sb.Append('"');
                    sb.Append(value.Replace("\"", "\"\""));
                    sb.Append('"');
                }
                else
                {
                    sb.Append(value);
                }

                first = false;
            }

            return sb.ToString();
        }
    }
}
