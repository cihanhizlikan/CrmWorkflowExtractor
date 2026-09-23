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

        AppendUsage(text, state);

        text.AppendLine("## Çalışma kitapları").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Sistem analisti `{Name(RunPaths.AnalystGuide)}` dosyasından başlar: dosyaların hangi sırayla açılacağını ve bir iş akışının adım adım nasıl çözümleneceğini anlatır.");
        text.AppendLine("Her kitabın ilk sayfası **Nasıl okunur**: sütunlar, uyarılar ve bu çalıştırmanın sayıları oradadır.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.PlanWorkbook)}` — **işin kendisi.** Sayfalar: {SheetNames.Plan} (taşınacak her iş akışı için bir satır) · {SheetNames.CallGraph} · {SheetNames.Trees} · {SheetNames.Unmapped} ({Count(state, "ir.workflowsWithUnmapped")} iş akışı) · {SheetNames.Drift} (çalışan kopyası farklı {Count(state, "drift.structureDiffers")} tanım).");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.OutOfScopeWorkbook)}` — **planın dışında kalanlar:** {Count(state, "plan.excluded")} iş akışı ({Count(state, "usage.drafts")} taslak, {Count(state, "clusters.suppliedHeldApart")} ürünle gelen, kalanı adı deneme gibi olup hiç çalışmamış olanlar). Plan sayfasını süzmeye gerek yok.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.FamilyWorkbook)}` — **hangileri aynı.** Sayfalar: {SheetNames.Families} · {SheetNames.Consolidation} ({Count(state, "consolidation.workflowsCombined")} iş akışını kapsayan {Count(state, "consolidation.combined")} aile birleştirildi, {Count(state, "consolidation.skipped")} birleştirilmedi) · {SheetNames.Pairs}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.DataWorkbook)}` — **ne neye dokunuyor.** Birden fazla iş akışının yazdığı {Count(state, "data.sharedFields")} alan; {Count(state, "data.cascadePairs")} çift arasında {Count(state, "data.cascades")} tetikleme zinciri.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.ExternalSystemsWorkbook)}` — **CRM dışına ne uzanıyor.** {Count(state, "external.activities")} özel etkinlik; iş akışlarının geçirdiği {Count(state, "external.addresses")} farklı adres.");
        text.AppendLine();
        text.AppendLine("## Kısıtlı ve yardımcı dosyalar").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- `{Name(RunPaths.SensitiveLiterals)}` — {Count(state, "sensitive.findings")} bulgu. KISITLI: bilgi güvenliği ekibi içindir, pakete konmaz.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Ayrıştırılmayan, elle yazılmış {Count(state, "manualReview")} iş akışı var: ayrıştırılmadıkları için taşıma planında satırları YOKTUR. Listesi çalıştırma klasöründeki `{RunPaths.ManualReviewIndex}` dosyasındadır.");
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

    /// <summary>
    /// What the run evidence says, in one table. Two things are certain — a Draft cannot start a run, and a logged
    /// run proves one happened — and the absence of a record proves nothing; that sentence travels with the numbers.
    /// </summary>
    private static void AppendUsage(StringBuilder text, RunState state)
    {
        if (state.UsageVerdicts.Count == 0)
        {
            return;
        }
        text.AppendLine("## Kullanım").AppendLine();
        text.AppendLine("Kesin olan iki şey var: **Taslak** bir tanım yeni çalıştırma başlatamaz ve **kayıtlı bir çalışma** o akışın çalıştığını kanıtlar. Kaydın bulunmaması hiçbir şeyi kanıtlamaz: sistem işleri düzenli olarak silinir, gerçek zamanlı akışlar yalnızca hatayı kaydeder, iş kuralları hiç kayıt bırakmaz.").AppendLine();
        if (state.UsageHorizon.Length > 0)
        {
            text.AppendLine(state.UsageHorizon).AppendLine();
        }
        text.AppendLine("| Hüküm | Tanım |").AppendLine("|---|---:|");
        foreach (KeyValuePair<string, int> verdict in state.UsageVerdicts.OrderBy(verdict => verdict.Key, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {verdict.Key} | {verdict.Value} |");
        }
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture,
            $"Adı taslak veya deneme gibi okunan {Count(state, "usage.testNamedActive")} etkin iş akışı var; kayıtlı çalışması olanlar planda bırakıldı, ada göre temizlik yapmayın. Kalanlar `{Name(RunPaths.OutOfScopeWorkbook)}` kitabındadır.").AppendLine();
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
