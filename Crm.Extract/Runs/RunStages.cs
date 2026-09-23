namespace Crm.Extract.Runs;

/// <summary>
/// The stage names a run records and the report prints. They are read by people, so they are Turkish; they are
/// also matched in code, so they live here once instead of as literals at every call site.
/// </summary>
public static class RunStages
{
    public const string Identity = "kimlik";
    public const string Privileges = "yetkiler";
    public const string Columns = "sütunlar";
    public const string Inventory = "envanter";
    public const string Reconciliation = "mutabakat";
    public const string Xaml = "xaml";
    public const string Drift = "sapma";
    public const string Metadata = "üst veri";
    public const string Plugins = "eklenti kayıtları";

    public const string ProcessStages = "süreç aşamaları";
    public const string Usage = "kullanım";
    public const string Ir = "ara model";
    public const string Bpmn = "bpmn";
    public const string Similarity = "benzerlik";
    public const string Consolidation = "birleştirme";
    public const string MigrationPlan = "taşıma planı";

    public static string Import(string fileName)
    {
        return "içe aktarma:" + fileName;
    }

    public static string Reprocess(string runId)
    {
        return "yeniden işleme:" + runId;
    }
}
