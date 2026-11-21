---
paths: ["**/*.cs"]
---

# C# Aesthetics (MANDATORY — apply to every edit)

All 13 rules adopted from Yalbuz on 2026-09-14 (maintainer's decision). Yalbuz's own `coding-style.md` carries the
history behind each rule; this file carries the rules and the traps that apply to them.

1. **Never use `var`.** Explicit types, preferring target-typed `new()`: `StringBuilder builder = new();`
2. **Always use curly brackets** for all control flow, even single-line blocks.
3. **No trailing empty lines** at the end of `.cs` files. A file ends `}` + one newline.
4. **Controller-based APIs only** — minimal APIs are banned. Console apps use a classic `Program` class with a
   `static Main`: no top-level statements, no minimal-host style.
5. **Pattern matching over `switch` statements**, especially `is` in an `if`:
   `if (node is XElement element)`, not a type-dispatching `switch`. Switch *expressions* stay welcome.
6. **`as`, never an explicit cast.** `x as Foo`, not `(Foo)x`. Use `if (x is Foo f)` when you test and use in one
   step. Numeric conversions between value types are not casts in this sense — `(int)longValue` has no `as` form.
7. **Collection expressions.** `[]` — not `new()`, `new List<T>()`, `Array.Empty<T>()` or `Enumerable.Empty<T>()` —
   and `[.. source]` to materialize rather than `.ToList()`/`.ToArray()` into a `new`.
   `IReadOnlyDictionary<,>` cannot be built from one, so a dictionary stays a `new` — and so do `Queue<T>` and
   `Stack<T>`, which have no `Add` (`StageMap`'s walk queue).
8. **Range and index operators** where they read better than arithmetic: `text[..200]`, `name[..^10]`,
   `url[(at + 1)..]`, not `Substring(0, 200)` or `Substring(at + 1)`.
9. **Exception filters over catch-and-retest.** `catch (Exception error) when (error is IOException or
   HttpRequestException)`. A filter that does not match never unwinds the stack. **Half build-enforced, and the half
   is exact:** Roslynator `RCS1236` fires on the NEGATED retest (`if (!(error is IOException)) { throw; }` as the
   catch body) and is silent on the inverted `if (error is IOException) { return; } throw;` — the same defect
   written the other way round. The analyzer catches one spelling; a reviewer owns the other.
10. **File-scoped namespaces** and **block-bodied members** — `get { return _x; }`, not `=> _x`. Test projects are
    scoped off for methods only.
11. **Primary constructors wherever one is possible.** A captured parameter is **not `readonly`**: where immutability
    matters, keep `private readonly Foo _foo = foo;`. Watch for **shadowing** — a method parameter with the same
    name as a primary-constructor parameter silently wins inside that method. Do not invent a static helper only to
    turn a multi-statement constructor into an initializer.
12. **Call a spade a spade, without being verbose.** The shortest name still honest; each word must rule something
    out. Naming a PRODUCT is right (`Crm`, `Bpmn`, `Xaml`); naming the company or its instances is not.
13. **No `else` after a branch that exits.** Return early and dedent. An `else if` CHAIN whose arms do not all exit
    is not this rule's business — dropping its `else` changes behaviour.

Interactions: rule 6 replaces a cast, not an `is`-pattern. Rule 7 overrides rule 1 for collections only.

**Enforcement.** Rules 1–2, 5, 7, 8, 10, 11 via `.editorconfig`; rule 13 via Roslynator `RCS1211`; half of rule 9
via `RCS1236` (2026-11-07); rule 3 via `RepositoryConventionTests.No_Source_File_Ends_With_A_Blank_Line`; rule 4 via
that test's `Program`-shape check. **Reviewer-carried: 6 and 12, and the inverted half of 9.** This list is
RE-DERIVED when an analyzer is adopted, never decremented — that is the only way the sentence stays true.

**String handling in this repository specifically:** comparison keys over CRM text go through `TurkishFold`, never
`ToLowerInvariant()` alone, and every string comparison names its `StringComparison` (CA1307/CA1310 are on).

Bring existing code into compliance when you touch it (from Yalbuz's copy, 2026-11-21).
