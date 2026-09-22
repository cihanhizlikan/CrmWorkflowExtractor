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
