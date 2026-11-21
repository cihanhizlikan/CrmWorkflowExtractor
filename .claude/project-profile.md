# Project profile — CRM Workflow Extractor

**What this file is.** The four workflow skills — `/sweep`, `/wp-start`, `/wp-verify`, `/wp-finish` — are written
to be copied unchanged between C# repositories. Everything they need to know that is true of ONE repository lives
here, as named keys, and the skills read it by key. **Copy the skills, not this file**: a new repository gets its
own profile, filled in from its own tree.

**An absent key is not a default.** A skill step whose key is missing, `none`, or names something that does not
exist reports that step **UNAVAILABLE (profile: `<key>`)** and does nothing else. It never reports "measured
clean" — a step that read nothing has measured nothing, and that is the failure this file exists to prevent: in a
repository without a structure document, a step that reads one finds nothing and calls it clean. A run that finds
no profile at all does preflight only and ends, listing the keys it would have needed.

**Values are facts about this repository, so they decay like any other.** The sweep's context-drift step checks
that every path below still resolves.

**Here** (re-adopted from Yalbuz 2026-11-21): every key the four skills name is present. A `none` is measured, and
says why in one line — a console tool with no UI, no network code, no secrets and one culture has several.

## Projects

| Key | Value |
|---|---|
| `solution` | `CrmWorkflowExtractor.slnx` |
| `production_projects` | `Crm.Extract/`, `Crm.Ir/`, `Crm.Bpmn/`, `Crm.Cli/` — and `tools/*.js`, the only code that reads CRM, which no C# analyzer sees |
| `test_projects` | `Crm.Tests/` |
| `fixture_excludes` | `Crm.Tests/Fixtures/` — recorded exports and XAML samples, byte-exact evidence (`-text` in `.gitattributes`); never edited to make a finding go away |
| `markup_globs` | none — no `.razor`, `.cshtml` or `.xaml` source; the 13 `.xaml` files are CRM definitions under `Crm.Tests/Fixtures/Xaml/`, DATA the parser reads as XML, binding no type |
| `co_located_products` | none — one product; every project in `CrmWorkflowExtractor.slnx` is this tool's |
| `host_types` | console (`Crm.Cli`, classic `Program.Main`, zero command-line arguments) — no hosted services, no UI thread |

## Conventions the build or suite already enforces

