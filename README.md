# ExpertEase

An 80s-style **Expert-Ease shell** that lets you build, train, and interrogate **explainable expert systems** using a C4.5 decision tree learner.

Instead of opaque ML models, ExpertEase:

- reads a small **knowledge base** in JSON or CSV format (attributes + examples),
- induces a **decision tree** (C4.5 gain ratio, categorical + numeric),
- lets you **consult** it interactively (with a `why` command),
- and explains its decisions with a human-readable **HOW** trace and extracted rules.

## Status

C# solution (library + console app, no external dependencies beyond the .NET 9 SDK).

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
  ExpertEase.Console/          Interactive CLI
    Program.cs
    *.json, *.csv              Knowledge base files
```

## Running

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

## Design notes

- **Goal**: small, understandable, 80s-style expert system shell. Explicitly inspect the induced tree, extract rules, ask WHY and HOW, treat the tree as a symbolic knowledge base.
- **Not optimized** for large datasets; meant as a teaching, exploration, and nostalgia tool.
- **Intentional simplifications** vs the original C4.5 C source: missing values are duplicated to all branches (rather than fractional instances), and pruning does subtree replacement only (no subtree raising). Neither affects correctness for small expert-system knowledge bases.

## License & author

[MIT License](LICENSE) — Bill Sithiro (sithiro@gmail.com)
