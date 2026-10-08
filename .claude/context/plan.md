# Plan — CRM Workflow Extractor & BPMN Transformer

Written 2026-09-14, revised 2026-09-15. Organized in the four sections of handout §9. Decisions the maintainer has
taken are marked **DECIDED** with their date.

## Status

| Package | State |
|---|---|
| Seed (context, rules, wp-* skills, build profile) | merged |
| M1 — inventory, privilege check, count reconciliation | merged |
| M2 — XAML retrieval, cross-run reuse, manual review, drift, option-set metadata, BPF stages | merged |
| M3 — XAML parser, IR, parse coverage, sensitive-literal scan | merged |
| M4 — BPMN 2.0 emission, layout, OMG XSD validation | merged |
| M5 — similarity, families, `clusters.csv` / `pairs.csv` | merged |
| M5b — combining each cohesive family into one workflow + BPMN | merged |
| M6 — count chain, `report.md`, offline reprocessing | merged |

**The prototype is complete end to end — and verified ONLY against a synthetic fake server and synthetic XAML.**
Nothing has touched a real CRM. The first company run is the real test; see *Acceptance on the company network*.

**DECIDED 2026-09-15:** build the bare-minimum plausible prototype of all steps rather than stopping at M1; each
package verified, merged and its branch deleted as it lands.

**DECIDED 2026-09-15 — deferred for the prototype:** flag-field plugin inference (§4.3), `uidata` spike (§4.5),
`cluster-lineage.csv`, resuming an unsealed run. BPF stages as `subProcess` and Dialog pages as `userTask` are NOT
mapped: no credible XAML sample exists to write them against; they surface as unmapped in `parse-coverage.md`.

## Handout disagreements found while building

| Handout | Finding | Source | Status |
|---|---|---|---|
| §3.1 selects `parentworkflowid`, `activeworkflowid` | Lookups are selectable only as `_parentworkflowid_value` / `_activeworkflowid_value`; the bare name fails the whole query | Web API query documentation (lookup properties) | Applied; confirm on live server |
| §2.4 "reconcile `$count` against records" catches the privilege trap | It cannot: both pass through the same security filter. Primary check is `RetrieveUserPrivileges` depth | Reasoning; confirm on live server | Applied |
| §1 non-goal "no automatic consolidation" | Overridden by the maintainer | Maintainer, 2026-09-14 | Built (M5b) |

## Pre-flight facts (development machine, 2026-09-14)

| Check | Result |
|---|---|
| `csharp-*` skills from the handout | Not installed. Replaced by Yalbuz's workflow — **DECIDED 2026-09-14.** |
| .NET SDK | 10.0.401 only → `net10.0`. |
| CRM host / org | Unknown; the development machine cannot reach CRM. **No live verification from here.** |
| Yalbuz fold | 12-entry table + `char.ToLowerInvariant`; misses `â`/`î`/`û` and decomposed `I`+U+0307. |

## Decisions taken

- **DECIDED 2026-09-14:** adopt Yalbuz's workflow; all 13 style rules; repository at `repos\CrmWorkflowExtractor`.
- **DECIDED 2026-09-14:** the tool combines each similarity family into one workflow, like a set union; the
  per-workflow BPMN files remain so a human can check the combination. No writes to CRM, no deletion anywhere.
- **DECIDED 2026-09-15:** download the OMG BPMN 2.0 XSDs (done; embedded unmodified).
- **DECIDED 2026-09-15:** low-cohesion families are NOT combined; they are listed as "split first".
- **DECIDED 2026-09-15:** merge and delete each package branch as it lands.

## Consolidation — as built

- A cohesive family (≥2 members, not low-cohesion, one primary entity, one category) is combined as a **prefix union**
  over literal-stripped step sequences. Shared steps become one step keeping every member's values (each with the
  workflows that set it) and every member's `(workflowid, step path)` source. At the first divergence a **Variant**
  split names exactly which members take each path, including an explicit "(ends here)" branch.
- Conditions merge when every branch tests the same thing; divergence inside a branch stays inside it.
- Triggers, dependencies and data touched are unioned.
- **Reconciliation:** every member step must appear in the combined provenance exactly once, or the run fails.
- Outputs: `consolidated/<clusterid>.json`, `consolidated/<clusterid>.bpmn` (XSD-validated), `reports/consolidation.md`.

---

## 1. Confident improvements within scope

1. **Real privilege check** (`RetrieveUserPrivileges` depth), plus the count reconciliation and single-owner warning.
2. **`$count` 5000 cap guard.**
3. **IFD detection as a hard stop** (Bearer challenge or AD FS redirect → exit code 3).
4. **Read-only, build-enforced**: `BannedSymbols.txt` makes every other way to send a request a compile error;
   `CrmHttpClient` has one guarded GET call site and refuses `nextLink`s outside the organization root.
5. **Count chain** (§8): `$count` → records → classified → XAML → IR (+ manual review + parse failures) → BPMN →
   clusters. Any unbalanced link fails the run; each gap has a named bucket.
6. **Fold** = Yalbuz table + canonical decomposition with combining marks dropped.
7. **Drift** compares raw hash and canonical structure, and lists Draft definitions.
8. **Deterministic output**: ids from workflow id + step path, ordered JSON, byte-identical BPMN on re-emission.
9. **Bounded retry** honouring `Retry-After`.
10. **Every XAML element is accounted for** as mapped, support or unmapped — nothing dropped silently.
11. **XSD validation is fatal** for every BPMN file, individual and combined; verified the validator rejects a broken file.
12. **Offline reprocessing** (`Run:ReprocessRunId`): rebuild every offline output from an earlier run's `raw/`
    with no network. This is the loop for fixing the parser against real XAML away from the company network.

## 2. Wanted but uncertain — need the maintainer's decision

1. **Live CRM access.** Who runs it on the corporate network, host + org, and can `raw/` be sent back? `raw/` is
   what makes offline parser fixing possible, and it holds production XAML (see 2.5).
2. **Target runtime on the run host.** .NET 10 installed, or a self-contained single-file `win-x64` publish?
3. **Drift report expectations.** Settle on the first live data whether definition/activation drift or the Draft
   list is the useful one.
4. **Excel in tr-TR — implemented as `;` + decimal comma + UTF-8 BOM, English headers.** Turkish headers? `.xlsx`?
5. **`out/` holds the secrets the scan redacts** — location and ACL are the security team's decision.
6. **Prefix union repeats steps shared after a divergence.** Measure on real data, then decide whether to merge
   common tails back. Recommendation unchanged: wait for data.
7. **Members on different entities or categories are never combined** (implemented per recommendation). Confirm.
8. **Does the `decision` column in `clusters.csv` feed back** into the next run? Not built.
9. **Default similarity weights.** Path-set Jaccard compares whole execution paths, so two linear workflows that
   differ in a single step score 0 on paths and get their structural score from shingles alone (weight 0.4). Exact
   copies score 1. This under-clusters near-copies by design of §7.1; the architects may want `PathWeight` lower
   after the first output. Tunable in `appsettings.json`, no rebuild.

## 3. Left to judgement — cheap to overrule

1. **No resume of an unsealed run** in the prototype; unchanged XAML is reused from the newest sealed run by
   `(workflowid, versionnumber)` with the hash re-verified.
2. **Manual-review records and templates get no BPMN** — explained buckets in the count chain.
3. **No server-side `$filter`** in the inventory pass.
4. **Configuration precedence:** `Program.cs` constants → `appsettings.json` → `appsettings.Development.json` →
   `CRMEXTRACT_`-prefixed environment variables → Credential Manager for the password only.
5. **Dependencies:** first-party `Microsoft.Extensions.*` and xUnit v3 only. Retry, file logger, Jaro–Winkler,
   union-find and layout are hand-rolled.
6. **Unknown option-set values** → raw int + `Unknown(<n>)` + warning.
7. **The plan lives in the repository.**
8. **`Crm.Consolidation` is a seventh project** (→ `Crm.Ir`, `Crm.Similarity`).
9. **Insufficient privilege or a count failure** keeps the inventory as evidence but stops before XAML retrieval.
10. **HTTP 500 is retried** (bounded).
11. **Run folder names are UTC**; a same-second clash gets `-2`.
12. **Relative `Output:Root` resolves against the executable's folder.**
13. **Every non-XAML API response body is kept verbatim** under `raw/http/`; XAML bodies are the `raw/xaml/` files.
14. **Parser recognition is by the designer's vocabulary** (`ConditionSequence`, step `Sequence`s holding
    `UpdateEntity`/`CreateEntity`/…, non-Microsoft assemblies as partner activities), written from the documented
    shape. Fixtures in `Crm.Tests/Fixtures/Xaml/` are SYNTHETIC and say so.
15. **A wait with a timeout is an event-based gateway** with conditional and timer catch events; a condition with no
    otherwise-branch gets an explicit "(no condition met)" default flow.
16. **Similarity compares all definitions**; combining is then restricted to one entity and category.
17. **A pair with structural ≥ 0.95 and lexical < 0.5 is flagged `rebuilt_under_other_name`** in `pairs.csv`.
18. **Console summary shows at most 25 warnings**; all are in `logs/warnings.txt` and `report.md` (first 50).

## 4. Out-of-scope findings — recorded, not acted on

1. Yalbuz `ScreenConceptBuilder` fold misses `â`/`î`/`û` and decomposed `İ`.
2. The handout's 4 style rules are a stale subset of Yalbuz's 13.
3. CRM 8.2 out of extended support, running partner code that may hold credentials — EA/security risk.
4. `BpmnFileNames.Slug`'s empty-name fallback is the English word `workflow`, in a tool whose output is Turkish
   down to the folder names. Reached only by a workflow whose name folds to nothing at all.

---

## Acceptance on the company network

Items only a run on the corporate network can close. Each has a **Do**, a **Pass** and a **Capture**.

### A — the run itself
- **Do:** as the service account, set `Crm:WebApiBaseUrl` in `appsettings.json` next to the exe
  (`https://<host>/<org>/api/data/v8.2/`) and start `CrmWorkflowExtractor.exe` with no arguments.
- **Pass:** exit code 0; all six privileges `Global`; every count-chain line ends `— ok`; distinct owners > 1; an
  administrator's own count of processes equals `$count`.
- **Capture:** the console output, the administrator's count, and from the run folder: `manifest.json`,
  `reports/report.md`, `reports/parse-coverage.md`, `logs/warnings.txt`.

### B — assumptions only the live server can settle
- **Pass / record here either way:**
  - `workflows/$count` answered a number on 8.2 (if not, the request is named in the failure).
  - No "Column … does not exist" warnings, or the list of columns missing on 8.2.
  - No "no privilege of that name" warning — especially `prvReadProcessStage`.
  - Option-set table in `reports/inventory.md`: every server label means the same as the handout's; no `Unknown(n)`.
  - Whether roles granted through **teams** appear in `RetrieveUserPrivileges`.
  - Exit code 3 means the deployment is IFD: stop, per §2.2.

### C — the parser against real XAML (expected to need work)
- **Pass:** `parse-coverage.md` shows the unmapped constructs, top of the list first. The prototype is *plausible*
  when most designer workflows have zero unmapped constructs; it is *not* expected to be there on the first run.
- **Capture:** `parse-coverage.md`, and — if security agrees — a handful of anonymized `raw/xaml/` files for the
  top unmapped constructs, or the whole run folder for `Run:ReprocessRunId` offline.

