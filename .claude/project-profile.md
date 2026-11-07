# Project profile — CRM Workflow Extractor

**What this file is.** The four workflow skills (`/sweep`, `/wp-start`, `/wp-verify`, `/wp-finish`) come from Yalbuz,
where `/sweep` is written to be copied between C# repositories UNCHANGED. Everything it needs that is true of ONE
repository lives here as named keys, and the skill reads it by key (`profile:<key>`). Adopted 2026-11-07. **Copy the
skill, not this file**: this one was filled in from this tree.

**An absent key is not a default.** A step whose key is missing, `none`, or names something that does not exist
reports **UNAVAILABLE (profile: `<key>`)** and does nothing else. It never reports "measured clean": a step that read
nothing has measured nothing. Several keys here are `none` on purpose, and each says why.

**Values are facts about this repository, so they decay like any other.** The sweep's context-drift step checks
that every path below still resolves.

## Projects

| Key | Value |
|---|---|
| `solution` | `CrmWorkflowExtractor.slnx` |
| `production_projects` | `Crm.Extract/`, `Crm.Ir/`, `Crm.Bpmn/`, `Crm.Cli/` — and `tools/*.js`, the only code that reads CRM, which no C# analyzer sees |
| `test_projects` | `Crm.Tests/` |
| `fixture_excludes` | `Crm.Tests/Fixtures/` — recorded exports and XAML samples, byte-exact evidence (`-text` in `.gitattributes`); never edited to make a finding go away |
| `markup_globs` | none — no Razor or WPF. The `.xaml` files are DATA (CRM workflow definitions under `Crm.Tests/Fixtures/Xaml/`), never markup that binds a type |
| `co_located_products` | none |
| `host_types` | console (`Crm.Cli`, classic `Program.Main`, zero command-line arguments) — no hosted services, no UI thread |

## Conventions the build or suite already enforces

| Key | Value |
|---|---|
| `one_type_per_file` | **not enforced** — several files hold a record beside the type that uses it (`ProcessMiningStage.cs`, `StageMap.cs`), so enumerate types by parsing declarations |
| `namespace_matches_folder` | not enforced by a test; true by convention (folder, project and root namespace are one name) |
| `line_endings` | **crlf** for source (`.editorconfig` `end_of_line = crlf`), not enforced by a test. **LF and byte-exact**: `Crm.Tests/Fixtures/**` and the six `tools/` files `.gitattributes` marks `-text` — the export pages embed their script's SHA-256. After any `sed -i` under MSYS on a CRLF file: `sed -i 's/\r*$/\r/' <file>`, then check lines == CRs |
| `enforced_guards` | reach bans (`BannedSymbols.txt` — every way to open a client, socket or request; RS0030 at **error**, never suppressed); `Program` shape and no trailing blank line (`RepositoryConventionTests`); the browser export never building one giant string (same class); the committed settings name files and no connection (`ConfigurationTests`); nothing internal named in a delivered file (`DeliveryTests`, `AnalystGuideTests`); each export page byte-matches the script it embeds (`BrowserExportImportTests`) |
| `warnings_as_errors` | **Release only** (`Directory.Build.props`), so a Release build that succeeds is a zero-warning one; Debug is the inventory |
| `analyzer_config` | `.editorconfig` (each rule's reason inline), `Directory.Build.props` (`AnalysisLevel=10.0`, `AnalysisMode=Default`), `BannedSymbols.txt`, `global.json` (SDK pin), `Directory.Packages.props` (every package version) |

## Documents the steps read

| Key | Value |
|---|---|
| `context_docs` | `CLAUDE.md`, `.claude/context/*.md`, `.claude/rules/*.md`, `.claude/skills/*/SKILL.md`, this file |
| `structure_doc` | `.claude/context/structure.md` — the project table (what each owns, and that only `tools/` reads the network) |
| `naming_rule` | `.claude/rules/coding-style.md` rule 12; and the Turkish naming of every output (`RunPaths`, `RunStages`, `ProcessLabels`, `ConditionWords`) described in `structure_doc` |
| `reviewer_carried_rules` | `.claude/rules/coding-style.md` — the **Enforcement** paragraph |
| `settled_decisions` | `.claude/context/plan.md`, *Decisions taken* and *3. Left to judgement* |
| `config_behaviours` | none — the settings name files only; a missing or wrong file stops the run with exit 2 (`Main_With_The_Committed_Settings_Exits_As_Configuration_Invalid`) |
| `config_coverage_test` | `ConfigurationTests` |
| `effective_config` | `Crm.Cli/appsettings.json` IS the effective configuration — there is nothing to substitute and no secrets (no network path since 2026-10-09). A gitignored `appsettings.Development.json` may override a file name locally |

## The plan

| Key | Value |
|---|---|
| `plan_rule` | `.claude/context/plan.md`, IN the repository; out-of-scope findings go to its section 4 (`CLAUDE.md`). **Because it is in the tree, the sweep's "append to the plan file" means: on the step's own branch, in the step's commit**, so a row merges with the step that raised it — never written in the primary checkout, which stays on a clean `main`. A step with nothing to correct but a row to hand over opens a branch for that row alone. There is no plan-mode round trip to make: git history is the record of what landed |
| `plan_known_path` | `.claude/context/plan.md` |
| `history_paths` | the repository moved from `C:\Users\ihizl\source\repos\CrmWorkflowExtractor` to `D:\source\repos\CrmWorkflowExtractor` in 2026-11, so session history from before the move is under the OTHER project folder in `~/.claude/projects/` |
| `offsite_register` | `.claude/context/plan.md`, section *Acceptance on the company network* — anything only a run against the CRM server or the production export can settle, with a Do, a Pass and a Capture |

## Verification

| Key | Value |
|---|---|
| `secrets_file` | none — no credentials exist to hold. No test reads `appsettings.Development.json`; never copy one into a worktree |
| `secrets_absent_effect` | none |
| `build_output_language` | Turkish (`hata`/`uyarı`); diagnostic tokens stay English |
| `baseline_skips` | **0** |
| `warning_baseline` | **0** distinct site+code |

## The surfaces people read

| Key | Value |
|---|---|
| `user_docs` | the analyst guide `nasil-kullanilir.docx`, written by `Crm.Cli/Reports/AnalystGuide.cs`, and each workbook's *Nasıl okunur* sheet (`Crm.Cli/Reports/Guides.cs`) — words live in the code, not a resource file |
| `cultures` | `tr` only — the output is Turkish; names that come from CRM are never translated |
| `wording_rules` | `structure_doc`: a delivered file names only what is delivered; "talep aşaması", "iş akışı", "aşama haritası", "kayıt türü" — never bare "akış" or "süreç" for a workflow; the user a stage assigns to and the holders of a role are never named |
| `ui_register` | none — there is no UI. The visual surface is the BPMN diagrams, read in a modeler; their layout is held by tests (`StageMapTests`, `SpineTests`) rather than a register |
| `run_command` | none from a worktree — a real run needs a production export, which never enters a worktree. The end-to-end tests build their own exports (`FakeOrganization`) |
| `render_harnesses` | none |
| `theme_rules` | none |
