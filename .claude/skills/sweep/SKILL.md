---
name: sweep
description: A sequenced quality audit of this repository — structure, test health, dependencies, dead code, configuration, robustness (C# and the browser scripts), maintainability, context-document drift and the delivered Turkish wording; the UI-drift step does not apply here. It runs UNATTENDED: it never stops and never asks, merging each step and writing anything that needs a decision into plan.md instead. Project-specific values come from .claude/project-profile.md. The maintainer starts it and reads the report when they return.
disable-model-invocation: true
---

# The sweep — CRM Workflow Extractor

**One audit, ten steps, in an order that is itself the deliverable.** It corrects what is unambiguous, merges each
step, and hands everything else to the plan as a work package.

**What this is not.** `csharp-refactor-harden` and `csharp-refactor-streamline` own LOOPS — one rule ID standing
over many sites, each triaged take-or-decline — and a robustness or maintainability finding of that shape is
handed to them. **The line is not a count.** A step CORRECTS a site whose repair is unambiguous and whose subject
is its own; the moment a finding needs a POLICY over sites it is a loop however few sites it has.
`csharp-refactor-modularize` owns anything crossing a project boundary. **All three are PLUGIN skills** (under
`anthropic-skills`), so a bare `/csharp-refactor-…` may not resolve; qualify it with the plugin name rather than
concluding the skill is gone.

## The profile, and why this file names no project

**Everything true of one repository lives in `.claude/project-profile.md`, as named keys** — its projects, the
conventions its build already enforces, the documents the steps read, its plan, its secrets file, its user docs,
its UI register. This file refers to them as `profile:<key>`. **That is what makes the skill copyable**: a new
repository gets its own profile, never an edited copy of this file.

**An absent key is UNAVAILABLE, never clean.** A step whose input key is missing, `none`, or names something that
does not exist reports `UNAVAILABLE (profile: <key>)` on its report line and does nothing else. This is the
failure the profile exists to prevent: a step that reads a structure document finds nothing in a repository that
has none, and "found nothing" reads exactly like "measured clean". **No profile at all: run preflight only, list
the keys the steps would have needed, and end.**

Examples in this file marked *(illustrative)* come from the repository this skill was written in. They show a
SHAPE to look for; they are not a list to check.

## Invocation

**The maintainer starts a sweep; a session never does** — hence `disable-model-invocation: true`. Starting one is a
decision to spend a session auditing, and work is ISSUED, never taken. A session may propose one, with a reason.

## It runs UNATTENDED, and that is the whole design

**A sweep is started when the maintainer is leaving — for work, for the night — and expects to come back to
finished work.** Everything below follows from that, and it overrides the ordinary cadence rather than bending it.

- **NEVER stop to ask. Never.** Not for a decision, a preference, permission, or to report progress. Do not call
  `AskUserQuestion`. Do not end a turn with a question. **There is nobody there**, so a question is not caution —
  it is the run halting for hours and delivering nothing. A run that reported after step 1 *(illustrative,
  2026-09-08)* had to be restarted by hand the next morning.
- **Anything that WOULD have been a question is written into the plan and SKIPPED.** A stop becomes a plan row
  plus a continue: what was measured, the options, which one is recommended and why — then move on and do not
  come back to it.
- **Merge, delete and continue.** Each step's branch is merged into `main` and deleted by the sweep itself, without
  asking, before the next step opens. **This is an explicit exception to three ordinary rules** — *merging is the
  maintainer's*, *a merge instruction is per-moment*, and *work whose choices have not been seen is not merged* —
  granted for this skill by the maintainer (*"it should merge, delete & publish as it completes its steps"* — the
  *publish* half lapsed on 2026-11-07, when the one register a sweep revised became a file in the repository). A
  session running this skill does not re-derive those rules and stop. **The third has a limit**: a sweep merges
  work nobody has reviewed, and only the unambiguity bar keeps that honest — a step CORRECTS when the repair is
  unambiguous and the suite proves it, and hands over everything else. Widening that bar turns the exception into
  something it was never granted for. **`/wp-finish` says "Report, and stop"**; the exception reaches that
  sentence too — a step uses it for the commit body and the size cap, then keeps going.
- **A register revised in a step travels on that step's branch.** `profile:ui_register` is a file in the
  repository, so a step that revises it commits the revision with the change that moved the rule, and the step's
  merge carries both. There is nothing to publish, and **nothing here is ever `git push`** — a session never pushes.
- **Report only at the END**, in one message covering every step. A mid-run progress report is a stop wearing a
  report's clothes.

### Ending a run is allowed; WAITING is not

A sweep may **finish early** — a red baseline, a tree that will not build, a session out of room — and that is a
complete, honest outcome: record why in the plan, say so in the report, exit. What it may never do is **sit
there** having asked something.

### The ways a run goes wrong mid-flight, and the one legal move for each

Each gets one answer, because an unattended run that has to invent one will invent a stop.

- **An action is REFUSED** — a permission classifier declines a `git rm`, a delete, a navigation. **Treat it
  exactly as a BLOCKED detector**: the exact refused command into the plan, and carry on without it. **Never route
  around a refusal** — re-issuing the refused operation in a different form (narrower, reworded, split up, through
  another tool) is taking the decision the refusal made. **The one distinction**: a command refused because it
  broke a STATED rule of this repository (a glob `rm -rf` where the rules require literal names) is replaced by
  the form the rule requires — that is following the rule, not routing around the refusal. If unsure which it
  is, it is the first kind. **Retrying a FAILURE is not routing around a refusal**: a `git worktree remove` that
  ended in `Permission denied` on an empty directory is retried by the same literal path, because nothing
  refused it.
- **The suite goes RED inside a step.** The step does not merge. **Capture the failing test NAME before re-running
  anything** — a failure that was tailed rather than captured leaves no name, and an intermittent failure without
  a name cannot be fixed. Then run that test ISOLATED several times:
  - fails isolated and the step's changes touch its subject → revert the step's changes, hand the finding over;
  - passes every isolated run → a load-sensitive test; re-run the full suite once, and if it is green the step
    merges and the test becomes a plan row for step 2 of the next run, carrying the captured failure message;
  - fails isolated and the step did not touch it → the tree moved under the run; that ends it as a red baseline does.
- **The browser, a navigation or starting the app is denied, or a screenshot times out.** That instrument is
  UNAVAILABLE for this run, named once in the report with the exact call. **Fall back to `profile:render_harnesses`**
  — committed tests that write the surfaces to files — and render those. Never call a step clean that rendered
  nothing.
- **Plan mode switches on mid-run.** It permits editing the plan file only, so no step can continue. **Record it as
  the reason the run ended**, leave no step half-applied (a worktree with uncommitted work is committed on its
  branch and left UNMERGED, and the report names it), deliver the report, and end. Never revert scaffolding
  partially to make room.
- **The session is running out of room.** The likeliest ending, and not a failure. **Finish the step in hand rather
  than opening the next**, and write into the plan which step stopped it and what the next one was.

### How a finding reaches the plan without blocking

**Only a plan-mode round trip reaches the maintainer's plan panel, and it needs approval** — which is a stop.

1. **Resolve the plan at preflight, from `profile:plan_rule` and `profile:plan_known_path`.** If the named file does
   not exist, look for it before concluding there is none: search `~/.claude/file-history/` for the plan's first
   line, and check the other project folders `profile:history_paths` names — a repository that MOVED leaves its
   history under the old path. **If it is still not found, the run continues and the report's FIRST line says
   "plan not found"**, naming every place searched. **Never create a fresh plan file and present it as the plan**:
   a run that did *(illustrative, 2026-11-06)* produced *"what I see is not the original plan"* — a second
   register, the failure every retired handoff file died of.
2. **APPEND to the plan FILE as each step ends.** Durable: if the session dies at step six, five steps' findings are
   on disk. **Append rather than rewrite** — the plan lives outside the repository, so a concurrent write is a
   silent overwrite and there is no reflog to recover the loser from.
3. **Deliver the final report, THEN make the one plan-mode round trip** — in that order. The round trip may block
   for as long as the maintainer is away; a report placed after it was lost behind a pending approval
   *(illustrative, 2026-09-11)*, and the report is the one thing they read. The round trip is where the wholesale
   revision happens — retiring what landed — after reconciling against `git log --oneline` (what landed) and
   `git worktree list` (what another session has in flight). **Every row appended this run must survive it**: a
   maintainer who cannot see the new packages in their panel has been handed nothing.

### What the contract means in this repository

**The plan is in the tree** (`profile:plan_rule`: `.claude/context/plan.md`), so the contract above lands like this,
and nothing in it is relaxed:

- *"Resolve the plan at preflight"* — it is `profile:plan_known_path`; if that file is missing the report's first
  line still says "plan not found", and no fresh plan is created.
- *"APPEND to the plan FILE as each step ends"* — **on the step's own branch, in the step's commit**, never in the
  primary checkout, which stays on a clean `main`. A step with rows and nothing to correct carries them on the NEXT
  step's branch, saying so in that merge subject (*"step 7 — maintainability (with step 6's row)"*), or on a branch
  of its own if no later step opens one. The first run opened a section **Sweep YYYY-MM-DD — handed over** at the
  end of the plan; each run appends its own. git protects it: a concurrent write is a conflict, not an overwrite.
