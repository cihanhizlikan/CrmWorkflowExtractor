# Working Agreement

How this project is worked on: the maintainer's durable expectations first, then branching, worktrees, the one
plan, committing, secrets, what makes a test evidence, the findings register, and how a package is finished.
Re-adopted from Yalbuz's `working-agreement.md` on 2026-11-21 (first adopted 2026-09-14), minus what only Yalbuz
has — its sinks, its web app, its desktop tool, its database, its log server.

**The rules are here; the incidents that bought them are in `settled-decisions.md`**, under *How the two-session
workflow was arrived at* and *How the working agreement was arrived at* — Yalbuz's incidents, imported as written.
Read those when a rule looks arbitrary; you do not need them to follow one. **Keep it that way**: a new rule
arrives here with at most one sentence of example, and its story goes to `plan.md`'s History.

## The maintainer's working style

- **No deployment exists, and quality outranks compatibility with one.** No legacy-key fallbacks, no deprecation
  shims, and never keep a wrong name or a wrong shape because correcting it would be a large diff. When a change
  exposes a deeper structural problem, plan and do that too, rather than delivering the minimal edit and noting the
  rest as future work.
- **Verification is mostly the assistant's job, not theirs.** Verify what is reachable from this machine without
  being asked, and reserve the maintainer's hands for what genuinely needs the company network — the CRM server, a
  production export — handed over as an item in `offsite-checks.md`: what to run, what passes, what to send back.
- **Evidence, not reasoning — for claims and for decisions alike.** A fix is not finished because the code looks
  right; expect to be asked for the evidence. A decision taken on evidence stays provisional, so record what would
  overturn it. A claim about the CRM server assembled from documentation is a hypothesis until a run confirms it:
  documentation is the source of truth, the live system the tiebreaker, and a disagreement is recorded in `plan.md`.
  Its corollaries:
  - **Prefer a real detector over an inferred zero.** An inferred zero is not a measurement.
  - **A measured "nothing to do here" is a deliverable.** Report the clean result; never manufacture work.
  - **A hypothesis derived from READING THE CODE is not a measurement either, and shipping one is the expensive
    mistake.** The tell: *this explanation was assembled from source rather than from evidence, and the evidence is
    one message away.* **Wait for it.**
  - **A number inside a question is evidence too, so it is measured or it says it is a guess.** The maintainer
    decides on the cost a question quotes. **When an answer widens the package, the quoted cost no longer holds** —
    re-measure and say so before building on it.
  - **Say so when the instrument, not the system, is what answered.** A fix that did not move the number is
    reported as one that did not, even when it is correct and stays in.
- **Take a position, and argue it.** An open question wants a recommendation and its reasoning, not a menu. Say
  what would change it, and flag any part that is an addition of your own. They contest freely and change position
  on a better argument or a number; treat a reversal as normal. **A menu is an invitation to redesign, not only to
  pick** — so give the analysis the options rest on, not just the options.
- **Hand over a DECISION, never an inventory.** For any analyzer, warning or other mechanical loop, the sites are
  read and each marked take-or-decline with its reason before they see it, and the count that SURVIVES leads.
  - **A bare rule-ID list is not reviewable, so approving one is not a review.**
  - **Every number states what produced it** — which projects, which analysis mode. **Default to *report only* on
    these loops**: if triage finds no named defect, the deliverable is "nothing here".
- **Correct a misused technical term, every time, and say what the right one is** — in one clause, then carry on;
  never as the answer to the question they asked. The bar is a term with a settled meaning OUTSIDE this project; the
  project's own names for its own things are not corrected.
- **Say plainly when what they supplied wasn't what was asked for**, before building on the nearest substitute.
- **Justify dependencies unprompted**: maintenance status and the first-party alternative.
- **What may run on this machine.** They review every command before it runs. **Never hand over a PowerShell
  script or suggest running loose scripts**: corporate Defender quarantines them. A paste-in one-liner is the most
  they run by hand; anything that runs against a live system belongs inside the tool, or in a browser script under
  `tools/` that sends GET only.
- **Never spawn a background task for an out-of-scope finding — write it into the plan.** The interruption is the
  problem, not the finding: **no task chips, no second session.** It goes into `plan.md` section 4, carrying the
  evidence that would otherwise be lost; *One register* below says which two fields it needs.
