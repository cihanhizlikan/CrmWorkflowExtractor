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
