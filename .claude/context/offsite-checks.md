# Offsite checks — what only the company network can settle

**What this file is** (`profile:offsite_register`). Every check that needs the CRM server, a production export, or a
person looking at a delivered file — nothing this development machine can reach. **Open items only**, each with a
**Do**, a **Pass** and a **Capture**, because the person reading it is on a network this session is not, with one
visit.

- **A package that leaves something only that network can prove adds its item HERE, in its own commit** — not to
  the plan, which carries no unverified list. A change that alters what an item checks rewrites that item in the
  same commit.
- **An item answered by a visit is DELETED** in the commit that reads its answer in — into `plan.md` or a commit
  body — so there are no ticks to drift. `git log -p -- .claude/context/offsite-checks.md` recovers every item.
- **A Capture is counts and console lines, never files**: `out/`, the exports and the restricted findings stay where
  they are (`CLAUDE.md`).

Moved out of `plan.md` (*Acceptance on the company network*) on 2026-11-21, word for word, when the workflow was
re-adopted from Yalbuz; the per-package *Acceptance* lines in `plan.md`'s History are history and stay there.

Only a run against production can settle these. Each has a **Do**, a **Pass** and a **Capture** — and a capture is
counts and console lines, never `ham/`, `ara-model/`, `bpmn/` or the restricted findings.

### A — the delivery run
- **Do:** on a signed-in CRM page, run `tools/crm-export.html` and `tools/crm-usage.html`; put the saved files'
  paths in `appsettings.json` (`Run:ImportFile`, `Run:UsageFile`) next to the exe; start it with no arguments.
- **Pass:** exit code 0; every count-chain line ends `— ok`; `bpmn/` holds exactly the plan's workflows and the
  stage maps; the five workbooks and `nasil-kullanilir.docx` open without Excel or Word offering a repair.
- **Capture:** the console output, and the counts in `raporlar/rapor.md` — never the files themselves.

### B — process mining (open since 2026-10-12)
- **Do:** run `tools/crm-process-mining.html` as a user whose role can read the stage-history entity; set
  `Run:ProcessMiningFile`; run again (or reprocess).
- **Pass:** the file holds stage entries and steps — or, if refused again, it is SAVED and names the table and the
  reason. The holiday calendar answered, or the book says weekends only. The stage boxes on the internal maps carry
  a median stay.
- **Capture:** the script's console lines, and the *Nasıl okunur* counts of `surec-madenciligi.xlsx`.
- **Settles:** whether the history entity exists and is readable; whether `$top` caused the 400s; whether
  `businessclosurecalendar` answers.

### C — limits only production shows
- Whether the usage aggregate stays under CRM's 50 000-record aggregate limit; how fast the `asyncoperations` /
  `processsessions` filters and `_workflowactivationid_value eq <id>` answer. **Capture:** the times the scripts print.

### D — what a person has to look at
- **Do:** open three workflow diagrams and one stage map in bpmn.io or Camunda Modeler.
- **Pass:** they read left to right without crossings worth naming; the embedded workflows open from the map.
- **Capture:** what looked wrong, with the file names.
