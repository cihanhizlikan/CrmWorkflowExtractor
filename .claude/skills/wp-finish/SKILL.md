---
name: wp-finish
description: Close a work package — write its archive entry into the commit body, check the size cap BEFORE committing, commit, and report in the handout's four sections. Invoke when a package's work is done and verified.
---

# Closing a work package

**A package is not finished when its tests pass.** Run `wp-verify` first. Adapted from Yalbuz's `wp-finish`
(2026-09-14).

## 1. Draft the message in a file (scratchpad)

The commit body **is** the archive entry. Subject with a Conventional-Commits tag, blank line, then:

- **what changed**
- **why it was not the obvious thing** — the alternative considered and why it lost
- **the evidence** — the numbers `wp-verify` produced, and what remains unverified against CRM

## 2. Measure the body BEFORE committing

```bash
tail -n +3 "$S/msg.txt" | grep -v '^Co-Authored-By' | wc -c    # must be <= ~1500
```

Draft to ~1,200 bytes. When over, cut argument, never evidence — argument belongs in `plan.md`.

## 3. Commit

```bash
git add -A
git status --porcelain | grep -E 'appsettings\.Development|\.pfx|\.p12|\.env|^.. out/'   # must print nothing
git commit -F "$S/msg.txt"
git show --stat --format="" HEAD
```

End the message with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## 4. Confirm the tree is clean

```bash
git status --porcelain    # empty
```

## 5. Update the plan, report, and stop

- The package's row in `plan.md` says *committed, awaiting merge* — in the same commit, not a separate `docs:` one.
- Report in the **four sections of handout §9**, then the branch name and commit. Then **stop**.
- **Merging is the maintainer's**, only when they name the branch. **A session NEVER pushes.**
- Verification that needs the CRM server goes into `plan.md` under *Acceptance on the company network*, with a
  **Do**, a **Pass** and a **Capture** (what to send back).
