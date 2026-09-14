---
name: wp-verify
description: Verify a work package before reporting it — Release build, warning inventory, formatter, style conformance and full test suite, with the measurement traps baked in. Invoke before claiming any change is green.
---

# Verifying a work package

**Every step is written the way it is because the obvious way measured nothing.** Adapted from Yalbuz's
`wp-verify` (2026-09-14). Run all of it from the worktree. Report one line at the end: numbers, not an impression.
Write scratch output to the session scratchpad, not the tree.

## 1. Build, into a file — Debug for the inventory, Release for the gate

```bash
dotnet build --no-incremental > "$S/build.txt" 2>&1; echo "exit=$?"
dotnet build -c Release --no-incremental > "$S/build-release.txt" 2>&1; echo "exit=$?"
```

`--no-incremental` because an incremental build re-emits warnings only for projects it rebuilt. Release has
`TreatWarningsAsErrors`, so **the Release exit code is the clean-output gate**; the Debug file is the inventory.

## 2. Errors

```bash
grep -c ': error ' "$S/build.txt"
```

**`: error ` with the colon, never `error CS`** (misses `NU1605`) **and never bare ` error `** (the SDK output is
localized to Turkish here, and English parameter names survive inside Turkish warning prose). **The exit code is
the primary signal; the grep is the diagnosis.** An `MSB4018` about a locked file in `obj/`: run
`dotnet build-server shutdown` and retry.

## 3. Warning inventory, deduplicated by SITE AND CODE

```bash
grep -oE '[A-Za-z0-9_]+\.cs\([0-9]+,[0-9]+\): warning [A-Za-z]+[0-9]+' "$S/build.txt" | sort -u
```

**Never `cut` before deduplicating** — two codes at one site collapse into one. **The baseline is ZERO sites.** Any
warning is this package's to fix or to suppress AT ITS SITE with a reason.

## 4. Formatter, into a file that you then READ

```bash
dotnet format --verify-no-changes > "$S/format.txt" 2>&1; echo "exit=$?"
grep -cE 'ENDOFLINE|WHITESPACE|IDE[0-9]+' "$S/format.txt"
```

**Never read a formatter's exit code through a pipe** — that is `tail`'s exit code. A non-zero exit is a reason to
read the file; a missing final newline on a new file is caught by the exit code and not by the grep.

## 5. Line endings, if anything was edited with `sed`

`sed -i` under MSYS strips CR from every line. After any `sed -i`: `sed -i 's/\r*$/\r/' <file>` and check
`wc -l` equals the CR count. Better: do not edit source with `sed`.

## 6. Style conformance — the reviewer-carried rules

The analyzers cannot see rules 6, 9 and 12 of `coding-style.md`. On the package's diff:

```bash
git diff main --unified=0 -- '*.cs' | grep -nE '^\+.*\((int|long|string|[A-Z][A-Za-z]+)\)[a-zA-Z_(]'   # rule 6 cast candidates
git diff main --unified=0 -- '*.cs' | grep -nE '^\+.*catch \([A-Za-z]+ [a-z]+\)$'                        # rule 9: catch without a filter
git diff main --unified=0 -- '*.cs' | grep -nE '^\+\s*(public|internal|private).*\b(Helper|Manager|Util|Data|Info)\b' # rule 12 vague names
```

Each hit is READ and marked take-or-decline with a reason — numeric value-type conversions are not rule-6 casts.
Report the count that survives triage, never the raw count.

## 7. The suite

```bash
dotnet test --no-build > "$S/test.txt" 2>&1; echo "exit=$?"
grep -cE '\[FAIL\]' "$S/test.txt"
grep -E '\[FAIL\]' "$S/test.txt"
tail -5 "$S/test.txt"
```

**Capture to a file and grep `[FAIL]`; never pipe the run straight to `tail`** — the summary says how many failed,
never which. If `[FAIL]` appears, record the name BEFORE re-running anything. **Baseline skips: zero.**

## 8. A failed build means the rest never compiled

When one project fails, everything downstream is skipped — **their silence is not a clean result.**

## Reporting

One line: Debug errors, Release exit code, distinct warning sites (baseline ZERO), formatter exit + count,
style-conformance survivors, suite passed/failed/skipped. **Then say what is unverified because it needs the CRM
server** — that sentence is never omitted.

**Never `git checkout` a file to undo an experiment** — it discards uncommitted work.