- **Work arrives in packages, and each one stops for a merge.** Finish one, report in the four sections of handout
  §9, and wait — don't chain the next package onto the same branch or start it unasked. A message carrying several
  tasks is a batch to sequence across branches, not to do at once on one.
- **The session they are talking to builds the packages — never a background agent.** An agent's work is out of
  their sight and it cannot ask them anything. Agents are for wide **read-only** searches whose conclusion is all
  that is wanted, or when they ask for one; building, verifying and merging stay here.
  - **"Fork a session" means a real session they can open and talk to.** If no tool here can create one, say so and
    give them a brief to paste — never substitute an agent silently.
  - **Plan mode is the whole session's, background agents included.** Revise the plan only when nothing runs in
    the background.

## Branching
- **Never check out `main` to work on it.** Its only two uses are branching a new task off its tip and merging a
  finished branch into it. (The only exception was the initial seed commit of an empty repository.)
- Branch from the current `main`, not from whichever branch was last active — including not from the branch just
  merged. Branching from an in-progress branch chains unmerged work onto it. **Solo maintenance is not an excuse**:
  the risk is sequencing, not textual conflict. Exceptions:
  - The maintainer says to continue unfinished work on a named branch — branch from that one.
  - A fix spawned directly out of work still in progress on a feature branch branches from that feature branch,
    and says so: `main` may not contain the code path yet.
- Name branches for the task: `feature/<short-task-slug>`, `fix/<short-task-slug>`.

## Two sessions, each in its own worktree

At most two sessions work at once, the maintainer opens both, and **each one enters its own git worktree before it
changes anything.** The primary checkout stays on `main` and is nobody's workspace — it exists to be merged into.

### Entering the worktree

A worktree lives under `.claude/worktrees/` (gitignored). Three skills carry the package lifecycle: `/wp-start`
opens one, `/wp-verify` proves it, `/wp-finish` closes it.

- **Any session about to change files enters a worktree first** — even for a one-line fix, and even when no second
  session is running.
- **Open one with `/wp-start`**: clean-tree check, `git worktree add .claude/worktrees/<slug> -b feature/<slug>
  main`, and a confirmation that the branch sits on main's tip.
- **It names local `main` rather than using `EnterWorktree`, and that is the point.** `EnterWorktree` branches from
  `origin/main`, and local `main` is routinely ahead of it because a session never pushes. It is also callable only
  once per session.
- **A session doing only merges stays in the primary checkout.** It changes nothing that could collide. (The plan
  is in the tree here, so revising it is a change like any other and is made on a branch.)
- `ExitWorktree` with `keep` leaves the branch and directory intact; `remove` deletes both and refuses if anything
  is uncommitted.

### What the maintainer keeps

- **Work is ISSUED, never taken.** A session does not choose its next package, and one that finishes reports and
  stops.
- **Merging is the maintainer's**, and a session merges only when told which branch. **Pushing is theirs too, and a
  session NEVER pushes** unless told to in that message.
- **Never assume a merge is still unpushed.** Check `git rev-list --count origin/main..main` before describing
  anything as reversible. Once a merge is on the remote, undoing it is a revert commit, never a reset.
- **A merge instruction is per-moment, never standing.** A branch that becomes eligible later needs a new
  instruction, and a branch declared out of scope stays out until the maintainer says otherwise. A session that
  changes its mind about scope says so BEFORE acting.
  **`/sweep` is the ONE exception, named here so a session running it does not stop to re-derive this rule.** It is
  started when the maintainer is leaving for hours; it merges each step, deletes the branch and continues on its own
  authority, and anything it would have asked becomes a plan row. The authorisation and its limits live in
  `.claude/skills/sweep/SKILL.md` under *It runs UNATTENDED* — read it there rather than widening this bullet.
- **Verify a branch landed before retiring its package** — *Finishing a work package* below.

### One plan, and which file it is

- **The plan is `.claude/context/plan.md`, IN the repository** (`profile:plan_rule`), so git protects it: a
  concurrent edit is a merge conflict, not a silent overwrite. It is revised on the branch of the package that
  changes it, and both sessions read the same file by construction.
