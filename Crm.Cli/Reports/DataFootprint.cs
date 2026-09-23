using System.Globalization;
using System.Text;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>One field, and every workflow that writes it, reads it, or is started by a change to it.</summary>
public sealed record FieldUse(string Entity, string Field, IReadOnlyList<Guid> Writers, IReadOnlyList<Guid> Readers, IReadOnlyList<Guid> TriggeredBy);

/// <summary>
/// One workflow's write starting another workflow. <see cref="Through"/> is the field that was written, or the
/// entity that was created.
/// </summary>
public sealed record Cascade(Guid Source, Guid Target, string Through, string Kind);

/// <summary>
/// What the workflows do to the data, seen field by field rather than diagram by diagram. Two things no single
/// diagram can show: a field several workflows write (in the new product their order has to be decided, because
/// CRM's was never guaranteed), and a write that starts another workflow — coupling that exists only because CRM
/// fires on field updates.
/// </summary>
public static class DataFootprint
{
    public const string CascadeOnUpdate = "alan güncellendi";
    public const string CascadeOnCreate = "kayıt oluşturuldu";

    public static IReadOnlyList<FieldUse> Fields(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<string, List<Guid>> writers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<Guid>> readers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<Guid>> triggered = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowIr document in documents)
        {
            foreach (string field in document.DataTouched.FieldsWritten)
            {
                Add(writers, field, document.Identity.WorkflowId);
            }
            foreach (string field in document.DataTouched.FieldsRead)
            {
                Add(readers, field, document.Identity.WorkflowId);
            }
            foreach (string field in document.Trigger.OnUpdateFields)
            {
                if (document.Identity.PrimaryEntity is string entity)
                {
                    Add(triggered, $"{entity}.{field}", document.Identity.WorkflowId);
                }
            }
        }

        List<FieldUse> uses = [];
        foreach (string key in writers.Keys.Concat(readers.Keys).Concat(triggered.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            int dot = key.IndexOf('.', StringComparison.Ordinal);
            uses.Add(new FieldUse(
                dot < 0 ? "" : key[..dot],
                dot < 0 ? key : key[(dot + 1)..],
                Get(writers, key),
                Get(readers, key),
                Get(triggered, key)));
        }
        return uses;
    }

