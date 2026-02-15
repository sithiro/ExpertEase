# ExpertEase

An 80s-style **Expert-Ease shell** that lets you build, train, and interrogate **explainable expert systems** using a C4.5 decision tree learner.

Instead of opaque ML models, ExpertEase:

- reads a small **knowledge base** in JSON or CSV format (attributes + examples),
- induces a **decision tree** (C4.5 gain ratio, categorical + numeric),
- lets you **consult** it interactively (with `why` and `how` commands),
- and explains its decisions with a human-readable **HOW** trace and extracted rules.

## Status

C# solution with three projects:
- **ExpertEase.Library** — core C4.5 algorithm (no external dependencies)
- **ExpertEase.Console** — interactive CLI
- **ExpertEase.Mcp** — MCP (Model Context Protocol) server for use with AI assistants like Claude

### C4.5 algorithm

Full implementation of Quinlan's C4.5 decision tree learner:

- **Gain ratio** attribute selection with **average-gain filter** (two-pass: only attributes with InfoGain >= average are considered for GainRatio ranking).
- **Categorical attributes**: multi-way splits, removed from candidate list after splitting.
- **Numeric attributes**: binary splits at midpoint thresholds, retained for re-splitting at different thresholds deeper in the tree. Includes the **log2(N-1)/|S| penalty** to prevent numeric attributes from having an unfair advantage over categoricals.
- **Wildcard / missing values** (`*`): excluded from entropy and gain calculations (with known-fraction down-weighting), propagated to all branches during partitioning.
- **Pessimistic error-based pruning**: bottom-up subtree replacement using the upper confidence bound on error rate (CF = 0.25, z = 0.6745).

### Interactive consultation

- Questions follow the **learned tree structure** (root to children), not a fixed attribute order.
- **WHY**: explains why a question is being asked, showing which outcomes are still possible.
- **HOW**: after reaching a conclusion, traces the full decision path and explains each step.
- Rule extraction and ASCII tree visualization.

### Knowledge bases

Two formats are supported — auto-detected by file extension:

- **JSON** (`.json`): explicit attribute metadata (name, kind, domain) plus examples. Best when you need full control over attribute types and domains.
- **CSV** (`.csv`): flat table with a header row. Last column is always the label. Attribute types and domains are inferred automatically — numeric if all non-`*` values parse as numbers, categorical otherwise. Easy to create in Excel or any text editor.

**CSV example** (`sunday.csv`):
```csv
Weather,Family,Car,Advice
raining,yes,yes,museum
sunny,yes,yes,beach
*,no,yes,fishing
*,*,no,home
```

Included example knowledge bases:

| File | Domain | Attributes |
|---|---|---|
| `sunday.json` / `.csv` | What to do on Sunday | categorical + wildcards |
| `car_pricing.json` / `.csv` | Used car pricing advice | categorical + numeric + wildcards |
| `flight_disruption.json` | Flight disruption handling | categorical + numeric |
| `flight_ops.json` | Flight operations / turnaround | categorical + numeric |
| `crew_disruption.json` | Crew-related disruptions | categorical + numeric |
| `incident_severity.json` | Production incident severity | categorical + numeric |
| `medical_triage.json` | Medical triage (non-clinical demo) | categorical + numeric |
| `stock_entry.json` | Stock entry decisions (educational) | categorical + numeric |

## Project structure

```
ExpertEase/
  ExpertEase.sln
  ExpertEase.Library/          Core algorithm (C45Trainer, RuleExtractor, data types)
    C45Trainer.cs
    KnowledgeBaseLoader.cs     KB loading (JSON + CSV), shared by Console and Mcp
  ExpertEase.Console/          Interactive CLI
    Program.cs
  ExpertEase.Mcp/              MCP server for AI assistants
    Program.cs
    Tools.cs                   MCP tool definitions
  ExpertEase.Knowledge/        Knowledge base files (*.json, *.csv)
```

## Running the Console

Build the solution:

```bash
dotnet build ExpertEase.sln
```

Run with the default knowledge base (`sunday.json`):
```bash
dotnet run --project ExpertEase.Console
```

Run with a specific knowledge base (JSON or CSV):
```bash
dotnet run --project ExpertEase.Console -- car_pricing.json
dotnet run --project ExpertEase.Console -- car_pricing.csv
dotnet run --project ExpertEase.Console -- flight_ops.json
dotnet run --project ExpertEase.Console -- flight_disruption.json
dotnet run --project ExpertEase.Console -- crew_disruption.json
dotnet run --project ExpertEase.Console -- incident_severity.json
dotnet run --project ExpertEase.Console -- medical_triage.json
dotnet run --project ExpertEase.Console -- stock_entry.json
```

## Running the MCP Server

