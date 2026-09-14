---
name: wp-start
description: Open a work package — create its git worktree, branch from LOCAL main, and confirm a clean base. Invoke this whenever the maintainer issues a package to build, before touching any file.
---

# Opening a work package

**Run this before the first edit of any package.** Adapted from Yalbuz's `wp-start` (2026-09-14).

## The steps

Run from the **primary checkout**, which stays on `main` and is nobody's workspace.

```bash
git status --porcelain          # must be empty; if not, stop and report
git branch --show-current       # must be main
git worktree add .claude/worktrees/<slug> -b feature/<slug> main
git -C .claude/worktrees/<slug> log --oneline -1    # must equal main's tip
```

Name the branch for the task: `feature/<short-task-slug>` or `fix/<short-task-slug>`.

## Why `git worktree add … main` and not `EnterWorktree`

`EnterWorktree` branches from `origin/main`, and local `main` is routinely ahead of it because a session never
pushes. Naming `main` makes the base local by construction. `EnterWorktree` also cannot be called twice in a session.

## What bites once you are in there

- **The gitignored `Crm.Cli/appsettings.Development.json` does not exist in a fresh worktree.** No test depends on
  it — the suite never reaches a server — so do not copy it in. It holds real credentials.
- **Never run `Crm.Cli` from a worktree against a real server.** This machine cannot reach CRM anyway; a run that
  appears to "work" here is talking to something else.
- **Use `awk 'NR==n'` to read a line, never `sed -n 'a,bp'`** — MSYS `sed` has been off by one against `grep -n`.

## What this skill does NOT do

It does not choose the package. **Work is ISSUED, never taken** — if no package has been named, ask.
