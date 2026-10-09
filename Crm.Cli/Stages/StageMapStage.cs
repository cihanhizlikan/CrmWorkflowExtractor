using System.Text.Json;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Bpmn.Stages;
using Crm.Extract.Metadata;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Ir.Stages;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// One diagram per ACTIVE PRIMARY stage: the process a case of that subcategory goes through, every stage reachable
/// from where it starts, and every workflow fired on the way, embedded whole.
///
/// <para>
/// This is the diagram the analysts asked for (chief analyst, 2026-10-09). The workflow diagrams say what a
/// workflow does; only this says WHEN it runs — on which outcome of which stage of which request — and that was
/// never in any workflow. It runs after the workflow diagrams because it embeds them.
/// </para>
/// </summary>
public static class StageMapStage
{
    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, ILogger logger, CancellationToken token)
    {
        string source = folder.PathOf(CaseStageRetriever.IndexFile);
        if (!File.Exists(source))
        {
            return;
        }
        using JsonDocument rows = JsonDocument.Parse(await File.ReadAllTextAsync(source, token));
        IReadOnlyList<CaseStage> stages = CaseStageReader.Read(rows.RootElement.EnumerateArray());
        state.CaseStages = stages;
        state.Counts["caseStages.active"] = stages.Count(stage => stage.Active);

        Drawing drawing = new(state, stages, documents);
        List<CaseStage> primaries = [.. stages.Where(stage => stage.Primary && stage.Active)];
        IReadOnlyDictionary<Guid, string> stems = BpmnFileNames.Assign(primaries.Select(stage => (stage.Id, stage.Process.Length > 0 ? stage.Process : stage.Name)));
        foreach (CaseStage primary in primaries)
        {
            string file = $"{RunPaths.StageMaps}/{stems[primary.Id]}.bpmn";
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
        state.StageMapFiles = drawing.AppearsOn();
        state.Counts["stageMaps.written"] = drawing.Written;
        state.Counts["stageMaps.lost"] = drawing.Lost;
        state.Counts["stageMaps.workflowsEmbedded"] = drawing.Embedded;
        state.Counts["stageMaps.workflowsMissing"] = drawing.Missing;
        state.StagesRun.Add(RunStages.StageMaps);
        logger.LogInformation("Stage maps: {Written} written from {Primaries} active primary stages, {Embedded} workflows embedded, {Missing} missing",
            drawing.Written, primaries.Count, drawing.Embedded, drawing.Missing);
    }

    /// <summary>
    /// What drawing the maps shares: the stages by id, each workflow's diagram drawn once however many maps embed it,
    /// and which maps each stage appeared on.
    /// </summary>
    private sealed class Drawing(RunState state, IReadOnlyList<CaseStage> stages, IReadOnlyList<WorkflowIr> documents)
    {
        private readonly Dictionary<Guid, CaseStage> _stages = stages.ToDictionary(stage => stage.Id);
        private readonly Dictionary<Guid, WorkflowIr> _workflows = documents.ToDictionary(document => document.Identity.WorkflowId);
        private readonly Dictionary<Guid, string> _names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        private readonly Dictionary<Guid, XDocument> _drawn = [];
        private readonly Dictionary<Guid, List<string>> _appearsOn = [];

        public int Written { get; private set; }

        public int Lost { get; set; }

        public int Embedded { get; private set; }

        public int Missing { get; private set; }

        public async Task DrawAsync(RunFolder folder, CaseStage primary, string file, CancellationToken token)
        {
            StageMapResult map = StageMap.Build(primary, _stages, Diagram, state.BpmnFiles, state.ToolVersion);
            await folder.WriteBytesAsync(file, BpmnSerializer.ToBytes(map.Xml), token);
            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(map.Xml);
            if (errors.Count > 0)
            {
                state.Fail(ExitCode.RunFailed, $"{file} ('{primary.Name}') BPMN 2.0 şemasına uymuyor: {string.Join(" | ", errors.Take(3))}");
            }
            foreach (Guid stage in map.Stages)
            {
                if (!_appearsOn.TryGetValue(stage, out List<string>? files))
                {
                    files = [];
                    _appearsOn[stage] = files;
                }
                files.Add(file);
            }
            Written++;
            Embedded += map.Workflows;
            Missing += map.MissingWorkflows;
        }

        public Dictionary<Guid, IReadOnlyList<string>> AppearsOn()
        {
            Dictionary<Guid, IReadOnlyList<string>> appearsOn = [];
            foreach ((Guid stage, List<string> files) in _appearsOn)
            {
                appearsOn[stage] = files;
            }
            return appearsOn;
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