- *"the one plan-mode round trip"* — **there is none**: the rows merged with their steps, and git history is the
  record of what landed. The report is still the last thing the run does.
- *"Fall back to `profile:render_harnesses`"* — none here: nothing is rendered on screen (no UI), so the bullet has
  no instrument to lose.

---

# How a run works

## The order is the deliverable, and a step ends in a merge

**A step fixes what it can and hands over what it cannot. The line is the step's own subject**: a defect inside it
is corrected on that step's branch and merged before the next step opens; anything reaching past it is a work
package, however small. **The order is real work, not a reporting convention**: structure moves files that dead
code and maintainability would otherwise write findings against, and a finding raised before the move names paths
that stop existing. **A step that produced only prose has not finished** — either it found nothing and says so, or
it left work behind.

## Scope: what changed since the last sweep

**Candidate sets are built from what changed since the last sweep's final merge**, not from the whole tree every
time — repeated whole-tree passes over unchanged code are most of why some steps measure clean run after run.

- **The baseline**: every sweep merge subject starts `merge: sweep <YYYY-MM-DD> step <n>`, so the last one is
  `git log --grep='^merge: sweep ' -1 --format='%H %ad %s' --date=short`. Report it. **No marker found → the scope
  is the whole tree, and the report says so.**
- **Changed files**: `git diff --name-only <baseline>..HEAD`.
- **Whole tree regardless, because their subject is not a diff**: step 1's one-word-one-meaning audit, step 2's
  repeated suite runs, step 3 (dependencies age without a commit), step 8's mechanical path check, and any step
  whose detector is newly LIVE this run — a rule switched on since the baseline has never seen the old code.

## Preflight

