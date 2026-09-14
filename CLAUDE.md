# CLAUDE.md — CRM Workflow Extractor

Reads every process (workflow) definition from a Microsoft Dynamics CRM 8.2 on-premises organization over its
Web API, parses the XAML into an intermediate representation, emits one BPMN 2.0 file per workflow, and proposes
similarity families for Business Architects to consolidate. Every intermediate artifact is retained as evidence.

The requirement is the handout from Enterprise Architecture (2026-09-14); its decisions and the open questions are
in @.claude/context/plan.md. Section numbers like "§2.4" in code comments refer to that handout.

## Critical rules
- **Read-only against PRODUCTION, structurally.** Only `CrmHttpClient` touches the network, and only with GET.
  `BannedSymbols.txt` bans every other way to build a client or send a request (RS0030 is an error). Never
  suppress RS0030 anywhere except the one guarded call site in `CrmHttpClient`.
- **No CRM SDK, no `Microsoft.Xrm.*`, no `System.Activities`.** Plain `HttpClient` + OData; XAML parsed as XML.
- **The tool COMBINES similar workflows** (maintainer, 2026-09-14 — overrides the handout's "no automatic
  consolidation" non-goal). Each family is merged by set union into one combined workflow, emitted as IR and BPMN.
  The per-workflow BPMN files stay as the evidence against which a human checks that the right ones were combined
  meaningfully. Every combined element carries the set of source workflows it came from.
- **Non-goals:** no HOPEX, no writes to CRM (combining happens in the output only — nothing is deleted or changed
  in CRM), no machine learning or external model calls, no Power Automate.
- **Verify with `dotnet build` / `dotnet test` yourself** — dotnet and git are on PATH. **This machine cannot reach
  the CRM server**: anything that needs it is stated as unverified, never reported as working.
- **Never work directly on `main`.** A work package runs through `/wp-start` → work → `/wp-verify` → `/wp-finish`,
  in a worktree, and stops for the maintainer to merge. See @.claude/context/working-agreement.md.
- **Secrets never enter `appsettings.json`** — `__CRM_PASSWORD__` placeholder only. Real values live in the gitignored
  `appsettings.Development.json` or the Windows Credential Manager. `out/` is gitignored: extracted production XAML
  may hold credentials.
- **Zero command-line arguments.** The target host has a locked-down command prompt; configuration is
  `appsettings.json` bound through the Options pattern, with a constants block in `Program.cs` as fallback.
- **Turkish text:** fold for comparison keys (`TurkishFold`), always keep and display the original string.
- **Session reports use the four sections** of handout §9: confident improvements · wanted but uncertain · left
  to judgement · out-of-scope findings. No section left empty for form's sake.
- **Never spawn background tasks or task chips.** Out-of-scope findings go into the plan's section 4.

## Style
@.claude/rules/coding-style.md — all 13 rules, build-enforced where an analyzer exists.

## Where things are
@.claude/context/structure.md
