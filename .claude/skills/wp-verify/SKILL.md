---
name: wp-verify
description: Verify a work package before reporting it — Release build, warning inventory, formatter, style conformance and full test suite, with every measurement trap this repository has already paid for baked in. Invoke before claiming any change is green.
---

# Verifying a work package

**Every step here is written the way it is because the obvious way measured nothing.** Run all of it from the
worktree. Report one line at the end; report the numbers, not an impression. `$S` is the session's scratchpad
directory — scratch output never goes into the tree.

## 1. Build, into a file — Debug for the inventory, Release for the gate

```bash
dotnet build --no-incremental > "$S/build.txt" 2>&1; echo "exit=$?"
dotnet build -c Release --no-incremental > "$S/build-release.txt" 2>&1; echo "exit=$?"
```

`--no-incremental` because **a warning count is only real on a quiet tree** — an incremental build re-emits
warnings only for projects it rebuilt, so it under-reports. `profile:warnings_as_errors` is **Release only**, so
**the Release exit code is the clean-output gate**; the Debug file is the inventory.

## 2. Errors

```bash
echo "exit=$?"          # the build's own exit code, read FIRST
grep -c ': error ' "$S/build.txt"
```

**`: error ` — with the colon — and never `error CS`.** A NuGet downgrade surfaces as `NU1605` and was missed once
by matching `error CS`. The build output is localized (`profile:build_output_language`: Turkish) but the
`error`/`warning` tokens on diagnostic lines stay English, and every one of them is preceded by the diagnostic's
own colon: `MSBUILD : error MSB1008:`, `Foo.csproj : error NU1605:`, `Bar.cs(9,3): error CS0103:`.

**A bare ` error ` matches a WARNING'S MESSAGE TEXT, and did** (Yalbuz, 2026-09-08). A localized `CA1716` reads
*"…, error parametresini ayrılmış dil anahtar sözcüğü 'Error' ile çakışmayacak şekilde…"* — the English parameter
name `error` survives inside Turkish prose, and this repository names its catch variables `error` throughout. A
build that exited **0** scored **8** on ` error ` and **0** on `: error `; a probe carrying a real `MSB1008` and a
real `NU1605` scores **2** on `: error `. So the bare pattern over-reports on a clean build, which is the worse
direction: it reads as a broken tree and sends you hunting a defect that is not there.

**The exit code is the primary signal and the grep is the diagnosis.** A build that exited non-zero has failed
however the grep scores, and `MSB1008` — *"only one project can be specified"* — is the shape that proves it: it
exits 1 with no `.cs` diagnostic at all, so a grep tuned to code errors alone would call it clean.

**An `MSB4018` about a locked file in `obj/` is not a code error.** Suspect a stale build node first:
`dotnet build-server shutdown` and retry.

## 3. Warning inventory, deduplicated by SITE AND CODE

```bash
grep -oE '[A-Za-z0-9_]+\.cs\([0-9]+,[0-9]+\): warning [A-Za-z]+[0-9]+' "$S/build.txt" | sort -u
```

**Never `cut` a warning line before deduplicating it.** Two different codes can sit at one site — CA1502 and CA1506
together are the usual pair — so cutting collapses them and reports a drop that did not happen.

**The baseline is ZERO sites** (`profile:warning_baseline`). Every finding is either fixed or suppressed AT ITS SITE
with the reason, on the policy `.editorconfig` states, so the rule starts at zero and the next one to fire is new.
**That is the whole point**: a remembered non-zero baseline makes this step a comparison against a number in a
document, and one more site has to be noticed rather than reported. Zero is checkable. **So any warning at all is
this package's to explain.**

## 4. Formatter, into a file that you then READ

```bash
dotnet format --verify-no-changes > "$S/format.txt" 2>&1; echo "exit=$?"
grep -cE 'ENDOFLINE|WHITESPACE|IDE[0-9]+' "$S/format.txt"
```

**Never `dotnet format … | tail -3; echo $?`** — that reports `tail`'s exit code, which is always 0, so every
claim made through it measured nothing. **Exit 0 is this tree's baseline.** Test the ENDOFLINE/WHITESPACE/IDE
count AND read the exit code: a non-zero exit is a reason to read the file, and a missing final newline on a new
file is caught by the exit code and not by the grep.

## 5. Line endings, if anything was edited with `sed` or CREATED with the Write tool

`profile:line_endings` is CRLF for source and documents, and **no test checks it here** — the formatter reports a
`.cs` file's ENDOFLINE, and nothing at all checks a `.md`. **`sed -i` under MSYS reads in text mode**, so it strips
the CR from every line it touches AND every line it does not. **A file the Write tool creates is LF throughout and
may lack a final newline**; append it (`printf '\r\n' >> <file>`) when the last byte is not one. After either:

```bash
sed -i 's/\r*$/\r/' <file>
echo "lines=$(wc -l < <file>) CRs=$(tr -cd '\r' < <file> | wc -c)"   # must be equal
```

