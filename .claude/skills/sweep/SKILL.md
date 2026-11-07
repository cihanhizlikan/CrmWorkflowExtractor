---
name: sweep
description: A sequenced quality audit of this repository — structure, test health, dependencies, dead code, configuration, robustness (C# and the browser scripts), maintainability, context-document drift and the delivered Turkish wording. It runs UNATTENDED: it never stops and never asks, merging each step and writing anything that needs a decision into plan.md instead. The maintainer starts it and reads the report when they return.
disable-model-invocation: true
---

# The sweep — CRM Workflow Extractor

**One audit, nine steps, in an order that is itself the deliverable.** It corrects what is unambiguous, merges each
step, and hands everything else to `plan.md` as a work package.

**This is this repository's own sweep** (2026-11-07), derived from Yalbuz's portable one after its first run here.
What has nothing to work on in a console tool with no UI, no network, no DI and one culture is gone: the UI-drift
step, the register, render harnesses, markup and DI traps, the plan-mode round trip, and the cross-culture
comparison. What the first run actually ran into is written in. Repository facts still come from
`.claude/project-profile.md` (`profile:<key>`), which the `wp-*` skills read too; **a key that is missing or `none`
makes its step UNAVAILABLE, never clean.**

**What this is not.** A finding that needs a POLICY over many sites — one rule ID, each site triaged take-or-decline
— is a loop for the plugin skills `anthropic-skills:csharp-refactor-harden` / `…-streamline`; one crossing a
project boundary is `…-modularize`'s. A step CORRECTS only a site whose repair is unambiguous and whose subject is
its own.

## It runs UNATTENDED

**The maintainer starts a sweep when leaving, and expects finished work on return.** A session never starts one.

- **NEVER stop to ask.** No `AskUserQuestion`, no turn ending in a question, no progress report. Anything that would
  have been a question becomes a **plan row** — what was measured, the options, the recommendation — and the run moves on.
- **Merge, delete and continue.** Each step's branch is merged into `main` and deleted by the sweep itself before
  the next opens — an explicit exception to *merging is the maintainer's*, granted for this skill. Its limit is the
  unambiguity bar: widen it and the exception is something it was never granted for. `/wp-finish`'s "stop" does not
  apply inside a sweep. **A session never pushes.**
- **Report only at the END**, in one message.

**Ending early is allowed; waiting is not.** A red baseline, an unbuildable tree, a session out of room: record why
in the plan, say so in the report, exit.

### Mid-flight, one legal move each

- **An action is REFUSED** (a classifier declines a delete, a `git` command): the exact command into the plan,
  carry on without it. Never re-issue it in another form. **Retrying a FAILURE is not routing around a refusal.**
- **The suite goes red inside a step.** Do not merge. Capture the failing test NAME into a file before re-running
  anything; run it isolated several times. Fails isolated and the step touched its subject → revert the step, hand
  it over. Passes isolated → re-run the suite once; green merges, and the test becomes a step-2 row. Fails isolated
  and untouched → the tree moved under the run; end as a red baseline does.
- **Plan mode switches on.** Commit any half-done step on its branch UNMERGED, report, end.
- **Out of room.** Finish the step in hand, write which step is next into the plan.

### Rows go into plan.md, in the tree

`profile:plan_rule`: the plan is `.claude/context/plan.md`, so a row is a commit, never an edit in the primary
checkout. **A step's rows travel in that step's own commit.** A step with rows and nothing to correct may carry them
on the NEXT step's branch, saying so in that merge subject (*"step 7 — maintainability (with step 6's row)"*) — or
on a branch of its own if no later step opens one. The first run opened section **Sweep YYYY-MM-DD — handed over**
at the end of the plan; each run appends its own. Each row: **What · Where** (path:line that resolves today) **·
Why not obvious · Recommendation · Falsify**, Origin `finding`, not yet committed to build. No plan-mode round trip:
git history is the record of what landed.

---

# How a run works

**Scope.** Every sweep merge subject starts `merge: sweep <YYYY-MM-DD> step <n>`; the baseline is
`git log --grep='^merge: sweep ' -1 --format='%H %ad %s' --date=short`. Candidate sets come from
`git diff --name-only <baseline>..HEAD`. **Whole tree regardless**: step 2's repeated suite runs, step 3, step 8's
path check, and any step whose detector became LIVE since the baseline.

**Preflight.**
1. **Clean tree on `main`, green Release build, green suite — three full runs**, which also serves step 2's
   load-sensitivity check. Not green → the run ENDS; never repair the baseline.
