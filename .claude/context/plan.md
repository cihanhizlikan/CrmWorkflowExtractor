# Plan — CRM Workflow Extractor & BPMN Transformer

Written 2026-09-14. Organized in the four sections of handout §9. Decisions the maintainer has taken are marked
**DECIDED** with their date.

## Status

| Package | State |
|---|---|
| Seed (context, rules, wp-* skills, build profile) | committed on `main` (initial commit of an empty repo) |
| M1 — inventory, privilege check, count reconciliation | **committed on `feature/m1-inventory`, awaiting merge.** Verified against a synthetic fake server only; its gate needs the company network (see *Acceptance*) |
| M2–M6, M5b | not started — M2 waits for the M1 acceptance run (handout §11: stop at M1) |

## Handout disagreements found while building

| Handout | Finding | Source | Status |
|---|---|---|---|
| §3.1 selects `parentworkflowid`, `activeworkflowid` | Lookups are selectable only as `_parentworkflowid_value` / `_activeworkflowid_value`; the bare name fails the whole query | Web API query documentation (lookup properties) | Applied in M1; confirm on live server |
| §2.4 "reconcile `$count` against records" catches the privilege trap | It cannot: both pass through the same security filter. Replaced as the primary check by `RetrieveUserPrivileges` depth | Reasoning; confirm on live server with a user-level account if one is available | Applied in M1 |

## Pre-flight facts (checked on the development machine, 2026-09-14)

| Check | Result |
|---|---|
| `csharp-context-maintenance` / `csharp-build-profile` / `csharp-style-conformance` skills | **Not installed anywhere.** Replaced by Yalbuz's workflow — **DECIDED 2026-09-14.** |
| .NET SDK | 10.0.401 only → `net10.0`. |
| CRM host / org | Unknown. The development machine is Windows 11 Home (cannot domain-join): **no live verification from here.** |
| Yalbuz fold (`ScreenConceptBuilder.DiacriticFold`) | 12-entry table + `char.ToLowerInvariant`; misses `â`/`î`/`û` and decomposed `I`+U+0307. |

## Decisions taken

- **DECIDED 2026-09-14:** adopt Yalbuz's workflow (context files, working agreement, `wp-start`/`wp-verify`/`wp-finish`
  adapted; `sweep` not adopted).
- **DECIDED 2026-09-14:** all 13 Yalbuz style rules apply.
- **DECIDED 2026-09-14:** repository at `repos\CrmWorkflowExtractor`, new `git init`, no remote.
- **DECIDED 2026-09-14 — overrides handout §1 non-goal "no automatic consolidation, merging":** the tool combines
  each similarity family into one workflow, like a set union, and presents the combined workflows as output. The
  unconsolidated per-workflow BPMN files remain, and their purpose is to let a human check that the right workflows
  were combined in a meaningful way. Still no writes to CRM and no deletion anywhere.

## Consolidation design (new stage, follows similarity)

**Proposed, not yet built — the open points are in section 2.**

- **Input:** a cluster from §7.3 and its members' IR documents. **Output:** `consolidated/<clusterId>.json`
  (combined IR), `consolidated/<clusterId>.bpmn`, and `reports/consolidation.md`.
- **Union as a prefix tree over step paths.** Every member's step tree is walked with the §7.1 literal-stripped
  step tokens. Steps with equal tokens at the same position in the same branch context are ONE combined step;
  where members diverge, the combined workflow gets an `exclusiveGateway` whose branches are labelled with the
  member set that takes them ("variant: workflows A, C"). A step present in every member is unconditional; a step
  present in some is behind a variant gateway. That is exactly a set union of the members' behaviour, with no step
  lost and none invented.
- **Literals are unioned, not chosen.** A combined `UpdateRecord` of `policy.status` records every value its members
  set, each with its source workflow (`100000003` ← A, `100000007` ← B). The raw and resolved values both survive.
- **Triggers are unioned** the same way: the combined start event lists every entity/field/stage trigger and which
  members declared it. Differing primary entities are a hard stop for that cluster — see section 2.
- **Provenance on every combined element:** the sorted set of `(workflowid, step path)` it was built from, in both
  `<documentation>` and the extension block, so every combined box traces back to the original boxes.
- **Deterministic:** member order is sorted by `workflowid`, so the same cluster always produces byte-identical output.
- **Validated like every BPMN file** (XSD fatal), and reconciled: every step of every member must appear in the
  combined workflow's provenance exactly once — a combined workflow that lost a step fails the run.

---

## 1. Confident improvements within scope

1. **A real privilege check, not only count reconciliation.** `$count` and the paged retrieval run under the *same*
   security filter: a user-level-read account gets a small count, retrieves exactly that many, and reconciles cleanly.
   The §2.4 reconciliation catches paging bugs; it **cannot** detect the privilege trap. Added at run start:
   `WhoAmI()` → `systemusers(<id>)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()` (both GET functions), asserting
   Global depth on read for Process, Process Stage, System Job, Plug-in Assembly, Plug-in Type and SDK Message
   Processing Step. The `$count` reconciliation and distinct-owner warning stay too.