    /// <summary>
    /// Every write that starts another workflow: a field update that another workflow triggers on, and a record
    /// created that another workflow triggers on. A workflow that starts itself is kept and marked, because that
    /// is a loop somebody has to break in the new product.
    /// </summary>
    public static IReadOnlyList<Cascade> Cascades(IReadOnlyList<WorkflowIr> documents)
    {
        List<Cascade> cascades = [];
        foreach (FieldUse use in Fields(documents).Where(use => use.Writers.Count > 0 && use.TriggeredBy.Count > 0))
        {
            foreach (Guid source in use.Writers)
            {
                foreach (Guid target in use.TriggeredBy)
                {
                    cascades.Add(new Cascade(source, target, $"{use.Entity}.{use.Field}", CascadeOnUpdate));
                }
            }
        }

        Dictionary<string, List<Guid>> createdBy = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowIr document in documents)
        {
            foreach (string entity in Created(document.Steps))
            {
                Add(createdBy, entity, document.Identity.WorkflowId);
            }
        }
        foreach (WorkflowIr document in documents.Where(document => document.Trigger.OnCreate && document.Identity.PrimaryEntity is not null))
        {
            foreach (Guid source in Get(createdBy, document.Identity.PrimaryEntity!))
            {
                cascades.Add(new Cascade(source, document.Identity.WorkflowId, document.Identity.PrimaryEntity!, CascadeOnCreate));
            }
        }
        return [.. cascades.DistinctBy(cascade => (cascade.Source, cascade.Target, cascade.Through, cascade.Kind))];
    }

    public static Sheet Build(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        Sheet csv = new(SheetNames.DataFootprint, "varlik", "alan", "yazan", "bu_alanin_baslattigi", "okuyan",
            "yazanlar", "baslattiklari", "okuyanlar");
        foreach (FieldUse use in Fields(documents).OrderByDescending(use => use.Writers.Count).ThenBy(use => use.Entity, StringComparer.Ordinal).ThenBy(use => use.Field, StringComparer.Ordinal))
        {
            csv.Row(use.Entity, use.Field, use.Writers.Count, use.TriggeredBy.Count, use.Readers.Count,
                Names(names, use.Writers), Names(names, use.TriggeredBy), Names(names, use.Readers));
        }
        return csv;
    }

    public static Sheet BuildCascades(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, WorkflowIdentity> byId = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity);
        Sheet csv = new(SheetNames.Cascades, "baslatan", "baslayan", "nasil", "hangi_veri", "kendini_baslatiyor", "baslatan_modu", "baslayan_modu");
        foreach (Cascade cascade in Cascades(documents)
            .OrderBy(cascade => byId[cascade.Source].Name, StringComparer.Ordinal)
            .ThenBy(cascade => byId[cascade.Target].Name, StringComparer.Ordinal))
        {
            csv.Row(byId[cascade.Source].Name, byId[cascade.Target].Name, cascade.Kind, cascade.Through,
                cascade.Source == cascade.Target, byId[cascade.Source].Mode, byId[cascade.Target].Mode);
        }
        return csv;
    }

    public static string Markdown(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, WorkflowIdentity> byId = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity);
        IReadOnlyList<FieldUse> fields = Fields(documents);
        List<FieldUse> shared = [.. fields.Where(use => use.Writers.Count > 1).OrderByDescending(use => use.Writers.Count)];
        IReadOnlyList<Cascade> cascades = Cascades(documents);

        StringBuilder text = new();
        text.AppendLine("# Veri ayak izi").AppendLine();
        text.AppendLine("Hangi iş akışının hangi veriye dokunduğu. Tek bir diyagramın gösteremediği iki şey: birden fazla akışın yazdığı bir alan ve başka bir akışı başlatan bir yazma.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- {documents.Count} iş akışı genelinde yazılan, okunan veya tetikleyen {fields.Count} alan");
        text.AppendLine(CultureInfo.InvariantCulture, $"- **{shared.Count} alana birden fazla iş akışı yazıyor.** CRM bunların hangi sırayla çalışacağını hiçbir zaman garanti etmedi; yeni üründe bir sıra kararlaştırılmalı");
        text.AppendLine(CultureInfo.InvariantCulture, $"- {cascades.Select(cascade => (cascade.Source, cascade.Target)).Distinct().Count()} iş akışı çifti arasında **{cascades.Count} tetikleme zinciri**: bir akışın yazması diğerini başlatıyor. Bunların {cascades.Count(cascade => cascade.Source == cascade.Target)} tanesi kendini başlatıyor").AppendLine();

        text.AppendLine("## En çok yazılan varlıklar").AppendLine();
        text.AppendLine("| Varlık | Yazılan alan | Yazan iş akışı | Okuyan iş akışı |").AppendLine("|---|---:|---:|---:|");
        foreach (IGrouping<string, FieldUse> entity in fields.Where(use => use.Entity.Length > 0)
            .GroupBy(use => use.Entity, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(use => use.Writers.Count))
            .Take(30))
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {entity.Key} | {entity.Count(use => use.Writers.Count > 0)} | {entity.SelectMany(use => use.Writers).Distinct().Count()} | {entity.SelectMany(use => use.Readers).Distinct().Count()} |");
        }
        text.AppendLine();

        text.AppendLine("## Birden fazla iş akışının yazdığı alanlar").AppendLine();
        text.AppendLine("Bunların her biri için bir sıra kararlaştırın ya da yazan akışları birleştirin. Gerçek zamanlı ile arka planı bir arada barındıran satırlar, bugün CRM'de en öngörülemeyen olanlardır.").AppendLine();
        text.AppendLine("| Alan | Yazan | Mod | İş akışları |").AppendLine("|---|---:|---|---|");
        foreach (FieldUse use in shared.Take(50))
        {
            IReadOnlyList<string> modes = [.. use.Writers.Select(writer => byId[writer].Mode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {use.Entity}.{use.Field} | {use.Writers.Count} | {string.Join(" + ", modes)} | {string.Join(", ", use.Writers.Take(8).Select(writer => byId[writer].Name))}{(use.Writers.Count > 8 ? ", …" : "")} |");
        }
        text.AppendLine();

        text.AppendLine("## Tetikleme zincirleri: başka bir iş akışını başlatan yazmalar").AppendLine();
        text.AppendLine("Bu zincirler diyagramlarda görünmez — ilk akış, ikincinin izlediği bir alana yazdığı ya da bir kayıt oluşturduğu için CRM ikinciyi başlatır. Yeni üründe bu bağ ya açıkça kurulmalı ya da iki akış tek süreç olarak yeniden yazılmalıdır. Zincirlerin tamamı `veri-analizi.xlsx` kitabının **Tetikleme zincirleri** sayfasındadır; liste okunamayacak kadar uzun olduğu için bu sayfa özetler.").AppendLine();

        text.AppendLine(CultureInfo.InvariantCulture, $"### En çok işi tetikleyen alanlar (bir şey başlatan {fields.Count(use => use.Writers.Count > 0 && use.TriggeredBy.Count > 0)} alan)").AppendLine();
        text.AppendLine("Bu alanların her birine birden çok akış yazar, birden çok akış da onu izler; dolayısıyla tek bir yazma dallanarak yayılır. Önce bunları ele alın: alanın sahibinin kim olduğuna karar verin.").AppendLine();
        text.AppendLine("| Alan | Yazan | Başlattığı | Zincir |").AppendLine("|---|---:|---:|---:|");
        foreach (FieldUse use in fields.Where(use => use.Writers.Count > 0 && use.TriggeredBy.Count > 0)
            .OrderByDescending(use => use.Writers.Count * use.TriggeredBy.Count)
            .ThenBy(use => use.Field, StringComparer.Ordinal)
            .Take(25))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {use.Entity}.{use.Field} | {use.Writers.Count} | {use.TriggeredBy.Count} | {use.Writers.Count * use.TriggeredBy.Count} |");
        }
        text.AppendLine();

        List<IGrouping<(Guid Source, Guid Target), Cascade>> pairs = [.. cascades.GroupBy(cascade => (cascade.Source, cascade.Target))];
        text.AppendLine(CultureInfo.InvariantCulture, $"### İş akışı çiftleri ({pairs.Count} çift; {pairs.Count(pair => pair.Key.Source == pair.Key.Target)} tanesi kendini başlatan akış)").AppendLine();
        text.AppendLine("| Başlatan | Sonra çalışan | Hangi veri üzerinden | Mod |").AppendLine("|---|---|---|---|");
        foreach (IGrouping<(Guid Source, Guid Target), Cascade> pair in pairs
            .OrderByDescending(pair => pair.Count())
            .ThenBy(pair => byId[pair.Key.Source].Name, StringComparer.Ordinal)
            .Take(60))
        {
            string self = pair.Key.Source == pair.Key.Target ? " **(kendisi)**" : "";
            IReadOnlyList<string> through = [.. pair.Select(cascade => cascade.Through).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];
            string shown = string.Join(", ", through.Take(3)) + (through.Count > 3 ? $", …({through.Count})" : "");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {byId[pair.Key.Source].Name} | {byId[pair.Key.Target].Name}{self} | {shown} | {byId[pair.Key.Source].Mode} → {byId[pair.Key.Target].Mode} |");
        }
        if (pairs.Count > 60)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"…ve `veri-analizi.xlsx` kitabındaki {pairs.Count - 60} çift daha.");
        }
        return text.ToString();
    }

    /// <summary>Per workflow: how many of its written fields another workflow also writes, and how many workflows its writes start.</summary>
    public static (IReadOnlyDictionary<Guid, int> SharedFields, IReadOnlyDictionary<Guid, int> Starts) PerWorkflow(IReadOnlyList<WorkflowIr> documents)
    {
        IReadOnlyList<FieldUse> fields = Fields(documents);
        Dictionary<Guid, int> sharedFields = [];
        foreach (FieldUse use in fields.Where(use => use.Writers.Count > 1))
        {
            foreach (Guid writer in use.Writers)
            {
                sharedFields[writer] = sharedFields.GetValueOrDefault(writer) + 1;
            }
        }
        Dictionary<Guid, int> starts = [];
        foreach (IGrouping<Guid, Cascade> group in Cascades(documents).Where(cascade => cascade.Source != cascade.Target).GroupBy(cascade => cascade.Source))
        {
            starts[group.Key] = group.Select(cascade => cascade.Target).Distinct().Count();
        }
        return (sharedFields, starts);
    }

    private static IReadOnlyList<string> Created(IReadOnlyList<StepNode> steps)
    {
        List<string> entities = [];
        foreach (StepNode step in steps)
        {
            if (step.Kind == StepKind.CreateRecord && step.Entity is string entity)
            {
                entities.Add(entity);
            }
            entities.AddRange(step.Branches.SelectMany(branch => Created(branch.Steps)));
        }
        return entities;
    }

    private static void Add(Dictionary<string, List<Guid>> map, string key, Guid workflowId)
    {
        if (!map.TryGetValue(key, out List<Guid>? list))
        {
            list = [];
            map[key] = list;
        }
        if (!list.Contains(workflowId))
        {
            list.Add(workflowId);
        }
    }

    private static IReadOnlyList<Guid> Get(Dictionary<string, List<Guid>> map, string key)
    {
        return map.TryGetValue(key, out List<Guid>? list) ? list : [];
    }

    private static string Names(IReadOnlyDictionary<Guid, string> names, IReadOnlyList<Guid> workflows)
    {
        return Sheet.List(workflows.Select(id => names.GetValueOrDefault(id, id.ToString("D"))).Order(StringComparer.Ordinal));
    }
}
