<Query Kind="Program" />

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using LINQPad;

// -------------------- MAIN --------------------

void Main()
{
    // 1. Attribute definitions (like your "definition" sheet)
    //    Domains: allowed values for each attribute.
    var attributes = new List<AttributeDef>
    {
        new AttributeDef("Weather", new[] { "raining", "cloudy", "sunny" }),
        new AttributeDef("Family",  new[] { "yes", "no"       }),
        new AttributeDef("Car",     new[] { "yes", "no"       })
    };

    // 2. Training examples (like your "examples" sheet)
    //    Each row says: given Weather + Family + Car, what was the Advice?
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
			["Weather"] = "cloudy",
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

		//new TrainingExample(new Dictionary<string,string>
		//{
		//	["Weather"] = "*",
		//	["Family"]  = "*",
		//	["Car"]     = "no"
		//}, "home"),	
	};

	// 3. Train the ID3 decision tree
    var root = C45Trainer.Train(examples, attributes);

    // 4. Extract and show rules (this is your "expert system")
    var rules = RuleExtractor.ExtractRules(root);
    //rules.Dump("Induced rules");
	RuleExtractor.DumpTree(root, "Decision tree");

    // 5. Try some classifications
    var query1 = new Dictionary<string,string>
    {
        ["Weather"] = "raining",
        ["Family"]  = "yes",
        ["Car"]     = "yes"
    };

    var query2 = new Dictionary<string,string>
    {
        ["Weather"] = "sunny",
        ["Family"]  = "no",
        ["Car"]     = "yes"
    };

	// An unseen combination: (sunny, no, no)
    var query3 = new Dictionary<string,string>
    {
        ["Weather"] = "cloudy",
        ["Family"]  = "yes",
        ["Car"]     = "yes"
    };

	var query = query3; // choose a query
	
    ClassifyAndDump(root, query);

    // 6. HOW / explanation for the unseen case
    var how = C45Trainer.How(root, query);
    how.Dump(FormatHowTitle(query));

    // 7. Interactive consultation
    //InteractiveConsult(root, attributes);
}

string FormatHowTitle(IDictionary<string, string> input) =>
	"HOW for (" + string.Join(", ", input.Select(kv => $"{kv.Key}={kv.Value}")) + ")";

// Helper to classify and dump a single query
void ClassifyAndDump(TreeNode root, Dictionary<string,string> input)
{
    var label = C45Trainer.Classify(root, input);
    new
    {
        Input = string.Join(", ", input.Select(kv => $"{kv.Key}={kv.Value}")),
        Advice = label
    }.Dump("Query");
}

// Interactive consultation: ask questions in attribute order,
// use Dump with caption for the question, and show the answer in the body.
// Still supports "why".
void InteractiveConsult(TreeNode root, List<AttributeDef> attributes)
{
	Console.WriteLine();

	// Top-level container with just text (no table)
	var headerDc = new DumpContainer();
	headerDc.Dump("Interactive Consultation");
	headerDc.Content =		
		"Type 'why' to ask why I'm asking a question.";

	var answers = new Dictionary<string, string>();

	// Helper to pretty-print answers so far
	string FormatAnswers()
		=> answers.Count == 0
		   ? "(none yet)"
		   : string.Join(", ", answers.Select(kv => $"{kv.Key}={kv.Value}"));

	// Ask in the order given by the attributes list
	foreach (var attr in attributes)
	{
		// One DumpContainer per question
		var dc = new DumpContainer();

		// empty content, caption is "Attr? (opt1/opt2/...)"
		dc.Dump($"{attr.Name}? ({string.Join("/", attr.Domain)})");

		while (true)
		{
			// You had this commented out, keeping that as-is:
			// Console.Write($"{attr.Name}? ({string.Join("/", attr.Domain)}) ");
			var raw = Console.ReadLine();
			var input = raw?.Trim();

			if (string.IsNullOrEmpty(input))
			{
				Console.WriteLine("Please enter a value or 'why'.");
				continue;
			}

			// Handle "why"
			if (string.Equals(input, "why", StringComparison.OrdinalIgnoreCase))
			{
				dc.Content = input;

				var whyText = C45Trainer.Why(root, attr, answers);

				var title =
					answers.Count == 0
					? $"WHY for {attr.Name} (no previous answers)"
					: $"WHY for {attr.Name} (given {FormatAnswers()})";

				whyText.Dump(title);
				continue; // re-ask the same attribute
			}

			// Normal value: validate against domain (case-insensitive)
			var match = attr.Domain
				.FirstOrDefault(d => string.Equals(d, input, StringComparison.OrdinalIgnoreCase));

			if (match == null)
			{
				Console.WriteLine("Invalid value, please choose one of: " +
								  string.Join(", ", attr.Domain) +
								  " or type 'why'.");
				continue;
			}

			// Store canonical value
			answers[attr.Name] = match;

			// Show the chosen answer in the *body* of the dump container
			dc.Content = match;

			break; // move to next attribute
		}
	}

	// Use the full answer set to classify
	var advice = C45Trainer.Classify(root, answers);
	advice.Dump("Advice");

	// HOW explanation with dynamic title
	var explanation = C45Trainer.How(root, answers);
	var howTitle = "HOW for (" + string.Join(", ", answers.Select(kv => $"{kv.Key}={kv.Value}")) + ")";
	explanation.Dump(howTitle);
}




