using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// M4: one BPMN file per IR document, named after its workflow, each validated against the OMG schemas. An invalid
/// file fails the run (§6.2). <c>bpmn/index.csv</c> maps file to workflow, since the file name is a readable name
/// and the workflow id is what the other reports carry.
/// </summary>
public static class BpmnStage
{
    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, ILogger logger, CancellationToken token)
    {
        int invalid = 0;
        IReadOnlyDictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        IReadOnlyDictionary<Guid, string> stems = BpmnFileNames.Assign(documents.Select(document => (document.Identity.WorkflowId, document.Identity.Name)));
        // 1437 files in one folder is a wall. Category, then entity, is how an analyst divides the work.
        Dictionary<Guid, string> fileNames = documents.ToDictionary(
            document => document.Identity.WorkflowId,
            document => $"{BpmnFileNames.Slug(document.Identity.Category)}/{BpmnFileNames.Slug(document.Identity.PrimaryEntity ?? "no entity")}/{stems[document.Identity.WorkflowId]}");
        ExcelCsv index = new("bpmn_file", "workflow_name", "workflow_id", "category", "mode", "state", "primary_entity");
        foreach (WorkflowIr document in documents)
        {
            XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(document, names), state.ToolVersion);
            string file = $"bpmn/{fileNames[document.Identity.WorkflowId]}.bpmn";
            WorkflowIdentity identity = document.Identity;
            index.Row(fileNames[identity.WorkflowId] + ".bpmn", identity.Name, identity.WorkflowId, identity.Category, identity.Mode, identity.State, identity.PrimaryEntity);
            await folder.WriteBytesAsync(file, BpmnSerializer.ToBytes(xml), token);

            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);
            if (errors.Count > 0)
            {
                invalid++;
                state.Fail(ExitCode.RunFailed, $"{file} ('{document.Identity.Name}') fails BPMN 2.0 schema validation: {string.Join(" | ", errors.Take(3))}");
            }
        }
        await folder.WriteBytesAsync("bpmn/index.csv", index.ToBytes(), token);
        state.BpmnFiles = fileNames;
        state.Counts["bpmn.written"] = documents.Count;
        state.Counts["bpmn.invalid"] = invalid;
        state.StagesRun.Add("bpmn");
        logger.LogInformation("BPMN: {Written} written, {Invalid} invalid", documents.Count, invalid);
    }
}
