using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ExpertEase
{
    public static class KnowledgeBaseLoader
    {
        private const string KnowledgeFolderName = "ExpertEase.Knowledge";

        /// <summary>
        /// Resolves a KB filename to a full path. Checks: exact/relative path first, then the ExpertEase.Knowledge folder.
        /// </summary>
        public static string ResolveKbPath(string filename)
        {
            if (File.Exists(filename))
                return Path.GetFullPath(filename);

            var knowledgeDir = FindKnowledgeDirectory();
            if (knowledgeDir != null)
            {
                var inKb = Path.Combine(knowledgeDir, filename);
                if (File.Exists(inKb))
                    return inKb;
            }

            throw new FileNotFoundException($"Knowledge base '{filename}' not found.");
        }

        /// <summary>
        /// Lists available KB files (*.json, *.csv) in the ExpertEase.Knowledge folder.
        /// </summary>
        public static List<string> ListKnowledgeBases()
        {
            var knowledgeDir = FindKnowledgeDirectory();
            if (knowledgeDir == null)
                return new List<string>();

            return Directory.GetFiles(knowledgeDir, "*.json")
                .Concat(Directory.GetFiles(knowledgeDir, "*.csv"))
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .OrderBy(f => f)
                .ToList()!;
        }

        /// <summary>
        /// Walks up from AppContext.BaseDirectory looking for the ExpertEase.Knowledge folder.
        /// </summary>
        private static string? FindKnowledgeDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, KnowledgeFolderName);
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        public static (List<AttributeDef> Attributes, List<TrainingExample> Examples) LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be null or empty.", nameof(path));

            return Path.GetExtension(path).ToLowerInvariant() == ".csv"
                ? LoadCsv(path)
                : LoadJson(path);
        }

        public static (List<AttributeDef> Attributes, List<TrainingExample> Examples) LoadJson(string path)
        {
            var json = File.ReadAllText(path);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var expert = JsonSerializer.Deserialize<ExpertFile>(json, options)
                         ?? throw new InvalidOperationException("Could not deserialize expert file.");

            if (expert.Attributes == null || expert.Examples == null)
                throw new InvalidOperationException("Expert file missing attributes or examples.");

            var attrs = expert.Attributes.Select(a =>
            {
                var kind = (a.Kind ?? "categorical").ToLowerInvariant();

                return kind switch
                {
                    "numeric" => new AttributeDef(a.Name),
                    "categorical" => new AttributeDef(a.Name, a.Domain ?? new List<string>()),
                    _ => throw new InvalidOperationException($"Unknown attribute kind '{a.Kind}' for '{a.Name}'.")
                };
            }).ToList();

            var examples = expert.Examples.Select(e =>
                new TrainingExample(
                    new Dictionary<string, string>(e.Attributes, StringComparer.OrdinalIgnoreCase),
                    e.Label)
            ).ToList();

            return (attrs, examples);
        }

        public static (List<AttributeDef> Attributes, List<TrainingExample> Examples) LoadCsv(string path)
        {
            var lines = File.ReadAllLines(path)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count < 2)
                throw new InvalidOperationException("CSV file must have a header row and at least one data row.");

            var header = ParseCsvLine(lines[0]);
            if (header.Count < 2)
                throw new InvalidOperationException("CSV file must have at least one attribute column and a label column.");

            var attrNames = header.Take(header.Count - 1).ToList();
            int attrCount = attrNames.Count;

            var rows = new List<string[]>();
            for (int i = 1; i < lines.Count; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                if (fields.Count != header.Count)
                    throw new InvalidOperationException(
                        $"Row {i + 1} has {fields.Count} fields but header has {header.Count}.");
                rows.Add(fields.ToArray());
            }

            var isNumeric = new bool[attrCount];
            for (int col = 0; col < attrCount; col++)
            {
                isNumeric[col] = rows
                    .Select(r => r[col])
                    .Where(v => v != "*")
                    .All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
            }

            var attrs = new List<AttributeDef>();
            for (int col = 0; col < attrCount; col++)
            {
                if (isNumeric[col])
                {
                    attrs.Add(new AttributeDef(attrNames[col]));
                }
                else
                {
                    var domain = rows
                        .Select(r => r[col])
                        .Where(v => v != "*")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    attrs.Add(new AttributeDef(attrNames[col], domain));
                }
            }

            var examples = rows.Select(r =>
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int col = 0; col < attrCount; col++)
                    dict[attrNames[col]] = r[col];
                var label = r[attrCount];
                return new TrainingExample(dict, label);
            }).ToList();

            return (attrs, examples);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            int i = 0;
            while (i <= line.Length)
            {
                if (i == line.Length)
                {
                    fields.Add("");
                    break;
                }

                if (line[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"')
                            {
                                sb.Append('"');
                                i += 2;
                            }
                            else
                            {
                                i++;
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(line[i]);
                            i++;
                        }
                    }
                    fields.Add(sb.ToString().Trim());
                    if (i < line.Length && line[i] == ',')
                        i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',')
                        i++;
                    fields.Add(line.Substring(start, i - start).Trim());
                    if (i < line.Length)
                        i++;
                    else
                        break;
                }
            }
            return fields;
        }
    }
}
