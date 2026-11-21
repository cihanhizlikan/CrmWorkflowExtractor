# Settled decisions

**What this file is** (`profile:settled_decisions`). Two parts:

- ***This repository's decisions*** — every ruling in force, moved here from `plan.md` on 2026-11-21. Dated
  history by design: a ruling is never rewritten to match today, and is re-opened by a new fact, never an argument.
- ***Imported from Yalbuz*** — exactly two entries, WORD FOR WORD from Yalbuz's `settled-decisions.md` (2026-11-21),
  because `working-agreement.md` adopts the rules they explain: *How the two-session workflow was arrived at* and
  *How the working agreement was arrived at*. Nothing else from that file was imported. **Nothing in that part is a
  rule, and nothing in it is about this repository**: the incidents, names and paths are Yalbuz's (its HOPEX sink,
  its web app, its office log), kept as written because a rule whose reason is lost gets re-argued. A path in them
  that does not resolve here is Yalbuz's and is not drift.

# This repository's decisions

**Every ruling in force, in one place** (`profile:settled_decisions`). Moved here word for word from `plan.md` on
2026-11-21 (maintainer: consolidate), each under the plan section it stood in; the plan keeps a pointer where each
was. A ruling is re-opened by a new fact, never by an argument. **Some are superseded and say so where the reversal
is recorded** — the similarity and consolidation rulings of 2026-09-14/15 were reversed on 2026-10-09 (`CLAUDE.md`).
A new decision is added here, dated, in the commit that takes it.

## How packages land (plan head, 2026-11-07)

Each runs `/wp-start` → `/wp-verify` → `/wp-finish` and is merged and deleted as it lands (**DECIDED 2026-11-07**,
maintainer: "merge & delete as you go").

## Deferred (plan head, unchanged since 2026-09-15)

**Deferred, DECIDED 2026-09-15 and unchanged:** flag-field plug-in inference (§4.3), the `uidata` spike (§4.5),
resuming an unsealed run. BPF stages as `subProcess` and Dialog pages as `userTask` stay unmapped until a real
sample exists to write them against.

## A JavaScript test harness for `tools/` (plan head, decisions open)