2. **Detector liveness — an analyzer rule that is off reads exactly like a rule at zero.** For every rule a step
   reads, its severity in `profile:analyzer_config`; a rule not at `warning` is UNAVAILABLE until a probe in a
   THROWAWAY worktree makes it fire. A rule at `warning` with zero sites was proven to fire on 2026-11-07; re-probe
   only if the SDK pin (`global.json`) or an analyzer version (`Directory.Packages.props`) moved since. Known states:
   - **LIVE**: CA1031, CA2016, CA2201, CA2008, CA1065, CA2219, the Security category, RCS1236, RCS1075, RCS1211,
     CA1502/CA1505/CA1506, VSTHRD*; the NuGet audit (`dotnet list package --vulnerable --include-transitive`,
     `--deprecated`, `--outdated` — from a worktree, it restores); **`jb inspectcode`** (ReSharper global tool,
     installed): `jb inspectcode CrmWorkflowExtractor.slnx --output=<scratch>/inspect.sarif --format=Sarif
     --severity=SUGGESTION --no-build` — a few minutes, so start it in the background at preflight.
   - **BLOCKED**: CA2000 — does not fire even on a probe (plan row, 2026-11-07). Re-try once per run; still silent
     → the row stands, no new one.
   - **UNAVAILABLE** unless installed since: a coverage collector (none referenced), Stryker. Never installed by an
     unattended run.

