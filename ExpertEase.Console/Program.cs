using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using ExpertEase;

namespace ExpertEase.Console
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // 1. Decide which knowledge base file to use
            string kbPath;

            if (args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                kbPath = args[0];
            }
            else
            {
                // Fallback default if nothing is passed
                kbPath = "sunday.json";
            }

            try
            {
                kbPath = KnowledgeBaseLoader.ResolveKbPath(kbPath);
            }
            catch (FileNotFoundException)
            {
                System.Console.WriteLine($"Error: file '{kbPath}' not found.");
                System.Console.WriteLine("Pass a path to a .json or .csv knowledge base, e.g.:");
                System.Console.WriteLine("  dotnet run -- sunday.json");
                System.Console.WriteLine("  dotnet run -- car_pricing.csv");
                return;
            }

            System.Console.WriteLine($"Loading knowledge base from: {kbPath}");

            // 2. Load attributes + examples from the chosen file
            var (attributes, examples) = KnowledgeBaseLoader.LoadFromFile(kbPath);

            // 3. Train the tree
            var root = C45Trainer.Train(examples, attributes);

            // 4. Show tree, rules, and attributes
            System.Console.WriteLine("=== Decision tree ===");
            System.Console.Write(RuleExtractor.FormatTree(root));

            System.Console.WriteLine();
            System.Console.WriteLine("=== Induced rules ===");
            System.Console.Write(RuleExtractor.FormatRules(root));

            System.Console.WriteLine();
            System.Console.WriteLine("=== Attributes ===");
            System.Console.Write(RuleExtractor.FormatAttributes(attributes));

            // 5. Interactive consultation
            InteractiveConsult(root, attributes);
        }

        private static void PrintQueryResult(TreeNode root, Dictionary<string, string> query)
        {
            string key = string.Join(", ", query.Select(kv => $"{kv.Key}={kv.Value}"));
            var advice = C45Trainer.Classify(root, query);
            System.Console.WriteLine($"{key} => {advice}");
        }

        // Console-only interactive consultation
        private static void InteractiveConsult(TreeNode root, List<AttributeDef> attributes)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            System.Console.WriteLine();
            System.Console.WriteLine("=== Interactive consultation ===");
            System.Console.WriteLine("Type 'why' to ask why I'm asking a question.");
            System.Console.WriteLine("Type 'quit' to exit the consultation.");
            System.Console.WriteLine();

            var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Walk down the learned tree
            var current = root;

            while (!current.IsLeaf)
            {
                if (current.AttributeName == null)
                    throw new InvalidOperationException("Non-leaf node without AttributeName.");

                // Find the attribute definition for this node
                var attrDef = attributes.FirstOrDefault(a =>
                    string.Equals(a.Name, current.AttributeName, StringComparison.OrdinalIgnoreCase));

                // Fallback: if not defined in attributes, synthesize something
                if (attrDef == null)
                {
                    if (current.IsNumericSplit)
                    {
                        attrDef = new AttributeDef(current.AttributeName);
                    }
                    else
                    {
                        var domain = current.Children?.Keys.ToList() ?? new List<string>();
                        attrDef = new AttributeDef(current.AttributeName, domain);
                    }
                }

                // Build prompt
                if (attrDef.Kind == AttributeKind.Numeric)
                {
                    // Numeric prompt
                    while (true)
                    {
                        System.Console.Write($"{attrDef.Name}? (numeric): ");
                        var raw = System.Console.ReadLine();
                        var input = raw?.Trim();

                        if (string.IsNullOrEmpty(input))
                        {
                            System.Console.WriteLine("Please enter a value, 'why', or 'quit'.");
                            continue;
                        }

                        if (string.Equals(input, "quit", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Console.WriteLine("Consultation cancelled.");
                            return;
                        }

                        if (string.Equals(input, "why", StringComparison.OrdinalIgnoreCase))
                        {
                            var whyText = C45Trainer.Why(root, attrDef, answers);

                            System.Console.WriteLine();
                            System.Console.WriteLine("--- WHY ---");
                            System.Console.WriteLine(whyText);
                            System.Console.WriteLine();

                            continue; // re-ask same question
                        }

                        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        {
                            System.Console.WriteLine("Invalid value, please enter a numeric value (e.g. 23.5), 'why', or 'quit'.");
                            continue;
                        }

                        // Store numeric answer
                        var canonical = d.ToString(CultureInfo.InvariantCulture);
                        answers[attrDef.Name] = canonical;

                        if (current.Threshold == null ||
                            current.LessOrEqualChild == null ||
                            current.GreaterChild == null)
                        {
                            throw new InvalidOperationException("Numeric split node not fully initialized.");
                        }

                        current = d <= current.Threshold.Value
                            ? current.LessOrEqualChild
                            : current.GreaterChild;

                        break;
                    }
                }
                else
                {
                    // Categorical prompt with numeric shortcuts
                    var domain = attrDef.Domain.ToList();
                    if (domain.Count == 0 && current.Children != null)
                        domain = current.Children.Keys.ToList();

                    while (true)
                    {
                        // Example: CauseCategory? (1) airline_control (2) extraordinary:
                        var optionsText = string.Join(" ",
                            domain.Select((v, i) => $"({i + 1}) {v}"));

                        System.Console.Write($"{attrDef.Name}? {optionsText}: ");
                        var raw = System.Console.ReadLine();
                        var input = raw?.Trim();

                        if (string.IsNullOrEmpty(input))
                        {
                            System.Console.WriteLine("Please enter a number, a value, 'why', or 'quit'.");
                            continue;
                        }

                        // QUIT
                        if (string.Equals(input, "quit", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Console.WriteLine("Consultation cancelled.");
                            return;
                        }

                        // WHY
                        if (string.Equals(input, "why", StringComparison.OrdinalIgnoreCase))
                        {
                            var whyText = C45Trainer.Why(root, attrDef, answers);

                            System.Console.WriteLine();
                            System.Console.WriteLine("--- WHY ---");
                            System.Console.WriteLine(whyText);
                            System.Console.WriteLine();

                            continue; // re-ask same question
                        }

                        string chosenValue = null;

                        // Try numeric selection first
                        if (int.TryParse(input, out var idx))
                        {
                            if (idx >= 1 && idx <= domain.Count)
                            {
                                chosenValue = domain[idx - 1];
                            }
                        }

                        // Then try textual match if numeric didn't work
                        if (chosenValue == null)
                        {
                            chosenValue = domain.FirstOrDefault(v =>
                                string.Equals(v, input, StringComparison.OrdinalIgnoreCase));
                        }

                        if (chosenValue == null)
                        {
                            System.Console.WriteLine("Invalid value. Enter one of:");
                            System.Console.WriteLine("- a number between 1 and " + domain.Count);
                            System.Console.WriteLine("- one of: " + string.Join(", ", domain));
                            System.Console.WriteLine("- or 'why' to understand why I'm asking");
                            System.Console.WriteLine("- or 'quit' to exit the consultation.");
                            continue;
                        }

                        // Store answer
                        answers[attrDef.Name] = chosenValue;

                        if (current.Children == null ||
                            !current.Children.TryGetValue(chosenValue, out var next))
                        {
                            throw new InvalidOperationException(
                                $"No branch in the tree for {attrDef.Name} = {chosenValue}.");
                        }

                        current = next;
                        break;
                    }
                }
            }

            // At leaf
            System.Console.WriteLine();
            System.Console.WriteLine("=== RESULT ===");
            System.Console.WriteLine($"Advice: {current.Label}");
            System.Console.WriteLine();

            var how = C45Trainer.How(root, answers);
            System.Console.WriteLine("--- HOW ---");
            System.Console.WriteLine(how);
        }
    }
}
