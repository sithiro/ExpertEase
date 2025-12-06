using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ExpertEase
{
	public static class Program
	{
		public static void Main()
		{
			// 1. Attribute definitions
			var attributes = new List<AttributeDef>
			{
				new AttributeDef("Weather", new[] { "raining", "sunny", "cloudy" }),
				new AttributeDef("Family",  new[] { "yes", "no"       			 }),
				new AttributeDef("Car",     new[] { "yes", "no"       			 })
			};

			// 2. Training examples (with "*" wildcards in examples only)
			var examples = new List<TrainingExample>
			{
				new TrainingExample(new Dictionary<string,string>
				{
					["Weather"] = "raining",
					["Family"]  = "yes",
					["Car"]     = "yes"
				}, "museum"),

				new TrainingExample(new Dictionary<string,string>
				{
					["Weather"] = "sunny",
					["Family"]  = "yes",
					["Car"]     = "yes"
				}, "beach"),

				new TrainingExample(new Dictionary<string,string>
				{
					["Weather"] = "*",
					["Family"]  = "no",
					["Car"]     = "yes"
				}, "fishing"),

				new TrainingExample(new Dictionary<string,string>
				{
					["Weather"] = "*",
					["Family"]  = "*",
					["Car"]     = "no"
				}, "home"),
			};

			// 3. Train the tree
			var root = C45Trainer.Train(examples, attributes);

			// 4. Show rules
			Console.WriteLine("=== Induced rules ===");
			var rules = RuleExtractor.ExtractRules(root);
			foreach (var r in rules)
				Console.WriteLine(r);

			Console.WriteLine();

			// 5. Show tree
			Console.WriteLine("=== Decision tree ===");
			Console.WriteLine(RuleExtractor.FormatTree(root));

			Console.WriteLine();

			// 6. Some test classifications
			var q1 = new Dictionary<string, string>
			{
				["Weather"] = "raining",
				["Family"] = "yes",
				["Car"] = "yes"
			};

			var q2 = new Dictionary<string, string>
			{
				["Weather"] = "sunny",
				["Family"] = "no",
				["Car"] = "yes"
			};

			var q3 = new Dictionary<string, string>
			{
				["Weather"] = "sunny",
				["Family"] = "yes",
				["Car"] = "no"
			};

			Console.WriteLine("=== Test queries ===");
			PrintQueryResult(root, q1);
			PrintQueryResult(root, q2);
			PrintQueryResult(root, q3);

			Console.WriteLine();

			// 7. Interactive consult
			Console.WriteLine("=== Interactive consultation ===");
			Console.WriteLine("Type 'why' to ask why I'm asking a question.");
			Console.WriteLine();

			InteractiveConsult(root, attributes);

			Console.WriteLine();
			Console.WriteLine("Press ENTER to exit.");
			Console.ReadLine();
		}

		private static void PrintQueryResult(TreeNode root, Dictionary<string, string> query)
		{
			string key = string.Join(", ", query.Select(kv => $"{kv.Key}={kv.Value}"));
			var advice = C45Trainer.Classify(root, query);
			Console.WriteLine($"{key} => {advice}");
		}

		// Console-only interactive consultation
		private static void InteractiveConsult(TreeNode root, List<AttributeDef> attributes)
		{
			var answers = new Dictionary<string, string>();

			string FormatAnswers() =>
				answers.Count == 0
					? "(none yet)"
					: string.Join(", ", answers.Select(kv => $"{kv.Key}={kv.Value}"));

			foreach (var attr in attributes)
			{
				while (true)
				{
					Console.Write($"{attr.Name}? ({string.Join("/", attr.Domain)}): ");
					var raw = Console.ReadLine();
					var input = raw?.Trim();

					if (string.IsNullOrEmpty(input))
					{
						Console.WriteLine("Please enter a value or 'why'.");
						continue;
					}

					if (string.Equals(input, "why", StringComparison.OrdinalIgnoreCase))
					{
						var whyText = C45Trainer.Why(root, attr, answers);

						Console.WriteLine();
						Console.WriteLine("--- WHY ---");
						Console.WriteLine(whyText);
						Console.WriteLine();

						continue;
					}

					var match = attr.Domain
						.FirstOrDefault(d =>
							string.Equals(d, input, StringComparison.OrdinalIgnoreCase));

					if (match == null)
					{
						Console.WriteLine("Invalid value, please choose one of: " +
										  string.Join(", ", attr.Domain) +
										  " or type 'why'.");
						continue;
					}

					answers[attr.Name] = match;
					break;
				}
			}

			var advice = C45Trainer.Classify(root, answers);
			var how = C45Trainer.How(root, answers);

			Console.WriteLine();
			Console.WriteLine("=== RESULT ===");
			Console.WriteLine($"Advice: {advice}");
			Console.WriteLine();
			Console.WriteLine("--- HOW ---");
			Console.WriteLine(how);
		}
	}
	// Attribute definition
	public sealed class AttributeDef
	{
		public string Name { get; }
		public IReadOnlyList<string> Domain { get; }

		public AttributeDef(string name, IReadOnlyList<string> domain)
        {
            Name = name;
            Domain = domain;
        }
    }

    // One training row: attributes -> label
    public sealed class TrainingExample
    {
        public Dictionary<string, string> Attributes { get; }
        public string Label { get; }

        public TrainingExample(Dictionary<string, string> attributes, string label)
        {
            Attributes = attributes;
            Label = label;
        }
    }

    public enum LeafReason
    {
        None,
        Pure,
        NoAttributesLeft,
        NoUsefulSplit,
        MajorityOfNodeForMissingBranch
    }

    // Decision tree node
    public sealed class TreeNode
    {
        public bool IsLeaf { get; set; }
        public string? Label { get; set; }

        public string? AttributeName { get; set; }
        public Dictionary<string, TreeNode>? Children { get; set; }

        public LeafReason Reason { get; set; }
    }

    // C4.5-style trainer (Gain Ratio) with wildcard support
    public static class C45Trainer
    {
        // Entry point: train tree
        public static TreeNode Train(
            IReadOnlyList<TrainingExample> examples,
            IReadOnlyList<AttributeDef> attributes)
        {
            if (examples == null) throw new ArgumentNullException(nameof(examples));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            // If no examples, return a dummy leaf
            if (examples.Count == 0)
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = "(no examples)",
                    Reason = LeafReason.NoUsefulSplit
                };
            }

            // If all labels are the same, stop
            if (AllSameLabel(examples, out var singleLabel))
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = singleLabel,
                    Reason = LeafReason.Pure
                };
            }

            // If no attributes left, return majority label
            if (attributes.Count == 0)
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = MajorityLabel(examples),
                    Reason = LeafReason.NoAttributesLeft
                };
            }

            // Choose attribute with best GainRatio
            AttributeDef? bestAttr = null;
            double bestScore = double.NegativeInfinity;

            foreach (var attr in attributes)
            {
                double score = GainRatio(examples, attr);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAttr = attr;
                }
            }

            // If no attribute gives positive gain, return majority leaf
            if (bestAttr == null || bestScore <= 0.0)
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = MajorityLabel(examples),
                    Reason = LeafReason.NoUsefulSplit
                };
            }

            var node = new TreeNode
            {
                IsLeaf = false,
                AttributeName = bestAttr.Name,
                Children = new Dictionary<string, TreeNode>(),
                Reason = LeafReason.None
            };

            // For each value in the domain, create a branch
            foreach (var value in bestAttr.Domain)
            {
                // Subset: examples with a concrete (not "*") value equal to this domain value
                var subset = examples
                    .Where(e =>
                        e.Attributes.TryGetValue(bestAttr.Name, out var v) &&
                        v != "*" &&
                        v == value)
                    .ToList();

                TreeNode child;

                if (subset.Count == 0)
                {
                    // No examples for this branch: use majority of parent
                    child = new TreeNode
                    {
                        IsLeaf = true,
                        Label = MajorityLabel(examples),
                        Reason = LeafReason.MajorityOfNodeForMissingBranch
                    };
                }
                else
                {
                    var remainingAttrs = attributes
                        .Where(a => a.Name != bestAttr.Name)
                        .ToList();

                    child = Train(subset, remainingAttrs);
                }

                node.Children[value] = child;
            }

            return node;
        }

        // Classify a fully specified case (no "*" here)
        public static string Classify(TreeNode root, IDictionary<string, string> input)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (input == null) throw new ArgumentNullException(nameof(input));

            var node = root;

            while (!node.IsLeaf)
            {
                if (node.AttributeName == null)
                    throw new InvalidOperationException("Non-leaf node without AttributeName.");

                if (node.Children == null)
                    throw new InvalidOperationException("Non-leaf node without children.");

                if (!input.TryGetValue(node.AttributeName, out var value))
                    throw new ArgumentException($"Missing attribute '{node.AttributeName}' in input.");

                if (!node.Children.TryGetValue(value, out var child))
                    throw new ArgumentException(
                        $"Unknown value '{value}' for attribute '{node.AttributeName}'.");

                node = child;
            }

            if (node.Label == null)
                throw new InvalidOperationException("Leaf node without Label.");

            return node.Label;
        }

        // HOW: explain path + leaf reason
        public static string How(TreeNode root, IDictionary<string, string> input)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (input == null) throw new ArgumentNullException(nameof(input));

            var sb = new StringBuilder();
            sb.AppendLine("Classification path:");

            var node = root;

            while (!node.IsLeaf)
            {
                if (node.AttributeName == null)
                    throw new InvalidOperationException("Non-leaf node without AttributeName.");

                if (node.Children == null)
                    throw new InvalidOperationException("Non-leaf node without children.");

                if (!input.TryGetValue(node.AttributeName, out var value))
                    throw new ArgumentException($"Missing attribute '{node.AttributeName}' in input.");

                if (!node.Children.TryGetValue(value, out var child))
                    throw new ArgumentException(
                        $"Unknown value '{value}' for attribute '{node.AttributeName}'.");

                sb.AppendLine(
                    $"- Tested {node.AttributeName}, your answer was '{value}', so I followed the branch {node.AttributeName} = {value}.");

                node = child;
            }

            sb.AppendLine();
            sb.AppendLine($"I reached a leaf with Advice = {node.Label}.");

            sb.AppendLine(node.Reason switch
            {
                LeafReason.Pure =>
                    "This corresponds to a pure leaf: all training examples that reached this point had this same advice.",
                LeafReason.NoAttributesLeft =>
                    "This leaf exists because there were no more attributes to test, so I used the majority advice at this node.",
                LeafReason.NoUsefulSplit =>
                    "This leaf exists because no attribute could further reduce the uncertainty, so I used the majority advice at this node.",
                LeafReason.MajorityOfNodeForMissingBranch =>
                    "This leaf exists because there were no training examples for this branch, so I used the majority advice from the parent node.",
                _ =>
                    "This leaf exists based on the training examples that reached this point."
            });

            return sb.ToString();
        }

        // WHY: explain why asking about a specific attribute, given partial answers
        public static string Why(
            TreeNode root,
            AttributeDef attribute,
            IDictionary<string, string> knownAnswers)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            if (knownAnswers == null) throw new ArgumentNullException(nameof(knownAnswers));

            var sb = new StringBuilder();

            string knownText = knownAnswers.Count == 0
                ? "nothing yet"
                : string.Join(", ", knownAnswers.Select(kv => $"{kv.Key}={kv.Value}"));

            var currentLabels = ClassifyWithUnknowns(root, knownAnswers);

            sb.AppendLine($"I'm asking about '{attribute.Name}' because, given what I know so far ({knownText}),");

            if (currentLabels.Count == 1)
            {
                sb.AppendLine($"the advice is already determined: {{ {string.Join(", ", currentLabels)} }}.");
                sb.AppendLine("I'm still asking due to the fixed question order, but this answer will not change the conclusion.");
            }
            else
            {
                sb.AppendLine($"the advice could still be one of: {{ {string.Join(", ", currentLabels)} }}.");
                sb.AppendLine();
                sb.AppendLine("Depending on your answer to this question, the possible advice becomes:");

                foreach (var value in attribute.Domain)
                {
                    var extended = new Dictionary<string, string>(knownAnswers)
                    {
                        [attribute.Name] = value
                    };

                    var labelsForValue = ClassifyWithUnknowns(root, extended);
                    sb.AppendLine($"- If {attribute.Name} = {value}, possible advice: {{ {string.Join(", ", labelsForValue)} }}");
                }
            }

            return sb.ToString();
        }

        // Internal: classify with partial answers; missing attrs = explore all branches
        private static HashSet<string> ClassifyWithUnknowns(
            TreeNode node,
            IDictionary<string, string> partialAnswers)
        {
            var result = new HashSet<string>();

            void Recurse(TreeNode n)
            {
                if (n.IsLeaf)
                {
                    if (n.Label != null)
                        result.Add(n.Label);
                    return;
                }

                if (n.AttributeName == null || n.Children == null)
                    throw new InvalidOperationException("Invalid non-leaf node.");

                if (partialAnswers.TryGetValue(n.AttributeName, out var value))
                {
                    if (n.Children.TryGetValue(value, out var child))
                        Recurse(child);
                    // If value not found, we could explore all children instead,
                    // but for now we'll just ignore that inconsistent answer.
                }
                else
                {
                    // No answer for this attribute: explore all branches
                    foreach (var child in n.Children.Values)
                        Recurse(child);
                }
            }

            Recurse(node);
            return result;
        }

        // === C4.5 maths ===

        // EntropyInfo(S) over labels
        private static double EntropyInfo(IReadOnlyList<TrainingExample> examples)
        {
            int n = examples.Count;
            if (n == 0) return 0.0;

            return examples
                .GroupBy(e => e.Label)
                .Select(g =>
                {
                    double p = (double)g.Count() / n;
                    return -p * Math.Log(p, 2.0);
                })
                .Sum();
        }

        // InfoGain(S, A) with "*" treated as "no info" for A
        private static double InfoGain(
            IReadOnlyList<TrainingExample> examples,
            AttributeDef attribute)
        {
            if (examples == null || examples.Count == 0)
                return 0.0;

            var known = examples
                .Where(e =>
                    e.Attributes.TryGetValue(attribute.Name, out var v) &&
                    v != "*")
                .ToList();

            int knownCount = known.Count;
            if (knownCount == 0)
                return 0.0;

            double hBefore = EntropyInfo(known);

            double hAfter = 0.0;

            foreach (var value in attribute.Domain)
            {
                var subset = known
                    .Where(e => e.Attributes[attribute.Name] == value)
                    .ToList();

                if (subset.Count == 0) continue;

                double weight = (double)subset.Count / knownCount;
                hAfter += weight * EntropyInfo(subset);
            }

            double infoGainKnown = hBefore - hAfter;

            double knownFraction = (double)knownCount / examples.Count;

            return knownFraction * infoGainKnown;
        }

        // SplitInfo(S, A) with "*" treated as "no info" for A
        private static double SplitInfo(
            IReadOnlyList<TrainingExample> examples,
            AttributeDef attribute)
        {
            if (examples == null || examples.Count == 0)
                return 0.0;

            var known = examples
                .Where(e =>
                    e.Attributes.TryGetValue(attribute.Name, out var v) &&
                    v != "*")
                .ToList();

            int knownCount = known.Count;
            if (knownCount == 0) return 0.0;

            double result = 0.0;

            foreach (var value in attribute.Domain)
            {
                int subsetCount = known
                    .Count(e => e.Attributes[attribute.Name] == value);

                if (subsetCount == 0) continue;

                double p = (double)subsetCount / knownCount;
                result += -p * Math.Log(p, 2.0);
            }

            return result;
        }

        // GainRatio(S, A) = InfoGain / SplitInfo
        private static double GainRatio(
            IReadOnlyList<TrainingExample> examples,
            AttributeDef attribute)
        {
            double infoGain = InfoGain(examples, attribute);
            if (infoGain <= 0.0) return 0.0;

            double splitInfo = SplitInfo(examples, attribute);
            if (splitInfo == 0.0) return 0.0;

            return infoGain / splitInfo;
        }

        // Helpers

        private static bool AllSameLabel(IReadOnlyList<TrainingExample> examples, out string label)
        {
            label = examples[0].Label;
            for (int i = 1; i < examples.Count; i++)
            {
                if (examples[i].Label != label)
                    return false;
            }
            return true;
        }

        private static string MajorityLabel(IReadOnlyList<TrainingExample> examples)
        {
            return examples
                .GroupBy(e => e.Label)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .First();
        }
    }

    // Rule extraction + tree formatting
    public static class RuleExtractor
    {
        public static List<string> ExtractRules(TreeNode root)
        {
            var rules = new List<string>();
            var path = new List<(string Attr, string Value)>();
            Traverse(root, path, rules);
            return rules;
        }

        private static void Traverse(
            TreeNode node,
            List<(string Attr, string Value)> path,
            List<string> rules)
        {
            if (node.IsLeaf)
            {
                var sb = new StringBuilder();

                if (path.Count == 0)
                {
                    sb.Append($"IF <always> THEN Advice = {node.Label}");
                }
                else
                {
                    sb.Append("IF ");
                    sb.Append(string.Join(" AND ",
                        path.Select(p => $"{p.Attr} = {p.Value}")));
                    sb.Append($" THEN Advice = {node.Label}");
                }

                rules.Add(sb.ToString());
                return;
            }

            if (node.AttributeName == null || node.Children == null)
                throw new InvalidOperationException("Invalid non-leaf node.");

            foreach (var kv in node.Children)
            {
                path.Add((node.AttributeName, kv.Key));
                Traverse(kv.Value, path, rules);
                path.RemoveAt(path.Count - 1);
            }
        }

        // ASCII tree formatter (console-friendly)
        public static string FormatTree(TreeNode root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var sb = new StringBuilder();

            if (root.IsLeaf)
            {
                sb.AppendLine(root.Label ?? "(leaf)");
                return sb.ToString();
            }

            // Root decision node at column 0
            sb.AppendLine($"{root.AttributeName}?");
            sb.AppendLine("│");

            RenderChildren(root, labelCol: 0, sb);
            return sb.ToString();
        }

        private static void RenderChildren(TreeNode node, int labelCol, StringBuilder sb)
        {
            if (node.Children == null || node.Children.Count == 0)
                return;

            var keys = node.Children.Keys.OrderBy(k => k).ToList();

            for (int i = 0; i < keys.Count; i++)
            {
                var edgeLabel = keys[i];
                var child = node.Children[edgeLabel];
                bool isLast = (i == keys.Count - 1);

                string connector = isLast ? "└─ " : "├─ ";

                var line = new StringBuilder();
                line.Append(' ', labelCol);
                line.Append(connector);
                line.Append(edgeLabel);

                if (child.IsLeaf)
                {
                    line.Append(" -> ");
                    line.Append(child.Label);
                    sb.AppendLine(line.ToString());
                }
                else
                {
                    line.Append(" -> ");
                    line.Append(child.AttributeName);
                    line.Append("?");
                    sb.AppendLine(line.ToString());

                    int childLabelCol =
                        labelCol +
                        connector.Length +
                        edgeLabel.Length +
                        " -> ".Length;

                    // Vertical line under first letter of child's label
                    sb.AppendLine(new string(' ', childLabelCol) + "│");

                    RenderChildren(child, childLabelCol, sb);
                }
            }
        }
    }

}

