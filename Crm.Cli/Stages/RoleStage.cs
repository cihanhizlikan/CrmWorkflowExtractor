using System.Text.Json;
using Crm.Extract.Runs;
using Crm.Extract.Security;

namespace Crm.Cli.Stages;

/// <summary>
/// Who may run a process, as the offline stages see it: read back from the run folder, never from the network, so a
/// reprocessed run has it too — <c>ham/</c> travels forward with the evidence.
/// </summary>
public static class RoleStage
{
    public static RunAuthority Load(RunFolder folder, RunState state)
    {
        if (!folder.Exists(RunAuthorityReader.IndexFile))
        {
            // A run made before this existed, or one whose export predates it. Saying so beats an empty page.
            return RunAuthority.Empty with { Note = "Bu çalıştırmada güvenlik rolleri okunmadı." };
        }
        try
        {
            return JsonSerializer.Deserialize<RunAuthority>(folder.ReadText(RunAuthorityReader.IndexFile), RunFolder.JsonOptions)
                ?? RunAuthority.Empty;
        }
        catch (JsonException error)
        {
            state.Warnings.Add($"{RunAuthorityReader.IndexFile} okunamadı; çalıştırma yetkisi bu raporda görünmeyecek: {error.Message}");
            return RunAuthority.Empty with { Note = error.Message };
        }
    }
}
