namespace Crm.Extract.Runs;

/// <summary>
/// Every path inside a run folder, in one place. The output is read by Turkish-speaking architects and analysts,
/// so folder and file names are Turkish, ASCII-folded the same way BPMN file names are (no diacritics, hyphens for
/// spaces) so they survive any tool, archive or mail client. Names that come FROM CRM — workflow names, entity and
/// field names, option labels the server sent — are never translated; they are evidence.
/// </summary>
public static class RunPaths
{
    public const string Raw = "ham";

    /// <summary>What <c>ham/</c> was called before the output was Turkish; still read, so older runs reprocess.</summary>
    public const string RawEnglish = "raw";

    public const string Ir = "ara-model";
    public const string Bpmn = "bpmn";
    public const string Families = "aileler";
    public const string Combined = "birlesik";
    public const string ManualReview = "elle-inceleme";
    public const string Reports = "raporlar";
    public const string Logs = "gunlukler";

    public const string RawWorkflows = Raw + "/is-akislari.jsonl";
    public const string RawBrowserExport = Raw + "/tarayici-disa-aktarim.json";
    public const string RawUsageExport = Raw + "/kullanim-disa-aktarim.json";
    public const string RawMetadata = Raw + "/ust-veri";
    public const string RawHttp = Raw + "/http";
    public const string RawHttpIndex = RawHttp + "/dizin.jsonl";
    public const string RawXaml = Raw + "/xaml";

    public const string RunLog = Logs + "/calistirma.log";
    public const string WarningLog = Logs + "/uyarilar.txt";

    public const string Report = Reports + "/rapor.md";
    public const string Inventory = Reports + "/envanter.md";
    public const string ParseCoverage = Reports + "/ayristirma-kapsami.md";
    public const string SensitiveLiterals = Reports + "/hassas-degerler.md";
    public const string Drift = Reports + "/sapma.md";
    public const string Consolidation = Reports + "/birlestirme.md";
    public const string Usage = Reports + "/kullanim.md";
    public const string UsageCsv = Reports + "/kullanim.csv";
    public const string MigrationPlan = Reports + "/tasima-plani.md";
    public const string MigrationPlanCsv = Reports + "/tasima-plani.csv";
    public const string CallGraph = Reports + "/cagri-agaci.md";
    public const string CallGraphCsv = Reports + "/cagri-agaci.csv";
    public const string DataFootprint = Reports + "/veri-ayak-izi.md";
    public const string DataFootprintCsv = Reports + "/veri-ayak-izi.csv";
    public const string DataCascadesCsv = Reports + "/veri-zincirleri.csv";

    public const string FamiliesJson = Families + "/aileler.json";
    public const string FamiliesCsv = Families + "/aileler.csv";
    public const string PairsCsv = Families + "/ciftler.csv";
    public const string DraftsCsv = Families + "/taslaklar.csv";
    public const string SuppliedCsv = Families + "/urunle-gelenler.csv";

    public const string BpmnIndex = Bpmn + "/dizin.csv";
    public const string ManualReviewIndex = ManualReview + "/dizin.md";

    public static string IrFile(Guid workflowId)
    {
        return $"{Ir}/{workflowId:D}.json";
    }

    public static string ManualReviewXaml(Guid workflowId)
    {
        return $"{ManualReview}/{workflowId:D}.xaml";
    }

    public static string MetadataFile(string entity)
    {
        return $"{RawMetadata}/{entity}.json";
    }
}
