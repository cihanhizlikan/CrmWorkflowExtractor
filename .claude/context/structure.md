# Structure

Folder, project and root namespace are the same name. Dependencies point one way: `Cli` → everything;
`Bpmn` and `Similarity` → `Ir`; `Consolidation` → `Ir`, `Similarity`; `Ir` → nothing of ours; `Extract` → nothing of ours.
The stages after retrieval read only the run folder, never the network.

| Project | Owns | Network |
|---|---|---|
| `Crm.Extract` | `CrmHttpClient` (the only network type), credentials, paging, preflight (WhoAmI, privileges, `$count`), inventory and XAML retrieval, raw persistence, run folder + manifest | **yes — GET only** |
| `Crm.Ir` | XAML parsing, IR types, metadata resolution, `TurkishFold`, coverage, sensitive-literal scan | no |
| `Crm.Bpmn` | IR → BPMN 2.0, DI layout, embedded OMG XSD validation | no |
| `Crm.Similarity` | step signatures, structural + lexical scores, union-find clustering, cohesion | no |
| `Crm.Consolidation` | combining a family into one workflow (prefix union with Variant splits), provenance reconciliation | no |
| `Crm.Cli` | `Program.Main`, configuration binding, stage orchestration, reports, exit codes | via `Crm.Extract` |
| `Crm.Tests` | xUnit v3 over recorded fixtures (`Crm.Tests/Fixtures/`) — never a live server | no |
| `tools/` | `crm-browser-export.js` and `crm-usage-export.js` (run on a CRM page in the user's browser, GET only) and the bookmarklet pages generated from them | the user's browser |

## Run output (`out/`, gitignored)

```
out/runs/<yyyyMMdd-HHmmss>/   manifest.json (written LAST — its presence seals the run)
  ham/  ara-model/  aileler/  birlesik/  elle-inceleme/  raporlar/  gunlukler/
  bpmn/<kategori>/<birincil varlık>/<iş akışı adı>.bpmn
  raporlar/  nasil-kullanilir.pdf · rapor.md · hassas-degerler.md (kısıtlı) · tasima-plani.xlsx · kapsam-disi.xlsx · aileler.xlsx · veri-analizi.xlsx · dis-sistemler.xlsx
out/cache/metadata/           shared across runs, copied into each run's ham/ust-veri/
```

**Every table is a sheet in a workbook, and no table is also a file.** Five workbooks, one per question the reader
has: `tasima-plani.xlsx` (what the work is), `kapsam-disi.xlsx` (what is NOT the work: drafts, product-supplied and
never-run test names, so the plan itself needs no filtering), `aileler.xlsx` (which of these are the same),
`veri-analizi.xlsx` (what touches what), `dis-sistemler.xlsx` (what reaches outside CRM). Each opens with a **Nasıl okunur** sheet
carrying the columns, the caveats and that run's numbers, so a reader who has the file needs nothing beside it.
**The package opens with a PDF.** `nasil-kullanilir.pdf` is the one document an analyst who has never seen this
CRM reads first: what the package is, which file to open in which order, and how to work a single workflow from
its plan row to a drawn process — demonstrated on a real live workflow picked from that run. `PdfDocument` writes
it by hand, as `ExcelWorkbook` writes .xlsx, and embeds an installed TrueType font because the PDF base encodings
have no ğ, ı or ş. No font on the machine that covers them and allows embedding means no PDF and a warning, never
a misspelled one. The cover carries the organisation's logo — embedded in `Crm.Cli` so the locked-down host needs
no loose file, read by `PngImage` (8-bit, non-interlaced), and replaceable with `Run:LogoFile` without a rebuild.
The document is set in the logo's own two colours.

Only two Markdown pages remain: `rapor.md` (the entry point) and `hassas-degerler.md` (restricted, kept separate so
it is easy to leave out of a delivery). `ExcelWorkbook` writes .xlsx by hand — a zip of XML, so no dependency.

Addresses in `dis-sistemler.xlsx` are read from every literal in the definition — the same text the sensitive
scan reads — not from the arguments the parser captured on steps: on the real data every address sat somewhere
else, and the delivered page came out empty while the restricted report was reporting embedded addresses. What a
custom activity does inside its own assembly is still invisible, and most endpoints are there. What CRM does keep
is the names an activity is called with, gathered per activity as its `parametreler`: the closest thing to a
signature, and the only record of which back-end operation a call stands for.

**The endpoint of a service call is not in any workflow record.** A CRM workflow cannot call a service; a custom
activity can, and its address is written inside that activity's assembly. CRM stores the assembly, so
`PluginRegistryRetriever` reads the registry (`pluginassemblies`, `plugintypes`, `sdkmessageprocessingsteps`),
downloads only the assemblies behind a workflow's activities, and `AssemblyStrings` scans their string constants
for addresses; the bytes are dropped, never written to disk. The browser export does the same scan in the browser
and sends only the text. What this says is "the code this step runs contains these addresses", never "this step
calls this address" — and it is said that way on the sheet, in the guide and on the diagram. Plug-in steps come
with it: code CRM runs on a message, not a process, invisible to the plan and listed on its own page.

**A column exists only if a line can be written in the workbook's guide saying what a reader does differently
because of it**, and a page exists only if its rows are things to act on: the call pages carry only workflows that
are part of a call, Sapma only the definitions whose running copy really differs, Yakın çiftler only the pairs that
nearly became a family or carry one structure under two names. The diagrams are emitted AFTER grouping, because
each one's header note carries what the rest of the run learned about it — role, family, usage, drift.

**The output is Turkish** — folder names, file names and every word the tool writes. Three tables carry it:
`RunPaths` (paths), `RunStages` (stage names) and `ProcessLabels` (category, mode and state labels, which code also
branches on). Names that come from CRM are never translated. A run made before this still reprocesses: its `raw/`
is read and copied forward as `ham/`.

A sealed run folder is never modified. Unchanged XAML is reused from the newest sealed run; an unsealed (crashed)
run is not resumed in the prototype — the next run starts fresh.

## Milestones
M1 inventory + privilege/count reconciliation · M2 XAML retrieval, resumable, hashed · M3 parser + IR + coverage ·
M4 BPMN + DI + XSD · M5 similarity + clusters.csv · M5b consolidation (set union per family, combined IR + BPMN,
`Crm.Consolidation` project) · M6 reports + end-to-end. Status lives in `plan.md`.
