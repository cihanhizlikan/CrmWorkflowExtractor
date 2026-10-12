using System.Text.Json;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Bpmn.Stages;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Ir.Stages;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// One diagram per ACTIVE PRIMARY stage: the process a case of that subcategory goes through, every stage reachable
/// from where it starts, and every workflow fired on the way, embedded.
///
/// <para>
/// This is the diagram the analysts asked for (chief analyst, 2026-10-09). The workflow diagrams say what a
/// workflow does; only this says WHEN it runs — on which outcome of which stage of which request — and that was
/// never in any workflow.
/// </para>
///
/// <para>
/// It runs in two halves. The stages are READ before any diagram is drawn, because a workflow that a stage fires has to
/// say so — on its plan row and on its own diagram — and is in scope for that reason alone. The maps are DRAWN after
/// the workflow diagrams, because they embed them.
/// </para>
/// </summary>
public static class StageMapStage
{
    /// <summary>The stages, which map each lands on, the file each map is written to, and which stages fire each workflow.</summary>
    public static async Task ReadAsync(RunFolder folder, RunState state, CancellationToken token)
    {
        string source = folder.PathOf(RunPaths.RawCaseStages);
        if (!File.Exists(source))
        {
            return;
        }
        using JsonDocument rows = JsonDocument.Parse(await File.ReadAllTextAsync(source, token));
        IReadOnlyList<CaseStage> stages = CaseStageReader.Read(rows.RootElement.EnumerateArray());
        state.CaseStages = stages;
        state.Counts["caseStages.active"] = stages.Count(stage => stage.Active);

        Dictionary<Guid, CaseStage> byId = stages.ToDictionary(stage => stage.Id);
        List<CaseStage> primaries = [.. stages.Where(stage => stage.Primary && stage.Active)];
        Dictionary<Guid, string> files = Files(primaries);
        state.StageMapPaths = files;
        state.StageMapFiles = AppearsOn(primaries, byId, files);
        state.FiredBy = Firing(stages);
    }

    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, ILogger logger, CancellationToken token)
    {
        if (state.CaseStages.Count == 0)
        {
            return;
        }
        Drawing drawing = new(state, state.CaseStages, documents);
        foreach (CaseStage primary in state.CaseStages.Where(stage => stage.Primary && stage.Active))
        {
            string file = state.StageMapPaths[primary.Id];
            // As with the workflow diagrams: one map may not cost the run. A cancellation is the operator's.
            try
            {
                await drawing.DrawAsync(folder, primary, file, token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                drawing.Lost++;
                state.Fail(ExitCode.RunFailed, $"{file} ('{primary.Name}') çizilemedi: {error.GetType().Name}: {error.Message}");
                logger.LogError(error, "Stage map: {File} could not be drawn", file);
            }
        }
        state.StageMapLabels = drawing.Labels;
        state.Counts["stageMaps.written"] = drawing.Written;
        state.Counts["stageMaps.lost"] = drawing.Lost;
        state.Counts["stageMaps.workflowsEmbedded"] = drawing.Embedded;
        state.Counts["stageMaps.workflowBoxes"] = drawing.Boxes;
        state.Counts["stageMaps.workflowsMissing"] = drawing.Missing;
        state.StagesRun.Add(RunStages.StageMaps);
        logger.LogInformation("Stage maps: {Written} written, {Embedded} workflows embedded in {Boxes} boxes, {Missing} missing",
            drawing.Written, drawing.Embedded, drawing.Boxes, drawing.Missing);
    }

    /// <summary>
    /// Each map's file, named for what a reader holds: the request's subject and the stage it starts at,
    /// <c>reklam--sikayet-aramasi-yap.bpmn</c>. An id is added only to the second of two that would collide.
    /// </summary>
    private static Dictionary<Guid, string> Files(IReadOnlyList<CaseStage> primaries)
    {
        Dictionary<Guid, string> files = [];
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        foreach (CaseStage primary in primaries.OrderBy(stage => stage.Id))
        {
            string subject = primary.Process.Length > 0 ? primary.Process : primary.Name;
            string stem = BpmnFileNames.Slug(subject) + "--" + BpmnFileNames.Slug(primary.ShortName);
            if (!taken.Add(stem))
            {
                stem += "-" + primary.Id.ToString("N")[..8];
                taken.Add(stem);
            }
            files[primary.Id] = $"{RunPaths.StageMaps}/{stem}.bpmn";
        }
        return files;
    }

    /// <summary>Which maps each stage is drawn on: every stage reachable from a primary, along its outcomes, as the map walks.</summary>
    private static Dictionary<Guid, IReadOnlyList<string>> AppearsOn(IReadOnlyList<CaseStage> primaries,
        Dictionary<Guid, CaseStage> byId, Dictionary<Guid, string> files)
    {
        Dictionary<Guid, List<string>> appearsOn = [];
        foreach (CaseStage primary in primaries)
        {
            HashSet<Guid> seen = [primary.Id];
            Queue<CaseStage> queue = new([primary]);
            while (queue.TryDequeue(out CaseStage? stage))
            {
                if (!appearsOn.TryGetValue(stage.Id, out List<string>? on))
                {
                    on = [];
                    appearsOn[stage.Id] = on;
                }
                on.Add(files[primary.Id]);
                foreach (StageExit exit in stage.Exits)
                {
                    if (exit.NextStage is Guid next && byId.TryGetValue(next, out CaseStage? following) && seen.Add(next))
                    {
                        queue.Enqueue(following);
                    }
                }
            }
        }
        Dictionary<Guid, IReadOnlyList<string>> result = [];
        foreach ((Guid stage, List<string> on) in appearsOn)
        {
            result[stage] = on;
        }
        return result;
    }

    /// <summary>Per workflow, the active stages that fire it and on which outcomes.</summary>
    private static Dictionary<Guid, IReadOnlyList<StageFiring>> Firing(IReadOnlyList<CaseStage> stages)
    {
        Dictionary<Guid, List<StageFiring>> firing = [];
        foreach (CaseStage stage in stages.Where(stage => stage.Active))
        {
            foreach (IGrouping<Guid, StageExit> fired in stage.Exits.Where(exit => exit.Workflow is not null).GroupBy(exit => exit.Workflow!.Value))
            {
                if (!firing.TryGetValue(fired.Key, out List<StageFiring>? by))
                {
                    by = [];
                    firing[fired.Key] = by;
                }
                by.Add(new StageFiring(stage, string.Join(", ", fired.Select(exit => exit.Outcome))));
            }
        }
        Dictionary<Guid, IReadOnlyList<StageFiring>> result = [];
        foreach ((Guid workflow, List<StageFiring> by) in firing)
        {
            result[workflow] = by;
        }
        return result;
    }

    /// <summary>
    /// What drawing the maps shares: the stages by id, each workflow's diagram drawn once however many maps embed it,
    /// and the text each stage's box carries.
    /// </summary>
    private sealed class Drawing(RunState state, IReadOnlyList<CaseStage> stages, IReadOnlyList<WorkflowIr> documents)
    {
        private readonly Dictionary<Guid, CaseStage> _stages = stages.ToDictionary(stage => stage.Id);
        private readonly Dictionary<Guid, WorkflowIr> _workflows = documents.ToDictionary(document => document.Identity.WorkflowId);
        private readonly Dictionary<Guid, string> _names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        private readonly Dictionary<Guid, XDocument> _drawn = [];

        // A map is delivered, so it may point only at delivered diagrams: a draft a stage fires is embedded — its own
        // note says it is a draft — but the internal folder its file sits in is not named.
        private readonly Dictionary<Guid, string> _delivered = state.BpmnFiles
            .Where(entry => entry.Value.StartsWith(RunPaths.WorkflowDiagrams + "/", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        private readonly Dictionary<Guid, string> _labels = [];

        public int Written { get; private set; }

        public int Lost { get; set; }

        /// <summary>Distinct workflows embedded, counted once per map.</summary>
        public int Embedded { get; private set; }

        public int Boxes { get; private set; }

        public int Missing { get; private set; }

        public IReadOnlyDictionary<Guid, string> Labels
        {
            get { return _labels; }
        }

        public async Task DrawAsync(RunFolder folder, CaseStage primary, string file, CancellationToken token)
        {
            StageMapResult map = StageMap.Build(primary, _stages, Diagram, _delivered, state.ToolVersion);
            await folder.WriteBytesAsync(file, BpmnSerializer.ToBytes(map.Xml), token);
            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(map.Xml);
            if (errors.Count > 0)
            {
                state.Fail(ExitCode.RunFailed, $"{file} ('{primary.Name}') BPMN 2.0 şemasına uymuyor: {string.Join(" | ", errors.Take(3))}");
            }
            foreach (MapStage stage in map.Stages)
            {
                _labels.TryAdd(stage.Id, stage.Label);
            }
            Written++;
            Embedded += map.Workflows;
            Boxes += map.Boxes;
            Missing += map.MissingWorkflows;
        }

        private XDocument? Diagram(Guid id)
        {
            if (_drawn.TryGetValue(id, out XDocument? cached))
            {
                return cached;
            }
            if (!_workflows.TryGetValue(id, out WorkflowIr? document))
            {
                return null;
            }
            XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(document, _names), state.ToolVersion);
            _drawn[id] = xml;
            return xml;
        }
    }
}