- **It is the file `CLAUDE.md` and the maintainer NAME, never one a harness created.** A session in plan mode is
  handed a path under `~/.claude/plans/` and told it is the only file it may edit; that is a permission, not an
  identification. **Never find the plan by globbing `~/.claude/plans/`** — a delegated agent in plan mode writes
  its own file there, and newest-wins or largest-wins is no rule. If anybody names a different file, ask.
- **Reconcile before writing, against BOTH halves of what moved.** `git log --oneline` says what LANDED;
  **`git worktree list` says what the other session has in flight.**
- **A row about the other session's in-flight branch NAMES the branch**, so the next reconcile is one command.

### Before two sessions run at once

- **Split by FILES TOUCHED, not by package count.** Overlapping packages go to one session in sequence.
- **A package with a known open decision is not parallel work at all** — it is a conversation first.

### Asking, and the bar for it

- **Being able to ask is not permission to decide.** Take the position and argue it — a recommendation is what makes
  answering cheap, not what replaces the answer.
- **The bar is "might a reviewer have chosen differently?", not "is this a big decision".** **Narrowing scope to
  avoid a decision counts as deciding.**
- **Disclosure is not blessing.** Listing a choice in a report does not authorise it. Work whose choices have not
  been seen is not merged.

### Between the two sessions: facts, never permissions

- A finding in the other session's area is worth passing directly. Anything that would change *who touches which
  files* goes through the maintainer — does acting on this message change the files I touch?
- **Never ask the other session to do something that was refused here.**

### What bites in a worktree

- **There is no app to preview, and nothing to run from a worktree** (`profile:run_command`: none). **Never point
  a worktree run at a real export**: production XAML may hold credentials, and a worktree is the wrong place for it
  to land. The tests build their own exports.
- **The gitignored `Crm.Cli/appsettings.Development.json` does not exist in a fresh worktree**, and no test needs
  it. It may name a real export file; do not copy it in.
- **A build server holds whatever checkout it last built, and that has TWO symptoms.** Run `dotnet build-server
  shutdown` before `git worktree remove`, or it unregisters and then fails with `Permission denied`. **And a
  post-merge build in the PRIMARY checkout can fail** with `MSB4018` on a locked file in `obj/` — suspect a stale
  node before the code, and shut the build server down first.
- **A husk is an EMPTY directory and a lingering handle — retry it, don't report it.** A `git worktree remove` that
  ends in `Permission denied` has already unregistered the worktree and deleted every file; a second `rm -rf` by
  its literal path clears it. **If files are still in there it is the build-server case above instead.**
- **Delete branches by their literal name**, never `$(git branch --show-current)`.

## Committing
- All changes happen on the task branch. Never commit directly to `main`.
- Commit after a coherent unit of work, not after every edit.
- **`/wp-verify` before the commit, `/wp-finish` for it.** The build, formatter and suite are run there, with their
  traps; they are not restated here.
- Conventional-Commits subject: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `build`.
- **No separate `docs:` commit for a package's own archive entry** — it belongs in the body of the commit it
  describes. A `docs:` commit is for a documentation change that IS the work.

## Secrets
- **The tool has no credentials to keep** (`profile:secrets_file`: none): `appsettings.json` names files only, and
  there is no network path since 2026-10-09. `appsettings.Development.json`, `*.user`, `*.pfx`, `*.p12` and `.env`
  are gitignored and treated as secrets on sight — never staged, whatever is already tracked.
- **The run output and the exports are sensitive**: production XAML may hold credentials (handout §5.3). `out/` is
  gitignored, never pasted anywhere, and never copied into fixtures unanonymized — a fixture taken from production
  has its names, GUIDs, URLs and literals replaced before it is committed.
- **`Crm.Cli/appsettings.json` IS the effective configuration** (`profile:effective_config`): there is nothing
  substituted into it, so reading it is reading what the run does.

## When the suite goes red
`/wp-verify` runs it; this is what to do with a failure.
1. Default assumption: the CODE broke something real. Fix the implementation first.
2. Only modify or remove a test when it demonstrably tests superseded or incorrect behaviour — and say so in the
   commit message.
3. Never delete or weaken a test purely to make the suite pass without stating why in the commit message.
4. Commit again, re-run. Repeat up to 4 times total.
5. Still failing after 4 attempts: STOP, report what fails and why, and wait for direction.

## What makes a test evidence