### D — outputs a human must look at
- **Pass:** three `bpmn/*.bpmn` files open in a BPMN viewer (e.g. Camunda Modeler or bpmn.io) and read sensibly;
  `clusters/clusters.csv` opens in Excel with columns split; one `consolidated/*.bpmn` compared against its
  members' files in `bpmn/` using `reports/consolidation.md`.
- **Capture:** what looked wrong, with the file names.

## First contact with production (2026-09-22)

The maintainer's account was authorized and read the Web API root `https://ahecrm.anadoluhayat.com.tr/api/data/v8.2/`.
The service document is kept in `reference/crm-service-document-2026-09-22.json`, with findings beside it in
`reference/crm-service-document-2026-09-22.md`. In short: every entity set the tool reads exists. The URL has no
organization segment, which may mean an internet-facing (IFD) deployment — open question, settle before the first
run. BPFs (14) and the North52 rules engine are in use; the latter's logic is invisible in workflow XAML
(wanted-but-uncertain: whether to inventory `north52_formulas` alongside workflows).

### Authentication on the first real run (2026-09-22)
- **Observed:** `WhoAmI()` → 401 with `WWW-Authenticate: Negotiate, NTLM`. So the deployment is **on-premises Windows
  authentication, not IFD** (answers the open question above). The browser on the same machine opened the Web API
  root; the tool, in Default mode, was refused.
- **Built (diagnostic, not a fix):** the refusal now names the Windows account and scheme used, and
  `Crm:AuthenticationScheme = Ntlm` binds the same account to NTLM only — the standard workaround when Kerberos is
  misconfigured for the host name. **Cause not yet known.**
- **Do:** (1) note whether the browser asked for a password; (2) re-run and read the account named in the failure;
  (3) if the account is the authorized one, set `Crm:AuthenticationScheme` to `Ntlm` and re-run.
- **Capture:** the three answers and the console output.

### The deployment is IFD / claims-based through AD FS (2026-09-22)
- **Observed:** browsing to `https://ahecrm.anadoluhayat.com.tr` sends the user to
  `https://ahecrmadfs.anadoluhayat.com.tr/adfs/ls/`; after that sign-in the browser's session cookie opens the Web API.
  Windows authentication straight to the API was refused for `ANADOLUHAYAT\KMM2456` (explicit, NTLM) and for the
  Windows login (Negotiate), in the tool and in the browser's own prompt.
- **Consequence:** handout §2.2 applies — stop and report; no OAuth path improvised. The tool's IFD detector did NOT
  fire, because the Web API answers with a Windows `Negotiate, NTLM` challenge rather than an AD FS redirect or a
  Bearer challenge. The earlier "on-premises, not IFD" conclusion (drawn from that challenge) was wrong.
- **Decision needed (maintainer):** (A) an internal Windows-authentication address, if the configuration team has
  one; (B) OAuth through AD FS, which needs AD FS registration and a token request — a POST to AD FS, which the
  read-only ban currently forbids everywhere; (C) read the workflow definitions from the CRM database's filtered
  views with a read-only database account instead of the Web API — outside the handout's design.

### Looking for an internal address (2026-09-22)
- Organization unique name **`AHECRM`**, organization id `48c22744-77c6-e411-80ce-005056b34efe`; discovery at
  `ahecrmdisco.anadoluhayat.com.tr` (the standard IFD host naming, alongside `ahecrmadfs.`).
- `ahecrm.anadoluhayat.com.tr` → `10.10.32.170`; reverse DNS → `form2crmsvc.anadoluhayat.com.tr`. That name serves a
  separate WCF application (`FormService.svc`), not CRM: `/AHECRM/api/…` and `/api/…` return 404 on both http and
  https. The address is shared (host-header routing or a load balancer). The CRM server's computer name must come
  from the server team — DNS cannot give it.

**Out-of-scope findings (for the security team, recorded, not acted on):**
- `https://form2crmsvc.anadoluhayat.com.tr/` has **IIS directory browsing enabled** and lists `bin/`, `Web.config`,
  `Global.asax`, `FormService.svc`. Web.config is normally blocked from download, but listing it at all is a finding;
  it was not opened.
- `FormService.svc` ("form to CRM service") looks like an integration that writes into CRM from outside — business
  logic that no workflow XAML will show, like North52.

### Browser export instead of the tool's own sign-in (2026-09-22) — DECIDED
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

### First look at the real workflow list (2026-09-22, names only)
- All sampled records are `iscrmuiworkflow = true`: manual review will likely be near-empty.
- All five categories occur: many **Dialogs (1)** and **Business Rules (2)**, ~25 **BPFs (4)**, a handful of
  **Actions (3)** (`GetURLAction`, `CrptoEncodeDecode`, `SendSmtpEmailNovaFlag`, …). The parser maps none of Dialogs,
  Business Rules or BPFs yet — expect them as unmapped in the first real `parse-coverage.md`.
- **Many names repeat** (`SET NAME`, `Enter Rule Name`, `SERVİS TALEPLERİNİ DAĞIT`, `Admin_Open_SmartMessage`, …): file
  names must be keyed by workflow id, never by name (the export does this).
- **Many `DRAFT_*`, `test*`, `Admin*` workflows**: noise for consolidation. Wanted-but-uncertain: whether the
  architects want those excluded from similarity (by state = Draft, or by a name pattern they choose).

### First real export and import (2026-09-22)
- **Observed:** the export held 3237 workflow records (1437 definitions, 1795 activations, 5 templates) from 6 owners.
  Every privilege was Global except `prvReadProcessStage`, which **does not exist under that name on 8.2**, so it
  cannot be checked (a warning, not a failure). There was 1 orphan activation, and 4 definitions point at an
  activation that was not retrieved.
- **Observed:** `workflows/$count` answers **-1** on this server, Dynamics' "no count available". The import failed
  it as a mismatch (-1 ≠ 3237).
- **Built:** a negative count now means *unavailable*. The run prints a loud warning instead of failing, and the
  `$count → records` link drops out of the count chain. Both the tool and the browser script then try an
  independent count through a FetchXML aggregate (`workflows?fetchXml=<fetch aggregate="true">…`, also a GET). The
  export records `rawCount` and `countSource` next to `count`. An export made before this change (count -1) still
  imports.
- **Fixed on the way:** `raw/workflows.jsonl` took every `/workflows` response. An aggregate row would have
  become a bogus workflow record, and a refused aggregate crashed the run. It now takes only successful,
  non-aggregate pages.
- **Unverified:** whether the production server allows the aggregate (the 50 000-record aggregate limit is far
  above 3237). The maintainer can check by opening the URL in the signed-in browser.

### First full run on production data (run 20260922-171100, tool 4d6aea4)
- **Result:** completed, exit 0. Every link in the count chain balances. Output: 1437 IR · 1437 BPMN · 68 families of
  2 or more · 52 combined (127 workflows) · 16 not combined · 0 drift · 211 drafts · 0 hand-authored. There are 148
  sensitive-literal findings in 34 workflows (the security team has the file; only the count leaves the machine).
- **Definitions by category:** Workflow 1137, Business Rule 142, Dialog 129, Business Process Flow 23, Action 6.
- **Parse coverage:** 1046 workflows had an unmapped construct. Grouped by cause:
  - **595 only helpers:** `ConvertCrmXrmTypes` (8353 occurrences), `OptionSetValue`, `XrmTimeSpan`. These are type
    conversion and typed literals, not steps. Parser bug; **fixed**: they are support now, and a value passed
    through a conversion keeps its literal.
  - **About 293 Dialog / Business Rule / BPF:** `Interaction*`, `Step*`, `Stage*`, `Control`, `Set*`. These are
    different designers with their own XAML vocabulary. **Not parsed yet (decision needed; see below).**
  - **158 real workflow constructs:** `If`+`RetrieveEntity` (106, loading a related record: now support, with the
    entity recorded as read), element-form `Postpone` (62, a wait: now a Timeout step), `SendEmailFromTemplate`
    (2: now Send Email).
  - Also: `SetEntityProperty` values in attribute form (`Value="[X]"`) are now read, not only the element form.
- **Next evidence:** reprocess this run (`Run:ReprocessRunId = 20260922-171100`; no new export) and compare
  parse-coverage.md. The constructs left over name the next fix.
- **Decisions needed (maintainer):** (1) whether Dialogs (deprecated by Microsoft), Business Rules and BPFs are in
  scope for BPMN, or should be listed only and kept out of grouping; (2) whether DRAFT_/TEST/DEBUG workflows (211
  drafts) are kept out of grouping. Several of the largest families have a draft as their medoid.

### Usage evidence and drafts (2026-09-23) — DECIDED
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

### Dialogs and business rules to BPMN (2026-09-23) — DECIDED
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

### The usage export stalled on production (2026-09-23)
- **Observed:** after listing 1437 definitions and 1795 activations, the script stopped with one `asyncoperations`
  request pending forever. It was the "oldest System Job" query: a filter on `operationtype` with
  `$orderby=createdon asc` sorts the whole System Job table, which production cannot answer.
- **Fixed:** the two whole-table queries are gone. How far back the logs reach is now the oldest run actually found
  (a floor, not the retention setting, and the report says so). Every lookup has a 90-second timeout and is recorded
  as a failed lookup instead of hanging, and progress prints every 10 definitions with a lookup count and elapsed
  time. A test asserts the script never sorts a whole table.
- **Still unverified:** how fast `_workflowactivationid_value eq <id>` answers on production's System Job table. The
  progress line after the first three definitions shows it; if those are slow, the next step is to drop per-workflow
  lookups for anything but background workflows, or to give up on run evidence altogether.

### Making the BPMN files usable by an analyst (2026-09-23)
- **Maintainer, after reading the first files:** GUID file names, condition text painted across the diamonds, and
  step names like `Assign: AssignStep3`.
- **File names:** `bpmn/<workflow name folded to ASCII>.bpmn` (`police-iptal-sureci-admin.bpmn`). A name shared by
  several workflows carries the first 8 characters of its id. `bpmn/index.csv` maps file to workflow, and
  `usage.csv` and `consolidation.md` name the file too. A combined family is
  `<medoid>-combined-<members>-<cluster tail>.bpmn`. Ids stay inside the files, where tooling reads them.
- **Labels:** the files carried no `BPMNLabel` bounds at all, so viewers painted every gateway and event name over
  its own shape. Each gateway and event now carries label bounds below the shape, and a branch condition is a
  caption above the first shape of the branch it leads to. Sizes follow how bpmn.io actually wraps an external
  label (90 pixels wide, growing downward), and rows are 120 pixels apart to leave room. A test asserts no label
  overlaps any shape, over every fixture; verified by eye in bpmn-js as well.
- **Step names:** `AssignStep3` is CRM's internal step id, not a name — it only shows when the author wrote no step
  description. Those steps are now labelled by what they do (`Update: new_policy · new_status, new_reason`), a
  diamond by the field its branches test (`new_policy.new_status?`), and a child call by the name of the workflow
  it calls. The internal id stays in the element documentation as evidence.

### Structure for the analysts (2026-09-23) — DECIDED
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

