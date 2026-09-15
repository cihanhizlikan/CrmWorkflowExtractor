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

## Run output (`out/`, gitignored)

```
out/runs/<yyyyMMdd-HHmmss>/   manifest.json (written LAST — its presence seals the run)
  raw/  ir/  bpmn/  clusters/  consolidated/  manual-review/  reports/  logs/
out/cache/metadata/           shared across runs, copied into each run's raw/metadata/
```

A sealed run folder is never modified. Unchanged XAML is reused from the newest sealed run; an unsealed (crashed)
run is not resumed in the prototype — the next run starts fresh.

## Milestones
M1 inventory + privilege/count reconciliation · M2 XAML retrieval, resumable, hashed · M3 parser + IR + coverage ·
M4 BPMN + DI + XSD · M5 similarity + clusters.csv · M5b consolidation (set union per family, combined IR + BPMN,
`Crm.Consolidation` project) · M6 reports + end-to-end. Status lives in `plan.md`.
