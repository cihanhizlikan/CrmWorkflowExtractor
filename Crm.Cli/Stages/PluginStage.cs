using System.Text.Json;
using Crm.Extract.Metadata;
using Crm.Extract.Runs;

namespace Crm.Cli.Stages;

/// <summary>
/// The plug-in registry as the offline stages see it: read back from the run folder, never from the network, so a
/// reprocessed run has it too — <c>ham/</c> travels forward with the evidence.
/// </summary>
public static class PluginStage
{
    public static PluginRegistry Load(RunFolder folder, RunState state)
    {
        if (!folder.Exists(PluginRegistryRetriever.IndexFile))
        {
            return PluginRegistry.Empty;
        }
        try
        {
            // The run folder holds the registry as this tool models it, not as CRM sent it: the OData shape is
            // only what an export speaks, and PluginRegistryRetriever.Parse is for that side of the fence.
            return JsonSerializer.Deserialize<PluginRegistry>(folder.ReadText(PluginRegistryRetriever.IndexFile), RunFolder.JsonOptions)
                ?? PluginRegistry.Empty;
        }
        catch (JsonException error)
        {
            state.Warnings.Add($"{PluginRegistryRetriever.IndexFile} okunamadı; derlemelerdeki adresler bu raporda görünmeyecek: {error.Message}");
            return PluginRegistry.Empty;
        }
    }
}
