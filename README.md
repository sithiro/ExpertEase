# ExpertEase

An 80s-style **Expert-Ease** shell that lets you build and interrogate small expert systems — but implemented as a modern, self-contained LINQPad script.

Think:

> “Spreadsheet-like attribute + example definitions in,  
> C4.5 decision tree out,  
> with interactive **HOW?** / **WHY?** explanations.”

---

## What this is

ExpertEase is a single `ExpertEase.linq` script that acts as a tiny *expert system shell*:

- You define:
  - a set of **attributes** (name + allowed values), and  
  - a set of **examples** (rows) mapping attribute combinations → a conclusion (label).
- The script trains a **C4.5-style decision tree** (Gain Ratio, not plain ID3).
- You can:
  - inspect the induced **rules**,
  - view a **tree diagram** in ASCII,
  - run an **interactive consultation** that asks you questions and then:
    - gives you an **advice** (classification),
    - and explains **HOW** it reached that advice,
    - and **WHY** it’s asking each question.

The vibe is deliberately “80s expert system shell”, but with modern C# and LINQPad ergonomics.

---

## Features

- **Attribute + example definition in code**  
  A pair of simple types:

  ```csharp
  record AttributeDef(string Name, IReadOnlyList<string> Domain);

  record TrainingExample(
      Dictionary<string, string> Attributes,
      string Label);