2. **`$count` 5000 cap guard** — assert count < 5000 so the check cannot silently become a lie.
3. **IFD detection as a hard stop** — `WWW-Authenticate: Negotiate/NTLM` = on-premises; redirect to `/adfs/` or
   `Bearer authorization_uri` = IFD, stop and report.
4. **Structural read-only, build-enforced.** `BannedSymbols.txt` bans every `HttpClient` constructor, every send/verb
   helper and the non-GET `HttpMethod` members, with RS0030 as an error. `CrmHttpClient` holds the single suppressed
   call site, guarded to GET, and refuses any absolute URI (including `@odata.nextLink`) outside the org base. Tests
   assert the guard and that nothing but GET reaches a fake handler.
5. **Explained-gap accounting** reconciling §8 "fail on unexplained gap" with §10 "a failure on 200 must not discard
   199": every gap is a named bucket in the manifest; a non-zero residual fails; parse/BPMN failures seal the run as
   `completed-with-failures` with a non-zero exit code.
6. **Fold** = Yalbuz table + `FormD` + drop `NonSpacingMark` + `ToLowerInvariant`, copied with attribution.
7. **Drift compares raw hash and canonicalized XML**, reported separately.
8. **Deterministic serialization** — explicit property order, sorted collections, invariant culture, UTC ISO-8601,
   LF, no BOM for JSON/XML/BPMN.
9. **Bounded retry, hand-rolled** — 5 attempts on 408/429/5xx/`HttpRequestException`/`IOException`, jittered
   exponential backoff, honours `Retry-After`.
10. **Stable BPMN ids** `wf_<workflowid:N>_<path>`; byte-identical re-emission tested.
11. **XSD validation is fatal** per file (counts into the non-zero exit code).

## 2. Wanted but uncertain — need the maintainer's decision

1. ~~Seed skills~~ — DECIDED.
2. ~~Repo location~~ — DECIDED.
3. **Live CRM access.** Who runs M1 on the corporate network, host + org, and can `raw/` and `logs/` be sent back?
4. **Target runtime on the run host.** .NET 10 installed, or a self-contained single-file `win-x64` publish?
5. **Downloading the OMG BPMN 2.0 XSDs** (needed at M4) — needs permission.
6. **Drift report expectations.** If 8.2 deletes the activation record on deactivation, drift is near-empty and the
   valuable list is "Draft definitions modified after last activation". Emit both; settle at M2 on live data.
7. **`cluster-lineage.csv`** mapping clusters to the previous sealed run's clusters by membership Jaccard?
8. **Excel in tr-TR:** `;`-delimited + UTF-8 BOM + `,` decimals, and/or `clusters.xlsx` (adds a dependency)? Turkish
   column headers?
9. **`out/` holds the secrets the scan redacts** — its location and ACL are the security team's decision.
10. **Consolidation — which clusters get combined.** Recommendation: combine only clusters that are NOT flagged
    `low-cohesion`; a chained cluster (A–B–C with A and C unrelated) would produce a union that is technically
    complete and meaningless. Low-cohesion clusters are listed in `consolidation.md` as "not combined, split first".
    Alternative: combine everything and let the check-against-originals catch it. Which?
11. **Consolidation — a prefix union duplicates shared tails.** Two members that diverge at step 3 and share steps
    6–9 get 6–9 twice, once per branch. Merging common suffixes back (a converging gateway before 6) is more faithful
    to "set union" and harder to explain. Recommendation: prefix union first (M5b), measure how often shared tails
    appear on real data, then decide. Agree?
12. **Consolidation — members with different primary entities or categories** (a Workflow and an Action; `policy`
    vs `claim`). Recommendation: never combined, even when similar — reported as a finding instead. Agree?
13. **Does the architects' `decision` column in `clusters.csv` feed back?** E.g. a second run reads an annotated CSV
    and excludes members marked "do not combine". Useful, but it makes the output depend on a hand-edited file.

## 3. Left to judgement — cheap to overrule

1. **Immutable vs resumable:** sealed = `manifest.json` written last; an unsealed run is resumed
   (`ResumeIncompleteRun`, default true). Cross-run XAML skip copies from the latest sealed run by
   `(workflowid, versionnumber)` and re-verifies the hash.
2. **Manual-review records and templates get no BPMN** — explained exclusions.
3. **No server-side `$filter` in the inventory pass** — classify client-side so counts cover everything.
4. **Business rules** parsed normally, gaps surface in coverage; **BPF** stages → `subProcess`.
5. **Configuration precedence:** `Program.cs` constants → `appsettings.json` → `appsettings.Development.json` →
   environment (`Crm__…`) → Credential Manager for the password only (P/Invoke `CredReadW`).
