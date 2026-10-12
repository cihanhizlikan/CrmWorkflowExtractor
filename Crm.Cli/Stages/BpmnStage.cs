using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// M4: one BPMN file per IR document, named after its workflow, each validated against the OMG schemas. An invalid
/// file fails the run (§6.2). The diagrams are what an analyst opens most, so each one carries on its face what the
/// rest of the run learned about it — which stages fire it, what calls it, whether it runs.
///
/// <para>
/// Only the workflows in the plan are drawn into <c>bpmn/</c>, which is delivered: the plan tells its reader they need
/// not filter, and a folder holding every definition contradicted it. The rest are drawn into an internal folder
/// beside it, which the out-of-scope book points to.
/// </para>
/// </summary>
public static class BpmnStage
{
    private const int MostListed = 5;

    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents,
        UsageEvidence? usage, ILogger logger, CancellationToken token)
    {
        int invalid = 0;
        int lost = 0;
        IReadOnlyDictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        IReadOnlyDictionary<Guid, string> stems = BpmnFileNames.Assign(documents.Select(document => (document.Identity.WorkflowId, document.Identity.Name)));
        // 1437 files in one folder is a wall. Category, then record type, is how an analyst divides the work.
        Dictionary<Guid, string> fileNames = documents.ToDictionary(
            document => document.Identity.WorkflowId,
            document => $"{(MigrationPlan.Reason(state, document.Identity, usage) is null ? RunPaths.WorkflowDiagrams : RunPaths.OutOfScopeDiagrams)}/"
                + $"{BpmnFileNames.Slug(document.Identity.Category)}/{BpmnFileNames.Slug(document.Identity.PrimaryEntity ?? "no entity")}/{stems[document.Identity.WorkflowId]}");
        Facts facts = new(documents, usage, state);
        foreach (WorkflowIr document in documents)
        {
            string file = $"{fileNames[document.Identity.WorkflowId]}.bpmn";
            // ONE WORKFLOW MAY NOT COST THE RUN. This stage runs before every report, so an exception here used to
            // take the workbooks and the guide with it — and the thing that threw was a single stray byte in a single
            // name. What one diagram costs is now that diagram: the failure is recorded against its name, the run is
            // marked failed, and the rest are still drawn.
            try
            {
                XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(document, names, facts.For(document)), state.ToolVersion);
                await folder.WriteBytesAsync(file, BpmnSerializer.ToBytes(xml), token);

                IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);
                if (errors.Count > 0)
                {
                    invalid++;
                    state.Fail(ExitCode.RunFailed, $"{file} ('{document.Identity.Name}') BPMN 2.0 şemasına uymuyor: {string.Join(" | ", errors.Take(3))}");
                }
            }
            // A cancellation is the operator's and travels; everything else belongs to this one workflow. The
            // filter rather than a catch-and-rethrow because a filter that does not match never unwinds the
            // stack, so a debugger still stops where the throw was.
            catch (Exception error) when (error is not OperationCanceledException)
            {
                lost++;
                // No file was written, so nothing may point at one: the plan's bpmn_dosyasi cell stays empty and
                // the guide picks a different workflow to walk the reader through.
                fileNames.Remove(document.Identity.WorkflowId);
                state.Fail(ExitCode.RunFailed, $"{file} ('{document.Identity.Name}') çizilemedi: {error.GetType().Name}: {error.Message}");
                logger.LogError(error, "BPMN: {File} could not be drawn", file);
            }
        }
        state.BpmnFiles = fileNames;
        // The count the chain reconciles is what was WRITTEN, so a lost diagram shows up as a drop rather than
        // disappearing into a number that still says 1437.
        state.Counts["bpmn.written"] = documents.Count - lost;
        state.Counts["bpmn.delivered"] = fileNames.Values.Count(path => path.StartsWith(RunPaths.WorkflowDiagrams + "/", StringComparison.Ordinal));
        state.Counts["bpmn.invalid"] = invalid;
        state.Counts["bpmn.lost"] = lost;
        state.StagesRun.Add(RunStages.Bpmn);
        logger.LogInformation("BPMN: {Written} written ({Delivered} in the plan), {Invalid} invalid, {Lost} lost",
            documents.Count - lost, state.Counts["bpmn.delivered"], invalid, lost);
    }

    /// <summary>
    /// What a reader of one diagram would otherwise have to open a workbook to learn. Worked out once for the whole
    /// run, because each of them takes a pass over every workflow.
    /// </summary>
    private sealed class Facts(IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, RunState state)
    {
        private readonly CallGraph _calls = CallGraph.Build(documents);
        private readonly UsageEvidence? _usage = usage;
        private readonly HashSet<Guid> _drifted = [.. (state.Drift?.Drifted ?? []).Where(finding => finding.StructureDiffers).Select(finding => finding.DefinitionId)];
        private readonly IReadOnlyDictionary<string, string> _addresses = ExternalSystems.AddressesByActivity(state.Plugins);
        private readonly IReadOnlyDictionary<string, string> _hints = ExternalSystems.HintsByActivity(state.Plugins);

        public DiagramFacts For(WorkflowIr document)
        {
            Guid id = document.Identity.WorkflowId;
            IReadOnlyList<StageFiring> fired = state.FiredBy.GetValueOrDefault(id, []);
            IReadOnlyList<Guid> callers = _calls.CalledBy.GetValueOrDefault(id, []);
            return new DiagramFacts(Role(id, fired.Count), Usage(document.Identity), _drifted.Contains(id), Called(document, _addresses))
            {
                ActivityHints = Called(document, _hints),
                FiredBy = Listed(fired.Select(firing => firing.Text + Map(firing))),
                Callers = Listed(callers.Select(caller => _calls.Names.GetValueOrDefault(caller, caller.ToString("D"))).Order(StringComparer.Ordinal))
            };
        }

        /// <summary>The map a firing stage is drawn on, so the reader can go from this diagram to the process around it.</summary>
        private string Map(StageFiring firing)
        {
            return state.StageMapFiles.TryGetValue(firing.Stage.Id, out IReadOnlyList<string>? maps) && maps.Count > 0 ? " — " + maps[0] : "";
        }

        private static IReadOnlyList<string> Listed(IEnumerable<string> all)
        {
            List<string> list = [.. all];
            if (list.Count <= MostListed)
            {
                return list;
            }
            return [.. list.Take(MostListed), string.Create(CultureInfo.InvariantCulture, $"…ve {list.Count - MostListed} tane daha")];
        }

        /// <summary>The short verdict, because the header note is read at a glance; the caveat behind it is in the guide.</summary>
        private string? Usage(WorkflowIdentity identity)
        {
            string verdict = UsageStage.ShortVerdict(identity, _usage);
            if (verdict.Length == 0)
            {
                return null;
            }
            return _usage?.Workflows.GetValueOrDefault(identity.WorkflowId)?.LastLoggedRun is DateTimeOffset last
                ? verdict + " (son: " + last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")"
                : verdict;
        }

        /// <summary>
        /// What is known about the custom activities THIS workflow calls — their addresses, or their call hints.
        /// Only those: a diagram should carry the endpoints of its own steps, not the estate's.
        /// </summary>
        private static IReadOnlyDictionary<string, string> Called(WorkflowIr document, IReadOnlyDictionary<string, string> byActivity)
        {
            Dictionary<string, string> called = new(StringComparer.Ordinal);
            foreach (string activity in document.Dependencies.CustomActivities)
            {
                string type = activity.Split(',')[0].Trim();
                if (byActivity.TryGetValue(type, out string? found))
                {
                    called[type] = found;
                }
            }
            return called;
        }

        /// <summary>Whether this diagram can be read on its own, or is one piece of something bigger.</summary>
        private string Role(Guid id, int stages)
        {
            int callers = _calls.CalledBy.TryGetValue(id, out IReadOnlyList<Guid>? parents) ? parents.Count : 0;
            int children = _calls.Calls.TryGetValue(id, out IReadOnlyList<Guid>? calls) ? calls.Count : 0;
            List<string> parts = [];
            if (stages > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"aşama adımı — {stages} talep aşamasının sonucunda çalışır; aşama haritasında bir adım olarak tasarlayın"));
            }
            if (callers > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"yapı taşı — {callers} iş akışı bunu çağırıyor, tek başına taşınmaz"));
            }
            if (parts.Count == 0)
            {
                parts.Add("giriş noktası — bunu ne bir aşama ne başka bir iş akışı çalıştırıyor");
            }
            string role = string.Join("; ", parts);
            return children == 0 ? role : role + string.Create(CultureInfo.InvariantCulture, $"; kendisi {children} alt iş akışı çağırıyor");
        }
    }
}
