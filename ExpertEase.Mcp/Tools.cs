using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ExpertEase;
using ModelContextProtocol.Server;

namespace ExpertEase.Mcp;

public sealed class ConsultationSession
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public TreeNode TreeRoot { get; set; } = null!;
    public TreeNode CurrentNode { get; set; } = null!;
    public List<(string AttributeName, string Value, string BranchLabel)> Path { get; } = new();
    public string KnowledgeBaseName { get; set; } = "";
    public IReadOnlyList<AttributeDef> Attributes { get; set; } = Array.Empty<AttributeDef>();
    public Dictionary<string, string> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsComplete { get; set; }
    public string? FinalAdvice { get; set; }
}

[McpServerToolType]
public static class Tools
{
    private static readonly ConcurrentDictionary<string, ConsultationSession> Sessions = new();
    [McpServerTool, Description("Classify a case using a trained decision tree from a knowledge base. Returns the predicted advice and an explanation of how the decision was reached.")]
    public static string Classify(
        [Description("KB filename (e.g. 'car_pricing.csv' or 'sunday.json'). Use list_knowledge_bases to discover available files.")] string knowledgeBase,
        [Description("JSON object of attribute key-value pairs, e.g. {\"Brand\":\"luxury\",\"Mileage\":\"30000\"}")] string attributes)
    {
        var kbPath = KnowledgeBaseLoader.ResolveKbPath(knowledgeBase);
        var (attrs, examples) = KnowledgeBaseLoader.LoadFromFile(kbPath);
        var root = C45Trainer.Train(examples, attrs);

        var input = JsonSerializer.Deserialize<Dictionary<string, string>>(attributes)
                    ?? throw new ArgumentException("Could not parse attributes JSON.");

        // Use case-insensitive dictionary
        var caseInsensitive = new Dictionary<string, string>(input, StringComparer.OrdinalIgnoreCase);

        var advice = C45Trainer.Classify(root, caseInsensitive);
        var how = C45Trainer.How(root, caseInsensitive);

        var sb = new StringBuilder();
        sb.AppendLine($"Advice: {advice}");
        sb.AppendLine();
        sb.AppendLine(how);
        return sb.ToString();
    }

    [McpServerTool, Description("Get the trained decision tree and extracted IF-THEN rules for a knowledge base. Useful for understanding the classification logic.")]
    public static string GetDecisionTree(
        [Description("KB filename (e.g. 'car_pricing.csv' or 'sunday.json'). Use list_knowledge_bases to discover available files.")] string knowledgeBase)
    {
        var kbPath = KnowledgeBaseLoader.ResolveKbPath(knowledgeBase);
        var (attrs, examples) = KnowledgeBaseLoader.LoadFromFile(kbPath);
        var root = C45Trainer.Train(examples, attrs);

        var sb = new StringBuilder();

        sb.AppendLine("=== IF-THEN Rules ===");
        foreach (var rule in RuleExtractor.ExtractRules(root))
            sb.AppendLine(rule);

        sb.AppendLine();
        sb.AppendLine("=== Decision Tree ===");
        sb.Append(RuleExtractor.FormatTree(root));

        sb.AppendLine();
        sb.AppendLine("=== Attributes ===");
        foreach (var attr in attrs)
        {
            if (attr.Kind == AttributeKind.Numeric)
                sb.AppendLine($"- {attr.Name} (numeric)");
            else
                sb.AppendLine($"- {attr.Name} (categorical): {string.Join(", ", attr.Domain)}");
        }

        return sb.ToString();
    }

    [McpServerTool, Description("List all available knowledge base files (.json and .csv) that can be used with classify and get_decision_tree.")]
    public static string ListKnowledgeBases()
    {
        var files = KnowledgeBaseLoader.ListKnowledgeBases();

        if (files.Count == 0)
            return "No knowledge base files found.";

        var sb = new StringBuilder();
        sb.AppendLine("Available knowledge bases:");
        foreach (var file in files)
            sb.AppendLine($"- {file}");
        return sb.ToString();
    }

    [McpServerTool, Description("Start an interactive consultation session with a knowledge base. Returns the first question to ask the user. Use answer_question to continue the consultation step by step.")]
    public static string StartConsultation(
        [Description("KB filename (e.g. 'car_pricing.csv' or 'sunday.json'). Use list_knowledge_bases to discover available files.")] string knowledgeBase)
    {
        var kbPath = KnowledgeBaseLoader.ResolveKbPath(knowledgeBase);
        var (attrs, examples) = KnowledgeBaseLoader.LoadFromFile(kbPath);
        var root = C45Trainer.Train(examples, attrs);

        var session = new ConsultationSession
        {
            TreeRoot = root,
            CurrentNode = root,
            KnowledgeBaseName = knowledgeBase,
            Attributes = attrs
        };

        Sessions[session.Id] = session;

        if (root.IsLeaf)
        {
            session.IsComplete = true;
            session.FinalAdvice = root.Label;
            return $"Conclusion: {root.Label}\nSession ID: {session.Id} — You can now ask 'how' to understand the reasoning.";
        }

        return FormatQuestion(session);
    }

    [McpServerTool, Description("Answer the current question in an interactive consultation session. Returns the next question, or the final advice if the consultation is complete.")]
    public static string AnswerQuestion(
        [Description("The session ID returned by start_consultation.")] string sessionId,
        [Description("The user's answer to the current question. For categorical attributes, must be one of the listed options. For numeric attributes, provide a number.")] string answer)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
            return "Error: Session not found. Start a new consultation with start_consultation.";

