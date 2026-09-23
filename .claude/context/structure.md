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
  raporlar/  rapor.md · hassas-degerler.md (kısıtlı) · tasima-plani.xlsx · kapsam-disi.xlsx · aileler.xlsx · veri-analizi.xlsx · dis-sistemler.xlsx
out/cache/metadata/           shared across runs, copied into each run's ham/ust-veri/
```

**Every table is a sheet in a workbook, and no table is also a file.** Five workbooks, one per question the reader
has: `tasima-plani.xlsx` (what the work is), `kapsam-disi.xlsx` (what is NOT the work: drafts, product-supplied and
never-run test names, so the plan itself needs no filtering), `aileler.xlsx` (which of these are the same),
`veri-analizi.xlsx` (what touches what), `dis-sistemler.xlsx` (what reaches outside CRM). Each opens with a **Nasıl okunur** sheet
carrying the columns, the caveats and that run's numbers, so a reader who has the file needs nothing beside it.
Only two Markdown pages remain: `rapor.md` (the entry point) and `hassas-degerler.md` (restricted, kept separate so
it is easy to leave out of a delivery). `ExcelWorkbook` writes .xlsx by hand — a zip of XML, so no dependency.

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
