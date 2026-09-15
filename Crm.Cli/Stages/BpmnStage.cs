using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>M4: one <c>bpmn/&lt;workflowid&gt;.bpmn</c> per IR document, each validated against the OMG schemas. An invalid file fails the run (§6.2).</summary>
public static class BpmnStage
{
    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, ILogger logger, CancellationToken token)
    {
        int invalid = 0;
        foreach (WorkflowIr document in documents)
        {
            XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(document), state.ToolVersion);
            string file = $"bpmn/{document.Identity.WorkflowId:D}.bpmn";
            await folder.WriteBytesAsync(file, BpmnSerializer.ToBytes(xml), token);

            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);
            if (errors.Count > 0)
            {
                invalid++;
                state.Fail(ExitCode.RunFailed, $"{file} ('{document.Identity.Name}') fails BPMN 2.0 schema validation: {string.Join(" | ", errors.Take(3))}");
            }
        }
        state.Counts["bpmn.written"] = documents.Count;
        state.Counts["bpmn.invalid"] = invalid;
        state.StagesRun.Add("bpmn");
        logger.LogInformation("BPMN: {Written} written, {Invalid} invalid", documents.Count, invalid);
    }
}
