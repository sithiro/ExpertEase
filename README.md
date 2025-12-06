# ExpertEase – Minimal C4.5-style Expert System Shell

This project implements a small expert-system shell using a **C4.5-style decision tree** (Gain Ratio).

You define:

- a set of **attributes** (name + domain values), and  
- a set of **training examples** mapping attribute combinations to a conclusion (label),

and the program:

- **induces a decision tree** from those examples,
- allows **interactive consultations** in the console (question/answer),
- and provides **HOW** and **WHY** explanations for its conclusions:
  - **HOW** – the path and reasoning it followed to reach a given advice,
  - **WHY** – why a particular question is being asked, given the answers so far.

The current example domain is a simple “What should we do on Sunday?” advisor, but the shell is generic and can be adapted to other domains by changing the attribute and example definitions.

---

## Author

- **Name:** Bill Sithiro  
- **Email:** sithiro@gmail.com  
- **Date:** 2025-12-06  

---

## License (permissive)

You may use, copy, modify, and distribute this source code, in whole or in part, for any purpose, with or without modification, without fee. If you redistribute modified versions, a brief attribution to the original author is appreciated but not required.

This software is provided **“as is”**, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and noninfringement. In no event shall the author be liable for any claim, damages or other liability, whether in an action of contract, tort or otherwise, arising from, out of or in connection with the software or the use or other dealings in the software.
