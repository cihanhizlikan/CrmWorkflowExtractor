# Structure

Folder, project and root namespace are the same name. Dependencies point one way: `Cli` → everything;
`Bpmn` and `Similarity` → `Ir`; `Consolidation` → `Ir`, `Similarity`; `Ir` → nothing of ours; `Extract` → nothing of ours.
The stages after retrieval read only the run folder, never the network.

| Project | Owns | Network |
|---|---|---|
| `Crm.Extract` | `CrmHttpClient` (the only network type), credentials, paging, preflight (WhoAmI, privileges, `$count`), inventory and XAML retrieval, raw persistence, run folder + manifest | **yes — GET only** |
| `Crm.Ir` | XAML parsing, IR types, metadata resolution, `TurkishFold`, `HtmlEntities`, `DocumentText`, coverage, sensitive-literal scan | no |
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
  raporlar/  nasil-kullanilir.docx · rapor.md · hassas-degerler.md (kısıtlı) · tasima-plani.xlsx · kapsam-disi.xlsx · aileler.xlsx · veri-analizi.xlsx · dis-sistemler.xlsx · calistirma-yetkisi.xlsx
out/cache/metadata/           shared across runs, copied into each run's ham/ust-veri/
```

**Every table is a sheet in a workbook, and no table is also a file.** Six workbooks, one per question the reader
has: `tasima-plani.xlsx` (what the work is), `kapsam-disi.xlsx` (what is NOT the work: drafts, product-supplied and
never-run test names, so the plan itself needs no filtering), `aileler.xlsx` (which of these are the same),
`veri-analizi.xlsx` (what touches what), `dis-sistemler.xlsx` (what reaches outside CRM),
`calistirma-yetkisi.xlsx` (who may start one by hand, and whose identity it then runs under). Each opens with a **Nasıl okunur** sheet
carrying the columns, the caveats and that run's numbers, so a reader who has the file needs nothing beside it.
**The package opens with a WORD DOCUMENT.** `nasil-kullanilir.docx` is the one document an analyst who
has never seen this CRM reads first: what the package is, which file to open in which order, and how to work a
single workflow from its plan row to a drawn process — demonstrated on a real live workflow picked from that run.
`WordDocument` writes it by hand, as `ExcelWorkbook` writes .xlsx — a zip of XML, so no dependency — in the
department’s own standard layout (AHE-BT-EY-STD, supplied 2026-10-02): A4 with its margins, Arial 11 justified,
headings in its navy, a cover carrying the document’s particulars, a revision table and a contents list. The
ORDER of the children of `w:pPr` and `w:rPr` is fixed by the schema and is written in one place for that reason:
a file with them out of order is one Word calls unreadable and "repairs". It was a PDF until 2026-10-02; Word
paginates, which is the whole reason for the change, and the hand-written PDF writer and its font embedding went
with it. `PngImage` now reads only the logo’s size, because a .docx carries the file itself.

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

**CRM has no per-workflow permission.** There is no record saying "role X may run workflow Y": starting a
process by hand needs one estate-wide privilege, `prvExecuteWorkflowJob`, plus the right to read the process
record. `RoleRetriever` reads the roles holding those two and how many hold each — counted, never named, so no
staff list leaves the building — and `calistirma-yetkisi.xlsx` keeps the two halves apart: the roles on one page,
and on the other the facts that really do vary per workflow, its on-demand flag and its run-as identity. A record
SHARED with one user or team grants access none of this can see: `principalobjectaccess` is not on the Web API,
and the guide says so. The intersect tables’ entity set names are asked of metadata rather than assumed.

**The export is written in one pass and never held twice.** Six thousand definitions are some six hundred
megabytes of JSON, past what one JavaScript string may hold, and collecting the pieces into an array instead kept
the escaped copies beside the originals until the tab died with no error at all. `crm-browser-export.js`
therefore GENERATES pieces and hands them to the browser in segments that leave the JavaScript heap as they are
made, then gzips the result where `CompressionStream` exists — about tenfold on XAML. `BrowserExportImport`
recognises a compressed export by its first two bytes rather than its name, and keeps it verbatim in `ham/`
under `.json.gz`. Every stage of the write announces itself, because the failure that cost a run printed nothing.

**What is delivered names only what is delivered.** The outsource partner gets `nasil-kullanilir.docx`, the three
workbooks a migration needs (`tasima-plani`, `veri-analizi`, `dis-sistemler`) and `bpmn/`.
`calistirma-yetkisi.xlsx` is INTERNAL (maintainer, 2026-10-08): it names security roles and counts the people
holding each, which is the estate's own business and not a migrator's.
The first delivery is the ORIGINAL diagrams only (maintainer, 2026-10-02): `aileler.xlsx`, `birlesik/` and
`aileler/` are still produced and still read by Enterprise Architecture, as are the evidence, `rapor.md`, the
restricted findings, `kapsam-disi.xlsx` and `calistirma-yetkisi.xlsx` — but none is handed over, so a consolidation nobody has
agreed to cannot be mistaken for the plan. A delivered file that names one of those sends a reader looking for
what they do not have, so none does — `DeliveryTests` opens every delivered workbook, and `AnalystGuideTests`
the guide, and both fail on any text that mentions something left behind.

**A column exists only if a line can be written in the workbook's guide saying what a reader does differently
because of it**, and a page exists only if its rows are things to act on: the call pages carry only workflows that
are part of a call, Sapma only the definitions whose running copy really differs, Yakın çiftler only the pairs that
nearly became a family or carry one structure under two names. The diagrams are emitted AFTER grouping, because
each one's header note carries what the rest of the run learned about it — role, family, usage, drift.

**A step box says what a step writes AND what it writes there.** A field whose value the definition FIXES is
shown as `description = …`, an option as `Aramadan İşlem Yapılmıştır (3)` — the label for the reader, the number
for CRM. A value CRM works out as it runs is not one: that field is named alone, because `= <dynamic>` costs a
line of the box and tells a reader nothing they could not see, and a field needs ALL its values fixed to be shown
with them, since half a list is worse than none. `LiteralValue.Dynamic` is the marker, on the MODEL rather than on
the parser that writes it, because every stage after the parser has to tell the two apart. The documentation and
the box now render a value through one function, so they cannot drift.

**A status change says which status.** `SetState` is CRM's own message and the only supported way to move a record
between Active, Inactive and the rest; it sets `statecode` and `statuscode` as a PAIR, so the state never arrives
as a `FieldWrite` and is read off the step. It arrives two ways and only one was being read: a state built through
an expression is a variable the index resolves, while a state typed into the designer is a literal nested as
`<mxs:OptionSetValue Value="1" />` under `SetState.State`, with no variable anywhere — so the box read
"Durum değiştir: phonecall" and never said what it changed the status TO. Each number is resolved against the
attribute it belongs to, `SetState.State` against `statecode` and `SetState.Status` against `statuscode`.

**Blocks are aligned on their SPINE, and the layout reserves the room every label will take.** A block's spine is
the height its flow comes in and goes out at; for a split whose branches all end the process, that is the row its
continuation takes, which is rarely the middle one. Aligning bounding-box centres instead put the diamond on
whichever row happened to be in the middle and drew the outgoing flow straight THROUGH the shape standing there —
invisible with an even number of branches, which is why every fixture passed and `CHECK_CAMPAIGN_FIELD`, with
three rows, did not. Spines are also what keep a chain of diamonds reading as one straight line.

**`LabelBox` is the one measurement of a label**, used by the layout to reserve room and by `BpmnSerializer` to
write bounds, because measuring in one place and spacing by a constant in the other is how a 160-pixel caption
came to be drawn into a 120-pixel gap. Three things are reserved from it: the corridor between a diamond and its
branches is at least a caption wide; the gap between branch rows is at least a caption tall; and a shape whose
text is drawn OUTSIDE it — every gateway and event — reserves that text below itself, so the row beneath is not
placed as though an end event were 36 pixels tall when it carries 100 pixels of name under it. A caption goes on
the middle of its flow's longest LEVEL run, because that is the part of a flow belonging to one branch alone: two
branches share the upright line out of their diamond, so captions measured along that line are drawn on top of
each other. Where the legs are short this is the same place it always was.

**A slug is a FOLDER name as often as a file name.** A diagram's path is
`bpmn/<kategori>/<birincil varlık>/<iş akışı adı>.bpmn`, so two of its three slugs are directories, and Windows
keeps about two dozen names for devices. Measured on this machine (Windows 11, 2026-10-08): `mkdir nul` throws,
while `nul.bpmn` writes perfectly well as an ordinary file — so the FILE half of this is no longer a fault on
Windows 11, though it was on every Windows before it, where that name opens the null device and the diagram is
written to nothing with nobody told. `BpmnFileNames.Slug` therefore moves an exact device word off itself in all
three positions, to `nul-ayrilmis`; only an exact match counts, so `console` and `com10` are ordinary names. That
the slug holds nothing but lower-case ASCII, digits and single hyphens is also what rules out a trailing dot or
space, which Windows strips silently — two names differing only there would land on one file.

**One workflow may not cost the run.** The BPMN stage runs before consolidation and every report, so anything
thrown while drawing one diagram used to take the workbooks and the guide with it. Each diagram is now drawn
inside a guard: a cancellation is the operator's and travels, everything else belongs to that one workflow — the
failure is recorded against its NAME, the run's exit code becomes a failure, and the rest are still drawn. The
workflow is dropped from `state.BpmnFiles`, so the plan's `bpmn_dosyasi` cell stays empty and the guide walks the
reader through a different one rather than naming a file nobody has. `bpmn.written` counts what was written, so
the count chain shows a loss as a drop instead of hiding it behind a number that still says 1437.

**XML has no escape for a control character.** There is no spelling of U+0001 that a document may hold, so a
writer handed one does not produce a bad file — it THROWS. CRM's own records are XML and cannot carry one, but the
names around them arrive as JSON, which can, and the strings scanned out of a plug-in assembly are full of them.
`ExcelWorkbook` and `WordDocument` each dropped them; `BpmnSerializer` did not, and its loop neither guards itself
nor runs after the reports, so one stray byte in one workflow name cost the whole run — every workbook with it.
The rule is now `DocumentText.Writable` and all three writers use it: it also drops a lone surrogate (half a
character, which `XmlConvert.IsXmlChar` accepts because inside a pair a high half IS character data), U+FFFE and
U+FFFF, and the C1 range that XML permits and nobody can read; tab, newline and carriage return are text and stay.
`BpmnSerializer` sweeps the BUILT document rather than each of the dozen places text goes in, because that is the
version a later site cannot forget to call.

**The text CRM stores is HTML, and a reader should see the words.** A step's label is the `DisplayName` of the
`Sequence` the designer wraps it in, typed into a web designer that leaves its own references behind in it — so
the XAML escapes them a SECOND time, reading it as XML peels one layer, and a step box came out reading
`Müşteri&#160Adı` where a space belonged. `HtmlEntities` resolves them at the three places CRM text enters the
IR: `XamlNames.DisplayName`, a condition's description, and every quoted literal in `ExpressionIndex` (which is
also what the sensitive scan and the address page read, so a query string stops arriving with its `&amp;` showing).
ONE PASS, never until nothing changes — text that really says `&amp;` means an ampersand and the letters "amp;" —
and a reference nobody there knows is left verbatim, because a guess on a diagram is worse than a visible oddity.
A semicolon is optional on a NUMERIC reference and required on a named one: `&#160` with nothing after it is what
the real data carried, while `&nbspAdı` is as likely to be someone's text as a dropped semicolon.

**The output is Turkish** — folder names, file names and every word the tool writes. Four tables carry it:
`RunPaths` (paths), `RunStages` (stage names) and `ProcessLabels` (category, mode and state labels, which code also
branches on), and `ConditionWords` (CRM's condition operators: a diagram reading `lead.leadid NotNull` left a
reader asking whether that was a null check, so it reads `lead.leadid dolu`; the operator's NAME stays on the
predicate, where it is a comparison key, and one nobody translated keeps CRM's own word rather than a guess).
Names that come from CRM are never translated. A run made before this still reprocesses: its `raw/`
is read and copied forward as `ham/`.

A sealed run folder is never modified. Unchanged XAML is reused from the newest sealed run; an unsealed (crashed)
run is not resumed in the prototype — the next run starts fresh.

## Milestones
M1 inventory + privilege/count reconciliation · M2 XAML retrieval, resumable, hashed · M3 parser + IR + coverage ·
M4 BPMN + DI + XSD · M5 similarity + clusters.csv · M5b consolidation (set union per family, combined IR + BPMN,
`Crm.Consolidation` project) · M6 reports + end-to-end. Status lives in `plan.md`.