### Data footprint and cascades (2026-09-23) — committed, awaiting merge
- **Maintainer:** build grouping by data footprint.
- **Built (branch `feature/data-footprint`):** `reports/data-footprint.md` / `.csv` (per field: writers, readers,
  and the workflows a change to it starts) and `data-cascades.csv` (one workflow's write starting another, through
  a watched field or a created record, with both modes and a self-start flag). `migration.csv` gains
  `shared_fields_written` and `starts_other_workflows`.
- **Why it matters for the rebuild:** a field several workflows write has no guaranteed order in CRM, so the target
  product must choose one; and a cascade is coupling with no call between the two workflows, invisible in the XAML
  and in the BPMN.
- **Acceptance on the company network — Do:** reprocess and open `data-footprint.md`. **Pass:** the shared-field
  and cascade counts are plausible for 1437 workflows, and a spot-checked cascade matches what CRM does (the
  target workflow really is triggered by that field). **Capture:** the three summary bullets at the top of the
  report and the count line from `report.md`; no workflow names needed.

### Second production run (20260922-194435) and what it showed — committed, awaiting merge
- **Result of the parser work:** unmapped step-level constructs 15405 → **879**, workflows with any unmapped
  construct 1046 → **90**. The dialog and business-rule markup guessed on 2026-09-23 was right: `Interaction`
  (1022), `InteractionPage` (766), `QueryData` (95), `SetVisibility` (209), `SetFieldRequiredLevel` (194),
  `SetDisplayMode` (106), `SetMessage` (25) all map. Families 68 → 56 because the 211 drafts are now held apart.
- **Usage, first real evidence:** 418 Used · 413 no logged run since 2024-01-11 · 265 real-time (failures only) ·
  112 business rules (never logged) · 211 Draft · 18 dialogs with nothing since 2014-11-13. **Six workflows named
  `DRAFT_*` are activated AND ran on the day of the export** — names do not say what runs.
- **Fixed here — `Postpone` (79 waits in 62 workflows):** the designer writes "wait N days, then do X" as ONE
  Sequence holding the `Postpone` beside the action. The wait was therefore never a step, and the diagram claimed
  the action happened at once. A step sequence holding a wait now becomes a group: timer first, then the action.
- **Fixed here — 7009 cascades were unreadable.** The page now rolls them up: the fields that set off the most
  work (writers × watchers), and workflow pairs (60 shown). The full list stays in `data-cascades.csv`.
- **Fixed here:** a reprocessed run said "WhoAmI did not complete"; it now carries the source run's user.
- **Left open:** Business Process Flows (23) are the only structural gap now — `Control`, `StepComposite`,
  `StageComposite`, `EntityComposite`, `PageComposite`, `SetNextStage`. Most are Microsoft's out-of-the-box
  samples; about 9 are the company's own. Maintainer's call.
- **Left open:** 9 `If` elements in 5 workflows that do more than load a related record.

### Workflows supplied with the product (2026-09-23) — committed
- **Maintainer:** weed out the Microsoft examples the company does not use.
- **Signal — CRM's own, not a guess:** `ismanaged`. A workflow in a managed solution was shipped with the product
  or a partner solution; one written here is unmanaged. Names are never used for this: "Opportunity to Invoice
  (B2B)" only *looks* like Microsoft's, and a company workflow could carry any name.
- **Built:** managed definitions are held apart from grouping and combining, listed in
  `clusters/supplied-with-the-product.csv`, marked `supplied_with_product` in `migration.csv` and sorted into
  priority band 5. They keep their IR and BPMN, so nothing disappears silently, and the count chain accounts for
  them: clustered + drafts held apart + supplied.