- **Revert-confirm-red on every new test.** A test that cannot fail is not evidence.
- **Falsify with the ACTUAL defect, not a convenient one.** A guard can go red on a break you invented and still be
  blind to the one it was written for. **The first break is the defect from the report; anything else is a second
  break, not a substitute.**
- **Never `git checkout` a file to undo a revert-confirm-red experiment** — it restores from HEAD and silently
  discards uncommitted work on that file. Re-edit it back.
- **Make and unmake a break with the EDIT tool, never with `perl -0pi` or `sed`.** A multi-line C# edit through them
  goes wrong in ways that compile and pass; `dotnet format` catches it, the compiler and the suite do not.
- **Confirm the BREAK landed before believing the red or the green.** Grep the changed line after making the
  break, and only then run the test.
- **The measurement recipes are `/wp-verify`, and running it is not optional.** Its traps are stated beside the
  command each belongs to; a session that verifies by hand is following an older path, not a shorter one.
- **When a change is about ordering or timing, run the suite repeatedly.** An intermittent test makes a bad trade
  look free.
- **Bound any wait** that guards against something never finishing, or a regression hangs the suite instead of
  naming an assertion.
- **A WINDOW of a log is not the log.** `grep -c` the whole file and count the DISTINCT values
  (`grep -o '<pattern>' file | sort | uniq -c`) before saying what it does or does not contain.
- **Say plainly when a failure path cannot be staged** rather than writing a test that passes for the wrong reason
  — most live-CRM behaviour cannot be, from here, and that is an item in `offsite-checks.md`.
- **A test asserts a requirement, never a preference.** The question that settles it: *if the style changed
  tomorrow, would anyone call that a regression?* A colour, a pixel size, an exact coordinate is **chosen**; an
  assertion pinning one goes red when somebody makes a decision, and what that trains is editing tests until they
  pass. A RELATION is a requirement — no two shapes overlap, no flow crosses a shape, a box holds its text, a
  delivered file names nothing left behind — and it stays true through any redesign. **A separate bucket does not
  rescue a preference-assertion**: giving it a home only makes it permanent.

## One register: findings are work packages too
**There is no separate list of parked findings.** Anything worth keeping — something the maintainer asked for, or
something a session noticed and must not lose — is a work package in the plan (out-of-scope findings in its section
4), with the same discipline as any other: a row, and a commit whose body carries its entry. Two fields are
required on an item a session raises itself:

- **Origin** — `finding` (a session noticed it) or a maintainer request. A finding is evidence, never a commitment.
- **Status** — whether it is committed to build. Findings default to *not yet committed to build*.

**A finding already refused on evidence is not a package at all** — it is a settled decision, recorded in `plan.md`
as **DECIDED** with the measurement that refused it and what would re-open it.

## Finishing a work package
**A package is not finished when its tests pass.** `/wp-finish` runs this, and checks the size cap BEFORE the
commit exists. In the same breath as the final commit:

1. **Write the archive prose into that commit's MESSAGE BODY.** There is no archive file; the commit is the record:
   **what changed, why it was not the obvious thing, and the evidence**. `git log --grep=` matches the body as well
   as the subject. **Cap it at about 1,500 bytes**: an entry owes **findability**, not the full argument —
   long-form reasoning goes to `plan.md`.
2. **The package's row stays in the plan until its branch is merged AND that merge has been checked** — not when
   the branch is committed. Until then it says *committed, awaiting merge*. This is the maintainer's safeguard.
3. **Verification only the company network can do is an ITEM in `offsite-checks.md`, IN THE SAME COMMIT** — not in
   the plan. Give it a Do, a Pass and a Capture. A change that alters what an item checks rewrites that item; an
   item answered by a visit is DELETED in the commit that reads its answer in.

**Backstop.** A session about to touch the plan reconciles it first — `git log --oneline` for what landed,
`git worktree list` for what is in flight.

## Delivering a build
On request, package the current branch with `git archive` — never by zipping the working tree, so untracked and
ignored paths (`bin/`, `obj/`, `out/`, `TestResults/`) are excluded by construction:

    git archive --format=zip -o crm-workflow-extractor-<branch>.zip HEAD

Write the zip OUTSIDE the repository. Uncommitted work is NOT in the archive — commit first, or say so.