1. **Clean tree, green build, green suite.** A sweep on a red tree measures nothing. **If it is not green, the run
   ENDS** — write what is broken into the plan, say so in the report, exit. Do not repair it: a sweep that fixes its
   own baseline has changed the tree it was about to measure. **Green is necessary and not sufficient** — step 2
   exists because a suite can be green precisely because some tests cannot fail. **Here: a clean `main`, a green
   RELEASE build (`profile:warnings_as_errors`), and three full suite runs**, which also serve step 2's
   load-sensitivity check.
2. **The profile.** Read `.claude/project-profile.md`; list every key a step needs that is absent.
3. **Detector liveness.** Every detector a step names must be **proven to emit a value on this repository, by
   running it**. Each lands in exactly one state:
   - **LIVE** — ran, produced a parseable value.
   - **UNAVAILABLE** — did not run here, this time. Reported once with the exact failing command.
   - **BLOCKED** — exists, should work, failed for a fixable repo-specific reason. Into the plan with the exact
     command; carry on without it.

   **An ANALYZER RULE is a detector, and a rule that is off reads exactly like a rule at zero.** For every rule ID a
   step names, read its effective severity from `profile:analyzer_config` first. A rule that is not configured on
   is UNAVAILABLE until a probe proves otherwise: in a THROWAWAY worktree, enable it at `warning`, add a file that
   breaks it, build, confirm the ID appears, discard the worktree. **Probe the SHAPE the rule documents, not a
   guess at it** — CA1065 permits `InvalidOperationException` in a property getter, and RCS1236 fires on
   `if (!(e is X)) throw;` but not on `if (e is X) return; throw;` *(illustrative, both measured 2026-11-06)*.
   Two rules were reported "measured" for five runs while switched off *(illustrative)*.
   **The NuGet audit is a named detector**: `dotnet list package --vulnerable --include-transitive` and
   `--deprecated`. It found a High-severity advisory a sweep was not looking for *(illustrative)*.

   **An inferred zero is not a measurement, and neither is an unexamined non-zero.**

   **The states known here** (re-probe a rule at `warning` with zero sites only if the SDK pin in `global.json` or
   an analyzer version in `Directory.Packages.props` moved since 2026-11-07, when each was proven to fire):
   - **LIVE**: CA1031, CA2000, CA2016, CA2201, CA2008, CA1065, CA2219, the Security category, RCS1236, RCS1075,
     RCS1211, CA1502/CA1505/CA1506, VSTHRD*; the NuGet audit (`--vulnerable --include-transitive`, `--deprecated`,
     `--outdated` — from a worktree, since it restores); **`jb inspectcode`** (ReSharper global tool, installed):
     `jb inspectcode CrmWorkflowExtractor.slnx --output=<scratch>/inspect.sarif --format=Sarif
     --severity=SUGGESTION --no-build` — a few minutes, so start it in the background at preflight.
   - **Probe CA2000 with a StreamReader or a plain IDisposable**, never `FileStream s = new(path, FileMode.Open);
     return s.Length;` — a shape it does not flag, which had it recorded as BLOCKED until P5 (2026-11-07).
   - **UNAVAILABLE** unless installed since: coverage (`profile:coverage_command`: none), Stryker. Never installed
     by an unattended run.

## Derive, never cache

**Derive every list at the start of every run, from the tree, and write none of them down** — entry points, type
counts, never-touch lists. Every cached list decays; one *(illustrative)* named a hosted service that had never
existed, which makes live code look dead.

The ways a derivation goes quietly wrong — shapes that look DEAD and are alive. Check every one before believing a
"no caller" result:

- **A folder is not a role**: `Stages/` holds the records a stage passes on as well as the stage; never promote a
  type by folder.
- **Reached by convention, not by name**: an xUnit test class or `[Fact]`/`[Theory]` (discovery), `Program.Main`,
  a `[GeneratedRegex]` partial (the generator writes its body).
