# ExpertEase

An 80s-style **Expert-Ease shell** that lets you build, train, and interrogate **explainable expert systems** using a C4.5-like decision tree learner.

Instead of opaque ML models, ExpertEase:

- reads a small **JSON “knowledge base”** (attributes + examples),
- induces a **decision tree** (C4.5-style gain ratio, categorical + numeric),
- lets you **consult** it interactively (with a `why` command),
- and explains its decisions with a human-readable **HOW** trace and extracted rules.

## Status

This is a minimal but working MVP:

- C# console app (no external dependencies beyond the .NET SDK).
- C4.5-style training:
  - categorical + numeric attributes,
  - gain ratio,
  - `*` wildcard in examples for “don’t care / unknown”.
- JSON-based knowledge bases:
  - `sunday.json` – “what to do on Sunday”.
  - `flight_disruption.json` – simplified flight disruption handling.
- Interactive consultation driven by the **learned tree**:
  - questions follow the actual decision path (root → children),
  - not a fixed attribute order from the file.
- WHY questions and HOW explanations.
- Rule extraction + ASCII tree visualization.

No pruning or advanced handling of missing values yet (by design – this is meant to be small and understandable).

## Design notes

- Algorithm: C4.5-style decision tree:
  - categorical + numeric attributes,
  - gain ratio,
  - wildcards in examples,
  - no pruning (yet).

- Interactive consultation:
  - questions follow the actual tree structure,
  - WHY and HOW expose the internal reasoning,
  - suitable as an “expert system shell” rather than a black-box ML model.

- Goal: small, understandable, 80s-style expert system:
  - explicitly inspect the induced tree,
  - extract rules,
  - ask WHY and HOW,
  - treat the tree as a symbolic knowledge base.

This is intentionally not optimized for large data sets; it’s meant as a teaching,
exploration, and nostalgia tool.

## License & author

The code in this repository is provided under a very permissive, “take it and run with it” style license:

Author: Bill Sithiro

Email: sithiro@gmail.com

```
Permission is hereby granted to use, copy, modify, and distribute this software
and its documentation for any purpose, free of charge, provided that this notice
is retained in any substantial portions of the software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```