- **Acceptance — Do:** reprocess and open `clusters/supplied-with-the-product.csv`. **Pass:** the out-of-the-box
  BPFs ("Opportunity to Invoice (B2B)", "Phone Sales Campaign", "Collaborative selling", "In store Excellence",
  "Marketing List Builder", "Multichannel Sales Campaign", "Service Appointment Scheduling", "Service Case
  Upsell", "Upsell after service interaction", "Contact to Order (B2C)", "Email Sales Campaign", "Guided Service
  Case") appear there, and the company's own Turkish-named flows do NOT. **Capture:** the row count and whether
  that holds. **If the file is empty**, this server keeps those processes unmanaged and the flag cannot do the
  job — then ask the CRM team which solution they belong to.

### Turkish output (2026-09-23) — committed
- **Maintainer:** every output file, in name and in content, is Turkish; anything that comes from CRM stays as it is.
- **Built:** `RunPaths` (folders and files: `ham/`, `ara-model/`, `aileler/`, `birlesik/`, `elle-inceleme/`,
  `raporlar/`, `gunlukler/`; `rapor.md`, `tasima-plani.csv`, `kullanim.md`, `cagri-agaci.md`, `veri-ayak-izi.md`,
  `ayristirma-kapsami.md`, `hassas-degerler.md`, `sapma.md`, `birlestirme.md`, `aileler.csv`, `taslaklar.csv`,
  `urunle-gelenler.csv`, `dizin.csv`), `RunStages` (stage names) and `ProcessLabels` (category, mode and state
  labels, which the usage verdicts, the priority bands and the Draft split branch on — `OptionLabelTests` pins them
  to the §3.1 tables). Every report, CSV header, BPMN label and documentation line, the console summary and every
  warning and failure is Turkish.
- **Left in English on purpose:** configuration messages (they name `appsettings.json` keys), `manifest.json` keys
  and the IR JSON schema (machine-read), `ILogger` diagnostics, and the code itself.
- **Compatibility:** a run from before this still reprocesses — its `raw/` is read and copied forward as `ham/`.

### Workbooks instead of CSV (2026-09-23) — committed
- **Maintainer:** replace the CSVs with Excel files and combine the conceptually similar ones; ease of use is king.
- **Built:** `ExcelWorkbook` writes .xlsx by hand — a workbook is a zip of XML parts, so it needs no dependency
  (§10). Header row frozen and bold, a filter on every column, widths from the content, numbers written as numbers
  so Excel sorts them, and a fixed zip timestamp so two runs over the same data are byte-identical.
- **Ten CSVs became three workbooks**, grouped by the question the reader has:
  `raporlar/tasima-plani.xlsx` (Taşıma planı · Kullanım · Çağrı ağacı · BPMN dizini) — what the work is;
  `raporlar/aileler.xlsx` (Aileler · Çiftler · Taslaklar · Ürünle gelenler) — which of these are the same;
  `raporlar/veri-analizi.xlsx` (Veri ayak izi · Tetikleme zincirleri) — what touches what.
- **Verified:** the tests read the files back through the zip, and SheetJS — an independent reader — opened both
  workbooks in a browser: sheet names with Turkish characters intact, 46 rows, `öncelik` typed as a number.
- **Unverified:** Excel itself, which is not on this machine. Nothing in the format is Excel-specific.

### External systems (2026-09-23) — committed
- **Maintainer:** are the web services the workflows call visible in the output?
- **Answer, and what it rests on:** a CRM workflow cannot call a service itself; the only route is a **custom
  activity** (compiled code registered in CRM). So each call was already in the diagrams as a service task with its
  arguments — what was missing was the inverted view.
- **Built:** `raporlar/dis-sistemler.xlsx` (**Dış bağımlılıklar**: activity, assembly, how many workflows call it,
  which ones, any address passed to it · **Adresler**: every http/ftp/UNC address a workflow passes, with the
  workflow, step and argument) and `dis-sistemler.md`, which states in its opening lines what cannot be seen.
- **The limit, in the report itself:** what an activity does inside its own assembly is not in the XAML, so an
  endpoint hardcoded in the code is invisible here; only its owner can say. Plug-ins registered on entity events
  are not workflows and never enter this inventory at all.
- **Acceptance — Do:** reprocess and open the workbook. **Pass:** the well-known integrations (Feniks, Nova, IGES,
  Genesys, Docman, Pisano, North52 `ExecuteFormula`) appear with plausible caller counts. **Capture:** the two
  summary bullets; the address sheet holds production URLs and stays on the company machine.

### One table, one place (2026-09-23) — committed
- **Maintainer:** what we moved into a workbook should not also be produced as a file.
- **Built:** every table is now a sheet, and each workbook opens with a **Nasıl okunur** guide sheet (columns,
  caveats, this run’s numbers). New sheets: Süreç ağaçları, Okunamayan yapılar, Yapı sıklığı, Sapma, Birleştirme.
  Nine Markdown pages deleted (tasima-plani, kullanim, cagri-agaci, veri-ayak-izi, dis-sistemler,
  ayristirma-kapsami, sapma, birlestirme, envanter) and their builders removed with them.
- **What moved rather than vanished:** the usage verdict table and its “absence proves nothing” wording now live in
  `rapor.md`; the process trees became a sheet; the column legend became each workbook’s first sheet.
- **Result:** `raporlar/` holds 2 pages and 4 workbooks. `rapor.md` is the entry point; `hassas-degerler.md` stays
  a separate file so a delivery can leave it out in one move.
### Columns an analyst acts on, and a plan with no filtering (2026-09-23) — committed, awaiting merge
- **Maintainer:** make sure every column of every workbook is one a System Analyst can use; put the more useful
  ones first; columns that only confuse may go. Then: there is too much data — split what is not the work into its
  own workbook so nobody has to filter.
- **Built:** `kapsam-disi.xlsx`. A workflow leaves `tasima-plani.xlsx` on three CERTAIN grounds only — CRM's own
  `ismanaged` flag, a Draft definition (cannot start a run), or a test-like name **with no logged run** — and lands
  in the counterpart book with that reason in its first column, its verdict, its last run and its diagram beside it.
  The name alone is never enough: the six activated `DRAFT_*` workflows that ran on export day stay in the plan.
- **Columns:** every sheet reordered most-useful-first and trimmed. `aile` is now the family's starting workflow by
  name, not a cluster id no reader can use; `gecen_adresler` (always empty, and read as "calls nothing"), the two
  arithmetic flags in Veri ayak izi, the raw pair ids and the id columns moved last or deleted.
- **Sheets deleted:** Kullanım (its verdict is a plan column), BPMN dizini (the plan carries the file name),
  Yapı sıklığı (the parser's own diagnostic), Taslaklar and Ürünle gelenler (now rows of Kapsam dışı).
- **Why not just an autofilter:** a filter is a state the reader has to notice and re-apply, and a saved filter in
  .xlsx hides rows without saying so. A second file cannot be misread as the work.
- **Count chain:** clustering now excludes the never-run test names too, so the `aileler` link gained a fourth
  term (`clusters.testNamedHeldApart`); the chain still has to balance or the run fails.
### The guide an analyst opens first (2026-09-23) — committed, awaiting merge
- **Maintainer:** one PDF, step by step, in what order the files are used; it must get a System Analyst who knows
  nothing about the subject working as fast as possible, with no confusing detail but clear answers to the
  questions the job actually raises; give them a working procedure on a real active workflow; use the corporate
  green and blue, and make it look like the company's site.
- **Built:** `raporlar/nasil-kullanilir.pdf`, 5 pages: what the package is · five minutes of background (what a CRM
  workflow is, the five kinds, mode and state, what the tool did) · the seven files in the order they are opened ·
  a ten-step walkthrough demonstrated on a live workflow chosen from that run · the seven traps · a nine-item
  finishing checklist · a ten-term glossary.
- **Why by hand:** `PdfDocument` writes the file itself, as `ExcelWorkbook` writes .xlsx — a PDF is numbered objects
  and an offset table. The alternative, a dependency, was not worth it for one document.
- **Why a font is embedded:** the PDF base fonts are WinAnsi, which has ö and ü but no ğ, ı or ş. `TrueTypeFont`
  reads an installed font's cmap, metrics and embedding permission (OS/2 fsType) and the file travels inside the
  PDF, with a ToUnicode map so the text still copies and searches. No usable font means no PDF and a warning.
- **Colours:** read off the company's own site — the blue it uses for headings and bands, the green it keeps for
  the one button it wants pressed, white cards with a pale rule on near-white. The code names them by role, not
  after the company.
- **Checked by eye:** rendered through pdf.js and read page by page; the text extracts back correctly, which is
  what proves the glyph mapping.
### The logo on the cover (2026-09-23) — committed, awaiting merge
- **Maintainer:** put the organisation's logo on the guide.
- **Built:** `PngImage` — a PNG opened far enough to put in a PDF. The two formats agree on deflated 8-bit rows and
  disagree on everything else: PNG filters each row against the one above, keeps a palette, and carries alpha in
  the colour stream where PDF wants a separate grey mask. So the file is unfiltered, de-paletted and handed on in
  the two pieces a PDF understands. Depth 8 and no interlacing, which is every logo anyone exports; anything else
  is refused rather than drawn wrong.
- **Where the file lives:** embedded in `Crm.Cli` (its own site, 2026-09-23, unmodified), because the host is locked
  down and a loose file would be one more thing to copy. `Run:LogoFile` names another PNG instead, no rebuild; a
  named file that cannot be read is a warning and a guide without a mark, never a failed run.
- **Colours:** the document now uses the logo's own blue and green (#005C9C, #6CB644) rather than the site's UI
  blue, so the page and the mark on it agree. The logo sits on a white card because the mark is drawn for a light
  ground — which is how the site carries it too.
- **Checked by eye:** rendered through pdf.js at 2× and read; the mark is sharp, the palette and the card are right.
### Every address in the definition reaches the page (2026-09-23) — committed, awaiting merge
- **Maintainer:** the Adresler page is completely empty, and the called web services' URLs are nowhere.
- **Two causes, one ours.** A CRM workflow cannot call a service: the only way out is a custom activity, whose
  endpoint is usually inside its own assembly and in no CRM record at all. That part cannot be fixed from here.
  But the page was also reading a much narrower source than the restricted report beside it — only the arguments
  captured on mapped steps — so an address in a variable, an expression or an unreadable construct never arrived.
  On the real data that set was empty while the sensitive scan was finding embedded addresses.
- **Built:** addresses now come from the literals of the definition, the same text the sensitive scan reads, and
  both are gathered in one pass (`IrStage.Literals`) so neither can quietly read less than the other. The page
  gained a `sunucu` column — sort by it and the question "which outside systems do we touch" answers itself — and
  a user name and password written into an address are masked, because this page is delivered and the other is not.
- **Guide:** the workbook now states in as many words that an empty Adresler page does NOT mean "calls nothing".
- **Removed:** `ExternalSystems.Markdown`, which no longer had a caller, and the address list on
  `ExternalDependency`, which no sheet had shown since the column audit.
### Read it as the analyst would (2026-09-23) — committed, awaiting merge
- **Maintainer:** teach the guide every sheet; order the sheets to match it; drop what does not help a decision;
  then WALK your own recommended workflow and fix what got in your way. Add to the diagrams too — they are what
  the analysts will open most.
- **Walked it.** The tool was run over the recorded export and the output read sheet by sheet, as an analyst who
  has to draw the new design. Eight things got in the way, and each one is now fixed:
  1. `kullanim_hukmu` carried a 120-character sentence in every row, so the column was 120 characters wide and the
     sheet unreadable. The cell now says `çalışıyor · 2026-09-20` or `kayıtlı çalışma yok`; the caveat that an
     absent record proves nothing is said once, in the guide and on the PDF's traps page.
  2. Çağrı ağacı had a row per workflow, 45 of 46 saying "calls nothing". Only workflows in a call remain.
  3. Süreç ağaçları listed a "tree" of one node. A tree needs two.
  4. Okunamayan yapılar located the problem with a XAML index (`0`, `1`) that means nothing to a reader. It now
     gives the diagram file and how many times, and the diagram marks the step OKUNAMADI.
  5. Adresler had the same useless index column; gone.
  6. A call to a workflow outside the inventory printed a bare guid; it now says `(envanterde bulunamadı)`.
  7. The diagram's call activity did not name the workflow it starts, and its service task did not name the
     registered code it runs. Both are now on the label, which is how a reader gets to the next file.
  8. Sapma mixed drafts and cosmetic differences into the one list that means "what runs is not what we drew".
- **Plan columns:** `bekleme_var` added — a process that spans time is not the same object as one that runs to the
  end, and it was invisible. `öncelik`, `durum`, `aile_buyuklugu` and `yazdigi_alanlar` removed: a band with two
  values that `kategori` already says, a column that never varies, a number that belongs to the family book, and
  twelve field names in one cell that veri-analizi.xlsx carries properly.
- **Guide:** every column now has a line saying what a reader does because of it, every workbook lists its sheets
  in reading order, and the PDF walks file → sheet → column at each of its ten steps.
### The call signature of a custom activity (2026-09-23) — committed, awaiting merge
- **Maintainer:** we cannot find a ServiceUrl anywhere in the diagrams — can the tool not see web service calls?
- **Answered from the data.** The parser captures the arguments (thousands of them across the estate; "no arguments"
  appeared 11 times). There is no `ServiceUrl` parameter at all: the URL-shaped names that do exist — `TargetUrl`,
  `CrmUrl`, `EntityURL`, `RecordUrl`, `*DynamicUrls` — are CRM's own addresses, record and dialog links. The
  endpoints live inside the activities' assemblies, in no CRM record, and cannot be reached from here.
- **What CRM does keep is the names.** `dis-sistemler.xlsx` → Dış bağımlılıklar gained a `parametreler` column: every
  name an activity is called with, gathered across its call sites. `GetPersonEntityInformationRq`,
  `ApproveClaimFundSellResult`, `ReversePendingCCPaymentsResult` — these name the back-end operation, which is what
  an analyst needs to plan the integration. The address would not have told them that.
- **Security, found on the way:** `UserName`, `PassWord` and `FtpUserID` appear as workflow arguments — credentials
  written into definitions. The column now shows which activity carries them; the values stay in the restricted
  report. The guide says to pass this to the security team.
### Where a service call goes (2026-09-23) — committed, awaiting merge
- **Maintainer:** I am after the URLs, not the credentials — show the called service's URL on the service-call steps
  if it can be done at all.
- **It can, from one place only.** The URL is in no workflow record: not in the arguments (proved by the parameter
  sweep), not in the activity's registration (workflow activities have no registration step — that mechanism is
  the plug-in pipeline's, and the earlier note saying otherwise was wrong). It is written inside the activity's
  own assembly, and CRM stores that assembly as a field. So: read the registry, download only the assemblies that
  back a workflow's custom activities, scan their string constants, keep the addresses and drop the bytes.
- **Said honestly everywhere it appears:** "the code this step runs contains these addresses", never "this step
  calls this address". A literal built at run time or read from a settings record is not there at all.
- **Where it shows:** `dis-sistemler.xlsx` → Dış bağımlılıklar gains `derlemedeki_adresler` and `kayitli` (a
  workflow calling a type CRM no longer has is a broken integration); the diagram carries it on the step's own
  label (`… → https://…`) and in the header note.
- **What came with it:** plug-in steps — code CRM runs on a message. They are not processes, never entered the
  inventory, and are a whole integration surface the migration would have missed. New `Eklentiler` page, with the
  addresses in their unsecure registration configuration. The secure configuration is not read: that is where
  credentials live and the maintainer is after URLs.
- **The browser export does the scan in the browser** and sends only the extracted text, so a multi-megabyte DLL
  never travels in the export file or lands on a machine.
### The export must not hang (2026-09-23) — committed, awaiting merge
- **Maintainer:** renewed the export; `workflows/$count` stayed Pending in the Network tab. It did answer in the
  end, after a very long wait — so the server is simply slow on that query, not broken.
- **Not the plug-in change:** `readPlugins()` runs at line 191, the count at line 88. The count is the first thing
  asked and nothing new touches it.
- **The real defect:** no request in the export script had a deadline. A locked-down 8.2 server can leave one
  pending with no answer and no error, and the console then prints nothing at all — which a reader takes for a
  broken script. The usage export was given timeouts when it hung on the System Job table; this one was not.
- **Built:** every request carries `AbortSignal.timeout`, 120 s by default. `workflows/$count` gets 20 s and its
  failure is not fatal: it falls through to the FetchXML aggregate that already stood behind it, which is what
  this server answers anyway, so nothing is lost by cutting the wait. A timeout is reported as a sentence rather
  than an unhandled rejection, and the phase is logged before it starts so a stall is attributable.
### One aggregate instead of eighteen hundred lookups (2026-09-23) — committed, awaiting merge
- **Measured, not guessed:** the maintainer's run reported `150/1437 — 340 lookups, 841s elapsed`. That is 5.6 s per
  definition and, at four in parallel, about ten seconds of server time for every single filtered lookup against
  the System Job table. Projected total: two hours fourteen minutes.
- **The query was the cost.** `asyncoperations?$filter=_workflowactivationid_value eq <guid>&$orderby=createdon
  desc&$top=1`, once per activation, on a table with no useful index for that predicate.
- **Built:** the same question asked once. `<fetch aggregate="true">` with `groupby` on the activation and
  `aggregate="max"` on `createdon` returns the newest run of every activation in a single GET, and the importer
  reads nothing but `createdon`, so it is a lossless substitute — the horizon (`oldest run seen`) is the same set
  and therefore the same minimum. Dialogs get the same treatment over `processsession`.
- **A refusal is not a failure.** Aggregates have a scan limit; if the server refuses, that source falls back to
  the per-record path, per source independently. The export records which way each was answered (`method`).
- **Verified by running the bookmarklet**, not by reading it: stubbed CRM in node, both paths. The aggregate path
  issues three requests in total and produces byte-identical `usage` shape; the refused path falls back and still
  produces it. `PARALLEL` is now 6, the browser's own per-host ceiling, for whatever still goes record by record.
### A bounded lookback, said in the words it deserves (2026-09-23) — committed, awaiting merge
- **What happened:** the aggregate did not come back. `the aggregate was refused … signal timed out` — the server
  did not refuse it, it could not group the whole System Job table inside ninety seconds.
- **Built:** the aggregate now carries `<condition attribute="createdon" operator="last-x-days" value="400" />`,
  which turns a whole-table group-by into a small scan, and it gets 180 s of its own rather than the shared 90.
- **The cost is a different sentence, and the reports now make it.** Bounded, "no logged run" means "nothing since
  that date" — a weaker claim than the one a reader would otherwise hear. The export declares the window per
  source (`lookback.jobsSinceUtc`, `lookback.sessionsSinceUtc`, null where the per-record path answered and
  nothing was skipped). `Verdict` becomes "yalnızca <tarih> tarihinden bugüne bakıldı, daha eskisi TARANMADI",
  `ShortVerdict` becomes "<tarih> sonrası çalışma yok", and the run report leads with the scanned range before any
  date a reader might mistake for the beginning of the record.
- **Verified by running the bookmarklet** against a stubbed CRM, both paths: the aggregate path makes three
  requests and declares the window; the refused path falls back to per-record lookups and declares none, which is
  the honest difference — there, everything ever logged was in reach.
### The delivered files name nothing the reader lacks (2026-09-23) — committed, awaiting merge
- **Maintainer:** the analysts get only some of the run folder — are there concepts in the guide they were never
  given? And then: not in the Excels either.
- **Audited all of it.** The PDF was already clean: the six files it walks are all in the delivery, and "ara model"
  appears only as a glossary term, not as a folder to open. Two workbook lines were not: the plan's
  `hassas_deger_var` sent the reader to "the restricted report", and the external book's credential line named
  `hassas-degerler.md` outright. Both now say the values are not in this package and who holds them.
- **`rapor.md`** pointed at `../elle-inceleme/dizin.md` as if it were part of the package. It now says what those
  workflows are — unparsed, and therefore with NO row in the plan — and that the list lives in the run folder.
- **Made a rule, not a fix.** `DeliveryTests` opens every delivered workbook from an end-to-end run and fails on
  any cell naming something left behind. Checked by breaking it: it named the workbook, the sheet and the cell.
### No cell Excel has to repair (2026-09-23) — committed, awaiting merge
- **Maintainer:** Excel opened `dis-sistemler.xlsx` "repaired": string properties from `sheet2.xml`, which is the
  Dış bağımlılıklar page.
- **The cause was length.** Excel holds 32,767 characters in one cell and discards a longer one, calling it a
  repair. `cagiran_is_akislari` joined every workflow that calls an activity into a single cell; on this estate,
  with around nine hundred call sites, one popular activity passes that limit easily. The reader is told the file
  was repaired, not that a cell was emptied — a silent loss at the far end.
- **Fixed in two places.** `ExcelWorkbook` now clamps every cell to the limit with a visible marker and strips the
  control characters XML cannot carry at all (a string scanned out of a binary assembly may hold one, and that
  would have crashed the run rather than been repaired). `Sheet.List` caps a list at forty names and says how many
  it left out — because a cell holding four hundred names is unreadable whatever Excel permits, and the count that
  matters is already its own column. Every list cell in every workbook goes through it.
- **What it does not fix:** the file already produced. Excel emptied that cell on open; the next run writes it
  properly.
### The name behind the question mark (2026-09-25) — committed, awaiting merge
- **Maintainer:** several `?` on the BPMNs where a parameter name should be; find the name and show it.
- **Where it came from.** A condition's left side was printed as `entity.attribute` when a `GetEntityProperty`
  filled the operand variable, and as a literal `?` when nothing did. Nothing does whenever the designer compares
  what an activity handed back rather than a field — which is exactly the interesting case, and the one an
  analyst redrawing the process most needs named.
- **The name is in the definition.** `ExpressionIndex` now also indexes who WROTE each variable: an
  `ActivityReference`'s keyed `OutArgument`s and a custom activity's own output property elements. A condition on
  an activity output reads `CheckPolicyStatus.Durum Equal Aktif`. `ConvertCrmXrmTypes` is followed back the way a
  literal already was, so the converter never takes the credit.
- **No question mark is left anywhere.** When neither a read nor a writer is known the designer's own variable is
  printed: a poor name, but a traceable one — it can be searched for in the XAML, which `?` cannot. The same goes
  for the two halves of an `AND`/`OR`, and a missing comparison operator is now omitted rather than printed as a
  `?` that looks like an operator.
- **Not done:** relaxing `GetEntityProperty` to accept a read with no `EntityName`. It would name more subjects,
  but no sample shows that shape exists, and a fixture invented to justify it would prove nothing.
- **Acceptance on the company network — Do:** reprocess and search the BPMN folder for `?`. **Pass:** no
  condition label contains one. **Capture:** the count of labels of the form `Activity.Argument`, and one example
  label; no workflow names needed.
### Who may run which process (2026-09-26) — committed, awaiting merge
- **Maintainer:** who, or which group of users, is authorized to run which workflow — can it be extracted and
  presented as a list?
- **The premise needed correcting first.** CRM has no per-workflow permission. There is no record saying "role X
  may run workflow Y": starting one by hand needs `prvExecuteWorkflowJob`, which a role either carries or does
  not, for every process at once. So the answer comes in two halves and the workbook keeps them apart — joining
  them into one "these people can run this workflow" column would read as a grant CRM never makes.
- **Built:** `RoleRetriever` (GET only) reads the roles holding `prvExecuteWorkflowJob` and `prvReadWorkflow`,
  their depths, and who holds them; `calistirma-yetkisi.xlsx` carries **Çalıştırma yetkisi** (the roles) and **Kim
  çalıştırabilir** (per workflow: `elle_baslatilabilir`, `calisma_kimligi`, `sahip`, `sahip_turu`,
  `kaydin_is_birimi`). The browser export reads the same and is the path that will actually be used.
- **No person is named.** Role holders are COUNTED and teams are NAMED — which is also the better answer to
  "which group of users", and it keeps the staff list out of a file that leaves the building.
- **The intersect tables' entity set names are asked of metadata**, not assumed: `roleprivileges` is not
  `roleprivileges` in a URL and the spelling differs by version. Same reason `WorkflowColumns` checks its columns.
- **`_owningbusinessunit_value` added to the inventory columns** (a deviation from §3.1, noted at the site): a
  role's read depth is measured against the record's business unit, so without it nothing can be said at all.
- **Dropped as duplication:** a `RunAs` on `WorkflowIdentity`. `WorkflowTrigger` already carries it.
- **Cannot be seen, and the guide says so:** a process SHARED with one user or team. `principalobjectaccess` is
  not exposed on the Web API, so a share grants access this list will never show. Read it as "at least these".
- **Acceptance on the company network — Do:** re-drag the export bookmarklet, re-run it, then reprocess.
  **Pass:** the console prints `Run authority: N role(s), …`; `calistirma-yetkisi.xlsx` opens with both pages
  populated; no role on the roles page is one that can only READ processes. **Capture:** the `Run authority:` line,
  the role count, and the "Bu çalıştırma" row from the workbook's Nasıl okunur sheet. No user names needed — the
  export does not carry any.
- **Unverified from here:** every query in it. The entity set names, the intersect key spellings and whether a
  locked-down service account is allowed to read `roles` at all are settled only by the live server. A refusal is
  caught and becomes a note on the sheet rather than a failed run.
### The export died on its last line (2026-09-28) — committed, awaiting merge
- **Maintainer:** data retrieval moved to the TEST organisation and the export failed —
  `FAILED: RangeError: Invalid string length at JSON.stringify` after `XAML 5960/5960`.
- **What happened.** TEST holds 5960 definitions and activations against production's 1437. A browser refuses a
  single string past about 512 million characters, and 5960 XAML documents pass that on their own. The failure was
  at the very last line, after twenty minutes of reading: everything retrieved, then thrown away.
- **Fixed by never building that string.** `jsonPieces` writes the document as an ARRAY of pieces and hands it to
  `Blob`, which joins them itself. Only the outer two levels are split — that is where the big collections are —
  so the piece count stays in the tens of thousands rather than the millions.
- **Checked against the real failure.** The function was lifted out of the shipping source and run over ten shapes
  (undefined in an object and in an array, Turkish text, the characters JSON escapes, nesting past the split
  depth, empty containers): byte-identical to `JSON.stringify` on every one. Then over a 540-million-character
  document: `JSON.stringify` threw exactly `RangeError: Invalid string length`, while `jsonPieces` produced 12,007
  pieces totalling 540 million characters, longest piece 90 thousand.
- **The importer was measured at that size rather than assumed.** A 560 MB export parses in 845 ms with a peak
  working set of about 1 GB, so no change was needed on the C# side. The run folder will hold a second copy of
  the file in `ham/` plus 5960 XAML files.
- **Guarded permanently.** `RepositoryConventionTests` fails if `JSON.stringify(exported)` comes back — it is a
  one-word edit away, and the cost of it returning is another twenty-minute run lost.
- **Left alone on purpose:** the file size itself. Roughly 560 MB will now download where nothing did before. If
  carrying it is a problem, the next lever is gzip in the browser (`CompressionStream`) with the importer
  detecting the magic bytes, or de-duplicating XAML by content hash — an activation is usually a copy of its
  definition. Both want their own package and a real number from a TEST run first.
- **Acceptance on the company network — Do:** re-drag the export bookmarklet, re-run against TEST. **Pass:** the
  console ends with `Saved crm-export-….json — NNN MB` and the file downloads. **Capture:** that line.
### The export produced no file, and said nothing (2026-09-28) — committed, awaiting merge
- **Maintainer:** the export ran to the end of the plug-in registry and stopped. No file, no error, no `Done`.
- **What the silence meant.** The previous fix removed the `RangeError` by collecting every piece into an ARRAY,
  which holds the escaped copies BESIDE the originals: about a gigabyte of strings in one tab for the TEST
  organisation. Nothing threw — the page died between two lines, which is why `.catch` printed nothing. The last
  line printed was the stage before the write, and the write stage printed nothing at all until it was over.
- **Fixed by never holding it twice.** `jsonPieces` is now a GENERATOR, and the write loop hands the browser
  8 MB segments whose bytes leave the JavaScript heap as each `Blob` is made. Measured against the shipping
  source at 1500 / 3000 / 6000 definitions: **0 MB retained beyond the source document at every size**, where the
  array held all 560 MB. The generator is still byte-identical to `JSON.stringify` on ten shapes.
- **And by making it smaller.** Where `CompressionStream` exists the document is gzipped — XAML compresses about
  tenfold, so ~560 MB becomes ~40 MB, a file that downloads and can be carried. `BrowserExportImport` recognises
  it by its first two bytes, not its name, and keeps it verbatim in `ham/` as `.json.gz`. Three tests: a
  compressed export imports to the same run as a plain one, the evidence stays verbatim, and one saved with the
  wrong extension is still read. Checked by disabling the sniff — all three failed.
- **Every stage of the write now announces itself** (`Assembling…`, `Assembled N MB in M segment(s)`,
  `Compressed to N MB`, `Download started.`). The three silent early returns in `readRunAuthority` were also
  given a line each — that stage decided something in both lost runs and said nothing either time.
- **Acceptance on the company network — Do:** re-drag the bookmarklet, re-run against TEST. **Pass:** the console
  ends with `Download started.` then `Saved crm-export-….json.gz — NN MB`. **Capture:** the lines from
  `Assembling the file…` onwards. If it dies again, the last line printed now names the stage.
### A count of 5978 is a count (2026-09-28) — committed, awaiting merge
- **Maintainer:** the first TEST run produced no workbooks and no diagrams. It stopped at the inventory with
  `$count 5978 döndürdü; bu, Web API üst sınırı 5000 değerinde veya üzerindedir: güvenilir bir sayım değildir.`
- **The rule was wrong in two ways.** It read `apiCount >= CountCap`. But the Web API's ceiling works by
  ANSWERING WITH THE CEILING: a collection larger than 5000 makes `/$count` reply 5000, so a reply of 5978 cannot
  have come from a capped endpoint — it is a real number. And it was a real number that AGREED with the 5978
  records retrieved, which is the strongest evidence an inventory can have. The run's own §8 count chain said so
  on the line above — `kayıtlar: $count 5978 = alınan 5978 — uygun` — and the reconciliation failed anyway.
- **Now:** only a count EXACTLY at the ceiling is ambiguous, and that is said as a warning rather than acted on,
  because at that value a mismatch is as likely to be the ceiling as a fault. Above it the number is real and a
  disagreement still fails the run, as it always did. Below it, unchanged.
- **Written against production's shape.** The rule was fine while the organisation held 1437 workflows; the first
  estate larger than five thousand was the first to meet it, and it stopped a run that had done everything right.
- **Acceptance on the company network — Do:** re-run the extractor over the same export. **Pass:** exit code 0,
  and `raporlar/` holds six workbooks and the PDF, with `bpmn/` populated. **Capture:** the summary block.
- **Not touched:** `BpmnStage` fails the whole run when one diagram does not validate against the BPMN 2.0
  schema. On 1531 definitions that is a wider net than it was on production's, but it is a correctness gate and
  weakening it on suspicion would be wrong. If it fires, it names the file and the error.
### A diagram has to stay a diagram (2026-09-28) — committed, awaiting merge
- **Maintainer:** some BPMN files will not open; the conditional diamonds say only "Koşul" and never what the
  condition is. One unopenable file supplied.
- **Why it would not open.** One `serviceTask` carried a `name` of **30,730 characters** and the header note
  31,199: the whole address list of the assembly behind its custom activity — **494 addresses**, of which **463
  were `tempuri.org`**. The label was built as `Truncate(verb + subject, 80) + Target(...)`, so the target was
  appended AFTER the cut and bounded by nothing.
- **Three changes, one story.** `tempuri.org` is .NET's default SOAP namespace, so those 463 are operation NAMES,
  not places — the filter excluded only its root and now excludes it whole, on both the C# scanner and the
  browser export. The diagram now carries the HOSTS (four at most, three on this estate) and the full list stays
  on `dis-sistemler.xlsx`, where it already was. And no label may exceed `MaxLabel` however much a later change
  appends to it.
- **The diamond now asks a question.** It named the field when the definition gave one and said "Koşul" — the
  word "condition" — otherwise. It now names whatever the condition is about, including an activity output
  (`CheckPolicyStatus.Durum?`), and the gateway's documentation carries every branch's condition in full, which
  it never did.
- **Superseded test, changed deliberately:** `ExternalSystemsTests` asserted the full ADDRESS on the diagram.
  That is the behaviour that broke the file; it now asserts the host on the diagram and the address on the sheet.
- **Acceptance on the company network — Do:** reprocess and open the same file in Camunda Modeler. **Pass:** it
  opens; the custom-activity step reads `… → paygate.com.tr · www.fadata.bg`; the diamond asks a named question.
  **Capture:** the longest `name=` in the folder — a one-line PowerShell over the BPMN files will do.
### A gateway that decides nothing (2026-09-28) — committed, awaiting merge
- **Maintainer:** a condition step on the diagram that does nothing and has only one path; and the diamonds now
  carry a name but still not the comparison.
- **The empty diamond.** A condition whose branch STOPS the process has nothing coming back to be joined, but the
  join was drawn anyway: `SplitBlock` created one whenever ANY path continued, so the lone bypass arrived at a
  diamond with one flow in and one out. On the supplied file two of the four joins were like that. A join is now
  drawn only where two or more paths actually meet; below that the one continuing path IS the block's exit, and
  where that path is the bypass the flow onward carries the bypass's own caption and stays the split's default.
  Nothing is removed after the fact, so the layout reserves no room for a shape that is not there.
- **The comparison.** The diamond asked about `ConditionBranchStep12_1` — the designer's generated variable, which
  names nothing — while the comparison sat on the arrow alone. Where there is nothing to name (no field, and no
  activity behind the value either) the diamond now carries the comparison and the arrows answer `evet` / `hayır`
  rather than repeating it. Where the condition DOES name a field or an activity output, that stays the question
  and each arrow keeps its own comparison: that case was already right and is left alone.
- **Still not solved:** on the real data the operand of these conditions is written by something the parser does
  not recognise, so the subject stays a generated variable. Diagnosing it needs the XAML of one such workflow,
  which does not belong in a chat; the shapes of its `GetEntityProperty`/`EvaluateCondition` elements — names
  only, no values — would settle it.
- **Acceptance on the company network — Do:** reprocess and open `cti-telefon-gorusmelerini-kapat.bpmn`.
  **Pass:** no diamond with a single flow in and out; the first diamond reads
  `ConditionBranchStep12_1 NotEqual ps_activitytype` with `evet` / `hayır` on its arrows. **Capture:** a
  screenshot of the first two gateways.
### An empty page is not an answer (2026-09-28) — committed, awaiting merge
- **Maintainer:** the **Çalıştırma yetkisi** page of `calistirma-yetkisi.xlsx` is completely empty.
- **Two faults, and only one of them is certain.** Certain: a page with no rows carried its reason on a DIFFERENT
  tab. The reason was always written — as an `Eksik` row on **Nasıl okunur** and as a run warning — but a reader
  who opened this tab sees headers and nothing, which reads as a broken file rather than a refused query. The
  page now carries the reason on itself.
- **Suspected, and removed as a class:** the entity-set names were resolved with
  `EntityDefinitions?$filter=LogicalName eq 'roleprivileges' or …`. The metadata endpoint's `$filter` support is
  a narrow subset that differs by version, and a filter it will not honour returns NO ROWS rather than an error —
  which is indistinguishable from "this server has no such table" and takes the early exit that empties the page.
  Both the extractor and the browser export now ask for every entity's set name (two short columns, one request)
  and pick the three here. Nothing about it can be version-dependent any more.
- **This server is known to differ on privilege names:** the same run reported `prvReadProcessStage` as absent,
  so `prvExecuteWorkflowJob` being absent under that name is a live possibility too. If it is, the page will now
  say so in its own first row.
- **Acceptance on the company network — Do:** re-drag the export bookmarklet, re-export, reprocess. **Pass:** the
  page lists roles. **Capture:** if it is still one row, that row — it now names which of the four causes it was.
### The field was there all along (2026-09-29) — committed, awaiting merge
- **Maintainer asked** whether `ConditionBranchStep15_1` was really the name of the variable tested, and then ran
  a redacting one-liner over one definition's condition machinery (names and argument keys only, long and
  URL/GUID-shaped literals masked). It settled both open questions at once.
- **The read was there; the parser could not see it.** `GetEntityProperty` writes the variable it reads into as an
  ATTRIBUTE on this organisation's definitions — `<GetEntityProperty Attribute="statecode" EntityName="phonecall"
  Value="[ConditionBranchStep15_1]" />` — not as the `<GetEntityProperty.Value>` property element every fixture
  had. `AddRead` looked only at descendants, so every such condition lost its field. It now also reads the
  element's own attributes.
- **And the trap inside that fix**, caught by the test before it was committed: matching any attribute that
  parses as an identifier makes `Attribute="ps_activitytypeid"` win, indexing a field's NAME as the variable
  holding its value. Only a `[Bracketed]` reference counts.
- **A lookup names the table before the record.**
  `{ WorkflowPropertyType.EntityReference, "ps_activitytype", "INBOUND - GELEN ARAMA", <id>, "Lookup" }` — taking
  the first quoted string, right for every other type, reported the TABLE as the value compared against. That is
  where `NotEqual ps_activitytype` came from. For an `EntityReference` the second is taken instead.
- **What this is worth:** `ConditionBranchStep12_1 NotEqual ps_activitytype` becomes
  `phonecall.ps_activitytypeid NotEqual INBOUND - GELEN ARAMA`. It also fixes the same wrong value wherever a
  lookup is WRITTEN — `ps_activitysubresultid = ps_activitysubresult` was the table's name too.
- **Acceptance on the company network — Do:** reprocess and reopen `cti-telefon-gorusmelerini-kapat.bpmn`.
  **Pass:** the diamonds read `phonecall.<field>?` with the comparison on the arrows. **Capture:** the four
  gateway names.
### A decision that says what it decides (2026-10-02) — committed, awaiting merge
- **Maintainer, on `lead-lifecycle-appointment.bpmn`:** diamonds with one arrow in and one out still exist; the
  caption `(hiçbir koşul sağlanmazsa)` makes no sense; and `lead.leadid?` does not say whether it is a null check.
- **The diamond they saw is two merges back to back.** No gateway in that file is literally one-in-one-out — the
  previous package's rule holds. A condition nested inside another produces an inner merge and an outer one
  joined by a single arrow, and along the path a reader follows the second reads exactly like a shape that
  decides nothing. `FoldMergeGateways` folds an unnamed merge into the unnamed merge it feeds; nothing is lost,
  because everything arriving at the first was already going on to the second. Flows are RETARGETED, not rebuilt,
  so a split still points its `default` at the right one.
- **A pre-existing defect found while fixing it.** The builder decided which arrow was the default by matching
  its caption against "Aksi hâlde"; the parser writes "Otherwise". So on EVERY condition with an explicit else,
  the else lost its default marker and was given a second, unreachable "nothing matched" arrow beside it — which
  then needed a join to arrive at. The default is now the branch with no condition on it, which is what it is.
- **The captions.** `(hiçbir koşul sağlanmazsa)` described how this tool drew the picture. An arrow carries an
  OUTCOME: `hayır` where there was one test, `diğer` where there were several.
- **The question.** One test, and the diamond asks it in full — `lead.leadid NotNull` — with `evet` / `hayır` on
  the arrows, which answers "is that a null check?" by looking at it. Several tests on one field, and the diamond
  names the field once (`lead.prioritycode?`) while each arrow carries only its own operator and value
  (`Equal Düşük (2)`), rather than repeating the field on every arrow leaving the diamond that just named it. An
  author's own name for the step still wins over both.
- **Reversal, said plainly:** two packages ago the subject was deliberately kept on the diamond and the
  comparison on the arrow. The maintainer is right that it reads as a repetition and leaves the question
  unanswered.
- **Acceptance on the company network — Do:** reprocess, reopen the same file. **Pass:** one merge diamond where
  there were two; no `(hiçbir koşul sağlanmazsa)`; the first diamond reads `lead.leadid NotNull`.
### What a single-step diagram can be made to say (2026-10-02) — committed, awaiting merge
- **Maintainer:** the estate has many one-step diagrams whose only label is an obscure name; are those method
  calls, and can anything useful be extracted about what the step does?
- **Not a method — a CLASS.** `GNB_Workflow.Contact_CheckRetirementEligibilityByNova` is a .NET type registered in
  CRM as a custom workflow activity. CRM instantiates it and runs its fixed entry point; there is no method name
  to show. What it does inside is compiled and cannot be read from the definition, and the label was spending its
  whole budget on where that code LIVES — assembly, version, culture, public key token, cut off mid-word.
- **What the definition does hold, and now shows:** the names it is called with and the names it writes back.
  `CheckRetirementEligibility(ContactId) → CanProceed, WarningMessage` is the nearest thing to a description of a
  step whose body is opaque, and it was already parsed — it sat in the documentation while the label carried the
  token. Direction comes from the XAML (`OutArgument` against `InArgument`) and is NOT guessed where the shape
  does not say it: an argument written as a plain attribute keeps neutral wording, because CRM does write back
  through an attribute elsewhere.
- **And for an Action, its own signature.** `x:Members` declares what the workflow takes and returns with TYPES;
  CRM's plumbing (`InputEntities`, `CreatedEntities`, …) is dropped and whatever remains is the contract a caller
  sees. On a one-step Action that is the only thing on the page worth reading, and nothing outside the compiled
  code describes it. A plain workflow declares only plumbing and gains no line at all.
- **Still not extractable:** which data a custom activity changes. It opens its own connection inside compiled
  code; the definition cannot see it and neither can this tool. Said plainly rather than guessed at.
- **Acceptance on the company network — Do:** reprocess, open
  `checkretirementeligibilitybeforeemeklilikhakedis.bpmn`. **Pass:** the step reads
  `Özel etkinlik: Contact_CheckRetirementEligibilityByNova(…) → …` and the note carries a `Parametreler:` line.
### Operators in the words a reader uses (2026-10-02) — committed, awaiting merge
- **Maintainer:** render CRM's condition operators in Turkish across diagrams and sheets.
- **`ConditionWords`** is the fourth table carrying the output's Turkish, beside `RunPaths`, `RunStages` and
  `ProcessLabels`. It sits in `Crm.Ir` because the predicate's text is composed there and read from the diagram
  and the sheets alike: `lead.leadid NotNull` becomes `lead.leadid dolu`, `Equal` becomes `=`, `In` becomes
  `şunlardan biri:`, and `AND`/`OR` become `VE`/`VEYA`.
- **The operator's NAME is untouched.** `Predicate.Operator` is a comparison key — two workflows testing the same
  field the same way must still fold into one family whatever the wording is — so only the text a person reads
  changes. A test pins both halves of that at once.
- **An operator nobody translated keeps CRM's own word.** The list is CRM's and it can grow; a guessed Turkish
  rendering would read as fact where the untranslated name reads as what it is.
- **A fragile thing removed on the way.** `Asked` found the subject by searching the predicate's text for the
  operator's name — which stops working the moment the operator is written in words. The subject is now carried
  on the predicate, where it was computed anyway.
- **Acceptance on the company network — Do:** reprocess. **Pass:** diamonds read `lead.leadid dolu`,
  `lead.prioritycode?` with `= Düşük (2)` on its arrows. **Capture:** one diamond and its arrows.
### A caption belongs to its line (2026-10-02) — committed, awaiting merge
- **Maintainer:** the "evet" and "hayır" captions are way off; and never shorten a condition's text.
- **They were placed against the TARGET shape** — centred over it, 8 above it — rather than against the flow they
  belong to. So "evet" landed past the arrowhead, floating over the task it pointed at, and "hayır" at the far
  end of a flow three hundred pixels long, beside the diamond it ARRIVED at rather than the one it left.
  Measured on the reported file: 97 pixels from the line in the first case.
- **A caption is now measured along its own flow:** halfway by LENGTH, not by counting corners — a flow that
  leaves a gateway sideways and then runs level is two segments of very different sizes, and its middle is the
  point an eye follows. Above a level run, beside an upright one.
- **Conditions are no longer shortened at all.** The gateway name and the branch captions were cut at 60
  characters, which is why a diamond read `(phonecall.ps_activitysubresultid = Satış Yapıldı) VE …` and hid the
  half that decides. Everything else on a diagram may be cut back to what fits; the test a process turns on may
  not. The ceiling that exists elsewhere is there because an ADDRESS LIST reached thirty thousand characters on
  one label; a condition is bounded by the fields it names and cannot run away like that. The label box also
  grows to twelve lines rather than five, so a long one is drawn inside its own bounds.
- **The superseded test said the caption sits above its target shape** — the behaviour at fault. It now measures
  the distance from the caption's centre to the nearest point on its own flow and fails past thirty pixels, which
  is what "looks about right" failed to catch.
- **Acceptance on the company network — Do:** reprocess, reopen
  `create-campaign-response-for-phonecall.bpmn`. **Pass:** each `evet` sits on its own arrow between the diamond
  and the task; each `hayır` sits on the long arrow between diamonds; the diamonds read their conditions whole.
### The box grows, the text stays (2026-10-02) — committed, awaiting merge
- **Maintainer:** stop shortening what goes into steps as well; draw the boxes larger to hold it.
- **What was being thrown away.** A create-record step writing ten fields was labelled
  `campaignresponse · customer, prioritycode, ps_campaignresponseresultid, …` — seven of the ten gone, and those
  fields are the substance of the step for anyone rebuilding it. Also cut: the step label at 140 characters, a
  child workflow's name and an activity's host at 60, and a custom activity's signature at 60 in and 50 out.
  All of them now arrive whole.
- **The box is sized from its text instead.** `FlowNode.Size` takes the label: a task grows WIDER first, to 300,
  then TALLER, in steps of ten pixels so the layout stays on a grid. Short steps keep the ordinary 130×70. The
  block layout reads `Width`/`Height` already, so the diagram reflows around the bigger boxes on its own.
- **Growing the text without growing the box would only have moved the problem** — the viewer draws the overflow
  over whatever is below. A test checks every task in six fixtures can hold its own text at the width it was
  given, and it was confirmed by pinning the box back to 130×70: two fixtures then needed 90 and 60 pixels of 54.
- **`MaxLabel` raised to 2000 and re-described as what it is:** a guard against a definition nobody has seen,
  not a style rule. Everything that once fed the thirty-thousand-character label is bounded at its source now,
  and no label the estate produces comes within an order of magnitude of it.
- **Acceptance on the company network — Do:** reprocess, open a create-record step with many fields.
  **Pass:** every field it writes is named on the box, and the text sits inside the border.
### The guide in the department's own hand (2026-10-02) — committed, awaiting merge
- **Maintainer:** this first version goes to the outsource partner as the ORIGINAL diagrams only — no `birlesik/`,
  no `aileler.xlsx` — and `nasil-kullanilir` takes the layout of the department's own standard document
  (`AHE-BT-EY-STD_V2.2`, supplied 2026-10-02) and becomes a .docx. **Decision taken on being asked:** DOCX only,
  the PDF dropped rather than kept beside it.
- **Why the format was the right thing to change.** The hand-written PDF writer paginated by counting lines,
  needed a TrueType font off the machine to spell ğ, ı and ş at all, and produced a file nobody could edit. Word
  paginates, carries the PNG as a file, and hyphenates Turkish. `PdfDocument` and `TrueTypeFont` are gone — 713
  lines — and `PngImage` is down to reading IHDR, because a .docx needs the logo's size and not its pixels.
- **`WordDocument` writes the .docx by hand, as `ExcelWorkbook` writes .xlsx:** a zip of XML, no dependency.
  `DocumentPart` is the vocabulary — `Paragraph`, `Table`, `PageBreak`, `Logo` — and the styles carry the standard:
  A4 with its margins, Arial 11 justified, headings in its navy, a cover with the document's particulars, a
  revision table and a contents list.
- **The trap that would have reached the reader's desk.** WordprocessingML fixes the ORDER of the children of
  `w:pPr` and `w:rPr`. The first draft emitted `outlineLvl` before `spacing` and `sz` before `color`, which is a
  file Word calls unreadable and "repairs" — which is to say throws away, silently, after the package has left.
  `Style` now takes a `Look` record and emits in schema order in one place, and a test reads the order back out of
  the styles; confirmed by putting each fault back, one at a time.
- **What the partner is given is now the whole truth of what they have.** `aileler.xlsx` and `birlesik/` are still
  produced and still read here, but the plan lost its `aile`, `aile_rolu` and `birlesik_dosya` columns, the guide
  its families section, and `DeliveryTests` now fails on `aileler` or `birlesik` in any delivered cell — a column
  pointing at a folder the reader has not got sends them looking for it.
- **Acceptance on the company network — Do:** reprocess, open `raporlar/nasil-kullanilir.docx` in Word.
  **Pass:** it opens with no repair prompt, the cover carries the logo and the particulars, the headings appear in
  the navigation pane, Turkish is spelt right throughout, and nothing in it names a file outside the delivery.
### The characters, not their references (2026-10-08) — committed, awaiting merge
- **Maintainer:** a weird text fragment in some labels — `&#160`.
- **Where it comes from.** A step's label is the `DisplayName` of the `Sequence` CRM's designer wraps the step in,
  and that text is typed into a web designer which leaves HTML's own references in it. The XAML therefore escapes
  them a second time; reading the XAML as XML peels one layer and hands the parser `&#160` as five characters of
  text. Nothing in this tool put it there and nothing was removing it, so it went straight onto the diagram. The
  missing semicolon is CRM's, not ours: the designer writes `&#160` with nothing after it, which every browser
  reads as a space.
- **`HtmlEntities.Decode` at the three places CRM text enters the IR:** `XamlNames.DisplayName`, a condition's
  `Description`, and every quoted literal in `ExpressionIndex` — the last of which is also what the sensitive scan
  and `dis-sistemler.xlsx` read, so an address stops arriving with its `&amp;` showing.
- **The two rules that keep it honest.** ONE PASS, never until nothing changes: text that really says `&amp;` means
  an ampersand followed by "amp;", and a second pass would hand the reader something CRM never held. And a
  reference nobody there knows is left verbatim — a guess on a diagram is worse than a visible oddity. A semicolon
  is optional on a numeric reference (the reported shape) and required on a named one, because `&nbspAdı` is as
  likely to be someone's text as a dropped semicolon.
- **A reference to a character no document may carry is dropped**, as `ExcelWorkbook` and `WordDocument` already
  drop a control character scanned out of a binary: `&#0;`, a lone surrogate and seven digits are refused, and
  `&#3;` resolves to nothing rather than to a character that would make the BPMN unwritable.
- **A non-breaking space becomes an ORDINARY space.** Keeping U+00A0 would leave two labels that read identically
  comparing unequal, and no reader can tell them apart anyway.
- **`entity-references-in-labels.xaml` records the real shape**, and the fix was confirmed by taking it out: the
  step then read `Müşteri&#160Adı&nbsp;güncelle`, which is the fault as reported. 28 new tests, 330 in all.
- **Acceptance on the company network — Do:** reprocess, reopen a diagram whose labels showed the fragment.
  **Pass:** the words read with spaces between them and no `&#` or `&…;` appears on any label, in any plan cell,
  or in the guide.
### What no document may carry (2026-10-08) — committed, awaiting merge
- **Maintainer:** close the BPMN control-character gap, reported out of the previous package's section 4.
- **It was worse than the finding said.** I had written that a control character makes the diagram "fail to write
  rather than render oddly". Measured: `BpmnStage`'s loop has no guard and runs at line 36 of `OfflineStages`,
  BEFORE consolidation and every report, and `Program.Main` catches nothing. One stray byte in one workflow name
  took the whole run with it — no workbooks, no guide, an unhandled `ArgumentException`. Proved before fixing, with
  a probe that failed on `'', hexadecimal value 0x01, is an invalid character`.
- **How one reaches a label at all.** Not through the XAML: that is XML and could not carry it. Through the
  inventory, which is JSON, and through `AssemblyStrings`, which reads bytes. `HtmlEntities` closed a third route
  last package by dropping `&#3;` rather than resolving it.
- **`DocumentText.Writable` is the rule, written once.** The gap existed BECAUSE the rule had been written twice —
  in `ExcelWorkbook` and in `WordDocument` — and missed in the third writer. All three now call it, and the two
  that had their own copies gained what those copies lacked: a lone surrogate, U+FFFE, U+FFFF.
- **A lone surrogate is the case that nearly got away.** `XmlConvert.IsXmlChar` says YES to a high half, because
  inside a pair it is character data, so the first draft let it through; the test caught it. A pair is kept or
  dropped as one thing. Also dropped: the C1 range, which XML permits and no reader can see.
- **`BpmnSerializer` sweeps the BUILT document**, not each of the dozen places text goes in — the version a later
  site cannot forget to call. `XmlText` was the honest name and collided with `System.Xml.XmlText` in exactly the
  files that needed it, so it is `DocumentText`.
- **A test trap worth remembering:** xUnit cannot carry a lone surrogate through `InlineData` — it arrives as the
  replacement character and the assertion then compares two legal strings. Those cases are a `[Fact]`. And a raw
  control character in a source file is invisible to the next reader, so the test writes `\u0001` as an escape.
- 16 new tests, 345 in all; confirmed by taking the sweep out, which restores the original exception.
- **Acceptance on the company network — Do:** reprocess. **Pass:** the run completes and writes every workbook
  even if a workflow name carries a stray byte; no label shows a replacement character.

### One workflow may not cost the run (2026-10-08) — committed, awaiting merge
- **Maintainer:** add the per-workflow guard, recorded as item 4 of section 4 in the previous package.
- **Each diagram is drawn inside a guard.** `catch (Exception error) when (error is not OperationCanceledException)`
  — a filter rather than a catch-and-rethrow, because a filter that does not match never unwinds the stack, so a
  debugger still stops at the throw. A cancellation is the operator's and travels; everything else belongs to the
  one workflow being drawn.
- **What a loss costs is now exactly that diagram.** The failure is recorded against the workflow's NAME, so a
  human knows which one to look at; the exit code becomes a failure, as an invalid schema already did; and the
  workflow is dropped from `state.BpmnFiles`, so the plan's `bpmn_dosyasi` cell stays empty and the guide walks
  the reader through a different workflow. Every consumer already read that dictionary with `TryGetValue` or
  `ContainsKey`, so nothing had to change to make an absent file mean "no file".
- **`bpmn.written` now counts what was WRITTEN** and `bpmn.lost` what was not, so the count chain shows a loss as
  a drop rather than hiding it behind a number that still says 1437.
- **The test needed a failure nobody had anticipated**, which is the whole point of a guard, so it uses one the
  code already refuses: two steps sharing one path, which `FlowGraph` rejects with "Duplicate BPMN element id".
  That is a malformed intermediate model — a parser fault, not a data one — and it stands for the class.
- **Both halves were confirmed by breaking them.** Disabling the filter makes the exception propagate out of the
  stage again; widening it to catch everything makes the cancellation test fail, which is the half that would
  otherwise have turned an operator's stop into 1437 recorded failures and a run that carried on regardless.
- 2 new tests, 347 in all.
- **Acceptance on the company network — Do:** reprocess. **Pass:** if any diagram fails, the run still writes
  every workbook and the guide, `rapor.md` names the workflow that failed, and that workflow's plan row has an
  empty `bpmn_dosyasi` cell rather than naming a file that is not there.
### A slug is a folder name too (2026-10-08) — committed, awaiting merge
- **Maintainer:** rule out the NUL case, noted in the previous package's section 4.
- **I had it half wrong, and measuring said so.** I had written that a workflow named `NUL` would slug to
  `nul.bpmn`, which `CreateFile` opens as the null device, leaving a silently empty file. Measured on this machine
  (Windows 11, 2026-10-08): `nul.bpmn`, `con.bpmn`, `aux.bpmn`, `com1.bpmn` and `lpt1.bpmn` all write as ordinary
  files with the right length. The FILE half is not a fault here.
- **The folder half is.** A diagram's path is `bpmn/<kategori>/<birincil varlık>/<ad>.bpmn`, so two of the three
  slugs are DIRECTORY names with no extension, and `mkdir nul` throws — measured. A CRM entity whose logical name
  is one of these words would lose every diagram under it. With last package's guard that is now a quiet loss of
  those diagrams instead of the run, which is better and still wrong.
- **Fixed in all three positions, not the two that are provably broken.** The target is a locked-down host nobody
  here can test, and on every Windows before 11 the file half was a fault too. An exact device word gets
  `-ayrilmis`: `nul` → `nul-ayrilmis`. Only an EXACT match counts — `console`, `com10`, `nula` and `com` are
  ordinary names and are untouched.
- **The trailing-dot class was already impossible** and is now said out loud in the doc comment: Windows strips a
  trailing dot or space silently, so two names differing only there would land on one file — but a slug holds
  nothing but lower-case ASCII, digits and single hyphens, so neither can occur.
- 16 new tests, 363 in all. The end-to-end one drives `BpmnStage` with an entity called `nul` and fails without
  the fix.
- **Acceptance on the company network — Do:** reprocess. **Pass:** `bpmn/` has no empty file, and if any folder
  is named for a device word it reads `…-ayrilmis` and has the diagrams in it.

### A block hangs from its spine (2026-10-08) — committed, awaiting merge
- **Maintainer:** `check-campaign-field.bpmn` still places labels and arrows wrongly; fix it without undoing the
  earlier placement fixes.
- **Three faults, one cause.** The flow leaving the first diamond ran straight THROUGH an end event; the two long
  branch captions overlapped each other; and the `diğer` caption landed on a shape. All of it came from aligning
  blocks on their BOUNDING-BOX CENTRE. That condition has two tests, each ending the process, and no
  otherwise-branch — so the tool adds the arrow taken when neither held, giving three rows, and a diamond centred
  on the block sits on the middle one. The flow leaving it runs along that line, and row two was standing there.
- **Why no fixture caught it.** An EVEN number of rows puts the centre between rows and hides the whole thing.
  Every condition fixture has one or two branches. `CREATE_CAMPAIGN_RESPONSE_FOR_PHONECALL`, which the maintainer
  confirmed reads correctly, has three diamonds with one branch each — two rows apiece.
- **Blocks now hang from a SPINE:** the height a block's flow enters and leaves at. For a split left through the
  diamond itself, that is the row its continuation takes; where the paths meet at a join, it is the middle, as
  before. `SequenceBlock` aligns its blocks on spines rather than centres, which also keeps a chain of diamonds on
  one line by construction instead of by luck of parity — `The_Start_And_The_Diamond_Stay_On_One_Line` pins that,
  and it passed BEFORE the fix too, so the earlier work is demonstrably still standing.
- **`LabelBox` is now the one measurement of a label**, used by the layout to reserve room and by the serializer
  to write bounds. The gap was a constant 120 "room for a five-line caption" while a caption, since conditions
  stopped being shortened, can be 162 pixels. Reserved from it: a corridor at least a caption wide between a
  diamond and its branches, a row gap at least a caption tall, and — the one I missed on the first pass — the
  text of a shape that draws its text OUTSIDE itself, which an end event does and which the row below was ignoring.
- **A caption goes on the middle of its flow's longest LEVEL run.** Two branches share the upright line out of
  their diamond, so two captions measured along it are drawn on top of one another. Each branch's level run is at
  its own height. Where the legs are the ordinary short ones this is the same place "halfway along" already chose,
  which is why the maintainer-approved file is unchanged in character.
- **The caption guard was measuring the wrong thing.** It asked that a caption's CENTRE be within 30 pixels of its
  flow, which a 110-pixel caption cannot be however well it is placed. It now measures from the box's nearest
  edge and asks for 10; confirmed by moving every caption 40 pixels off its line, which fails all four fixtures.
- **Three new guards, and one that did not exist at all:** no flow passes through a shape it is not an end of; no
  label lands on a shape; no label lands on ANOTHER label — nothing was watching for that anywhere. Each runs over
  one, two and three branches so no parity can hide a fault again.
- 8 new tests, 383 in all. Confirmed by reverting the spine, which restores the arrow through the end event.
- **The cost, stated plainly:** these diagrams are taller. A three-row condition now spans about 700 pixels where
  it spanned 350. That is the price of "never shorten a condition" — the room was always needed; it just was not
  being reserved, so the text was drawn over whatever was under it.
- **Acceptance on the company network — Do:** reprocess, reopen `check-campaign-field.bpmn` and
  `create-campaign-response-for-phonecall.bpmn`. **Pass:** on the first, no arrow crosses a shape and no two
  captions touch; on the second, the diamonds still sit on one line and `evet`/`hayır` are where they were.
### What a step writes, and what it writes there (2026-10-08) — committed, awaiting merge
- **Maintainer:** show the static values on `Kayıt oluştur`/`Kayıt güncelle` boxes and only the static ones; show
  the state on `Durum değiştir`; take `calistirma-yetkisi.xlsx` out of what the outsource partner is given.
- **The values were already parsed.** The box named the fields and the documentation held the values, so the
  substance of a step — what it sets them TO — was readable only by opening the XML. `Written` now renders a field
  as `description = …` where the definition fixes the value, and the documentation and the box go through ONE
  rendering function so they cannot drift. An option keeps its number behind its label: `Aramadan İşlem
  Yapılmıştır (3)` — the label for the reader, the number for CRM.
- **A dynamic value is not shown at all.** `= <dynamic>` costs a line of the box to tell a reader nothing they
  could not already see, so that field is named alone beside the ones that say something. A field needs ALL its
  values fixed to be shown with them: half a list is worse than none.
- **`LiteralValue.Dynamic` moved the marker onto the MODEL.** It lived on `ExpressionIndex`, which is internal to
  `Crm.Ir`, so no stage after the parser could tell a fixed value from a run-time one — which is exactly the
  question every stage now has to answer. `ExpressionIndex.Dynamic` still exists and refers to it, so there is
  one string.
- **`Durum değiştir` never said which status, and the fixture had the answer all along.** `SetState` is CRM's own
  message, not a field write, and sets `statecode` and `statuscode` as a pair — so the state is not a `FieldWrite`
  and has to be read off the step. It arrives TWO ways and only one was being read: a state built through an
  expression is a variable the index resolves, while a state typed into the designer is a literal nested as
  `<mxs:OptionSetValue Value="1" />` under `SetState.State`, with no variable anywhere. `production-helpers.xaml`
  has carried that shape since the fixtures were recorded; nothing was looking at it. Each number is resolved
  against the attribute it belongs to, and falls back to the bare number rather than to nothing.
- **`calistirma-yetkisi.xlsx` is internal.** It names security roles and counts the people holding each: the
  estate's own business, not a migrator's. It is still produced. The guide lost its section 2.5 and its row in the
  opening table, the walkthrough step that sent a reader to it now answers the trigger question from the plan and
  the diagram, `DeliveryTests` moved it to the not-delivered list, and `AnalystGuideTests` now requires its
  ABSENCE where it used to require its presence — the assertion was flipped, not deleted.
- 5 new tests, 388 in all. Each fix confirmed by taking it out: the boxes lose their values, the status change
  loses its state, and the guide fails the delivery guard on a re-added row.
- **Acceptance on the company network — Do:** reprocess, open `close-cti-calls-systemclosed.bpmn`. **Pass:** the
  update box reads `description = …`, `ps_activityresultcode = 99`, `ps_activitysubresultid = … (…)`; the next box
  says which status it changes to; and nothing in `nasil-kullanilir.docx` mentions `calistirma-yetkisi.xlsx`.