- **An EXTENSION-METHOD HOLDER**: callers name the METHOD, never the type. **Count members, not the type.**
- **Positional records serialized to JSON** (`RunManifest`, the readers' records): ReSharper reports ~68
  never-accessed positional properties; they are written to `manifest.json` and `ham/` and stay.
- **Test classes and fixtures**: filter test classes out before reading anything, and exclude
  `profile:fixture_excludes` — recorded evidence is uncalled by design.
- **The type inventory is the file listing ONLY when `profile:one_type_per_file` is enforced.** It is not here, so
  enumerate types by parsing declarations, or files holding several types shrink the candidate set silently.
- **Read what the matches ARE, not how many there are**: a doc comment, `plan.md` or a context file naming a member
  in prose pads a count without being a caller.

*(No DI container and no markup exist here — `profile:markup_globs` and `host_types` say why — so Yalbuz's builder-
registration and markup traps have nothing to catch.)*

## What a finding looks like

**Hand over a DECISION, never an inventory.** Triage before the maintainer sees anything: read each site, mark it
take-or-decline with its reason, and lead with the count that SURVIVED.

**Each triaged item lands in exactly one place:**

- **Corrected in this step** — inside the step's subject, the repair unambiguous, the suite proves it. The commit
  body is the record.
- **A work package in the plan** — needs a judgement the sweep cannot make, or reaches outside this step's subject.
- **A settled decision** (`profile:settled_decisions`) — refused on evidence, with the measurement and what would
  re-open it.
- **An item on `profile:offsite_register`** — sound, but the CHECK that would settle it needs a network or machine
  this session cannot reach: here, the CRM server or a production export. Give it a Do, a Pass and a Capture.

**When it is not obvious, it is a package rather than a correction.** Narrowing scope to avoid a decision counts as
deciding, and widening it to avoid asking is the same move in reverse. **Neither is a reason to ask.**

Each item handed over carries: **What** (one sentence); **Where** (a path and line that resolve today); **Why it
is not the obvious thing**; **What would falsify it**; **Origin: `finding`, Status: not yet committed to build** —
and a **Recommendation**. **Those fields are for a handed-over item, never a commit body**, which `/wp-finish` caps
at about 1,500 bytes: a step that corrected a dozen sites writes what changed, why, and the evidence ONCE.

**A measured "nothing here" is a complete deliverable.** It opens no branch and writes no plan row; its record is
its report line. **Every number states what produced it** — which projects, which analysis mode, which scope.

---

# The ten steps

## 1. Structure and naming
**First, because every later finding is written against the tree this step would move.**

*Do not re-detect* anything in `profile:enforced_guards` — the build or suite already refuses it, and reporting it
is noise. **But only what the profile lists**: a convention the profile does not name as enforced is checked here,
not assumed.
*The candidate set*: **`profile:structure_doc`'s placement rule**, read against its own project table, over the
types in scope — `Cli` → everything, `Bpmn` → `Ir`, `Ir` and `Extract` → nothing of ours (read the
`ProjectReference`s), each type in the project that owns its subject; and **`profile:naming_rule`** — names a reader
would have to open the file to understand, above all a name still defined by something removed (the network path,
similarity, consolidation, `raw/`). A rename is a choice of word, so it is a ROW with a recommendation, unless it is
a private member with one obvious name. Either key absent → that half is UNAVAILABLE, never clean.

**One word, one meaning — whole tree, every run.** A concept name is any word that recurs across project folders,
namespaces, top-level folders and type stems. For each one used in more than one place, **write down what each use
MEANS, read from the code it names and never from the name itself**: what goes in and what comes out. Two different
meanings are a finding, and so is a meaning that clashes with the term's settled meaning in a framework or tool the
repository itself uses. *(Illustrative, 2026-11-21: "analyzer" meant a Yalbuz extractor in `Analyzers/` and a Roslyn
diagnostic analyzer in the build's own analyzer packages, and three folders named `Analysis` each meant something
else.)* **The sweep never renames a concept itself.** A rename is a judgement, it reaches across every layer and often
into other repositories, and this run is unattended. So **the finding IS the question, written as a plan row**:
- each use, with the meaning the code gives it;
- two or three candidate names, each with the outside meaning that supports it;
- the measured cost of each: hits and files, and every OTHER repository that names the concept;
- a recommendation, and what would change it.

Names in `profile:adopted_names` are taken from an upstream's own convention and are not candidates.

**Projects that share a dotted prefix are grouped, and this one IS corrected.** When two or more project folders at
the same level are named `X.<A>`, `X.<B>`…, and no project folder is named `X`, they move to `X/<A>/`, `X/<B>/`, and
the test folders mirror them. That is a pure move: a folder path read with dots for slashes is the same name, so no
namespace changes. **Not when `X` is itself a project** (`App` beside `App.Tests` stays as it is). The repair, in order:
1. `git mv` every project folder and its test folder;
2. rewrite every relative path that crossed the move (project references, the solution, path-scoped
   `.editorconfig` sections) as LITERAL string replacements, never a regex over backslashes;
3. update the documents that state where the code lives. A historical record keeps its old path.

**Before believing the green suite, prove each guard still finds the moved projects**: `profile:project_discovery`
names the guards that find projects. A guard that reads only a path's FIRST segment, or matches folders by name,
selects nothing after the move and passes vacuously. Put the old discovery back and watch the guards go red, as with
any new test. Every other repository that references a moved path (a pinned submodule, a solution file) goes in the
report and the plan, with the exact path its next pin bump must change. **Here**: `profile:adopted_names` carries a
note on the `Crm.` prefix — read it before moving anything.

## 2. Test-suite health
**Second, out of blast-radius order, deliberately: every step after this leans on the suite.**

*The rule it enforces*: a test asserts a requirement, never a preference — *would a redesign nobody would call a
regression turn this red?* Over every project in `profile:test_projects`, excluding `profile:fixture_excludes`.

*The candidate set, derived — searches, not judgements:*
- **A test that cannot fail by construction** — no assertion; the only assertion inside a loop that can iterate
  zero times; a literal compared with itself. **Every assertion inside a loop whose collection a regression could
  empty** — the layout and readability tests are this shape. The repair: assert the collection first — non-empty,
  or **at least two items where the test compares PAIRS** (one caption has nothing to overlap). Falsify by
  suppressing what the writer emits (rename the emitted element) and watch them go red.
- **A preference-assertion** — a hex colour, a px value, a ratio, an exact layout coordinate. A RELATION (no overlap,
  no crossing, a box holds its text) is a requirement; `Assert.Equal((130, 70), …)` is a preference — compare
  against the ordinary case.
- **An unbounded wait** — no timeout, no cancellation (`.Wait()`, `.Result`, `Thread.Sleep`, `Task.Delay`). It hangs
  the suite instead of naming an assertion.
- **A name that no longer describes the body** — the tell for a test edited until it passed.
- **Load-sensitive tests** — run the WHOLE suite at least three times (preflight's three runs count); any test that
  fails once is captured by name and then run isolated ten times. Passes isolated, fails under load → a timing
  assumption, handed over with its failure message. Any `load_sensitive` rows the plan already carries are
  re-checked first. Method count is not test count: `[Theory]` cases expand.
- **Uncovered production code** — run `profile:coverage_command`; absent → UNAVAILABLE. **Prove it is LIVE in
  preflight by checking the report exists and the run executed tests**: a collector the runner does not load can
  run zero tests and still look like a coverage step *(illustrative: a VSTest collector under Microsoft Testing
  Platform, 2026-11-07)*. An uncovered branch in changed code is where a test is missing; an uncovered MEMBER with
  no test caller is handed to step 4 as a dead-code candidate.

*Then revert-confirm-red the survivors*, each against the ACTUAL defect it claims to catch. **Mutation testing
(Stryker.NET) automates exactly that**, scoped to changed files; it is a third-party download, so it runs only if
already installed and is otherwise UNAVAILABLE — never installed by an unattended run.

## 3. Dependency hygiene
**After test health, because a version bump changes runtime behaviour and needs a suite already shown to fail when
it should. Before everything that reads an analyzer**, because an analyzer package bump changes what steps 6 and 7
measure.

- **Vulnerable and deprecated** — from preflight's audit. A vulnerable package is a correction if a fixed version
  exists in the same major; otherwise a package.
- **Skew** — every version is in `Directory.Packages.props` and a version on a `PackageReference` is NU1008, so
  per-project skew cannot be written here; what is left is framework packages (`Microsoft.Extensions.*`) at mixed
  servicing versions. They are ONE patch, the installed runtime's (`dotnet --list-runtimes`); aligning to it within
  the same major is a correction.
- **Floating toolchain** — no `global.json` means the SDK, and with it every analyzer count, differs between
  machines; no `Directory.Packages.props` means skew has nowhere to be prevented. Both exist here; a change that
  removes or loosens either is a package, not a correction.
- **Outdated** — `dotnet list package --outdated`. Patch and minor updates within the current major are
  corrections, the whole suite as proof; a MAJOR update is a package with the breaking-change notes named — or the
  existing row re-confirmed, never a second one.
- **`dotnet list package` restores**, which rewrites gitignored `obj/` folders — run it from the step's worktree,
  not the primary checkout.

## 4. Dead code
Needs the net step 2 just checked. **Refactor residue is the subject** — a helper left behind by a change, a seam
nothing uses, a property written and never read, a parameter nothing uses, a name that stopped being true. No
analyzer finds it.

**The MAIN pass is member-level, production references only.** A count that is solution-wide answers *is anybody
using this?* when the question is *is the PRODUCT using this?* — and those differ by exactly the class this step
exists to find. **A production member whose only caller is a test is dead production code.** Count production
(`profile:production_projects`) and test references separately, never as one number. The type-level singleton pass
is the cheap second pass, not the first: in the runs it led, it found nothing four times while member-level triage
found the real cases *(illustrative)*.

**Read the build's neighbour, ReSharper**: from preflight's SARIF, production files only: `UnusedMember.*`,
`UnusedAutoPropertyAccessor.*`, `UnusedParameter.*`, `NotAccessedVariable`, `RedundantAssignment`. Each is a
candidate, re-checked by search with production and test references counted apart.

**Residue vs a deliberate test SEAM: the seam says so** — its name (`…ForTests`) or its doc comment names the test.
Residue claims a production purpose and has no production caller. **Read the name and the doc comment before the
count**, then put every candidate through *Derive, never cache*.

**A computed fact nobody reads is not automatically residue**: if it is the run's only record of something a
reader would want, it is a row *(illustrative: `CallGraph.MissingTargets`, 2026-11-07 — the fact turned out to be shown elsewhere, and P2 removed it)* — show it or delete it.

**Signature extraction has a hole**: a member whose RETURN TYPE contains parentheses — any tuple — defeats a
`…Name(` pattern. Take the FIRST identifier followed by `(` on the declaration line, not the last token before it.
After removing, the Release build names any `using` left behind (IDE0005 is an error there).

*Before configuration, because removing code is what orphans a configuration knob.*

## 5. Configuration knobs
**Triggered, not routine.** It runs when step 4 removed code, or when a validator, a range check or an options type
(`Crm.Cli/Configuration/ExtractorSettings.cs`) changed since the baseline. Otherwise its line says *not triggered*
and names the check that decided it.

When it runs: demote or remove what step 4 orphaned, and review **validation DESIGN** — what should be refused,
where, and which behaviour each check takes, per `profile:config_behaviours`: **the behaviour is chosen by what is
LOST, never by how wrong the value looks**. Not which keys exist — `profile:config_coverage_test` holds that. Here
`config_behaviours` is none (a missing or wrong file stops the run with exit 2), so the review is short: every
property of `ExtractorSettings` read by production code outside `Configuration/`, and present in `appsettings.json`,
the `Program` constants block and `ConfigurationTests`.

**Never decide anything from a committed placeholder configuration file.** Read `profile:effective_config` — here
`Crm.Cli/appsettings.json` IS the effective configuration, because nothing is substituted into it.

## 6. Robustness and resilience
The audit half of `csharp-refactor-harden`. The surface is settled by now, so a guard goes where it belongs.

**First, read the build**: every robustness rule that `profile:analyzer_config` holds at `warning` starts at zero
and needs no search — a non-zero is new and the build names it. Preflight proved which are LIVE. **Then the shapes
no analyzer catches:**

- **Exceptions that are lost, not thrown.** A task discarded with `_ = SomethingAsync()` — `VSTHRD110` ignores the
  discard deliberately, so no rule sees it. For each site, read what the discarded method CATCHES: if it catches
  only listed types, everything else lands in a task nobody observes. `Task.Run` whose result is dropped is the
  same shape.
- **Global handlers, per host type** (`profile:host_types`: console). Console: `AppDomain.CurrentDomain.
  UnhandledException` before anything starts — or, as here, **a last-resort catch around the run**:
  `ExtractionRun.RunAsync` ends an unlisted exception in a sealed folder marked failed, with its stack in the run
  log (P1, 2026-11-07, which closed sweep row S6). Re-confirm it still wraps the whole run; an exception that
  escapes it leaves `Main` as a runtime crash. **A missing handler is a correction only when the project already has a logging path to
  route it to**; otherwise a package.
- **The guards that make one failure cost one item**: the per-workflow guard in `BpmnStage`, the per-map guards in
  `StageMapStage`, `IrStage`'s parse guard, `ExtractionRun`'s run-level filter. A broad catch is legitimate ONLY
  as one of these, filtered `when (error is not OperationCanceledException)` or by listed types; **a broad catch
  outside a genuine boundary** is a finding. **A cancellation is the operator's and must travel.**
- **A missing timeout or cancellation check**, **an undisposed `IDisposable`** (CA2000 is LIVE here, so read the
  build first), **input reaching a query or a path unvalidated** — a slug is a FOLDER name as often as a file name
  (`BpmnFileNames.Slug`).
- **Text into XML**: every string that reaches a writer goes through `DocumentText.Writable` (the BPMN serializer
  sweeps the built document); a new writer that does not is a finding.
- **Configuration is input, and this step reads it EVERY run** — whatever step 5's trigger decided, because step 5
  reviews validation DESIGN when something changed, and a gap that never changed is invisible to a diff. For every
  knob an options type reads, find the check that decides whether it is set and usable. (`profile:placeholder_rule`
  is none: nothing is pipeline-substituted, so there is no marker to accept by mistake.) **A silent clamp
  (`Math.Max(1, …)` where the value is used) is a finding**, and **the same kind of key checked two ways in two
  places is a finding by itself**.
- **The reviewer-carried robustness rules** in `profile:reviewer_carried_rules` — exception filters over
  catch-and-retest is the usual one, because **a `when` filter that does not match never unwinds the stack**.
  Where an analyzer covers only some spellings of a rule, search for the others: rule 9's inverted half, which
  `RCS1236` misses, is a `throw;` preceded by `if (error is X) { return; }`.
- **The browser scripts** (`tools/*.js`) are production code no analyzer reads, and the ONLY code that touches
  CRM. They send GET only; every read that can be refused goes through the guarded path, so a refusal SAVES the
  file with the reason (the 2026-10-12 production failure was the one unguarded read); ids in error text are
  blanked; process-mining output carries no case, customer, user or exact time. **A script edit regenerates its page
  — `node tools/build-export-page.js` — in the same commit**, or `BrowserExportImportTests` fails on the SHA.
  Whatever a script change needs to be proven against CRM is an offsite item, never "verified".

## 7. Maintainability and performance
The audit half of `csharp-refactor-streamline`. **Late, because it operates on code whose shape and guards are now
settled.** Its ordering is this skill's own; the robustness veto under *Rules that bind every step* is where the
two refactoring skills agree.

**A performance finding needs a measurement**, and two need no profiler: **a stated complexity change**
(`O(n·m)` → `O(n+m)`, named in the commit) and **counters** (`dotnet-counters`, `dotnet-gcdump`) for allocation.
The layout's measured quantities (crossings, longest arrow, box count on the Reklam-shaped fixture) are the only
performance numbers this tool carries. Anything else is speculation and is dropped.

**Complexity detectors**: `CA1502`/`CA1505`/`CA1506` are at `warning` and at zero, with no suppression in the tree —
lifting one site's suppression and building makes the analyzer state the number. **Never `cut` a warning line
before deduplicating it**: one site can carry two codes. `Microsoft.CodeAnalysis.Metrics` is UNAVAILABLE on modern
projects (5.6.0 ships `Metrics.exe` without its BuildHost; 3.3.4 cannot load a modern project); under MSYS,
`/t:Metrics` is path-mangled — use `-t:Metrics`.

**From the SARIF, as leads to read, not corrections to apply**: `DuplicatedStatements`, `RedundantCast`,
`MemberHidesStaticFromOuterClass`. `PatternAlwaysMatches` on a nullable tuple deconstruction
(`is (Guid, string name)`) is a NULL CHECK and a false positive; a cast that types a switch arm is not redundant.

**A suppression justification that STATES a number the analyzer computes is a cached derived value**, and
re-stating drifted numbers took most of this step's corrections in eight runs *(illustrative)*. **The correction
is to remove the number**, keeping the reason — the analyzer states the current figure whenever the suppression
is lifted. That is a mechanical sub-step, done first and fast, so the step's time goes to maintainability.

## 8. Context-document drift
**After every step that moves code, before the user-facing words**, because steps 1–7 change what these documents
describe. Over `profile:context_docs`.

- **Mechanical, whole tree**: every backticked repository path must resolve. Triage each miss before calling it
  drift: **gitignored** (`git check-ignore -q <path>`) — expected; **deleted on purpose and named as history** — the
  surrounding sentence says so; **relative to a folder the sentence names** — resolve it there. What is left is
  drift, and the fix is the path the file moved to (`git log --follow --diff-filter=R`). The count is never the
  finding — the first run here had 120 misses and no real one. Expected misses in this repository:
  - run-output names (`ham/`, `bpmn/…`, `raporlar/`, every `.xlsx`, `.docx`, `.bpmn`, `rapor.md`, `manifest.json`);
  - bare file names relative to a folder the sentence names (`reference/…` is under `.claude/context/`);
  - `plan.md`'s DATED entries — they are history and are never rewritten to match today;
  - `settled-decisions.md`'s paths, which are Yalbuz's by its own preamble;
  - a `path:line` — the file must resolve and the line must still be the thing named.
- **Claims of STATE**: a sentence saying a rule "is off", "is worth its own package", "has not been split yet", or
  stating a count, a network or a similarity claim. Read each against the file that decides it. **A claim decays by
  being OVERTAKEN, not contradicted** — the code moved and the sentence stayed true-looking — so nothing else will
  notice. Doc comments in code are in scope when they describe what the code no longer does.
- **This skill and the profile are in scope.** The profile's paths are checked like any other.
- **`profile:offsite_register` is the maintainer's checklist**: report drift there as a row, never rewrite it.

### 8b. Architecture claims, against the code
**The half of this step that is cheap to skip, and was.** A run reported step 8 clean; an audit straight after it
found ~40 wrong current-fact claims across eight architecture documents and one live code regression that a
corrected sentence exposed *(illustrative, 2026-11-13)*. A path check cannot see any of it: every path resolved,
and the sentences around them described code that had moved. **So this sub-step is not optional, and it reports
what it READ, never only what it changed.**

*The documents*: `profile:architecture_docs`. `profile:architecture_doc_code` maps each to the code it describes.
Either key absent → `UNAVAILABLE (profile: architecture_docs)`, never clean.

*The scope is the doc's CODE, not the doc's diff* — a claim decays because the code under it moved, so a document
untouched since the baseline is exactly the one most likely to be stale. A document is in scope when any path
mapped to it appears in `git diff --name-only <base>..HEAD`, where `<base>` is the LATER of the sweep baseline and
`profile:architecture_audit_baseline` (the commit at which every document was last checked whole). No audit
baseline recorded → every document is in scope: the whole-tree pass. Report the in-scope list and, for each, the
changed path that put it there.

*The method — parallel readers, then confirmation:*
1. **Split the in-scope documents across readers** (the Agent tool), about 3 documents or 600 lines each, a
   heavy document alone. Each reader gets: its documents, their mapped code paths, and this brief —
   *check every sentence stating a CURRENT fact (a type, member, route, key, count, default, order, which class
   does what) against the source; skip history explicitly dated as such; return for each wrong claim the doc line,
   the claim, the `file:line` that contradicts it and a proposed wording; return for each GAP — code a reader of
   this document now needs and finds nothing about — the code and a proposed paragraph; and return a list of the
   claims you verified TRUE.* The TRUE list is the coverage evidence: a reader that returns only corrections has
   not shown it read the rest.
2. **Confirm every correction yourself before editing** — open the cited `file:line`. A reader's finding is a
   candidate, never a measurement — the same rule as any other detector's output.
3. **Two passes, in this order.** *Corrections* — the code contradicts the sentence: rewrite it to the code,
   keeping the document's voice and any history the sentence carries. *Gaps* — add the paragraph where a reader of
   that section would look, at the document's existing depth. A gap is a description, not a design note.
4. **A CODE defect found on the way is not this step's to fix** — its subject is documents. It goes to the plan
   as a finding with the five fields, and the document says what the code DOES, not what it should.

*What stays out*: documents whose claims are facts about an EXTERNAL system (only a measurement against that
system moves them) and documents that are history by design — the profile excludes them by name, and a reader is
never given one.

*Cost, stated so the run can budget it*: a whole-tree pass over eight documents took three readers about 500k
tokens and five minutes *(illustrative)*. Here there is one document, `structure.md`, dense enough to be a heavy
document alone. A run short on room does the in-scope documents with the most changed paths first and names the
rest.

*The report line*: documents in scope (and why), claims verified true, corrected, gaps filled, code defects handed
over. **After a WHOLE-TREE pass, and only then, set `profile:architecture_audit_baseline`** to the commit the
pass read, on the step's branch. A scoped pass leaves it alone: the documents it did not read are no fresher.

## 9. The user-facing docs, in every culture
**The words a reader meets** — `profile:user_docs` in `profile:cultures`. Late because it describes what every step
above would change. Here: `Crm.Cli/Reports/AnalystGuide.cs` (the guide) and `Crm.Cli/Reports/Guides.cs` (every
workbook's *Nasıl okunur*). `rapor.md`'s writers are internal and out of scope. `DeliveryTests` and
`AnalystGuideTests` already refuse any internal file named in a delivered one; do not re-detect that.

1. **Structure and order** — does the section sequence follow how somebody approaches the product — what the
   package is, what to open, one workflow walked end to end — or where each feature happened to be added?
2. **Wording** — concise and plain; cut a sentence that restates its heading and a qualifier that rules nothing out.
   Apply `profile:wording_rules`, and **search for the CONSTRUCTION a rule forbids, across every string, not the
   paragraph somebody complained about last time**: `grep -nP '(?<!İş |iş |İŞ )\b(akış|Akış|süreç|Süreç)'` over
   both files. Read each: a workflow becomes "iş akışı" WITH its case ending (`akışı` → `iş akışını`, `akışın` →
   `iş akışının`, `süreçtir` → `iş akışıdır`). **Keep**: "süreç" as CRM's umbrella for every process definition
   (dialogs and business rules included), CRM's product name "İş Süreci Akışı", and "akış" meaning a DIAGRAM's flow
   ("akışı soldan sağa izleyin", "biten akış"). A stage is "talep aşaması", a diagram of one "aşama haritası", an
   entity "kayıt türü"; a stage's assigned user and a role's holders are never named. A change to what the guide
   SAYS adds a row to its revision table (`AnalystGuide.Revisions`); its disclaimer is never weakened.
3. **Every culture, together** — does not apply: `profile:cultures` is `tr` alone, so there is no second text to
   compare as prose.

## 10. UI drift, against the register — does not apply: the tool has no user interface, only files it writes (profile: `ui_register` = none)

---

# Rules that bind every step

**Order is not priority.** Robustness outranks maintainability and performance wherever they meet, by veto:

> No simplification or optimization may delete a failure path, widen a `catch`, drop or lengthen a timeout, remove
> a cancellation check, or make a previously-isolated failure propagate.

A step-7 finding that would do any of those is recorded `waived (robustness)` and dropped. The mirror holds: a guard
from step 6 that pushes a method past readable size is a named cost, not a free win.

**Check the INSTRUMENT before believing what it says.** A probe is written in a minute and is wrong as easily as
the thing it measures, so **a confident negative is the shape to distrust first** — read WHAT a search matched
before concluding anything from HOW MANY. A search once concluded four skills did not exist because it never
looked in the plugin directory; a bare ` error ` grep scored 8 on a build that exited 0 by matching a parameter
name inside localized warning prose *(both illustrative)*. The build output here is Turkish (`hata`/`uyarı`,
`Başarılı`, `Başarısız`): grep `: error ` and `^(başarısız|failed) `, never a bare word.

**Never re-raise a settled decision.** `profile:settled_decisions` carries what earlier runs ruled. A ruling is
re-opened by a new fact, never by an argument. Where a context document and a settled decision disagree in tone,
the decision is the ruling and the document is drift — step 8's.

**Co-located products** — none here (`profile:co_located_products`): every project is this tool's.

**Line endings are `profile:line_endings`.** Under MSYS, `sed -i` rewrites every line it reads; repair and check
lines == CRs after any use on a CRLF tree. Make and unmake a multi-line break with an editor tool, never `sed`
or `perl -0pi`.

**Bytes that must not move**: `Crm.Tests/Fixtures/**` (evidence, LF) and the six `tools/` files (`-text`,
SHA-checked). Never edited to make a finding go away.

**Scripted edits** (node, from the scratchpad): split on `\r\n` and rejoin, and fail when a match count is not
exactly one. A pattern holding a backslash (a regex inside C#) does not survive a heredoc into JS — make that edit
with the Edit tool.

**Never `git checkout -- <file>` to undo an experiment**: it restores `main`'s version and silently drops the step's
own uncommitted edit to that file (it did, once, in the first run here). Copy the file to the scratchpad first and
copy it back.

**Never run `Crm.Cli` against a real export from a worktree**, and nothing here can reach CRM: anything that needs
it is UNVERIFIED and becomes an item on `profile:offsite_register`.

---

# Finishing

**Each step with something to correct opens its own branch with `/wp-start`**, is verified through `/wp-verify`,
closed through `/wp-finish`, merged, and deleted; then the next opens. **A step with nothing to correct opens no
branch** — unless it carries plan rows and no later step opens one (*What the contract means in this repository*).

- **Branch `sweep-<n>-<subject>`; merge with `git merge --no-ff -m "merge: sweep <YYYY-MM-DD> step <n> — <subject>"`.**
  The subject prefix is how the NEXT run finds its baseline; a merge without it — a fast-forward has none — makes
  the next run's scope the whole tree.
- **`/wp-start` runs from the PRIMARY checkout**, on a clean `main`; return there between steps, and merge there.
- **`dotnet build-server shutdown` before `git worktree remove`**, from the primary checkout, or the build host
  holds the directory. **A husk — an EMPTY directory left by a removal that reported `Permission denied` — is
  retried by its literal path, not reported**; one still holding files is the build host's, and is left and said.
- **The throwaway worktree** used for probes and `dotnet list package` is removed at the end with its branch.
- **Never `EnterWorktree`**: it branches from `origin/main`, routinely behind local `main` because a session never
  pushes, and it cannot be called twice in one session.
- **Corrections travel in the step's own commits**; everything handed over is an ordinary work package; anything
  refused on evidence goes to `profile:settled_decisions`.
- **When the last step closes**, every row is already in the plan on `main`, merged with its step — there is no
  closing round trip here (`profile:plan_rule`).

## The report, which is the one thing the maintainer reads

One message, at the end, in a fixed shape so two runs can be compared:

- **The run's own facts, first**: the plan path resolved (or **"plan not found"** as the first line, with every
  place searched); the scope baseline (commit and date, or "whole tree — no marker"); each detector as LIVE,
  UNAVAILABLE or BLOCKED; every profile key that was absent; anything REFUSED, with the exact command.
- **One line per step, all ten, in order** — a step that found nothing still gets its line, saying **what it
  measured**. Each says which it did: corrected (the commit), handed over (the plan row), refused on evidence (the
  settled entry), measured clean, not triggered, does not apply (step 10), or UNAVAILABLE (the key or the
  instrument).
- **What the maintainer has to do** — the rows needing a decision, each with its recommendation already in the
  plan, and anything now waiting on `profile:offsite_register`.
- **What was left behind** — a husk, an unmerged branch, a step that ended the run early and why.

**A recommendation is not permission**: nothing in the report is a request to act; it is a record of what was done
and what is now the maintainer's.
