using System.Globalization;
using System.Text;
using Crm.Extract.Inventory;
using Crm.Extract.Runs;
using Crm.Similarity;

namespace Crm.Cli.Reports;

/// <summary>§7.4 <c>raporlar/rapor.md</c>: çalıştırmadan sonra önce okunacak tek sayfa.</summary>
public static class RunReport
{
    public static string Markdown(RunState state)
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Çalıştırma raporu — {state.RunId}").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Durum **{state.Status}** (çıkış kodu {(int)state.ExitCode}). Araç {state.ToolVersion}. Kuruluş {state.OrganizationUrl ?? "(erişilmedi)"}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Çalışan aşamalar: {string.Join(" → ", state.StagesRun)}.").AppendLine();

        text.AppendLine("## Sayım zinciri").AppendLine();
        text.AppendLine("API sayımından BPMN dosyalarına kadar her iş akışının hesabı verilir. EKSİK satırı açıklanamayan bir kayıptır ve çalıştırmayı başarısız kılar.").AppendLine();
        foreach (CountLink link in state.CountChain)
        {
            text.Append("- ").AppendLine(link.ToString());
        }
        text.AppendLine();

        AppendInventory(text, state);
        AppendFamilies(text, state);

        text.AppendLine("## Diğer raporlar").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.ParseCoverage)}` — en az bir yapısı okunamayan {Count(state, "ir.workflowsWithUnmapped")} iş akışı; hiç ayrıştırılamayan {Count(state, "ir.parseFailed")} XAML dosyası.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.Drift)}` — çalışan kopyası farklı mantık taşıyan {Count(state, "drift.structureDiffers")} tanım; {Count(state, "drift.draftDefinitions")} taslak tanım.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.Consolidation)}` — {Count(state, "consolidation.workflowsCombined")} iş akışını kapsayan {Count(state, "consolidation.combined")} aile birleştirildi; {Count(state, "consolidation.skipped")} aile birleştirilmedi.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.PlanWorkbook)}` — çalışma kitabı: **{SheetNames.Plan}** (her iş akışı için bir satır, canlı süreçler başta), **{SheetNames.Usage}**, **{SheetNames.CallGraph}**, **{SheetNames.Diagrams}**. Sütun açıklamaları `{Name(RunPaths.MigrationPlan)}` dosyasındadır.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.ExternalSystemsWorkbook)}` / `{Name(RunPaths.ExternalSystems)}` — CRM dışına uzanan {Count(state, "external.activities")} özel etkinlik; iş akışlarının geçirdiği {Count(state, "external.addresses")} farklı adres.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.FamilyWorkbook)}` — gruplama kitabı: **{SheetNames.Families}**, **{SheetNames.Pairs}** (her çiftin benzerlik puanı), **{SheetNames.Drafts}**, **{SheetNames.Supplied}**.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.DataWorkbook)}` (**{SheetNames.DataFootprint}**, **{SheetNames.Cascades}**) ve `{Name(RunPaths.DataFootprint)}` — birden fazla iş akışının yazdığı {Count(state, "data.sharedFields")} alan; başka bir iş akışını başlatan {Count(state, "data.cascades")} yazma, {Count(state, "data.cascadePairs")} çift arasında.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.CallGraph)}` — {Count(state, "callGraph.entryPoints")} giriş noktası; başka bir iş akışının çağırdığı {Count(state, "callGraph.buildingBlocks")} iş akışı (tablosu çalışma kitabının **{SheetNames.CallGraph}** sayfasındadır).");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.Usage)}` (tablosu **{SheetNames.Usage}** sayfasında) — gruplamanın dışında tutulan {Count(state, "usage.drafts")} taslak tanım ve ürünle gelen {Count(state, "clusters.suppliedHeldApart")} iş akışı; adı deneme gibi okunan {Count(state, "usage.testNamedActive")} etkin iş akışı; {(state.StagesRun.Contains(RunStages.Usage) ? $"{Count(state, "usage.definitions")} tanımın {Count(state, "usage.withLoggedRun")} tanesinin çalıştığına dair kayıt var" : "kullanım kanıtı yok (Run:UsageFile)")}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.SensitiveLiterals)}` — {Count(state, "sensitive.findings")} bulgu. KISITLI: bilgi güvenliği ekibi içindir.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `../{RunPaths.ManualReviewIndex}` — ayrıştırılmayan, elle yazılmış {Count(state, "manualReview")} iş akışı.");
        text.AppendLine();

        text.AppendLine(CultureInfo.InvariantCulture, $"## Uyarılar ({state.Warnings.Count})").AppendLine();
        foreach (string warning in state.Warnings.Take(50))
        {
            text.Append("- ").AppendLine(warning);
        }
        if (state.Warnings.Count > 50)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- … ve `{RunPaths.WarningLog}` dosyasındaki {state.Warnings.Count - 50} uyarı daha.");
        }
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Başarısızlıklar ({state.Failures.Count})").AppendLine();
        foreach (string failure in state.Failures)
        {
            text.Append("- ").AppendLine(failure);
        }
        return text.ToString();
    }

    /// <summary>The file name on its own: inside raporlar/ the folder prefix is noise.</summary>
    private static string Name(string path)
    {
        return path[(path.IndexOf('/', StringComparison.Ordinal) + 1)..];
    }

    private static void AppendInventory(StringBuilder text, RunState state)
    {
        if (state.Records.Count == 0)
        {
            return;
        }
        text.AppendLine("## Kategori ve duruma göre iş akışları").AppendLine();
        List<string> states = [.. state.Records.Select(record => record.State.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        text.AppendLine("| Kategori | " + string.Join(" | ", states) + " | Toplam |");
        text.AppendLine("|---|" + string.Concat(states.Select(_ => "---:|")) + "---:|");
        foreach (IGrouping<string, WorkflowInventoryRecord> category in state.Records
            .Where(record => record.Type.Raw == WorkflowOptionSets.TypeDefinition)
            .GroupBy(record => record.Category.Label, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            IEnumerable<string> cells = states.Select(label => category.Count(record => record.State.Label == label).ToString(CultureInfo.InvariantCulture));
            text.AppendLine(CultureInfo.InvariantCulture, $"| {category.Key} | {string.Join(" | ", cells)} | {category.Count()} |");
        }
        text.AppendLine().AppendLine("Yalnızca tanımlar; etkinleştirme kayıtları tanımın kopyasıdır ve yukarıdaki zincirde sayılır.").AppendLine();
    }

    private static void AppendFamilies(StringBuilder text, RunState state)
    {
        if (state.Similarity is not SimilarityResult similarity)
        {
            return;
        }
        text.AppendLine("## Aileler").AppendLine();
        text.AppendLine("| Aile büyüklüğü | Aile sayısı |").AppendLine("|---:|---:|");
        foreach (IGrouping<int, WorkflowCluster> size in similarity.Clusters.GroupBy(cluster => cluster.Members.Count).OrderByDescending(group => group.Key))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {size.Key} | {size.Count()} |");
        }
        text.AppendLine().AppendLine("### En büyük 20 aile").AppendLine();
        text.AppendLine("| Aile | Üye | Önerilen başlangıç noktası | En zayıf iç benzerlik | Zincir derinliği | Tutarlılık |").AppendLine("|---|---:|---|---:|---:|---|");
        foreach (WorkflowCluster cluster in similarity.Clusters.Where(cluster => cluster.Members.Count > 1).Take(20))
        {
            string medoid = cluster.Members.First(member => member.WorkflowId == cluster.Medoid).Name;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{cluster.ClusterId}` | {cluster.Members.Count} | {medoid} | {cluster.MinimumInternalScore:0.00} | {cluster.ChainDepth} | {(cluster.LowCohesion ? "**zayıf — önce ayırın**" : "uygun")} |");
        }
        text.AppendLine();
    }

    private static int Count(RunState state, string key)
    {
        return state.Counts.TryGetValue(key, out int value) ? value : 0;
    }
}