1. ~~A JavaScript test harness for `tools/`~~ — **DECIDED 2026-11-07** (maintainer: "start implementing work
   packages one after the other"): `node` run from xUnit, no npm. Built differently from the recommendation: the
   script is not split into exported functions but run WHOLE against a fake CRM, so the file a user pastes is the
   file tested, and every request it makes is checked to be a GET.

## The prototype's scope (prototype plan, 2026-09-15)

**DECIDED 2026-09-15:** build the bare-minimum plausible prototype of all steps rather than stopping at M1; each
package verified, merged and its branch deleted as it lands.

**DECIDED 2026-09-15 — deferred for the prototype:** flag-field plugin inference (§4.3), `uidata` spike (§4.5),
`cluster-lineage.csv`, resuming an unsealed run. BPF stages as `subProcess` and Dialog pages as `userTask` are NOT
mapped: no credible XAML sample exists to write them against; they surface as unmapped in `parse-coverage.md`.

## The handout's `csharp-*` skills (prototype plan, pre-flight facts, 2026-09-14)

| Check | Result |
|---|---|
| `csharp-*` skills from the handout | Not installed. Replaced by Yalbuz's workflow — **DECIDED 2026-09-14.** |

## Decisions taken (prototype plan, 2026-09-14/15)

- **DECIDED 2026-09-14:** adopt Yalbuz's workflow; all 13 style rules; repository at `repos\CrmWorkflowExtractor`.
- **DECIDED 2026-09-14:** the tool combines each similarity family into one workflow, like a set union; the
  per-workflow BPMN files remain so a human can check the combination. No writes to CRM, no deletion anywhere.
- **DECIDED 2026-09-15:** download the OMG BPMN 2.0 XSDs (done; embedded unmodified).
- **DECIDED 2026-09-15:** low-cohesion families are NOT combined; they are listed as "split first".
- **DECIDED 2026-09-15:** merge and delete each package branch as it lands.

## Browser export instead of the tool's own sign-in (2026-09-22) — DECIDED (plan History)

- **DECIDED (maintainer):** option B (AD FS registration) is not practical; the data is read through the maintainer's
  own signed-in browser session instead, and the tool works offline from the saved file.
- **Built:** `tools/crm-browser-export.js` (GET-only, paged, same columns and payloads as the tool) and
  `tools/crm-export.html`, a bookmarklet page generated from it by `node tools/build-export-page.js`.
  `Run:ImportFile` turns the export into the same `raw/` layout a network run writes; every later stage is unchanged.
- **Why a bookmarklet, not an HTML page that fetches:** a page on claude.ai or opened from disk is a different
  origin; the browser blocks it from reading CRM with the user's session (CORS, cookies). Only a script running on a
  CRM page itself can.
- **Verified:** the committed script ran in a browser against a local mock of the Web API; its output is the fixture
  `Crm.Tests/Fixtures/BrowserExport/mock-crm-export.json`, imported end to end by the tests. **Unverified:** the real
  server (page size behaviour, `$count`, privilege names) — same items as *Acceptance B*.
- **Provenance is weaker** than a network run: the tool copies the export verbatim to `raw/browser-export.json` and
  records its hash, but did not make the requests. The manifest carries a warning saying so.

## Usage evidence and drafts (2026-09-23) — DECIDED (plan History)

- **Maintainer:** keep draft/test workflows apart only if there is a *guaranteed* way to know that they are unused.
- **What is guaranteed:** a Draft definition cannot start new runs, and a logged run proves use. Nothing proves
  non-use: System Jobs are deleted, real-time workflows log only failures, and business rules are never logged.
- **Built:** Draft definitions (by `statecode`, never by name) are held apart from similarity grouping and
  combining. They keep their IR and BPMN, are listed in `clusters/drafts.csv`, and have their own link in the
  count chain. Activated workflows with test-like names stay in the grouping and are flagged.
  `tools/crm-usage-export.js` (bookmarklet `tools/crm-usage.html`, GET only) records the last logged System Job of
  each activation, the last dialog session of each dialog, and how far back the logs reach. `Run:UsageFile` makes it
  run evidence (`raw/usage-export.json`, which a reprocessed run inherits), and it feeds `reports/usage.md` /
  `usage.csv` and the `last_logged_run` column of `clusters.csv`.
- **Unverified on production:** that the `asyncoperations` / `processsessions` filters answer in reasonable time on
  a large System Job table.

## Dialogs and business rules to BPMN (2026-09-23) — DECIDED (plan History)

- **Maintainer:** convert Dialogs and Business Rules too; the analysis needs as much information as possible.
- **Built:** a dialog page → `userTask` (its prompts as `Prompt1.*`, `Prompt2.*` arguments); a dialog query →
  `serviceTask` (the entity recorded as read); a child dialog → `callActivity`. A business-rule action (show/hide,
  required level, lock/unlock, set value, default value, error message) → `businessRuleTask` naming the action.
  Every argument is captured verbatim, with no guessed meanings.
- **Uncertain:** the construct *names* come from the production coverage report; their *markup* (which attributes,
  element or ActivityReference form, where prompts sit) is guessed, so the fixtures are marked that way. The
  reprocess run's parse-coverage.md is the check. If constructs are left over there, the next step is a
  structure-only XAML sample (values stripped), which the maintainer reviews before it leaves the company.
- **Not done:** Business Process Flows (23), which were not requested.

## Structure for the analysts (2026-09-23) — DECIDED (plan History)

- **Maintainer:** 1437 BPMN files are a lot; the more structure and information the analysts get, the better.
- **Built:**
  - **Folders:** `bpmn/<category>/<primary entity>/<workflow name>.bpmn`, so a person working on claims opens one
    folder. `bpmn/index.csv` maps every file to its workflow.
  - **`reports/migration.csv`** — one row per workflow: priority band (live process · dialog/rule/flow · test-like
    name · Draft), diagram path, trigger, step count, unmapped steps, custom activities, calls / called by / role,
    family and combined file, last logged run and usage verdict, sensitive-literal flag, entities and fields
    written. `migration.md` explains the columns. Sorted live processes first.
  - **`reports/call-graph.md` / `.csv`** — entry points, building blocks ordered by how many callers they have,
    one process tree per entry point (a tree is one migration unit), and calls pointing at workflows not in the run.
  - **A header note on every diagram** — name, category/mode/state/entity, what starts it, step count with how many
    are unmapped, the CRM id, and that the model is descriptive and not executable. A Draft says so on the canvas.
    Verified in bpmn-js; a test asserts no shape overlaps the note.
- **Not done:** grouping by data footprint (which workflows write the same field), which would show migration
  ordering conflicts in the new product. Worth doing if the analysts ask.

## The run host's runtime and the SDK (2026-11-21) — DECIDED

- **The run host has .NET 10 installed** (maintainer, 2026-11-21; open decision 2 since 2026-09-14). The projects
  stay `net10.0` and the `Microsoft.Extensions.*` packages stay on the 10.0 servicing line, at the installed
  runtime's patch: an 11.0.0 offered by `--outdated` is not a bump (sweep row T1). Re-opened by a host that changes
  runtime, or a self-contained publish.
- **The SDK is pinned**: `global.json` 11.0.100, `rollForward: latestPatch` (sweep row T2). A floor let any newer
  SDK build the tree, and the analyzers' implementation moves with it, so a warning count was only comparable on one
  machine. An SDK 11 builds a `net10.0` target. Re-opened by a second machine that has only SDK 10.

# Imported from Yalbuz

## How the two-session workflow was arrived at

Moved here from `working-agreement.md` on 2026-08-31, when the always-loaded context was measured at ~15,000 tokens and
split by tense: **the rules stayed there, the incidents came here.** Nothing below is a rule. It is what each rule
cost, kept because a rule whose reason is lost gets re-argued, and because several of these look arbitrary until
you know what happened.

**The second lane was a spawned subagent first, and that version worked and was still wrong (2026-08-30).** The
agent ran thirty-nine minutes and a hundred and ninety-four tool calls the maintainer never saw, made six
assumptions nobody could answer, and could not ask a question of the one person whose answers it needed. **A
question asked in the moment beats a decision disclosed afterwards**, and only a session the maintainer can talk to
can ask one. Two conversations cost attention; that is the thing being bought, not a thing being spent. This is why
the lane is a session in a worktree rather than an agent.

**The shared checkout is what worktrees replaced.** On 2026-08-30 two sessions repeatedly left each other on the
wrong branch and locked each other's build output; once one stopped dead on the other's uncommitted files. Two
worktrees cannot do any of that.

**`EnterWorktree` does not branch from local `main`, and it bit on the very first use.** It uses
`worktree.baseRef: fresh`, which branches from `origin/main`; the worktree opened two commits behind and did not
contain the workflow that had just been merged locally. Since pushing is asynchronous, local `main` is routinely
ahead of the remote, so the remote is never the right base — hence *the first act after entering a worktree is
`git merge main`*.

**A session spent a whole day calling merges provisional without ever checking where they landed**, then guessed
the opposite when it finally looked. Both were the same error: asserting the state of the remote without asking it.
That is why the rule names the command.

**Retiring a package before its branch merged became possible by design, not by accident.** Once two lanes began
leaving finished branches standing, the window between *committed* and *merged* stopped being minutes and started
being days. A package retired in that window is gone from the plan and absent from `main` at the same time —
invisible in both places — and if the branch is then never merged, the work has left the register with nothing to
show for it.

## How the working agreement was arrived at

Moved here from `working-agreement.md` on 2026-09-11 — the second split by tense, after the 2026-08-31 one above,
and for the same reason: that file is loaded into every session, and it had grown to 43 KB of a ~97 KB
always-loaded set, nearly half of it the stories behind its rules. **The rules stayed there, the incidents came
here.** Nothing below is a rule. Each block names the rule it bought, in that file's words, and keeps its date.

**The file's own name.** It was `git-workflow.md` plus `maintainer.md` until 2026-09-08. They were read together,
referenced together and edited together; two files meant two places to keep in step. The name changed with them
because `git-workflow` had long since stopped describing what the file contained.

**"Verification is mostly the assistant's job."** Assuming otherwise is what left a list of unverified items
stalled for weeks; three of them fell in a single session once that assumption was dropped.

**"A measured 'nothing to do here' is a deliverable."** Runs that proposed no work for the sinks, and none for the
sink boundary, were accepted without pushback.

**"A hypothesis derived from READING THE CODE is not a measurement either" (2026-09-10).** The flow hunt is the
worked example and it cost most of a day: with the office log NOT yet in hand, two defects were reasoned out of the
source — a stale dependency gate, then an answer key that measured document size — and both were real, both were
fixed, both were verified green, and **neither moved the maintainer's number.** The third attempt stopped reasoning
and asked the SERVER what the object held; that named the field in one line. **"Say so when the instrument, not
the system, is what answered"** comes from the same day: each of those two fixes was correct and stays in; what was
wrong was presenting either as *the* cause.

**"Hand over a DECISION, never an inventory."** The maintainer described their own review of two code-quality
loops as *"blindly pressing next next next"*, and that was the correct response to what they were given, not a
lapse. **The occasion, because the failure mode is specific.** A `/csharp-refactor-harden` run ended with a bare
notes list — rule IDs and counts, no triage, no value column — and the counts were solution-wide at DEFAULT
analysis mode, which inflated `SYSLIB1045` from 4 to 51 and `CA1859` from 9 to 19. It read as a 60-item backlog
worth clearing; it was 3 items, and the `/csharp-refactor-streamline` pass it authorised produced four regex
conversions and three `static` keywords. Quoting a solution-wide default-mode count as if it were a production
count is what turned a reasonable delegation into a wasted pass.

**"Correct a misused technical term, every time."** The occasion that produced it shows the shape. They wrote
*"so it is called an explicit type-ahead"* after a package that added the behaviour to three controls. The feature
is **type-ahead** — that is the whole term, and it is what WPF's `TextSearch` and a native `<select>` both
implement. *Explicit* was my adjective for how we implemented it in one of the three, by hand rather than by the
platform, and it is not part of the name.

**"Take a position, and argue it" — the menu example in full.** Offered two ways to handle columns found inside a
SQL `CASE`, they answered with a third that beat both: keep the candidates, but confirm each against the rest of
the scan before publishing it.

**"Say plainly when what they supplied wasn't what was asked for."** Explicit instruction after they handed over
integration notes when the schema was wanted, and work was wasted designing around the gap.

**"Justify dependencies unprompted."** A LibGit2Sharp advisory became a work package within the same message, and
that dependency is now gone from production entirely.

**"What may run on this machine."** Defender took `Web/wwwroot/yalbuz.js` and then a diagnostic `.ps1`. The rule
to say beforehand when a check needs something running is their instruction after I reported a log line as
unobservable "by design" when Seq simply was not running that minute: *"From now on tell me beforehand if you need
an application running for you to test something."*

**"Never spawn a background task for an out-of-scope finding."** There is no separate findings register any more
(their decision, 2026-08-28; *One register* in the working agreement). They pick a finding up when they choose the
next package, which is the cadence they already work in. It also avoids a concrete hazard already met once: a
spawned session edited `GitPublishService` while this one wanted to, so a real defect had to be left unfixed to
avoid colliding.

**"One plan, and which file it is" (2026-09-09).** The maintainer's words: *"I made the other session use this
one's plan. It was not before that."* **Why globbing is refused**: on 2026-09-09 the plans directory held two files
and **the one that was NOT the plan was four times the size and five days stale**, carrying a ranked analysis of
the ApplicationFlow refusal that had been closed on 2026-09-05. Newest-wins would have picked correctly that day;
largest-wins or first-alphabetically would not. **Why an in-flight row names its branch**: a CA2025 row was written
while `fix/gitcli-settles-its-readers` had no commits, and it merged within the hour.

**"`preview_start` runs the app from the PRIMARY checkout" (2026-08-31).** Verified in an interactive
`EnterWorktree` session, which is what the note used to call unknown: every path in the server log was the primary
tree. The build also failed on locked DLLs held by the maintainer's own `Yalbuz.Web` and a Visual Studio debug
adapter.

**"A build server holds whatever checkout it last built" (2026-08-31).** A post-merge verification build in the
primary checkout died with `MSB4018` — *"The process cannot access the file
`Web/obj/Debug/net10.0/rpswa.dswa.cache.json` because it is being used by another process"*. `build-server
shutdown` cleared it on the first try, which is the signature of a stale node rather than of two processes racing.
The worktree rule isolates work; it deliberately does not isolate merging, so nobody has to break a rule for this
to happen. The same day found the holder it does not release: VS Code's C# Dev Kit build host
(`Microsoft.VisualStudio.ProjectSystem.Server.BuildHost.dll`), which clears when the editor lets go.

**"What makes a test evidence" — where the section came from.** Its rules lived in the work-package plan until
2026-08-28, which meant they only applied when somebody happened to read it.

**"Never `git checkout` a file to undo a revert-confirm-red experiment."** It cost two rewritten files in WP-G.

**"Falsify with the ACTUAL defect" (2026-09-08).** W3's route guard is the case: broken with a wrong VERB it failed
loudly, and the same run quietly passed the removed jobs-list route the whole package exists to catch — because
normalising `{id:guid}` to `{id}` had thrown away the constraint that made that segment not a job id. Reproducing
the original README line, byte for byte, is what exposed it. The route is named in this file and not in the
working agreement, and that is the guard working: a LIVE document must not spell a path that does not resolve,
however historical the sentence around it. The working agreement's paragraph named it in its first draft and went
red within the minute.

**"Make and unmake a break with the EDIT tool" (2026-09-10).** Inserting a line with `sed` reproduced its
indentation as sixteen spaces inside a method body at eight, and removing it again **ate the leading whitespace of
the following line** — a diff that builds, passes and reads as gibberish. Both were caught by `dotnet format`, not
by the compiler and not by the suite.

**"Confirm the BREAK landed" (2026-09-08).** During W2's own revert-confirm-red, a `sed` pattern named
`Yalbuz.Service.Sinks.Git` for a file whose namespace is `Yalbuz.Service.Git`, so nothing changed and the guard
reported green. The grep is what caught it.

**"The measurement recipes are `/wp-verify`."** Four bullets used to sit in the working agreement spelling out how
to build, how to count warnings, how to run the formatter and how to survive `sed` — the commands, not the
judgement. They became the skill because a recipe re-derived from prose every session is a recipe that drifts, and
this one drifted six times in one day.

**"A WINDOW of a log is not the log" (2026-09-08).** A session read a 75-line window of the office log, saw
`seep cycle paths taken: (none)` on the heartbeats inside it, and reported that the probe had produced nothing
across 27 cycles — into the plan, into the acceptance register, and to the maintainer as *"the one thing the
triage made worse"*. Counted properly the file holds **97** such lines: 71 `(none)`, all heartbeats fired before
any path is taken, and **26 non-empty ones at cycle ends** naming five distinct paths. The probe was working and
the earlier reading it supposedly contradicted was confirmed. It was the second time the same rule was paid for.

**"A separate bucket does not rescue" a preference-assertion.** That was tried on 2026-08-28 and withdrawn the same
day.

**"Finishing a work package" — the commit body as the archive (2026-09-08).** The maintainer: *"let's use git
commit comments when searching for completed work."* The ~1,500-byte cap is inherited from the file this replaced.
Between 2026-09-01 and 2026-09-08 the old scheme produced 156 `docs:` commits against 147 code commits — one ritual
commit per package, for a register that was also 55% over its own cap. `completed-work.md` itself reached 681 KB
and 293 entries at a mean of 2,324 B against its own 1,500 B cap, was not in the alphabetical order it claimed,
and answered one question (*"did this already land?"*) that `git log --grep` answers faster and cannot drift.
**Why a row stays until the merge is checked**: a finished branch waits for the maintainer, who may merge it
themselves, may merge it days later, or may not merge it at all. The plan's unverified list went to the Office Run
Acceptance register on 2026-08-28.