// -------------------- MODEL TYPES --------------------

public class AttributeDef
{
	public string Name { get; }
	public IReadOnlyList<string> Domain { get; }

	public AttributeDef(string name, IReadOnlyList<string> domain)
	{
		Name = name;
		Domain = domain;
	}
}

public class TrainingExample
{
	public IReadOnlyDictionary<string, string> Attributes { get; }
	public string Label { get; }

	public TrainingExample(IReadOnlyDictionary<string, string> attributes, string label)
	{
		Attributes = attributes;
		Label = label;
	}
}

public enum LeafReason
{
	PureSubset,                      // all training examples at node had same label
	NoAttributesLeft,                // no attrs left, used majority label
	MajorityOfNodeForMissingBranch   // no examples for this branch, used majority label of node
}

public class TreeNode
{
	// If true, this node is a leaf and Label is set
	public bool IsLeaf { get; set; }
	public string? Label { get; set; } // for leaf nodes

	// If IsLeaf == false, this is a decision node:
	public string? AttributeName { get; set; }              // attribute to test
	public Dictionary<string, TreeNode>? Children { get; set; } // value -> child node

	// Explanation metadata for leaves
	public LeafReason? Reason { get; set; }
}

// A human-readable rule extracted from the tree
public class Rule
{
	public IReadOnlyDictionary<string, string> Conditions { get; }
	public string Label { get; }

	public Rule(IDictionary<string, string> conditions, string label)
	{
		// Clone the conditions so later modifications don't affect this rule
		Conditions = new Dictionary<string, string>(conditions);
		Label = label;
	}

	public override string ToString()
	{
		var cond = Conditions.Count == 0
			? "(always)"
			: string.Join(" AND ", Conditions.Select(kv => $"{kv.Key} = {kv.Value}"));
		return $"IF {cond} THEN Advice = {Label}";
	}
}

// -------------------- ID3 TRAINER + HOW --------------------

