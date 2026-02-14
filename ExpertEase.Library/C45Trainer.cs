using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace ExpertEase
{
    public enum AttributeKind
    {
        Categorical,
        Numeric
    }

    // Attribute definition
    public sealed class AttributeDef
    {
        public string Name { get; }
        public AttributeKind Kind { get; }
        public IReadOnlyList<string> Domain { get; }

        // Categorical
        public AttributeDef(string name, IReadOnlyList<string> domain)
        {
            Name = name;
            Kind = AttributeKind.Categorical;
            Domain = domain;
        }

        // Numeric
        public AttributeDef(string name)
        {
            Name = name;
            Kind = AttributeKind.Numeric;
            Domain = Array.Empty<string>();
        }
    }

    // One training row: attributes -> label
    public sealed class TrainingExample
    {
        public Dictionary<string,string> Attributes { get; }
        public string Label { get; }

        public TrainingExample(Dictionary<string,string> attributes, string label)
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

        // Categorical children
        public Dictionary<string,TreeNode>? Children { get; set; }

        // Numeric split info
        public bool IsNumericSplit { get; set; }
        public double? Threshold { get; set; }
        public TreeNode? LessOrEqualChild { get; set; }
        public TreeNode? GreaterChild { get; set; }

        public LeafReason Reason { get; set; }

        // Pruning metadata (set during training, used by Prune)
        internal int ExampleCount { get; set; }
        internal int ErrorCount { get; set; }
        internal string? MajorityLabel { get; set; }
    }

    // C4.5-style trainer (Gain Ratio) with wildcard and numeric support
    public static class C45Trainer
    {
        // Entry point: train tree, then prune
        public static TreeNode Train(
            IReadOnlyList<TrainingExample> examples,
            IReadOnlyList<AttributeDef> attributes)
        {
            if (examples == null) throw new ArgumentNullException(nameof(examples));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            var root = BuildTree(examples, attributes);
            Prune(root);
            return root;
        }

        // Recursive tree builder
        private static TreeNode BuildTree(
            IReadOnlyList<TrainingExample> examples,
            IReadOnlyList<AttributeDef> attributes)
        {

            // Compute majority label and error count for this node (used by pruning)
            string majority = examples.Count > 0 ? MajorityLabel(examples) : "(no examples)";
            int errorCount = examples.Count(e => e.Label != majority);

            // If no examples, return a dummy leaf
            if (examples.Count == 0)
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = "(no examples)",
                    Reason = LeafReason.NoUsefulSplit,
                    ExampleCount = 0, ErrorCount = 0, MajorityLabel = "(no examples)"
                };
            }

            // If all labels are the same, stop
            if (AllSameLabel(examples, out var singleLabel))
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = singleLabel,
                    Reason = LeafReason.Pure,
                    ExampleCount = examples.Count, ErrorCount = 0, MajorityLabel = singleLabel
                };
            }

            // If no attributes left, return majority label
            if (attributes.Count == 0)
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = majority,
                    Reason = LeafReason.NoAttributesLeft,
                    ExampleCount = examples.Count, ErrorCount = errorCount, MajorityLabel = majority
                };
            }

            // C4.5 two-pass attribute selection:
            // Pass 1: compute InfoGain for each candidate
            // Pass 2: among those with InfoGain >= average, pick by GainRatio

            var candidates = new List<(
                AttributeDef attr,
                double infoGain,
                double gainRatio,
                bool isNumeric,
                double? threshold)>();

            foreach (var attr in attributes)
            {
                if (attr.Kind == AttributeKind.Categorical)
                {
                    double ig = InfoGain(examples, attr);
                    double gr = GainRatioCategorical(examples, attr);
                    if (ig > 0.0)
                        candidates.Add((attr, ig, gr, false, null));
                }
                else // Numeric
                {
                    var (gr, ig, threshold) = BestNumericSplit(examples, attr);
                    if (ig > 0.0 && threshold.HasValue)
                        candidates.Add((attr, ig, gr, true, threshold));
                }
            }

            // Filter: only consider attributes with InfoGain >= average
            if (candidates.Count > 1)
            {
                double avgGain = candidates.Average(c => c.infoGain);
                candidates = candidates.Where(c => c.infoGain >= avgGain).ToList();
            }

            // Pick the candidate with the best GainRatio
            AttributeDef? bestAttr = null;
            double bestScore = double.NegativeInfinity;
            bool bestIsNumeric = false;
            double? bestThreshold = null;

            foreach (var (attr, _, gr, isNum, thr) in candidates)
            {
                if (gr > bestScore)
                {
                    bestScore = gr;
                    bestAttr = attr;
                    bestIsNumeric = isNum;
                    bestThreshold = thr;
                }
            }

            // If no attribute gives positive gain, return majority leaf
            if (bestAttr == null || bestScore <= 0.0)
            {
                return new TreeNode
                {
                    IsLeaf = true,
                    Label = majority,
                    Reason = LeafReason.NoUsefulSplit,
                    ExampleCount = examples.Count, ErrorCount = errorCount, MajorityLabel = majority
                };
            }

            // Build node
            if (bestIsNumeric)
            {
                if (!bestThreshold.HasValue)
                {
                    // Fallback safety
                    return new TreeNode
                    {
                        IsLeaf = true,
                        Label = majority,
                        Reason = LeafReason.NoUsefulSplit,
                        ExampleCount = examples.Count, ErrorCount = errorCount, MajorityLabel = majority
                    };
                }

                double threshold = bestThreshold.Value;

                var node = new TreeNode
                {
                    IsLeaf = false,
                    AttributeName = bestAttr.Name,
                    IsNumericSplit = true,
                    Threshold = threshold,
                    Reason = LeafReason.None,
                    ExampleCount = examples.Count, ErrorCount = errorCount, MajorityLabel = majority
                };

                // Partition examples with concrete numeric values for bestAttr
                var left = new List<TrainingExample>();
                var right = new List<TrainingExample>();

                foreach (var e in examples)
                {
                    if (!e.Attributes.TryGetValue(bestAttr.Name, out var raw) || raw == "*")
                    {
                        // Wildcard or missing: send to both branches
                        left.Add(e);
                        right.Add(e);
                        continue;
                    }
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        if (v <= threshold)
                            left.Add(e);
                        else
                            right.Add(e);
                    }
                }

                var remainingAttrs = attributes.ToList();  // numeric can re-split at different thresholds

                node.LessOrEqualChild = BuildTree(left, remainingAttrs);
                node.GreaterChild     = BuildTree(right, remainingAttrs);

                return node;
            }
            else
            {
                // Categorical split
                var node = new TreeNode
                {
                    IsLeaf = false,
                    AttributeName = bestAttr.Name,
                    Children = new Dictionary<string,TreeNode>(),
                    Reason = LeafReason.None,
                    ExampleCount = examples.Count, ErrorCount = errorCount, MajorityLabel = majority
                };

                foreach (var value in bestAttr.Domain)
                {
                    // Subset: examples matching this value, plus wildcards
                    var subset = examples
                        .Where(e =>
                            e.Attributes.TryGetValue(bestAttr.Name, out var v) &&
                            (v == value || v == "*"))
                        .ToList();

                    TreeNode child;

                    if (subset.Count == 0)
                    {
                        // No examples for this branch: use majority of parent
                        child = new TreeNode
                        {
                            IsLeaf = true,
                            Label = majority,
                            Reason = LeafReason.MajorityOfNodeForMissingBranch,
                            ExampleCount = 0, ErrorCount = 0, MajorityLabel = majority
                        };
                    }
                    else
                    {
                        var remainingAttrs = attributes
                            .Where(a => a.Name != bestAttr.Name)
                            .ToList();

                        child = BuildTree(subset, remainingAttrs);
                    }

                    node.Children[value] = child;
                }

                return node;
            }
        }

        // Pessimistic error-based pruning (C4.5 style, CF = 0.25)
        private static void Prune(TreeNode node)
        {
            if (node.IsLeaf)
                return;

            // Recurse into children first (bottom-up)
            if (node.IsNumericSplit)
            {
                if (node.LessOrEqualChild != null) Prune(node.LessOrEqualChild);
                if (node.GreaterChild != null) Prune(node.GreaterChild);
            }
            else if (node.Children != null)
            {
                foreach (var child in node.Children.Values)
                    Prune(child);
            }

            // Compare: estimated error as leaf vs estimated error as subtree
            double leafError = PessimisticError(node.ExampleCount, node.ErrorCount);
            double subtreeError = SubtreeError(node);

            if (leafError <= subtreeError)
            {
                // Prune: replace this subtree with a leaf
                node.IsLeaf = true;
                node.Label = node.MajorityLabel;
                node.Reason = node.ErrorCount == 0 ? LeafReason.Pure : LeafReason.NoUsefulSplit;
                node.AttributeName = null;
                node.Children = null;
                node.IsNumericSplit = false;
                node.Threshold = null;
                node.LessOrEqualChild = null;
                node.GreaterChild = null;
            }
        }

        // Sum of pessimistic error estimates across all leaves in a subtree
        private static double SubtreeError(TreeNode node)
        {
            if (node.IsLeaf)
                return PessimisticError(node.ExampleCount, node.ErrorCount);

            double total = 0.0;

            if (node.IsNumericSplit)
            {
                if (node.LessOrEqualChild != null) total += SubtreeError(node.LessOrEqualChild);
                if (node.GreaterChild != null) total += SubtreeError(node.GreaterChild);
            }
            else if (node.Children != null)
            {
                foreach (var child in node.Children.Values)
                    total += SubtreeError(child);
            }

            return total;
        }

        // Upper confidence bound on error rate (Quinlan's formula, CF = 0.25 -> z = 0.6745)
        // Returns estimated number of errors (N * upper_bound_rate)
        private static double PessimisticError(int n, int e)
        {
            if (n == 0)
                return 0.0;

            const double z = 0.6745; // z-value for 25% confidence
            double f = (double)e / n;
            double z2 = z * z;

            // Upper confidence bound using normal approximation to binomial
            double numerator = f + z2 / (2.0 * n)
                             + z * Math.Sqrt(f / n - f * f / n + z2 / (4.0 * n * n));
            double denominator = 1.0 + z2 / n;

            return n * (numerator / denominator);
        }

        // Classify a fully specified case (no "*" here)
        public static string Classify(TreeNode root, IDictionary<string,string> input)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (input == null) throw new ArgumentNullException(nameof(input));

            var node = root;

            while (!node.IsLeaf)
            {
                if (node.AttributeName == null)
                    throw new InvalidOperationException("Non-leaf node without AttributeName.");

                if (node.IsNumericSplit)
                {
                    if (!input.TryGetValue(node.AttributeName, out var raw))
                        throw new ArgumentException($"Missing attribute '{node.AttributeName}' in input.");

                    if (node.Threshold == null ||
                        node.LessOrEqualChild == null ||
                        node.GreaterChild == null)
                    {
                        throw new InvalidOperationException("Numeric split node not fully initialized.");
                    }

                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new ArgumentException($"Attribute '{node.AttributeName}' must be numeric.");

                    node = v <= node.Threshold.Value
                        ? node.LessOrEqualChild
                        : node.GreaterChild;

                    continue;
                }

                // Categorical
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

			// Track which attributes were actually tested along this path
			var usedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			while (!node.IsLeaf)
			{
				if (node.AttributeName == null)
					throw new InvalidOperationException("Non-leaf node without AttributeName.");

				usedAttributes.Add(node.AttributeName);

				if (node.IsNumericSplit)
				{
					if (node.Threshold == null ||
						node.LessOrEqualChild == null ||
						node.GreaterChild == null)
					{
						throw new InvalidOperationException("Numeric split node not fully initialized.");
					}

					if (!input.TryGetValue(node.AttributeName, out var raw))
						throw new ArgumentException($"Missing attribute '{node.AttributeName}' in input.");

					if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
						throw new ArgumentException($"Attribute '{node.AttributeName}' must be numeric.");

					var branch = v <= node.Threshold.Value ? "<=" : ">";
					sb.AppendLine(
						$"- Tested {node.AttributeName}, your value was {v}, threshold is {node.Threshold.Value}, so I followed the branch {node.AttributeName} {branch} {node.Threshold.Value}.");

					node = v <= node.Threshold.Value ? node.LessOrEqualChild : node.GreaterChild;
					continue;
				}

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

			// Now explain attributes that were asked but not used on this path
			var unused = input.Keys
				.Where(k => !usedAttributes.Contains(k))
				.OrderBy(k => k)
				.ToList();

			if (unused.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("Attributes you provided that did not affect this particular decision:");

				foreach (var attrName in unused)
				{
					bool usedSomewhere = AttributeUsedInTree(root, attrName);

					if (!usedSomewhere)
					{
						sb.AppendLine(
							$"- {attrName}: the learned tree never tests this attribute at all, given the current training examples.");
					}
					else
					{
						sb.AppendLine(
							$"- {attrName}: this attribute can matter in other situations, but for your answers it was not needed because other attributes already determined the advice.");
					}
				}
			}

			return sb.ToString();
		}


		// WHY: explain why asking about a specific attribute, given partial answers
		public static string Why(
            TreeNode root,
            AttributeDef attribute,
            IDictionary<string,string> knownAnswers)
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
                return sb.ToString();
            }

            sb.AppendLine($"the advice could still be one of: {{ {string.Join(", ", currentLabels)} }}.");
            sb.AppendLine();

            if (attribute.Kind == AttributeKind.Categorical)
            {
                sb.AppendLine("Depending on your answer to this question, the possible advice becomes:");

                foreach (var value in attribute.Domain)
                {
                    var extended = new Dictionary<string,string>(knownAnswers)
                    {
                        [attribute.Name] = value
                    };

                    var labelsForValue = ClassifyWithUnknowns(root, extended);
                    sb.AppendLine($"- If {attribute.Name} = {value}, possible advice: {{ {string.Join(", ", labelsForValue)} }}");
                }
            }
            else
            {
                sb.AppendLine("This attribute is numeric. Different ranges of values may lead to different advice,");
                sb.AppendLine("but I can't enumerate all possibilities here. Try different numeric values to see how the advice changes.");
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

				if (n.AttributeName == null)
					throw new InvalidOperationException("Invalid non-leaf node.");

				if (n.IsNumericSplit)
				{
					if (n.Threshold == null ||
						n.LessOrEqualChild == null ||
						n.GreaterChild == null)
					{
						throw new InvalidOperationException("Numeric split node not fully initialized.");
					}

					if (partialAnswers.TryGetValue(n.AttributeName, out var raw))
					{
						if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
						{
							var child = v <= n.Threshold.Value
								? n.LessOrEqualChild
								: n.GreaterChild;
							Recurse(child);
						}
						// If cannot parse, ignore this inconsistent answer and explore both
						else
						{
							Recurse(n.LessOrEqualChild);
							Recurse(n.GreaterChild);
						}
					}
					else
					{
						// No answer: explore both branches
						Recurse(n.LessOrEqualChild);
						Recurse(n.GreaterChild);
					}

					return;
				}

				if (n.Children == null)
					throw new InvalidOperationException("Invalid non-leaf node.");

				if (partialAnswers.TryGetValue(n.AttributeName, out var value))
				{
					if (n.Children.TryGetValue(value, out var child))
						Recurse(child);
					// If value not found in children, ignore and explore all branches
					else
					{
						foreach (var c in n.Children.Values)
							Recurse(c);
					}
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

		// EntropyInfo(S) over labels (examples)
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

		// EntropyInfo over a list of labels (for numeric splits)
		private static double EntropyInfo(IReadOnlyList<string> labels)
		{
			int n = labels.Count;
			if (n == 0) return 0.0;

			return labels
				.GroupBy(l => l)
				.Select(g =>
				{
					double p = (double)g.Count() / n;
					return -p * Math.Log(p, 2.0);
				})
				.Sum();
		}

		// InfoGain(S, A) for categorical A, with "*" treated as "no info" for A
		private static double InfoGain(
			IReadOnlyList<TrainingExample> examples,
			AttributeDef attribute)
		{
			if (examples == null || examples.Count == 0)
				return 0.0;

			if (attribute.Kind != AttributeKind.Categorical)
				throw new ArgumentException("InfoGain is only for categorical attributes here.");

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

		// SplitInfo(S, A) for categorical A, with "*" treated as "no info" for A
		private static double SplitInfo(
			IReadOnlyList<TrainingExample> examples,
			AttributeDef attribute)
		{
			if (examples == null || examples.Count == 0)
				return 0.0;

			if (attribute.Kind != AttributeKind.Categorical)
				throw new ArgumentException("SplitInfo is only for categorical attributes here.");

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

		// GainRatio(S, A) for categorical A
		private static double GainRatioCategorical(
			IReadOnlyList<TrainingExample> examples,
			AttributeDef attribute)
		{
			if (attribute.Kind != AttributeKind.Categorical)
				return 0.0;

			double infoGain = InfoGain(examples, attribute);
			if (infoGain <= 0.0) return 0.0;

			double splitInfo = SplitInfo(examples, attribute);
			if (splitInfo == 0.0) return 0.0;

			return infoGain / splitInfo;
		}

		// Numeric: find best threshold & corresponding GainRatio and InfoGain
		// Returns (gainRatio, infoGain, threshold) for the best binary split
		private static (double bestGainRatio, double bestInfoGain, double? bestThreshold) BestNumericSplit(
			IReadOnlyList<TrainingExample> examples,
			AttributeDef attribute)
		{
			if (attribute.Kind != AttributeKind.Numeric)
				return (0.0, 0.0, null);

			// Collect numeric samples for this attribute
			var samples = new List<(double value, string label)>();

			foreach (var e in examples)
			{
				if (e.Attributes.TryGetValue(attribute.Name, out var raw) &&
					raw != "*" &&
					double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
				{
					samples.Add((v, e.Label));
				}
			}

			int knownCount = samples.Count;
			if (knownCount < 2)
				return (0.0, 0.0, null);

			// Sort by value
			samples.Sort((a, b) => a.value.CompareTo(b.value));

			// Count distinct candidate thresholds for the penalty
			int numCandidates = 0;
			for (int i = 0; i < samples.Count - 1; i++)
			{
				if (Math.Abs(samples[i].value - samples[i + 1].value) >= 1e-9)
					numCandidates++;
			}

			// Penalty for testing multiple thresholds: log2(N-1) / |S|
			double penalty = numCandidates > 1
				? Math.Log(numCandidates, 2.0) / examples.Count
				: 0.0;

			// Precompute parent entropy on known labels
			var allLabels = samples.Select(s => s.label).ToList();
			double hBefore = EntropyInfo(allLabels);

			double bestGainRatio = double.NegativeInfinity;
			double bestInfoGain = 0.0;
			double? bestThreshold = null;

			// Only consider thresholds between distinct values
			for (int i = 0; i < samples.Count - 1; i++)
			{
				double v1 = samples[i].value;
				double v2 = samples[i + 1].value;

				if (Math.Abs(v1 - v2) < 1e-9)
					continue;

				double threshold = (v1 + v2) / 2.0;

				var leftLabels = new List<string>();
				var rightLabels = new List<string>();

				foreach (var (value, label) in samples)
				{
					if (value <= threshold)
						leftLabels.Add(label);
					else
						rightLabels.Add(label);
				}

				int leftCount = leftLabels.Count;
				int rightCount = rightLabels.Count;
				if (leftCount == 0 || rightCount == 0)
					continue;

				double hLeft = EntropyInfo(leftLabels);
				double hRight = EntropyInfo(rightLabels);

				double knownTotal = samples.Count;
				double hAfter = (leftCount / knownTotal) * hLeft +
								(rightCount / knownTotal) * hRight;

				double infoGainKnown = hBefore - hAfter;

				// Down-weight by fraction of all examples that have a value
				double knownFraction = knownTotal / examples.Count;
				double infoGain = knownFraction * infoGainKnown;

				// Apply multi-threshold penalty
				infoGain -= penalty;

				if (infoGain <= 0.0)
					continue;

				// SplitInfo for binary partition
				double pLeft = leftCount / knownTotal;
				double pRight = rightCount / knownTotal;

				double splitInfo = -pLeft * Math.Log(pLeft, 2.0)
								 - pRight * Math.Log(pRight, 2.0);

				if (splitInfo == 0.0)
					continue;

				double gainRatio = infoGain / splitInfo;

				if (gainRatio > bestGainRatio)
				{
					bestGainRatio = gainRatio;
					bestInfoGain = infoGain;
					bestThreshold = threshold;
				}
			}

			if (double.IsNegativeInfinity(bestGainRatio))
				return (0.0, 0.0, null);

			return (bestGainRatio, bestInfoGain, bestThreshold);
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

		private static bool AttributeUsedInTree(TreeNode node, string attrName)
		{
			if (node == null) return false;

			if (node.AttributeName != null &&
				string.Equals(node.AttributeName, attrName, StringComparison.OrdinalIgnoreCase))
				return true;

			if (node.IsNumericSplit)
			{
				return (node.LessOrEqualChild != null && AttributeUsedInTree(node.LessOrEqualChild, attrName))
					|| (node.GreaterChild != null && AttributeUsedInTree(node.GreaterChild, attrName));
			}

			if (node.Children != null)
			{
				foreach (var child in node.Children.Values)
				{
					if (AttributeUsedInTree(child, attrName))
						return true;
				}
			}

			return false;
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

			if (node.AttributeName == null)
				throw new InvalidOperationException("Invalid non-leaf node.");

			if (node.IsNumericSplit)
			{
				if (node.Threshold == null ||
					node.LessOrEqualChild == null ||
					node.GreaterChild == null)
				{
					throw new InvalidOperationException("Numeric split node not fully initialized.");
				}

				path.Add((node.AttributeName, $"<= {node.Threshold.Value}"));
				Traverse(node.LessOrEqualChild, path, rules);
				path.RemoveAt(path.Count - 1);

				path.Add((node.AttributeName, $"> {node.Threshold.Value}"));
				Traverse(node.GreaterChild, path, rules);
				path.RemoveAt(path.Count - 1);
			}
			else
			{
				if (node.Children == null)
					throw new InvalidOperationException("Invalid non-leaf node.");

				foreach (var kv in node.Children)
				{
					path.Add((node.AttributeName, kv.Key));
					Traverse(kv.Value, path, rules);
					path.RemoveAt(path.Count - 1);
				}
			}
		}

		// ASCII tree formatter (console-friendly), supports both categorical and numeric nodes
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
			if (node.IsNumericSplit)
			{
				if (node.Threshold == null ||
					node.LessOrEqualChild == null ||
					node.GreaterChild == null)
				{
					throw new InvalidOperationException("Numeric split node not fully initialized.");
				}

				string th = node.Threshold.Value.ToString(CultureInfo.InvariantCulture);

				// <= branch
				var line1 = new StringBuilder();
				line1.Append(' ', labelCol);
				line1.Append("├─ ");
				line1.Append($"<= {th}");

				if (node.LessOrEqualChild.IsLeaf)
				{
					line1.Append(" -> ");
					line1.Append(node.LessOrEqualChild.Label);
					sb.AppendLine(line1.ToString());
				}
				else
				{
					line1.Append(" -> ");
					line1.Append(node.LessOrEqualChild.AttributeName);
					line1.Append("?");
					sb.AppendLine(line1.ToString());

					int childLabelCol =
						labelCol +
						"├─ ".Length +
						$"<= {th}".Length +
						" -> ".Length;

					sb.AppendLine(new string(' ', childLabelCol) + "│");

					RenderChildren(node.LessOrEqualChild, childLabelCol, sb);
				}

				// > branch
				var line2 = new StringBuilder();
				line2.Append(' ', labelCol);
				line2.Append("└─ ");
				line2.Append($"> {th}");

				if (node.GreaterChild.IsLeaf)
				{
					line2.Append(" -> ");
					line2.Append(node.GreaterChild.Label);
					sb.AppendLine(line2.ToString());
				}
				else
				{
					line2.Append(" -> ");
					line2.Append(node.GreaterChild.AttributeName);
					line2.Append("?");
					sb.AppendLine(line2.ToString());

					int childLabelCol =
						labelCol +
						"└─ ".Length +
						$"> {th}".Length +
						" -> ".Length;

					sb.AppendLine(new string(' ', childLabelCol) + "│");

					RenderChildren(node.GreaterChild, childLabelCol, sb);
				}

				return;
			}

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

	public sealed class ExpertFile
	{
		public string? Name { get; set; }
		public string? Description { get; set; }

		public List<ExpertAttribute>? Attributes { get; set; }
		public List<ExpertExample>? Examples { get; set; }

	}

	public sealed class ExpertAttribute
	{
		public string Name { get; set; } = "";
		public string Kind { get; set; } = "categorical"; // "categorical" or "numeric"
		public List<string>? Domain { get; set; }         // for categorical
	}

	public sealed class ExpertExample
	{
		public string Label { get; set; } = "";
		public Dictionary<string, string> Attributes { get; set; } = new();
	}

}
