using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Doczonal.Services
{
    internal static class CsvExporter
    {
        public static void Export(List<Dictionary<string, string>> rows, string outputPath)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var allHeaders = rows.SelectMany(r => r.Keys).Distinct().ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", allHeaders));

            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",", allHeaders.Select(h =>
                    row.TryGetValue(h, out var v) ? Escape(v) : "")));
            }

            WriteText(outputPath, sb.ToString());
        }

        public static void WriteText(string outputPath, string content)
        {
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));
            if (content == null) content = string.Empty;

            File.WriteAllText(outputPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
