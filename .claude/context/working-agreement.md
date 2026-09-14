# Working Agreement

Adopted from Yalbuz's `working-agreement.md` on 2026-09-14 and cut to what applies to this tool. Yalbuz's copy
holds the incidents behind each rule.

## The maintainer's working style
- **Quality outranks compatibility.** No deployment exists yet: no legacy fallbacks, no shims, no keeping a wrong
  name because renaming is a large diff.
- **Verification is the assistant's job** for everything reachable from this machine. What needs the company
  network (the CRM server) is handed over as an explicit acceptance item: what to run, what passes, what to send back.
- **Evidence, not reasoning.** A claim about the CRM server that was assembled from documentation is a hypothesis
  until a run confirms it. Documentation is the source of truth; the live system is the tiebreaker; record
  disagreements in `plan.md`.
- **Take a position and argue it.** An open question wants a recommendation with its reasoning, not a menu.
- **Correct a misused technical term** in one clause, then carry on.
- **Say plainly when what was supplied is not what was asked for**, before building on a substitute.
- **Justify dependencies unprompted**: maintenance status and the first-party alternative.
- **Never hand over a PowerShell script** or suggest running loose scripts — corporate Defender quarantines them.
  Anything that runs against a live system belongs inside the application itself.
- **Never spawn background tasks or task chips.** Findings go into `plan.md`.
- **Work arrives in packages, and each one stops for a merge.** Finish, report in the four §9 sections, wait.

## Branching and worktrees
- **Never edit files while `main` is checked out.** `main` is for branching from and merging into.
  (The only exception was the initial seed commit of an empty repository.)
- Branch from LOCAL `main` via `/wp-start`: `feature/<slug>` or `fix/<slug>`, in `.claude/worktrees/<slug>`.
- A follow-on fix spawned from in-progress work branches from that feature branch, and says so.

## The plan
- **The plan is `.claude/context/plan.md`, in the repository**, so git protects it. It is revised on the branch of
  the package that changes it, never concurrently by two sessions.

## Committing
- On the task branch only. Conventional-Commits subject (`feat`, `fix`, `refactor`, `test`, `docs`, `chore`).
- The commit body is the archive entry (see `/wp-finish`). **A session never pushes; merging is the maintainer's.**

## Secrets
- `appsettings.json` carries `__CRM_PASSWORD__` only. Real values: gitignored `appsettings.Development.json` or
  Windows Credential Manager. Treat `*.pfx`, `*.p12`, `.env` as secrets on sight.
- `out/` is gitignored and is never copied into fixtures unanonymized. A fixture taken from production is
  anonymized (names, GUIDs, URLs, literals) before it is committed.

## Test loop
- After each commit run the suite. On failure assume the CODE is wrong first; change a test only when it asserts
  superseded behaviour, and say so in the commit. Stop after 4 failed attempts and report.

## What makes a test evidence
- **Revert-confirm-red on every new test.** A test that cannot fail is not evidence.
- **Falsify with the actual defect**, not a convenient one.
- **Confirm the break landed** (grep the changed line) before believing the red or the green.
- **Make and unmake a break with the Edit tool**, never `sed`/`perl`, and never `git checkout` a file to undo one.
- **Bound every wait**; a hung suite names no assertion.
- **A test asserts a requirement, never a preference.**
- **Say plainly when a failure path cannot be staged** — most live-CRM behaviour cannot be, from here.
- **The measurement recipes are `/wp-verify`**, and running it is not optional.
