using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Similarity;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// M4: one BPMN file per IR document, named after its workflow, each validated against the OMG schemas. An invalid
/// file fails the run (§6.2). The diagrams are what an analyst opens most, so each one carries on its face what the
/// rest of the run learned about it — which is why this stage runs after grouping rather than before it.
/// </summary>
public static class BpmnStage
{
    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents,
        SimilarityResult families, UsageEvidence? usage, ILogger logger, CancellationToken token)
    {
        int invalid = 0;
        IReadOnlyDictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        IReadOnlyDictionary<Guid, string> stems = BpmnFileNames.Assign(documents.Select(document => (document.Identity.WorkflowId, document.Identity.Name)));
        // 1437 files in one folder is a wall. Category, then entity, is how an analyst divides the work.
        Dictionary<Guid, string> fileNames = documents.ToDictionary(
            document => document.Identity.WorkflowId,
            document => $"{BpmnFileNames.Slug(document.Identity.Category)}/{BpmnFileNames.Slug(document.Identity.PrimaryEntity ?? "no entity")}/{stems[document.Identity.WorkflowId]}");
        Facts facts = new(documents, families, usage, state.Drift);
        foreach (WorkflowIr document in documents)
        {
            XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(document, names, facts.For(document)), state.ToolVersion);
            string file = $"{RunPaths.Bpmn}/{fileNames[document.Identity.WorkflowId]}.bpmn";
            await folder.WriteBytesAsync(file, BpmnSerializer.ToBytes(xml), token);

            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);
            if (errors.Count > 0)
            {
                invalid++;
                state.Fail(ExitCode.RunFailed, $"{file} ('{document.Identity.Name}') BPMN 2.0 şemasına uymuyor: {string.Join(" | ", errors.Take(3))}");
            }
        }
        state.BpmnFiles = fileNames;
        state.Counts["bpmn.written"] = documents.Count;
        state.Counts["bpmn.invalid"] = invalid;
        state.StagesRun.Add(RunStages.Bpmn);
        logger.LogInformation("BPMN: {Written} written, {Invalid} invalid", documents.Count, invalid);
    }

    /// <summary>
    /// The four things a reader of one diagram would otherwise have to open a workbook to learn. Worked out once
    /// for the whole run, because each of them takes a pass over every workflow.
    /// </summary>
    private sealed class Facts
    {
        private readonly CallGraph _calls;
        private readonly UsageEvidence? _usage;
        private readonly Dictionary<Guid, WorkflowCluster> _familyOf = [];
        private readonly Dictionary<Guid, string> _names;
        private readonly HashSet<Guid> _drifted;

        public Facts(IReadOnlyList<WorkflowIr> documents, SimilarityResult families, UsageEvidence? usage, Crm.Extract.Xaml.DriftReport? drift)
        {
            _calls = CallGraph.Build(documents);
            _usage = usage;
            _names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
            foreach (WorkflowCluster cluster in families.Clusters.Where(cluster => cluster.Members.Count > 1))
            {
                foreach (ClusterMember member in cluster.Members)
                {
                    _familyOf[member.WorkflowId] = cluster;
                }
            }
            _drifted = [.. (drift?.Drifted ?? []).Where(finding => finding.StructureDiffers).Select(finding => finding.DefinitionId)];
        }

        public DiagramFacts For(WorkflowIr document)
        {
            Guid id = document.Identity.WorkflowId;
            // The short verdict, because the header note is read at a glance; the caveat behind it is in the guide.
            return new DiagramFacts(Role(id), Family(id), UsageStage.ShortVerdict(document.Identity, _usage) is { Length: > 0 } verdict ? verdict : null,
                _drifted.Contains(id));
        }

        /// <summary>Whether this diagram can be read on its own, or is one piece of something bigger.</summary>
        private string Role(Guid id)
        {
            int callers = _calls.CalledBy.TryGetValue(id, out IReadOnlyList<Guid>? parents) ? parents.Count : 0;
            int children = _calls.Calls.TryGetValue(id, out IReadOnlyList<Guid>? calls) ? calls.Count : 0;
            string role = callers == 0
                ? "giriş noktası — bunu başka bir iş akışı çağırmıyor"
                : string.Create(CultureInfo.InvariantCulture, $"yapı taşı — {callers} iş akışı bunu çağırıyor, tek başına taşınmaz");
            return children == 0 ? role : role + string.Create(CultureInfo.InvariantCulture, $"; kendisi {children} alt akış çağırıyor");
        }

        /// <summary>Whether someone is about to redesign the same process several times over.</summary>
        private string? Family(Guid id)
        {
            if (!_familyOf.TryGetValue(id, out WorkflowCluster? cluster))
            {
                return null;
            }
            string medoid = _names.GetValueOrDefault(cluster.Medoid, cluster.ClusterId);
            return string.Create(CultureInfo.InvariantCulture,
                $"\"{medoid}\" ailesinden {cluster.Members.Count} benzer akıştan biri — hepsini birlikte ele alın (aileler.xlsx)");
        }
    }
}