| Key | Value |
|---|---|
| `one_type_per_file` | **not enforced** — several files hold a record beside the type that uses it (`Crm.Cli/Stages/ProcessMiningStage.cs`, `Crm.Bpmn/Stages/StageMap.cs`), so enumerate types by parsing declarations |
| `namespace_matches_folder` | not enforced by a test; true by convention (folder, project and root namespace are one name) |
| `line_endings` | **crlf** for source and documents (`.editorconfig` `end_of_line = crlf`), not enforced by a test — `dotnet format` reports a `.cs` file's ENDOFLINE, nothing checks a `.md`. **LF and byte-exact**: `Crm.Tests/Fixtures/**` and the six `tools/` files `.gitattributes` marks `-text` — the export pages embed their script's SHA-256. After any `sed -i` under MSYS on a CRLF file: `sed -i 's/\r*$/\r/' <file>`, then check lines == CRs |
| `enforced_guards` | reach bans (`BannedSymbols.txt` — every way to open a client, socket or request; RS0030 at **error**, never suppressed); `Program` shape and no trailing blank line (`RepositoryConventionTests`); the browser export never building one giant string (same class); the committed settings name files and no connection (`ConfigurationTests`); nothing internal named in a delivered file (`DeliveryTests`, `AnalystGuideTests`); each export page byte-matches the script it embeds (`BrowserExportImportTests`) |
| `warnings_as_errors` | **Release only** (`Directory.Build.props`), so a Release build that succeeds is a zero-warning one; Debug is the inventory |
| `analyzer_config` | `.editorconfig` (each rule's reason inline), `Directory.Build.props` (`AnalysisLevel=10.0`, `AnalysisMode=Default`, `LangVersion=14.0`), `BannedSymbols.txt`, `global.json` (SDK pinned 11.0.100, `rollForward: latestPatch`, since 2026-11-21), `Directory.Packages.props` (every package version) |

## Documents the steps read

| Key | Value |
|---|---|
| `context_docs` | `CLAUDE.md`, `.claude/context/*.md`, `.claude/rules/*.md`, `.claude/skills/*/SKILL.md`, this file |
| `architecture_docs` | `.claude/context/structure.md` — the one document that says what the code IS, which step 8b checks claim by claim. **Excluded by name**: `plan.md` (decisions and dated history), `offsite-checks.md` (checks a person runs, rewritten by the package that changes them), `settled-decisions.md` (rulings and history by design), `working-agreement.md` (how work is done, not what the code is), `reference/` (the CRM service document, a fact about the SERVER) |
| `architecture_doc_code` | `structure.md` → `Crm.Extract/`, `Crm.Ir/`, `Crm.Bpmn/`, `Crm.Cli/`, `tools/`, `*.csproj`, `Directory.*.props`, `global.json`, `.gitignore` |
| `architecture_audit_baseline` | none — no whole-tree 8b pass has run here yet, so the next sweep's 8b is the whole-tree pass and sets this key |
| `structure_doc` | `.claude/context/structure.md` — the project table (what each owns, and that only `tools/` reads the network) and the dependency rule above it |
| `naming_rule` | `.claude/rules/coding-style.md` rule 12; and the Turkish naming of every output (`RunPaths`, `RunStages`, `ProcessLabels`, `ConditionWords`) described in `structure_doc` |
| `adopted_names` | none — no name here is taken from an upstream's convention. **Note for step 1**: the four production projects share the dotted prefix `Crm.` and no project is named `Crm`, so step 1's grouping applies to them: the maintainer ruled on 2026-11-21 that the sweep groups them (`Crm/Extract/`, `Crm/Ir/`, …) |
| `project_discovery` | `Crm.Tests/RepositoryTree.cs` → `Root()` (the folder holding `CrmWorkflowExtractor.slnx`), read by `RepositoryConventionTests`, which walks EVERY `*.cs` below it minus `bin/`, `obj/` and `.claude/` — it selects no project by name, so a move cannot empty it. `BrowserExportImportTests`, `UsageStageTests` and `RunAuthorityExportTests` read `tools/` by literal path, and fail on a move rather than pass |
| `reviewer_carried_rules` | `.claude/rules/coding-style.md` — the **Enforcement** paragraph |
| `settled_decisions` | `.claude/context/settled-decisions.md` — *This repository's decisions* (every ruling, moved out of `plan.md` on 2026-11-21) and the two workflow-history entries imported from Yalbuz |
| `config_behaviours` | none — the settings name files only; a missing or wrong file stops the run with exit 2 (`Main_With_The_Committed_Settings_Exits_As_Configuration_Invalid`) |
| `placeholder_rule` | none — nothing is pipeline-substituted: `Crm.Cli/appsettings.json` holds file names and no `__NAME__` marker |
| `config_coverage_test` | `ConfigurationTests` |
| `effective_config` | `Crm.Cli/appsettings.json` IS the effective configuration — there is nothing to substitute and no secrets (no network path since 2026-10-09). A gitignored `appsettings.Development.json` may override a file name locally |

## The plan

| Key | Value |
|---|---|
| `plan_rule` | `.claude/context/plan.md`, IN the repository; out-of-scope findings go to its section 4 (`CLAUDE.md`). **Because it is in the tree, the sweep's "append to the plan file" means: on the step's own branch, in the step's commit**, so a row merges with the step that raised it — never written in the primary checkout, which stays on a clean `main`. A step with rows and nothing to correct carries them on the next step's branch, or on one of its own if none follows. There is no plan-mode round trip: git history is the record of what landed. Never found by globbing `~/.claude/plans/` |
| `plan_known_path` | `.claude/context/plan.md` |
| `history_paths` | the repository moved from `C:\Users\ihizl\source\repos\CrmWorkflowExtractor` to `D:\source\repos\CrmWorkflowExtractor`, so session history from before the move is under the OTHER project folder in `~/.claude/projects/` (`C--Users-ihizl-source-repos-CrmWorkflowExtractor`) |
| `offsite_register` | `.claude/context/offsite-checks.md` — anything only the CRM server, a production export or a person looking at a delivered file can settle, open items only, each with a Do, a Pass and a Capture. Moved out of `plan.md` on 2026-11-21, so a step adds its item on its own branch and the merge carries it |

## Verification

| Key | Value |
|---|---|
| `secrets_file` | none — no credentials exist to hold. No test reads `appsettings.Development.json`; never copy one into a worktree |
| `secrets_absent_effect` | none — with no secrets file, a worktree's suite count IS the real count |
| `build_output_language` | Turkish (`hata`/`uyarı`); diagnostic tokens stay English |
| `coverage_command` | none — no coverage extension is referenced (`Directory.Packages.props` has no `Microsoft.Testing.Extensions.CodeCoverage`); adding one is a package, never an unattended install |
| `baseline_skips` | **0** — except on a machine with no `node`, where the three `Crm.Tests.Tools.RunAuthorityExportTests` skip and say why |
| `warning_baseline` | **0** distinct site+code |

## The surfaces people read

| Key | Value |
|---|---|
| `user_docs` | the analyst guide `nasil-kullanilir.docx`, written by `Crm.Cli/Reports/AnalystGuide.cs`, and each workbook's *Nasıl okunur* sheet (`Crm.Cli/Reports/Guides.cs`) — words live in the code, not a resource file |
| `cultures` | `tr` only — every word the tool writes is Turkish (`structure_doc`); there is no second culture to compare |
| `wording_rules` | `structure_doc`: a delivered file names only what is delivered; "talep aşaması", "iş akışı", "aşama haritası", "kayıt türü" — never bare "akış" or "süreç" for a workflow; the user a stage assigns to and the holders of a role are never named; the guide's voice is formal and plain ("açın", not "açınız") |
| `ui_register` | none — no user interface: the tool is a console run that writes files (`host_types`) |
| `run_command` | none from a worktree — a real run needs a production export, which never enters a worktree. The end-to-end tests build their own exports (`FakeOrganization`) |
| `render_harnesses` | none — nothing is rendered on screen; the delivered `.docx`, `.xlsx` and `.bpmn` are opened by the tests as files (`DeliveryTests`, `AnalystGuideTests`), and looking at them in Word or a BPMN viewer is `offsite_register` item D |
