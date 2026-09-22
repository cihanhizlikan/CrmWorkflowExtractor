using System.Globalization;
using System.Text;
using Crm.Cli.Stages;
using Crm.Ir.Model;
using Crm.Similarity;

namespace Crm.Cli.Reports;

/// <summary>
/// <c>reports/migration.csv</c>: one row per workflow, with everything an analyst needs to plan the rebuild in
/// another product — what it is, what it touches, who calls it, which family it belongs to, whether it is known to
/// run, and how much of it the parser could not read. Sorted so the work that matters most is at the top.
/// </summary>
public static class MigrationPlan
{
    public static byte[] Csv(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity, UsageEvidence? usage)
    {
        Dictionary<Guid, WorkflowCluster> familyOf = [];
        foreach (WorkflowCluster cluster in similarity?.Clusters ?? [])
        {
            foreach (ClusterMember member in cluster.Members)
            {
                familyOf[member.WorkflowId] = cluster;
            }
        }
        CallGraph calls = CallGraph.Build(documents);
        (IReadOnlyDictionary<Guid, int> sharedFields, IReadOnlyDictionary<Guid, int> starts) = DataFootprint.PerWorkflow(documents);

        ExcelCsv csv = new("öncelik", "is_akisi", "bpmn_dosyasi", "kategori", "mod", "durum", "birincil_varlik", "tetikleyici",
            "adim", "okunamayan_adim", "ozel_etkinlikler", "cagirdigi", "cagiran", "rol",
            "aile", "aile_buyuklugu", "aile_rolu", "birlesik_dosya", "son_kayitli_calisma", "kullanim_hukmu",
            "adi_deneme_gibi", "urunle_gelen", "hassas_deger_var", "paylasilan_alan", "baslattigi_is_akisi", "yazdigi_varliklar", "yazdigi_alanlar");
        foreach (WorkflowIr document in documents.OrderBy(Priority).ThenBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            WorkflowIdentity identity = document.Identity;
            WorkflowCluster? family = familyOf.GetValueOrDefault(identity.WorkflowId);
            WorkflowUsage? found = usage?.Workflows.GetValueOrDefault(identity.WorkflowId);
            csv.Row(
                Priority(document),
                identity.Name,
                state.BpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "",
                identity.Category,
                identity.Mode,
                identity.State,
                identity.PrimaryEntity,
                Trigger(document.Trigger),
                CountSteps(document.Steps),
                state.UnmappedSteps.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.Dependencies.CustomActivities.Select(ShortType)),
                document.Dependencies.ChildWorkflowCalls.Count,
                calls.CalledBy.GetValueOrDefault(identity.WorkflowId, []).Count,
                calls.RoleOf(identity.WorkflowId),
                family is null || family.Members.Count < 2 ? "" : family.ClusterId,
                family is null || family.Members.Count < 2 ? "" : family.Members.Count,
                family is null || family.Members.Count < 2 ? "" : family.Medoid == identity.WorkflowId ? "başlangıç noktası" : "üye",
                family is not null && state.CombinedFiles.TryGetValue(family.ClusterId, out string? combined) ? combined + ".bpmn" : "",
                found?.LastLoggedRun is DateTimeOffset last ? last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                UsageStage.Verdict(identity, usage),
                UsageStage.NameSuggestsTest(identity.Name),
                identity.IsManaged == true,
                state.SensitiveWorkflows.Contains(identity.WorkflowId),
                sharedFields.GetValueOrDefault(identity.WorkflowId),
                starts.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.DataTouched.EntitiesWritten),
                string.Join(" | ", document.DataTouched.FieldsWritten.Take(12)));
        }
        return csv.ToBytes();
    }

    /// <summary>
    /// Reading order, not importance: what is certainly live first, what cannot run last. Inside a band the
    /// analyst sorts the sheet however they like — that is why every fact is its own column.
    /// </summary>
    private static int Priority(WorkflowIr document)
    {
        if (document.Identity.IsManaged == true)
        {
            return 5;
        }
        if (document.Identity.State == UsageStage.DraftState)
        {
            return 4;
        }
        if (UsageStage.NameSuggestsTest(document.Identity.Name))
        {
            return 3;
        }
        return document.Identity.Category is ProcessLabels.CategoryWorkflow or ProcessLabels.CategoryAction ? 1 : 2;
    }

    public static string Markdown(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity)
    {
        CallGraph calls = CallGraph.Build(documents);
        StringBuilder text = new();
        text.AppendLine("# Taşıma çalışma sayfası").AppendLine();
        text.AppendLine("`tasima-plani.csv` her iş akışı için bir satır tutar. Excel'de açıp sıralayın veya süzün; her bilgi ayrı bir sütun olduğundan sayfa tek bir sıraya mahkûm değildir, aklınıza gelen soruyu yanıtlar.").AppendLine();
        text.AppendLine("| Sütun | Ne işe yarar |").AppendLine("|---|---|");
        text.AppendLine("| `öncelik` | 1 canlı süreç · 2 diyalog, iş kuralı veya süreç akışı · 3 adı deneme gibi okunuyor · 4 taslak, çalışamaz · 5 ürünle gelmiş |");
        text.AppendLine("| `bpmn_dosyasi` | diyagram; `bpmn/<kategori>/<varlık>/` altında |");
        text.AppendLine("| `tetikleyici` | akışı ne başlatır: kayıt oluşturma, adı verilen alanların güncellenmesi, silme, istek üzerine |");
        text.AppendLine("| `adim` / `okunamayan_adim` | akışın büyüklüğü ve ayrıştırıcının okuyamadığı adım sayısı (bunları elle kontrol edin) |");
        text.AppendLine("| `ozel_etkinlikler` | yeni üründe karşılığı bulunmayan iş ortağı veya kurum içi kod |");
        text.AppendLine("| `cagirdigi` / `cagiran` / `rol` | çağrı ağacı: giriş noktası bir bütün olarak taşınır, yapı taşı birden çok süreççe paylaşılır |");
        text.AppendLine("| `aile` / `aile_rolu` / `birlesik_dosya` | birbirine çok benzeyen akışlar ve ailenin birleşik modeli |");
        text.AppendLine("| `son_kayitli_calisma` / `kullanim_hukmu` | kullanım kanıtı. Kaydın bulunmaması kullanılmadığını kanıtlamaz |");
        text.AppendLine("| `urunle_gelen` | CRM bu akışı yönetilen çözümün parçası olarak bildiriyor: ürünle gelmiş, burada yazılmamış. Gruplanmaz, yeniden kurulması gerekmez |");
        text.AppendLine("| `hassas_deger_var` | XAML içinde adres, kullanıcı adı veya parola benzeri değer var; kısıtlı rapora bakın |");
        text.AppendLine("| `paylasilan_alan` | yazdığı alanlardan başka bir iş akışının da yazdıkları — bkz. `veri-ayak-izi.md` |");
        text.AppendLine("| `baslattigi_is_akisi` | açıkça çağırmadan, yalnızca yazdığı için başlattığı iş akışları — bkz. `veri-zincirleri.csv` |");
        text.AppendLine("| `yazdigi_varliklar` / `yazdigi_alanlar` | veri ayak izi — aynı alana yazan iki akış yeni üründe dikkat ister |");
        text.AppendLine();

        int live = documents.Count(document => Priority(document) == 1);
        int blocks = calls.CalledBy.Count(entry => entry.Value.Count > 0);
        text.AppendLine(CultureInfo.InvariantCulture, $"**{documents.Count} iş akışı.** {live} tanesi canlı süreç (öncelik 1); {documents.Count(document => Priority(document) == 4)} tanesi taslak olduğu için çalışamaz; {documents.Count(document => Priority(document) == 5)} tanesi ürünle gelmiştir ve `{Crm.Extract.Runs.RunPaths.SuppliedCsv}` dosyasında listelenir. {blocks} tanesi başka bir iş akışınca çağrılır; bunlar ayrı bir kalem değil, ortak yapı taşıdır.").AppendLine();
        int families = similarity?.Clusters.Count(cluster => cluster.Members.Count > 1) ?? 0;
        int inFamilies = similarity?.Clusters.Where(cluster => cluster.Members.Count > 1).Sum(cluster => cluster.Members.Count) ?? 0;
        text.AppendLine(CultureInfo.InvariantCulture, $"{inFamilies} iş akışı, birbirine çok benzeyen {families} aileye ayrılır. Her ailenin başlangıç noktasından başlayın; kalanları ayrı birer kurulum değil, o akışın çeşitlemeleri olarak ele alın.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{state.SensitiveWorkflows.Count} iş akışı hassas değer içerir. Bu değerler diyagramlara da geçer — `bpmn/` klasörünü üretim verisi gibi koruyun.");
        return text.ToString();
    }

    private static int CountSteps(IReadOnlyList<StepNode> steps)
    {
        return steps.Sum(step => 1 + step.Branches.Sum(branch => CountSteps(branch.Steps)));
    }

    private static string ShortType(string assemblyQualifiedName)
    {
        string type = assemblyQualifiedName.Split(',')[0];
        int dot = type.LastIndexOf('.');
        return dot < 0 ? type : type[(dot + 1)..];
    }

    private static string Trigger(WorkflowTrigger trigger)
    {
        List<string> parts = [];
        if (trigger.OnCreate)
        {
            parts.Add("kayıt oluşturulunca");
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add("şu alanlar güncellenince: " + string.Join(", ", trigger.OnUpdateFields.Take(6)));
        }
        if (trigger.OnDelete)
        {
            parts.Add("kayıt silinince");
        }
        if (trigger.OnDemand)
        {
            parts.Add("istek üzerine");
        }
        return parts.Count == 0 ? "yalnızca alt süreç olarak" : string.Join(" · ", parts);
    }
}