        if (session.IsComplete)
            return $"This consultation is already complete.\nConclusion: {session.FinalAdvice}\nSession ID: {session.Id} — You can ask 'how' to understand the reasoning.";

        var node = session.CurrentNode;

        if (node.IsLeaf || node.AttributeName == null)
            return "Error: Unexpected state — current node is a leaf or has no attribute.";

        if (node.IsNumericSplit)
        {
            if (node.Threshold == null || node.LessOrEqualChild == null || node.GreaterChild == null)
                return "Error: Numeric split node not fully initialized.";

            if (!double.TryParse(answer, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return $"Please enter a numeric value for '{node.AttributeName}'.";

            var branchLabel = v <= node.Threshold.Value
                ? $"<= {node.Threshold.Value.ToString(CultureInfo.InvariantCulture)}"
                : $"> {node.Threshold.Value.ToString(CultureInfo.InvariantCulture)}";

            session.Path.Add((node.AttributeName, answer, branchLabel));
            session.Answers[node.AttributeName] = answer;
            session.CurrentNode = v <= node.Threshold.Value ? node.LessOrEqualChild : node.GreaterChild;
        }
        else
        {
            if (node.Children == null)
                return "Error: Categorical node has no children.";

            var keys = node.Children.Keys.OrderBy(k => k).ToList();
            string? matchedKey = null;

            // Try numeric selection first
            if (int.TryParse(answer, out var idx) && idx >= 1 && idx <= keys.Count)
            {
                matchedKey = keys[idx - 1];
            }

            // Then try textual case-insensitive match
            if (matchedKey == null)
            {
                matchedKey = keys.FirstOrDefault(k => string.Equals(k, answer, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedKey == null)
            {
                var optionsText = string.Join(" ", keys.Select((v, i) => $"({i + 1}) {v}"));
                return $"'{answer}' is not a valid option for '{node.AttributeName}'. Options: {optionsText}";
            }

            session.Path.Add((node.AttributeName, matchedKey, $"{node.AttributeName} = {matchedKey}"));
            session.Answers[node.AttributeName] = matchedKey;
            session.CurrentNode = node.Children[matchedKey];
        }

        // Check if we've reached a leaf
        if (session.CurrentNode.IsLeaf)
        {
            session.IsComplete = true;
            session.FinalAdvice = session.CurrentNode.Label;
            return $"Conclusion: {session.CurrentNode.Label}\nSession ID: {session.Id} — You can now ask 'how' to understand the reasoning.";
        }

        return FormatQuestion(session);
    }

    [McpServerTool, Description("Explain why the current question is being asked during a consultation. Only works mid-consultation — use explain_how after completion to see the full reasoning path.")]
    public static string ExplainWhy(
        [Description("The session ID returned by start_consultation.")] string sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
            return "Error: Session not found.";

        if (!session.IsComplete)
        {
            var currentNode = session.CurrentNode;
            if (currentNode.IsLeaf || currentNode.AttributeName == null)
                return "No current question to explain.";

            var attrDef = session.Attributes.FirstOrDefault(a =>
                string.Equals(a.Name, currentNode.AttributeName, StringComparison.OrdinalIgnoreCase));

            if (attrDef == null)
                return $"Error: Could not find attribute definition for '{currentNode.AttributeName}'.";

            return C45Trainer.Why(session.TreeRoot, attrDef, session.Answers);
        }

        return "The consultation is already complete — there is no current question to explain. Use explain_how to see how the conclusion was reached.";
    }

    [McpServerTool, Description("Show the full decision tree and IF-THEN rules for the knowledge base used in a consultation session.")]
    public static string ExplainHow(
        [Description("The session ID returned by start_consultation.")] string sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
            return "Error: Session not found.";

        var sb = new StringBuilder();

        sb.AppendLine($"Decision tree for knowledge base '{session.KnowledgeBaseName}':");
        sb.AppendLine();

        sb.AppendLine("=== IF-THEN Rules ===");
        foreach (var rule in RuleExtractor.ExtractRules(session.TreeRoot))
            sb.AppendLine(rule);

        sb.AppendLine();
        sb.AppendLine("=== Decision Tree ===");
        sb.Append(RuleExtractor.FormatTree(session.TreeRoot));

        sb.AppendLine();
        sb.AppendLine("=== Attributes ===");
        foreach (var attr in session.Attributes)
        {
            if (attr.Kind == AttributeKind.Numeric)
                sb.AppendLine($"- {attr.Name} (numeric)");
            else
                sb.AppendLine($"- {attr.Name} (categorical): {string.Join(", ", attr.Domain)}");
        }

        return sb.ToString();
    }

    private static string FormatQuestion(ConsultationSession session)
    {
        var node = session.CurrentNode;
        var sb = new StringBuilder();
        sb.AppendLine($"Session ID: {session.Id}");

        if (node.IsNumericSplit)
        {
            sb.AppendLine($"Question: What is the {node.AttributeName}? (Enter a numeric value)");
        }
        else if (node.Children != null)
        {
            var keys = node.Children.Keys.OrderBy(k => k).ToList();
            var optionsText = string.Join(" ", keys.Select((v, i) => $"({i + 1}) {v}"));
            sb.AppendLine($"Question: What is the {node.AttributeName}? {optionsText}");
        }

        return sb.ToString();
    }
}
