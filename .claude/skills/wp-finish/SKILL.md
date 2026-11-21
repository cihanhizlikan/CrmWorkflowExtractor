---
name: wp-finish
description: Close a work package — write its archive entry into the commit body, check the size cap BEFORE committing, commit, and report in the handout's four sections. Invoke when a package's work is done and verified.
---

# Closing a work package

**A package is not finished when its tests pass.** Run `wp-verify` first; this is what happens after it is green.
`$S` is the session's scratchpad directory — scratch files never go into the tree.

## 1. Draft the message in a file

The commit body **is** the archive entry — there is no archive file. Subject with a Conventional-Commits tag
(`feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `build`), blank line, then:

- **what changed**
- **why it was not the obvious thing** — the alternative considered and the reason it lost
- **the evidence** — the numbers `wp-verify` produced, and what remains unverified because it needs the CRM server

`git log --grep=<term>` matches subject and body, which is how this is searched later.

## 2. Measure the body BEFORE committing

```bash
tail -n +3 "$S/msg.txt" | grep -v '^Co-Authored-By' | wc -c    # must be <= ~1500
```

**Draft to about 1,200 bytes, not 1,500.** Every package in Yalbuz's overhaul overshot on the first measurement and
needed two or three trims; leaving headroom costs a sentence and saves the round trips.

**Do this before `git commit`, not after.** Committing first and amending down is how Yalbuz's W2 went — the cap
was checked once the commit existed, and three amend rounds followed. The cap is ~1,500 bytes / ~25 lines: what an
entry owes is **findability**, not the full argument.

**When it is over, cut argument, never evidence.** Long-form reasoning has a better home — a ruling goes to
`profile:settled_decisions` (`settled-decisions.md`) — and it is consulted on purpose, where an over-long
commit body is read by nobody.

## 3. Commit

```bash
git add -A
git status --porcelain | grep -E 'appsettings\.Development|\.pfx|\.p12|\.env|^.. out/'   # must print nothing
git commit -F "$S/msg.txt"
git show --stat --format="" HEAD
```

End the message with the attribution line the session's system prompt specifies, verbatim. **Never a model name
written here**: this skill said `Claude Opus 5` while sessions ran a later model, so every commit that copied it
attributed the wrong one.

**No separate `docs:` commit for the entry.** It travels in the commit it describes, so it cannot drift from the
tree it was written against. A `docs:` commit is for a documentation change that IS the work. **The plan row
travels with it too**: `profile:plan_rule` puts the plan in the tree, so the row's *committed, awaiting merge* is
part of this commit.

## 4. Confirm the tree is clean and holds no secrets

```bash
git status --porcelain                                    # empty
```

`profile:secrets_file` is none here, so there is no file to check for; the grep in step 3 is what keeps a local
settings file, a certificate or the run output out of the commit.

## 5. Report, and stop

Say the branch name, the commit, and what was verified — **in the four sections of handout §9** (confident
improvements · wanted but uncertain · left to judgement · out-of-scope findings), none left empty for form's sake.
Then **stop**.

- **Merging is the maintainer's**, and only when they name the branch. **A session NEVER pushes.**
- **A merge instruction is per-moment, never standing** — a branch that becomes eligible later needs a new one.
- **The package's row stays in the plan** until its branch is merged AND that merge has been checked. Not when the
  branch is committed: a finished branch may wait days or never be merged, and a row removed at commit time is
  invisible in both places meanwhile. The row says *committed, awaiting merge*. This is the maintainer's
  safeguard, so do not tidy it away early.
- If the package still owes verification that can only happen on a network or machine this session cannot reach —
  here, anything needing the CRM server or a production export — add an ITEM to `profile:offsite_register`
  (`offsite-checks.md`) in the same commit, not to the plan, with a Do, a Pass and a Capture.
- **Under `/sweep`**, "stop" does not apply: a sweep step uses this skill for the commit body and the size cap, then
  merges and keeps going (see that skill's *Merge, delete and continue*).

**Values marked `profile:<key>`, and the paths in the commands above, are this repository's**, read from
`.claude/project-profile.md`. Copying this skill elsewhere means a new profile, not an edited skill.