6. **Metadata cache** at `out/cache/metadata/`, copied into each run.
7. **Dependencies:** `Microsoft.Extensions.{Configuration.Json, Configuration.EnvironmentVariables,
   Configuration.Binder, Options, Logging}` (first-party) + xUnit v3. File logger and Jaro–Winkler hand-rolled.
8. **Unknown option set values** → raw int + `Unknown(<n>)` + warning.
9. **`uidata` spike** timeboxed during M2.
10. **The plan lives in the repository** (`.claude/context/plan.md`), unlike Yalbuz's, so git protects it.
11. **Consolidation gets its own project, `Crm.Consolidation`** (→ `Crm.Ir`, `Crm.Similarity`), a seventh beside
    the handout's six: similarity *scores*, consolidation *combines*, and either is testable without the other.
    Created at M5b, not before.
12. **Milestones re-sequenced:** M5 similarity + clusters → **M5b consolidation** → M6 reports + end-to-end.
13. **(M1) Insufficient privilege fails the run but the inventory still executes**, so the retrieved count exists to
    compare with an administrator's. The run is sealed `failed`, so the output cannot pass as complete.
14. **(M1) HTTP 500 is retried** (bounded, 5 attempts): CRM reports SQL timeouts and deadlocks as 500. A
    deterministic 500 costs a few backed-off seconds.
15. **(M1) Run folder names use UTC**, and a same-second clash gets `-2`, never an overwrite.
16. **(M1) A relative `Output:Root` resolves against the executable's folder**, not the working directory, because a
    double-clicked or scheduled run on the locked-down host has an unpredictable working directory.
17. **(M1) Every final API response body is kept verbatim** under `raw/http/NNNN.body` with `raw/http/index.jsonl`,
    in addition to `raw/workflows.jsonl` — the handout asks for verbatim payloads, and the preflight responses
    (who the user was, which privileges) are evidence too.
18. **(M1) Configuration environment-variable prefix is `CRMEXTRACT_`** (e.g. `CRMEXTRACT_Crm__WebApiBaseUrl`).
19. **(M1) Read-only is also build-enforced**: `BannedSymbols.txt` bans `HttpClient` construction, every send/verb
    helper and non-GET `HttpMethod` members; a probe using `HttpMethod.Post` failed the build with RS0030 (verified).
20. **(M1) Option-set labels are cross-checked, not trusted**: each record's FormattedValue annotation is recorded
    beside the handout's label in `reports/inventory.md`.

## 4. Out-of-scope findings — recorded, not acted on

1. Yalbuz `ScreenConceptBuilder` fold misses `â`/`î`/`û` and decomposed `İ`.
2. The handout's 4 style rules are a stale subset of Yalbuz's 13.
3. The plugin-step retrieval yields the partner's full integration inventory; only the flag-field join is reported.
4. CRM 8.2 out of extended support, running partner code that may hold credentials — EA/security risk.

---

## Acceptance on the company network

Items only a run on the corporate network can close. Each has a **Do**, a **Pass** and a **Capture**.

### M1-A — the run itself (the M1 gate)
- **Do:** on a machine on the corporate network, as the service account, put the Web API root in
  `appsettings.json` → `Crm:WebApiBaseUrl` (e.g. `https://<host>/<org>/api/data/v8.2/`) and start
  `CrmWorkflowExtractor.exe` with no arguments.
- **Pass:** exit code 0; the summary shows all six privileges at `Global`; `$count N -> retrieved N`; distinct
  owners > 1; an administrator's own count of the Process table (Settings → Processes, all views, or Advanced Find
  on Process with no filter) equals N.
- **Capture:** the console summary, the administrator's count, and `manifest.json` + `reports/inventory.md` +
  `logs/warnings.txt` from the run folder. **Not** `raw/` unless security agrees — it holds production metadata.

### M1-B — assumptions only the live server can settle
- **Do:** read the same run's `reports/inventory.md` and `logs/warnings.txt`.
- **Pass / record in the plan either way:**
  - `workflows/$count` answered a number on 8.2. If it did not, the run failed naming the request, and M1 needs a
    change to the `?$count=true` form — not implemented, because nothing says 8.2 lacks the path form.
  - No "Column … does not exist" warnings, or the list of columns that do not exist on 8.2.
  - No "no privilege of that name" warning — especially `prvReadProcessStage`, whose name is unverified.
  - The option-set table: every server label means the same as the handout's label; no `Unknown(n)`.
  - Whether privileges granted through **team** roles appear in `RetrieveUserPrivileges` — if the service account
    gets its roles through a team and the check reports `Missing`, the check needs `RetrievePrincipalAccess` or a
    team walk instead.
- **Capture:** those two files.

### M1-C — deployment type
- **Pass:** the run does not stop with exit code 3. If it does, the deployment is IFD: stop, per §2.2.
- **Capture:** the console output.