**Derive, never cache.** Every list is derived at the start of the run from the tree. Shapes that LOOK dead here
and are alive:
- **Positional records serialized to JSON** (`RunManifest`, the readers' records): ReSharper reports ~68
  never-accessed positional properties; they are written to `manifest.json` and `ham/` and stay.
- **xUnit discovery** reaches every `[Fact]`/`[Theory]`; **extension-method holders** are reached by the method name.
- **Several types per file** (`one_type_per_file` is not enforced): enumerate types by declaration, not by file.
- **Read what a match IS**: a doc comment or `plan.md` naming a member pads a count without being a caller.

**What a finding is.** Hand over a DECISION, never an inventory: triage every site, lead with the count that
survived. Each item lands in exactly one place — **corrected** in the step (the commit body is the record), **a plan
row**, **a settled decision** (`profile:settled_decisions`, with the measurement and what would reopen it), or **an
acceptance item** (`profile:offsite_register`: anything only the CRM server or a production export can settle, with
a Do, a Pass and a Capture). When it is not obvious, it is a row. A measured "nothing here" opens no branch; its
report line is its record, and says what produced the number.

---

# The nine steps

## 1. Structure and naming
**First: every later finding is written against the tree this step would move.** Do not re-detect
`profile:enforced_guards`.
- **Placement** against `profile:structure_doc`'s project table: `Cli` → everything, `Bpmn` → `Ir`, `Ir` and
  `Extract` → nothing of ours (read the `ProjectReference`s); each type in the project that owns its subject.
- **Naming** (`profile:naming_rule`): a name a reader must open the file to understand — above all a name still
  defined by something removed (the network path, similarity, consolidation, `raw/`). A rename is a choice of
  word, so it is a ROW with a recommendation, unless it is a private member with one obvious name.

## 2. Test-suite health
*A test asserts a requirement, never a preference.* Over `Crm.Tests/`, excluding `Crm.Tests/Fixtures/`.
- **Cannot fail**: no assertion; **every assertion inside a loop** whose collection a regression could empty — the
  layout and readability tests are this shape. The repair: assert the collection first — non-empty, or **at least two
  items where the test compares PAIRS** (one caption has nothing to overlap). Falsify by suppressing what the
  writer emits (rename the emitted element) and watch them go red.
- **Preference**: a pinned pixel size, colour or exact layout coordinate. A RELATION (no overlap, no crossing, a box
  holds its text) is a requirement; `Assert.Equal((130, 70), …)` is a preference — compare against the ordinary case.
- **Unbounded waits** (`.Wait()`, `.Result`, `Thread.Sleep`, `Task.Delay`); **a name that no longer describes its body**.
- **Load-sensitive**: preflight's three runs; a test failing once is captured by name and run isolated ten times.
- Method count is not test count: ~250 methods report 390 because `[Theory]` cases expand.

## 3. Dependency hygiene
Before everything that reads an analyzer, because an analyzer bump changes what steps 6 and 7 measure.
- Every version is in `Directory.Packages.props`; `global.json` pins the SDK. A version on a `PackageReference` is
  NU1008, so skew cannot be written — check only that the Microsoft.Extensions line is ONE patch, the installed
  runtime's (`dotnet --list-runtimes`).
- **Vulnerable** with a fixed version in the same major → correction. **Patch/minor outdated** → correction, the
  whole suite as proof. **A major** (xunit 4, Test.Sdk 18 were open on 2026-11-07) → a row, or the existing row
  re-confirmed — never a second one.

## 4. Dead code
**Refactor residue**: a helper the change left behind, a property written and never read, a parameter nothing uses.
- **Read the build's neighbour, ReSharper**: from the SARIF, production files only (`Crm.Tests/` excluded):
  `UnusedMember.*`, `UnusedAutoPropertyAccessor.*`, `UnusedParameter.*`, `NotAccessedVariable`,
  `RedundantAssignment`. Re-check each by search with **production and test references counted apart** — a
  production member only a test calls is dead production code, unless its name or doc comment says it is a seam.
- **A computed fact nobody reads is not automatically residue**: if it is the run's only record of something a
  reader would want (`CallGraph.MissingTargets`, 2026-11-07), it is a row — show it or delete it.
- After removing, the Release build names any `using` left behind (IDE0005 is an error there).
- Signature extraction: a tuple return type defeats `Name(` patterns — take the first identifier before `(`.

## 5. Configuration knobs
**Triggered** when step 4 removed code or `ExtractorSettings` changed since the baseline; otherwise *not triggered*.
There is no validation design to review (`config_behaviours: none` — a bad file name stops the run with exit 2).
The step is: every property of `Crm.Cli/Configuration/ExtractorSettings.cs` read by production code outside
`Configuration/`, and present in `appsettings.json`, the `Program` constants block and `ConfigurationTests`
(`profile:config_coverage_test`). `appsettings.json` IS the effective configuration; there are no secrets.

## 6. Robustness
**Read the build first**: every rule preflight called LIVE starts at zero; a non-zero is new and the build names it.
Then what no analyzer sees:
- **The guards that make one failure cost one item**: the per-workflow guard in `BpmnStage`, the per-map guards in
  `StageMapStage`, `IrStage`'s parse guard, `ExtractionRun`'s run-level filter. A broad catch is legitimate ONLY
  as one of these, filtered `when (error is not OperationCanceledException)` or by listed types; a new one
  elsewhere is a finding. **A cancellation is the operator's and must travel.**
- **The console's last resort** (`host_types`: console only): an exception outside `ExtractionRun`'s list leaves
  `Main` as a runtime crash — plan row S6 (2026-11-07). Re-confirm or retire it; do not duplicate it.
- **Lost exceptions**: `_ = …Async(`, a dropped `Task.Run`.
- **Rule 9's inverted half**, which `RCS1236` misses: a `throw;` preceded by `if (error is X) { return; }`.
- **Text into XML**: every string that reaches a writer goes through `DocumentText.Writable` (the BPMN serializer
  sweeps the built document); a new writer that does not is a finding.
- **The browser scripts** (`tools/*.js`) are production code no analyzer reads, and the ONLY code that touches
  CRM. They send GET only; every read that can be refused goes through the guarded path, so a refusal SAVES the
  file with the reason (the 2026-10-12 production failure was the one unguarded read); ids in error text are
  blanked; process-mining output carries no case, customer, user or exact time. **A script edit regenerates its page
  — `node tools/build-export-page.js` — in the same commit**, or `BrowserExportImportTests` fails on the SHA.
  Whatever a script change needs to be proven against CRM is an acceptance item, never "verified".

**Robustness vetoes step 7**: no simplification may delete a failure path, widen a catch, remove a cancellation
check, or let an isolated failure propagate. Such a finding is recorded `waived (robustness)` and dropped.

## 7. Maintainability and performance
- **Complexity**: CA1502/CA1505/CA1506 are at `warning` and at zero, with no suppression in the tree. A new
  suppression's justification must NOT state the number the analyzer computes — that number rots; the reason stays.
- **From the SARIF, as leads to read, not corrections to apply**: `DuplicatedStatements`, `RedundantCast`,
  `MemberHidesStaticFromOuterClass`. `PatternAlwaysMatches` on a nullable tuple deconstruction
  (`is (Guid, string name)`) is a NULL CHECK and a false positive; a cast that types a switch arm is not redundant.
- **Performance needs a measurement** — a stated complexity change named in the commit, or counters. The layout's
  measured quantities (crossings, longest arrow, box count on the Reklam-shaped fixture) are the only performance
  numbers this tool carries; anything else is speculation and is dropped.

## 8. Context-document drift
After every step that moves code. Over `profile:context_docs`, the profile included.
- **Mechanical, whole tree**: every backticked repository path resolves. Triage before calling a miss drift — the
  first run had 120 misses and no real one:
  - run-output names (`ham/`, `bpmn/…`, `raporlar/`, every `.xlsx`, `.docx`, `.bpmn`, `rapor.md`, `manifest.json`);
  - bare file names relative to a folder the sentence names (`reference/…` is under `.claude/context/`);
  - gitignored files (`git check-ignore -q`);
  - `plan.md`'s DATED entries — they are history and are never rewritten to match today;
  - a `path:line` — the file must resolve and the line must still be the thing named.
- **Claims of state**: a sentence saying something "is off", "is not split yet", a count, a network or similarity
  claim. Read each against the file that decides it; a claim decays by being OVERTAKEN. Doc comments in code are in
  scope when they describe what the code no longer does.
- **`plan.md`'s *Acceptance on the company network* is the maintainer's checklist**: report drift there as a row,
  never rewrite it.

## 9. The delivered words
`profile:user_docs`: `Crm.Cli/Reports/AnalystGuide.cs` (the guide) and `Crm.Cli/Reports/Guides.cs` (every workbook's
*Nasıl okunur*). Turkish only — there is no second culture to compare. `rapor.md`'s writers are internal and out of
scope. `DeliveryTests` and `AnalystGuideTests` already refuse any internal file named in a delivered one; do not
re-detect that.
1. **Structure**: does the guide follow how an analyst approaches the package — what it is, what to open, one
   workflow walked end to end — or where each feature happened to be added?
2. **The constructions `profile:wording_rules` forbids, searched across EVERY string**, not the paragraph fixed
   last time: `grep -nP '(?<!İş |iş |İŞ )\b(akış|Akış|süreç|Süreç)'` over both files. Read each: a workflow becomes
   "iş akışı" WITH its case ending (`akışı` → `iş akışını`, `akışın` → `iş akışının`, `süreçtir` → `iş akışıdır`).
   **Keep**: "süreç" as CRM's umbrella for every process definition (dialogs and business rules included), CRM's
   product name "İş Süreci Akışı", and "akış" meaning a DIAGRAM's flow ("akışı soldan sağa izleyin", "biten akış").
   Also: a stage is "talep aşaması", a diagram of one "aşama haritası", an entity "kayıt türü"; a stage's assigned
   user and a role's holders are never named.
3. **Concise and plain**: cut a sentence that restates its heading and a qualifier that rules nothing out.

---

# Rules that bind every step

- **Check the INSTRUMENT before believing it.** A confident negative is the shape to distrust first; read WHAT a
  search matched before concluding from HOW MANY. The build output is Turkish (`hata`/`uyarı`, `Başarılı`,
  `Başarısız`): grep `: error ` and `[FAIL]`, never a bare word.
- **Never re-raise a settled decision** (`profile:settled_decisions`); a ruling reopens on a new fact only.
- **Bytes that must not move**: `Crm.Tests/Fixtures/**` (evidence, LF) and the six `tools/` files (`-text`,
  SHA-checked). Never edited to make a finding go away.
- **Line endings**: source is CRLF. Under MSYS `sed -i` rewrites every line; after one, `sed -i 's/\r*$/\r/'` and
  check lines == CRs. Prefer the Edit tool for anything multi-line.
- **Scripted edits** (node, from the scratchpad): split on `\r\n` and rejoin, and fail when a match count is not
  exactly one. A pattern holding a backslash (a regex inside C#) does not survive a heredoc into JS — make that
  edit with the Edit tool.
- **Never `git checkout -- <file>` to undo an experiment**: it restores `main`'s version and silently drops the
  step's own uncommitted edit to that file (it did, once, in the first run). Copy the file to the scratchpad first
  and copy it back.
- **Never run `Crm.Cli` against a real export from a worktree**, and nothing here can reach CRM: anything that needs
  it is UNVERIFIED and becomes an acceptance item.

---

# Finishing

Each step with something to correct runs `/wp-start` → work → `/wp-verify` → `/wp-finish` (commit body and size
cap only), then merges and deletes; the next step opens from the primary checkout on a clean `main`.

- **Branch `sweep-<n>-<subject>`; merge with `git merge --no-ff -m "merge: sweep <YYYY-MM-DD> step <n> — <subject>"`.**
  The subject is the next run's baseline; a fast-forward has no merge subject and loses it.
- **Leave the worktree before removing it**: `cd` to the primary checkout, `dotnet build-server shutdown`, then
  `git worktree remove`. A shell still inside it, or the build server, leaves a husk (`Permission denied`,
  `Device or resource busy`); prune and `rm -rf` it by its literal path from the primary checkout.
- **The throwaway worktree** used for probes and `dotnet list package` is removed at the end with its branch.
- **Never `EnterWorktree`**: it branches from `origin/main`, behind local `main`.

## The report — the one thing the maintainer reads

One message at the end, in a fixed shape so two runs compare:
- **The run's facts**: the scope baseline (commit and date, or "whole tree — no marker"); each detector LIVE,
  UNAVAILABLE or BLOCKED; anything REFUSED, with the exact command.
- **One line per step, all nine, in order**, each saying what it measured and which it did: corrected (the merge),
  handed over (the row), settled, measured clean, not triggered, or UNAVAILABLE.
- **What the maintainer has to decide** — the rows, each with its recommendation; anything added to the acceptance list.
- **What was left behind** — a husk, an unmerged branch, a step that ended the run and why.

**A recommendation is not permission.**