The MCP server exposes ExpertEase as a set of tools for AI assistants via the [Model Context Protocol](https://modelcontextprotocol.io/).

```bash
dotnet run --project ExpertEase.Mcp
```

### Available MCP tools

| Tool | Description |
|------|-------------|
| `list_knowledge_bases` | Lists all available .json and .csv KB files |
| `classify` | One-shot classification — pass all attributes, get advice + explanation |
| `get_decision_tree` | Shows the full tree, IF-THEN rules, and attributes for a KB |
| `reload_knowledge_base` | Reloads and retrains a KB from disk, returns stats (useful after editing a KB file) |
| `start_consultation` | Begins an interactive session, returns the first question |
| `answer_question` | Answers the current question, returns next question or conclusion |
| `explain_why` | Explains why the current question is being asked (mid-consultation) |
| `explain_how` | Shows the full decision tree and rules for the session's KB |

### Claude Code configuration

Add to `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "expertease": {
      "command": "dotnet",
      "args": ["run", "--project", "ExpertEase.Mcp"]
    }
  }
}
```

## Creating a knowledge base

A knowledge base is a table of past examples. Each example has a set of **attributes** (the inputs) and a **label** (the advice/decision). ExpertEase learns a decision tree from these examples and uses it to advise on new cases.

There are two attribute types:
- **Categorical** — a fixed set of text values (e.g. `luxury`, `standard`, `budget`)
- **Numeric** — any number (e.g. `30000`, `4.5`)

Use `*` as a wildcard when an attribute's value is unknown or doesn't matter for that example.

### CSV format

The simplest way to create a KB. Write a plain CSV table — the last column is always the label. Attribute types are inferred automatically: if every non-`*` value in a column parses as a number, it's numeric; otherwise it's categorical.

```csv
Brand,Mileage,Age,Condition,Advice
luxury,30000,2,excellent,premium
luxury,45000,3,good,premium
luxury,120000,7,good,fair
standard,40000,3,good,fair
standard,80000,5,excellent,fair
budget,60000,4,good,budget
standard,130000,8,good,budget
*,200000,12,poor,avoid
*,180000,10,poor,avoid
budget,20000,1,*,fair
budget,160000,9,poor,avoid
luxury,15000,1,*,premium
```

Here `Brand` and `Condition` are inferred as categorical (text values), while `Mileage` and `Age` are inferred as numeric. The `*` in the Brand column means "any brand" and in Condition means "any condition".

### JSON format

Gives you explicit control over attribute types and domains. The file has three sections: metadata (`name`, `description`), `attributes` (each with a `name`, `kind`, and optional `domain`), and `examples`.

```json
{
  "name": "CarPricingAdvisor",
  "description": "Used car pricing advice based on brand, mileage, age, and condition.",
  "attributes": [
    { "name": "Brand",     "kind": "categorical", "domain": ["luxury", "standard", "budget"] },
    { "name": "Mileage",   "kind": "numeric" },
    { "name": "Age",       "kind": "numeric" },
    { "name": "Condition",  "kind": "categorical", "domain": ["excellent", "good", "poor"] }
  ],
  "examples": [
    { "label": "premium", "attributes": { "Brand": "luxury",   "Mileage": "30000",  "Age": "2",  "Condition": "excellent" } },
    { "label": "premium", "attributes": { "Brand": "luxury",   "Mileage": "45000",  "Age": "3",  "Condition": "good" } },
    { "label": "fair",    "attributes": { "Brand": "luxury",   "Mileage": "120000", "Age": "7",  "Condition": "good" } },
    { "label": "fair",    "attributes": { "Brand": "standard", "Mileage": "40000",  "Age": "3",  "Condition": "good" } },
    { "label": "fair",    "attributes": { "Brand": "standard", "Mileage": "80000",  "Age": "5",  "Condition": "excellent" } },
    { "label": "budget",  "attributes": { "Brand": "budget",   "Mileage": "60000",  "Age": "4",  "Condition": "good" } },
    { "label": "budget",  "attributes": { "Brand": "standard", "Mileage": "130000", "Age": "8",  "Condition": "good" } },
    { "label": "avoid",   "attributes": { "Brand": "*",        "Mileage": "200000", "Age": "12", "Condition": "poor" } },
    { "label": "avoid",   "attributes": { "Brand": "*",        "Mileage": "180000", "Age": "10", "Condition": "poor" } },
    { "label": "fair",    "attributes": { "Brand": "budget",   "Mileage": "20000",  "Age": "1",  "Condition": "*" } },
    { "label": "avoid",   "attributes": { "Brand": "budget",   "Mileage": "160000", "Age": "9",  "Condition": "poor" } },
    { "label": "premium", "attributes": { "Brand": "luxury",   "Mileage": "15000",  "Age": "1",  "Condition": "*" } }
  ]
}
```

The JSON `domain` array for categorical attributes defines the allowed values and their order during consultation. For numeric attributes, no domain is needed. Both formats produce identical decision trees.

## Design notes

- **Goal**: small, understandable, 80s-style expert system shell. Explicitly inspect the induced tree, extract rules, ask WHY and HOW, treat the tree as a symbolic knowledge base.
- **Not optimized** for large datasets; meant as a teaching, exploration, and nostalgia tool.
- **Intentional simplifications** vs the original C4.5 C source: missing values are duplicated to all branches (rather than fractional instances), and pruning does subtree replacement only (no subtree raising). Neither affects correctness for small expert-system knowledge bases.

## License & author

[MIT License](LICENSE) — Bill Sithiro (sithiro@gmail.com)
