# CLAUDE.md — CRM Workflow Extractor

Reads every process (workflow) definition from a Microsoft Dynamics CRM 8.2 on-premises organization over its
Web API, parses the XAML into an intermediate representation, emits one BPMN 2.0 file per workflow, and draws one
stage map per active primary case stage — the process a request goes through, with the workflows it fires embedded
in it. Every intermediate artifact is retained as evidence.

The requirement is the handout from Enterprise Architecture (2026-09-14); its decisions and the open questions are
in @.claude/context/plan.md. Section numbers like "§2.4" in code comments refer to that handout.

## Critical rules
- **Read-only against PRODUCTION, structurally: the tool has NO network code** (since 2026-10-09). Data enters a run
  only as a file saved by the browser scripts in `tools/`, which run in the user's own signed-in session and send
  GET only. `BannedSymbols.txt` bans every way to build a client, open a socket or send a request, with no exception
  anywhere (RS0030 is an error). Never suppress RS0030.
- **No CRM SDK, no `Microsoft.Xrm.*`, no `System.Activities`.** The export is plain OData JSON; XAML parsed as XML.
- **The tool does NOT group or combine workflows** (maintainer, 2026-10-09 — reverses 2026-09-14). Grouping by
  resemblance answered a question the analysts were not asking: how workflows are used together is DATA, held in
  the case stages (`ps_step`) that fire them, and the stage maps draw it. Similarity and consolidation were removed.
- **Non-goals:** no HOPEX, no writes to CRM, no machine learning or external model calls, no Power Automate.
- **Verify with `dotnet build` / `dotnet test` yourself** — dotnet and git are on PATH. **This machine cannot reach
  the CRM server**: anything that needs it is stated as unverified, never reported as working.
- **Never work directly on `main`** — only branch off its current tip or merge a finished branch into it. A work
  package runs through `/wp-start` → work → `/wp-verify` → `/wp-finish`, in a worktree, and stops for the maintainer
  to merge. **A session never pushes**, and merges only when the maintainer names the branch — `/sweep` alone merges
  its own steps. See @.claude/context/working-agreement.md.
- **Work in a worktree.** Any session about to change files opens one with `/wp-start` — branching from LOCAL `main`,
  never `EnterWorktree` — and works there: not in the primary checkout, even for a one-line fix, and even when no
  second session is running. A session doing only a merge stays in the primary checkout. See *Two sessions, each in
  its own worktree* in the working agreement.
- **A work package runs through three skills, and they are not optional.** `/wp-start` opens its worktree,
  `/wp-verify` builds, counts warnings, runs the formatter and the full suite with every measurement trap baked in,
  and `/wp-finish` writes the archive entry into the commit body, checks the size cap before committing, and stops
  without merging. **They exist because the recipe drifted, not because a gesture was missing** — the maintainer
  does not have to type them; a session invokes each at its moment. Doing any of the three by hand is the older
  path, not a shorter one.
- **There are no secrets to configure.** With no network path there are no credentials and no connection settings;
  `appsettings.json` names files (`Run:ImportFile`, `Run:UsageFile`, …) and nothing else. `out/` and the exports
  are still gitignored and never pasted anywhere: extracted production XAML may hold credentials.
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

[project-profile.md](.claude/project-profile.md) — every value the four workflow skills (`/sweep`, `/wp-start`,
`/wp-verify`, `/wp-finish`) need that is true of THIS repository. The skills are Yalbuz's, re-adopted on 2026-11-21
with this repository's adaptations kept; the profile is not copied. `/sweep` keeps Yalbuz's ten numbered steps, step
10 (UI drift) marked as not applying here. A sweep is started by the maintainer, never by a session.

[offsite-checks.md](.claude/context/offsite-checks.md) — what only the company network can verify (the CRM server,
a production export, a person opening a delivered file), open items only, each with a Do, a Pass and a Capture.

[settled-decisions.md](.claude/context/settled-decisions.md) — every ruling in force (moved out of `plan.md` on
2026-11-21), and the two Yalbuz entries that explain the working agreement's rules.
