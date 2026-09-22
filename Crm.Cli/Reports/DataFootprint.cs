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
    public const string CascadeOnUpdate = "field update";
    public const string CascadeOnCreate = "record created";

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

    public static byte[] Csv(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        ExcelCsv csv = new("entity", "field", "writers", "readers", "workflows_started_by_this_field",
            "shared_write", "starts_a_workflow", "writer_names", "reader_names", "started_workflow_names");
        foreach (FieldUse use in Fields(documents).OrderByDescending(use => use.Writers.Count).ThenBy(use => use.Entity, StringComparer.Ordinal).ThenBy(use => use.Field, StringComparer.Ordinal))
        {
            csv.Row(use.Entity, use.Field, use.Writers.Count, use.Readers.Count, use.TriggeredBy.Count,
                use.Writers.Count > 1, use.TriggeredBy.Count > 0 && use.Writers.Count > 0,
                Names(names, use.Writers), Names(names, use.Readers), Names(names, use.TriggeredBy));
        }
        return csv.ToBytes();
    }

    public static byte[] CascadeCsv(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, WorkflowIdentity> byId = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity);
        ExcelCsv csv = new("source_workflow", "starts", "target_workflow", "through", "source_mode", "target_mode", "self");
        foreach (Cascade cascade in Cascades(documents)
            .OrderBy(cascade => byId[cascade.Source].Name, StringComparer.Ordinal)
            .ThenBy(cascade => byId[cascade.Target].Name, StringComparer.Ordinal))
        {
            csv.Row(byId[cascade.Source].Name, cascade.Kind, byId[cascade.Target].Name, cascade.Through,
                byId[cascade.Source].Mode, byId[cascade.Target].Mode, cascade.Source == cascade.Target);
        }
        return csv.ToBytes();
    }

    public static string Markdown(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, WorkflowIdentity> byId = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity);
        IReadOnlyList<FieldUse> fields = Fields(documents);
        List<FieldUse> shared = [.. fields.Where(use => use.Writers.Count > 1).OrderByDescending(use => use.Writers.Count)];
        IReadOnlyList<Cascade> cascades = Cascades(documents);

        StringBuilder text = new();
        text.AppendLine("# Data footprint").AppendLine();
        text.AppendLine("Which workflows touch which data. Two things no single diagram shows: a field several workflows write, and a write that starts another workflow.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- {fields.Count} field(s) written, read or triggering across {documents.Count} workflows");
        text.AppendLine(CultureInfo.InvariantCulture, $"- **{shared.Count} field(s) are written by more than one workflow.** CRM never guaranteed the order they ran in; the new product has to decide one");
        text.AppendLine(CultureInfo.InvariantCulture, $"- **{cascades.Count} cascade(s)** between {cascades.Select(cascade => (cascade.Source, cascade.Target)).Distinct().Count()} pairs of workflows: one workflow's write starts another. {cascades.Count(cascade => cascade.Source == cascade.Target)} of them start themselves").AppendLine();

        text.AppendLine("## Entities, most written first").AppendLine();
        text.AppendLine("| Entity | Fields written | Workflows writing | Workflows reading |").AppendLine("|---|---:|---:|---:|");
        foreach (IGrouping<string, FieldUse> entity in fields.Where(use => use.Entity.Length > 0)
            .GroupBy(use => use.Entity, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(use => use.Writers.Count))
            .Take(30))
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {entity.Key} | {entity.Count(use => use.Writers.Count > 0)} | {entity.SelectMany(use => use.Writers).Distinct().Count()} | {entity.SelectMany(use => use.Readers).Distinct().Count()} |");
        }
        text.AppendLine();

        text.AppendLine("## Fields several workflows write").AppendLine();
        text.AppendLine("Decide an order for each of these, or merge the writers. A row mixing Real-time and Background is the least predictable in CRM today.").AppendLine();
        text.AppendLine("| Field | Writers | Modes | Workflows |").AppendLine("|---|---:|---|---|");
        foreach (FieldUse use in shared.Take(50))
        {
            IReadOnlyList<string> modes = [.. use.Writers.Select(writer => byId[writer].Mode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {use.Entity}.{use.Field} | {use.Writers.Count} | {string.Join(" + ", modes)} | {string.Join(", ", use.Writers.Take(8).Select(writer => byId[writer].Name))}{(use.Writers.Count > 8 ? ", …" : "")} |");
        }
        text.AppendLine();

        text.AppendLine("## Cascades: a write that starts another workflow").AppendLine();
        text.AppendLine("These chains are invisible in the diagrams — CRM starts the second workflow because the first one wrote a field it watches, or created a record. In the new product they have to be made explicit or rebuilt as one process. Every cascade is in `data-cascades.csv`; this page rolls them up, because the list itself is too long to read.").AppendLine();

        text.AppendLine(CultureInfo.InvariantCulture, $"### The fields that set off the most work ({fields.Count(use => use.Writers.Count > 0 && use.TriggeredBy.Count > 0)} fields start something)").AppendLine();
        text.AppendLine("Each of these is written by several workflows and watched by several others, so one write fans out. Fix these first: decide who owns the field.").AppendLine();
        text.AppendLine("| Field | Written by | Starts | Cascades |").AppendLine("|---|---:|---:|---:|");
        foreach (FieldUse use in fields.Where(use => use.Writers.Count > 0 && use.TriggeredBy.Count > 0)
            .OrderByDescending(use => use.Writers.Count * use.TriggeredBy.Count)
            .ThenBy(use => use.Field, StringComparer.Ordinal)
            .Take(25))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {use.Entity}.{use.Field} | {use.Writers.Count} | {use.TriggeredBy.Count} | {use.Writers.Count * use.TriggeredBy.Count} |");
        }
        text.AppendLine();

        List<IGrouping<(Guid Source, Guid Target), Cascade>> pairs = [.. cascades.GroupBy(cascade => (cascade.Source, cascade.Target))];
        text.AppendLine(CultureInfo.InvariantCulture, $"### Workflow pairs ({pairs.Count} pairs, {pairs.Count(pair => pair.Key.Source == pair.Key.Target)} of them a workflow starting itself)").AppendLine();
        text.AppendLine("| Starts | Then runs | Through | Modes |").AppendLine("|---|---|---|---|");
        foreach (IGrouping<(Guid Source, Guid Target), Cascade> pair in pairs
            .OrderByDescending(pair => pair.Count())
            .ThenBy(pair => byId[pair.Key.Source].Name, StringComparer.Ordinal)
            .Take(60))
        {
            string self = pair.Key.Source == pair.Key.Target ? " **(itself)**" : "";
            IReadOnlyList<string> through = [.. pair.Select(cascade => cascade.Through).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];
            string shown = string.Join(", ", through.Take(3)) + (through.Count > 3 ? $", …({through.Count})" : "");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {byId[pair.Key.Source].Name} | {byId[pair.Key.Target].Name}{self} | {shown} | {byId[pair.Key.Source].Mode} → {byId[pair.Key.Target].Mode} |");
        }
        if (pairs.Count > 60)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"…and {pairs.Count - 60} more pairs in `data-cascades.csv`.");
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
        return string.Join(" | ", workflows.Select(id => names.GetValueOrDefault(id, id.ToString("D"))).Order(StringComparer.Ordinal));
    }
}