public static class C45Trainer
{	
	public static TreeNode Train(
		IReadOnlyList<TrainingExample> examples,
		IReadOnlyList<AttributeDef> attributes)
	{
		if (examples == null) throw new ArgumentNullException(nameof(examples));
		if (examples.Count == 0)
			throw new ArgumentException("At least one example is required", nameof(examples));

		// 1. If all examples have the same label, return a leaf.
        var distinctLabels = examples.Select(e => e.Label).Distinct().ToList();
        if (distinctLabels.Count == 1)
        {
            return new TreeNode
            {
                IsLeaf = true,
                Label = distinctLabels[0],
                Reason = LeafReason.PureSubset
            };
        }

        // 2. If there are no attributes left, return a majority-class leaf.
        if (attributes.Count == 0)
        {
            var majority = MajorityLabel(examples);
            return new TreeNode
            {
                IsLeaf = true,
                Label = majority,
                Reason = LeafReason.NoAttributesLeft
            };
        }

		// 3. Choose the attribute with highest score (ID3 = InfoGain, C4.5 = GainRatio).
		AttributeDef bestAttr = attributes
			.OrderByDescending(a => GainRatio(examples, a))
			.First();

		var node = new TreeNode
        {
            IsLeaf = false,
            AttributeName = bestAttr.Name,
            Children = new Dictionary<string, TreeNode>()
        };

		// 4. For each value of that attribute, create a branch.
		foreach (var value in bestAttr.Domain)
		{
			var subset = examples
				.Where(e =>
					e.Attributes.TryGetValue(bestAttr.Name, out var v) &&
					v != "*" &&
					v == value)
				.ToList();

			TreeNode child;
			if (subset.Count == 0)
			{
				var majority = MajorityLabel(examples);
				child = new TreeNode
				{
					IsLeaf = true,
					Label = majority,
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

	public static string Classify(TreeNode root, IDictionary<string,string> input)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var node = root;
        while (!node.IsLeaf)
        {
            if (node.AttributeName == null || node.Children == null)
                throw new InvalidOperationException("Invalid non-leaf node");

            if (!input.TryGetValue(node.AttributeName, out var value))
                throw new ArgumentException($"Missing attribute '{node.AttributeName}' in input");

            if (!node.Children.TryGetValue(value, out var child))
                throw new ArgumentException(
                    $"Unknown value '{value}' for attribute '{node.AttributeName}'");

            node = child;
        }

        if (node.Label == null)
            throw new InvalidOperationException("Leaf node without label");

        return node.Label;
    }

	// Explore the tree with partial answers, treating missing attributes as "unknown"
	// and following all possible branches for unknowns. Returns the set of possible labels.
	public static HashSet<string> ClassifyWithUnknowns(TreeNode node, IDictionary<string, string> partialAnswers)
	{
		if (node.IsLeaf)
		{
			return new HashSet<string> { node.Label! };
		}

		if (node.AttributeName == null || node.Children == null)
			throw new InvalidOperationException("Invalid non-leaf node.");

		var attr = node.AttributeName;

		if (partialAnswers.TryGetValue(attr, out var val))
		{
			if (!node.Children.TryGetValue(val, out var child))
				throw new ArgumentException($"Unknown value '{val}' for attribute '{attr}'.");

			return ClassifyWithUnknowns(child, partialAnswers);
		}
		else
		{
			// Attribute not yet known: explore all branches
			var result = new HashSet<string>();
			foreach (var child in node.Children.Values)
			{
				foreach (var label in ClassifyWithUnknowns(child, partialAnswers))
					result.Add(label);
			}
			return result;
		}
	}

	// WHY: explain why we are asking for a particular attribute, given what we know so far
	public static string Why(
		TreeNode root,
		AttributeDef attribute,
		IDictionary<string, string> knownAnswers)
	{
		var sb = new StringBuilder();

		string knownText = knownAnswers.Count == 0
			? "nothing yet"
			: string.Join(", ", knownAnswers.Select(kv => $"{kv.Key}={kv.Value}"));

		// What advice is currently possible, before we know this attribute?
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

	// HOW / explanation facility (renamed from WHY)
	public static string How(TreeNode root, IDictionary<string,string> input)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var conditions = new Dictionary<string,string>();
        var steps = new List<string>();

        var node = root;
        while (!node.IsLeaf)
        {
            if (node.AttributeName == null || node.Children == null)
                throw new InvalidOperationException("Invalid non-leaf node.");

            var attr = node.AttributeName;
            if (!input.TryGetValue(attr, out var val))
                throw new ArgumentException($"Missing attribute '{attr}' in input for explanation.");

            if (!node.Children.TryGetValue(val, out var child))
                throw new ArgumentException($"Unknown value '{val}' for attribute '{attr}'.");

            conditions[attr] = val;
            steps.Add($"Tested {attr}, your answer was '{val}', so I followed the branch {attr} = {val}.");

            node = child;
        }

        if (node.Label == null)
            throw new InvalidOperationException("Leaf node without label.");

        string ruleCond = conditions.Count == 0
            ? "(always)"
            : string.Join(" AND ", conditions.Select(kv => $"{kv.Key} = {kv.Value}"));

        var sb = new StringBuilder();
        sb.AppendLine("Classification path:");
        foreach (var s in steps)
            sb.AppendLine("- " + s);

        sb.AppendLine();
        sb.AppendLine($"I reached a leaf with Advice = {node.Label}.");
        sb.AppendLine($"This corresponds to the rule: IF {ruleCond} THEN Advice = {node.Label}.");
        sb.AppendLine();
        sb.AppendLine("Why this leaf exists:");

        switch (node.Reason)
        {
            case LeafReason.PureSubset:
                sb.AppendLine("During training, all examples that reached this point had this same advice, so no further tests were needed.");
                break;
            case LeafReason.NoAttributesLeft:
                sb.AppendLine("At this point there were no more attributes left to test, so I chose the majority advice among the training examples at this node.");
				break;
			case LeafReason.MajorityOfNodeForMissingBranch:
				sb.AppendLine("There were no training examples with this exact combination at this point in the tree, so I used the majority advice from the training examples that reached this node.");
				break;
			default:
				sb.AppendLine("This leaf was created for a default/unspecified reason in training.");
				break;
		}

		return sb.ToString();
	}

	// --- ID3 helpers: entropy, information gain, majority class ---

	static double Entropy(IReadOnlyList<TrainingExample> examples)
	{
		int n = examples.Count;
		if (n == 0) return 0.0;

		return examples
			.GroupBy(e => e.Label)
			.Select(g =>
			{
				double p = (double)g.Count() / n;
				return -p * Math.Log(p, 2.0); // log base 2
			})
			.Sum();
	}

	static double InformationGain(
	    IReadOnlyList<TrainingExample> examples,
	    AttributeDef attribute)
	{
	    if (examples == null || examples.Count == 0)
	        return 0.0;

	    // Only consider examples that actually have a concrete value (not "*")
	    var known = examples
	        .Where(e =>
	            e.Attributes.TryGetValue(attribute.Name, out var v) &&
	            v != "*")
	        .ToList();

	    int knownCount = known.Count;
		if (knownCount == 0)
			return 0.0; // no usable information for this attribute

		// Entropy before, among known examples
		double hBefore = Entropy(known);

		// Expected entropy after splitting known examples by attribute value
		double hAfter = 0.0;

		foreach (var value in attribute.Domain)
		{
			var subset = known
				.Where(e => e.Attributes[attribute.Name] == value)
				.ToList();

			if (subset.Count == 0) continue;

			double weight = (double)subset.Count / knownCount;
			hAfter += weight * Entropy(subset);
		}

		double infoGainKnown = hBefore - hAfter;

		// Optionally down-weight by how many examples actually have a value:
		double knownFraction = (double)knownCount / examples.Count;

		return knownFraction * infoGainKnown;
	}

	static double SplitInfo(
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
			result += -p * Math.Log(p, 2.0); // log base 2
		}

		return result;
	}

	// C4.5-style Gain Ratio, using the new InformationGain / SplitInfo
	static double GainRatio(
		IReadOnlyList<TrainingExample> examples,
		AttributeDef attribute)
	{
		double infoGain = InformationGain(examples, attribute);
		if (infoGain <= 0.0) return 0.0;

		double splitInfo = SplitInfo(examples, attribute);
		if (splitInfo == 0.0) return 0.0;

		return infoGain / splitInfo;
	}


	static string MajorityLabel(IReadOnlyList<TrainingExample> examples)
	{
		return examples
			.GroupBy(e => e.Label)
			.OrderByDescending(g => g.Count())
			.ThenBy(g => g.Key)
			.First()
			.Key;
	}
}

// -------------------- RULE EXTRACTION --------------------

public static class RuleExtractor
{
	public static List<Rule> ExtractRules(TreeNode root)
	{
		var rules = new List<Rule>();
		var currentConditions = new Dictionary<string, string>();
		Traverse(root, currentConditions, rules);
		return rules;
	}

	// Internal recursive renderer
	static void RenderNode(
		TreeNode node,
		StringBuilder sb,
		string indent,
		string? edgeLabel,
		bool isLast)
	{
		if (edgeLabel == null)
		{
			// Root node
			if (node.IsLeaf)
			{
				sb.AppendLine($"(root) {node.Label}");
			}
			else
			{
				sb.AppendLine($"{node.AttributeName}?");
			}
		}
		else
		{
			sb.Append(indent);
			sb.Append(isLast ? "└─ " : "├─ ");

			if (node.IsLeaf)
			{
				sb.AppendLine($"{edgeLabel} -> {node.Label}");
			}
			else
			{
				sb.AppendLine($"{edgeLabel} -> {node.AttributeName}?");
			}
		}

		if (node.IsLeaf || node.Children == null || node.Children.Count == 0)
			return;

		// For deterministic output, sort children by edge label
		var keys = node.Children.Keys.OrderBy(k => k).ToList();

		for (int i = 0; i < keys.Count; i++)
		{
			var childKey = keys[i];
			var child = node.Children[childKey];
			bool childIsLast = (i == keys.Count - 1);

			// For children of the root, start from column 0;
			// otherwise extend the indent according to whether this branch is last.
			string childIndent =
				edgeLabel == null
					? ""
					: indent + (isLast ? "   " : "│  ");

			RenderNode(child, sb, childIndent, childKey, childIsLast);
		}
	}

	static void Traverse(
		TreeNode node,
		Dictionary<string, string> conditions,
		List<Rule> output)
	{
		if (node.IsLeaf)
		{
			if (node.Label == null)
				throw new InvalidOperationException("Leaf without label");

			output.Add(new Rule(conditions, node.Label));
			return;
		}

		if (node.AttributeName == null || node.Children == null)
			throw new InvalidOperationException("Invalid non-leaf node");

		foreach (var kvp in node.Children)
		{
			string value = kvp.Key;
			var child = kvp.Value;

			// Add this condition, go down, then remove it (backtracking).
			conditions[node.AttributeName] = value;
			Traverse(child, conditions, output);
			conditions.Remove(node.AttributeName);
		}
	}

	// Pretty-print the decision tree as an ASCII tree
	public static string FormatTree(TreeNode root)
	{
		if (root == null) throw new ArgumentNullException(nameof(root));

		var sb = new StringBuilder();

		if (root.IsLeaf)
		{
			// Degenerate tree: just a single leaf
			sb.AppendLine(root.Label ?? "(leaf)");
			return sb.ToString();
		}

		// Root decision node: label at column 0
		sb.AppendLine($"{root.AttributeName}?");

		// Vertical line under the first letter of the root label
		sb.AppendLine("│");

		// Render children, with label column at 0
		RenderChildren(root, labelCol: 0, sb);

		return sb.ToString();
	}

	// Dump the tree in LINQPad with monospaced styling
	public static void DumpTree(TreeNode root, string? title = null)
	{
		var text = FormatTree(root);

		var html =
			"<pre style=\"font-family:Consolas, 'Courier New', monospace; font-size:0.9em;\">" +
			System.Net.WebUtility.HtmlEncode(text) +
			"</pre>";

		Util.RawHtml(html).Dump(title ?? "Decision Tree");
	}

	// Internal: render all children of a decision node
	// labelCol = column of the first letter of the parent's label
	static void RenderChildren(TreeNode node, int labelCol, StringBuilder sb)
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

			// Line with edge label and, if non-leaf, the child's decision/leaf label
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

				// Compute the column where the child's decision label starts
				int childLabelCol =
					labelCol +
					connector.Length +
					edgeLabel.Length +
					" -> ".Length;

				// Vertical line under the first letter of the child's label
				sb.AppendLine(new string(' ', childLabelCol) + "│");

				// Recurse for this child's children, using its label column
				RenderChildren(child, childLabelCol, sb);
			}
		}
	}
}