**Never on the LF files**: `Crm.Tests/Fixtures/**` and the six `tools/` files `.gitattributes` marks `-text` — the
export pages embed their script's SHA-256, and `BrowserExportImportTests` fails on one changed byte. `cat -A` and a
`sed`-piped `xxd` lie about line endings for the same reason `sed` does. `git diff --stat` is the second opinion: a
whole-file ending flip shows there as every line changed.

## 6. Style conformance — the reviewer-carried rules

`profile:reviewer_carried_rules`: the analyzers cannot see rules 6 and 12 of `coding-style.md`, nor the half of
rule 9 `RCS1236` misses — the inverted retest `if (error is X) { return; } throw;`. On the package's diff:

```bash
git diff main --unified=0 -- '*.cs' | grep -nE '^\+.*\((int|long|string|[A-Z][A-Za-z]+)\)[a-zA-Z_(]'   # rule 6 cast candidates
git diff main --unified=0 -- '*.cs' | grep -nE '^\+.*catch \([A-Za-z]+ [a-z]+\)$'                        # rule 9: catch without a filter
git diff main --unified=0 -- '*.cs' | grep -nE '^\+\s*throw;$'                                          # rule 9: a bare rethrow — read the lines above it
git diff main --unified=0 -- '*.cs' | grep -nE '^\+\s*(public|internal|private).*\b(Helper|Manager|Util|Data|Info)\b' # rule 12 vague names
```

Each hit is READ and marked take-or-decline with a reason — numeric value-type conversions are not rule-6 casts.
Report the count that survives triage, never the raw count.

## 7. The suite

The file is `profile:secrets_file` and the skips are `profile:baseline_skips` (`.claude/project-profile.md`).
**No secrets file in the profile → skip the copy and the delete; never invent one.** Here there is none, so there
is nothing to copy in first, and `profile:secrets_absent_effect` is none: the worktree count is the real count.

```bash
dotnet test --no-build > "$S/test.txt" 2>&1; echo "exit=$?"
grep -cE '^(başarısız|failed) ' "$S/test.txt"     # 0, or read the names below
grep -E '^(başarısız|failed) ' "$S/test.txt"
tail -6 "$S/test.txt"                               # toplam / başarısız oldu / başarılı / atlandı
```

**`profile:baseline_skips` skips are the baseline and are not drift** — read the number there, never from this
sentence. It is zero here, except on a machine with no `node`, where the three `Crm.Tests.Tools` tests skip and say
why. Each skips rather than returning quietly, because a run that did not happen must not read as one that did.

**Capture to a FILE and grep the failure lines; never pipe the run straight to `tail`.** The summary says how many
failed and never which. In Yalbuz a test twice failed inside a combined run, passed on every isolated re-run, and
left NO record of its name — both times because the output had been tailed. An intermittent failure you cannot name
is one you cannot fix. If a failure line appears, record the test name BEFORE re-running anything.

**The runner is Microsoft Testing Platform, and its output is not VSTest's** (2026-11-07, xunit 4). `global.json`'s
`test.runner` opts `dotnet test` into it; without that line the .NET 10 SDK refuses xunit 4 outright. A failure
prints at column 0 as `başarısız <Test.Name>` — localized, exactly as a skip prints `atlandı <Test.Name>` — where
VSTest printed `[FAIL]`. **The old `[FAIL]` grep scored 0 against a probe that really failed**, so a recipe carried
over from before the switch reports green on red. The `^` matters: the indented summary line `başarısız oldu: N`
must not count as a failure. `failed ` is there for an English-locale machine and is unmeasured here. One class:
`dotnet test --no-build --filter-class <Namespace.Class>`; VSTest's `--filter "FullyQualifiedName~…"` is gone.

## 8. A failed build means the rest never compiled

When one project fails, everything downstream of it is skipped — **their silence is not a clean result.** Yalbuz's
W5 read two probes as "clean" when neither had been compiled, because an upstream project had errored first.
Re-run the remaining projects isolated before concluding anything from an absence.

The rules about *believing* a red or a green — revert-confirm-red, falsify with the actual defect, confirm the
break landed — are judgement rather than recipe and live in `working-agreement.md` under *What makes a test evidence*.
They are not repeated here, because a rule in two places is a rule with two versions.

## Reporting

One line: Debug errors, Release exit code, distinct warning sites against a baseline of ZERO, formatter exit code
and ENDOFLINE/IDE count, style-conformance survivors, suite passed/failed/skipped. **`profile:baseline_skips` skips
are the baseline** — step 7 says which — and **the formatter's EXIT CODE catches what the ENDOFLINE/IDE grep does
not**, a missing final newline on a new file being the case that proved it. **Then say what is unverified because
it needs the CRM server** — that sentence is never omitted.

**Never `git checkout` a file to undo an experiment** — it discards uncommitted work.
