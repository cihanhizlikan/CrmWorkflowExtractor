using System.Globalization;
using System.Text;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;

namespace Crm.Cli.Reports;

/// <summary>Human-readable M1 output: the run summary printed to the console and <c>reports/inventory.md</c>.</summary>
public static class InventoryReport
{
    public static string Summary(RunState state)
    {
        StringBuilder text = new();
        Line(text, "Çalıştırma", $"{state.RunId}   ({state.RunRoot})");
        Line(text, "Araç sürümü", state.ToolVersion);
        Line(text, "Kuruluş", state.OrganizationUrl ?? "(erişilmedi)");
        Line(text, "Kimlik", state.Identity is null
            ? "(bilinmiyor — WhoAmI tamamlanmadı)"
            : $"{state.Identity.DomainName ?? "?"} ({state.Identity.FullName ?? "?"}) {state.Identity.UserId:D}");

        if (state.Privileges.Count > 0)
        {
            text.AppendLine("Yetkiler (§2.4 kuruluş düzeyinde okuma gerekir):");
            foreach (PrivilegeFinding finding in state.Privileges)
            {
                text.Append(CultureInfo.InvariantCulture, $"    {finding.Privilege.Name,-34} {finding.Privilege.Table,-28} {finding.Depth,-8} {Verdict(finding.Verdict)}").AppendLine();
            }
        }

        if (state.Reconciliation is ReconciliationResult result)
        {
            InventoryCounts counts = result.Counts;
            Line(text, "Sayım", string.Create(CultureInfo.InvariantCulture,
                $"$count {(counts.ApiCount < 0 ? "alınamadı" : counts.ApiCount.ToString(CultureInfo.InvariantCulture))} -> alınan {counts.Retrieved} (tanım {counts.Definitions} · etkinleştirme {counts.Activations} · şablon {counts.Templates} · diğer {counts.OtherType})"));
            Line(text, "Tasarımcı dışı", string.Create(CultureInfo.InvariantCulture,
                $"{counts.DefinitionsNotDesignerAuthored} tanım → {Crm.Extract.Runs.RunPaths.ManualReview}/, ayrıştırılmadı"));
            Line(text, "Farklı sahip", counts.DistinctOwners.ToString(CultureInfo.InvariantCulture));
        }

        if (state.CountChain.Count > 0)
        {
            text.AppendLine("Sayım zinciri (§8):");
            foreach (CountLink link in state.CountChain)
            {
                text.Append("    ").AppendLine(link.ToString());
            }
        }
        if (state.Counts.ContainsKey("bpmn.written"))
        {
            Line(text, "Çıktı", string.Create(CultureInfo.InvariantCulture,
                $"{Get(state, "ir.documents")} ara model · {Get(state, "bpmn.written")} BPMN · 2+ üyeli {Get(state, "clusters.families")} aile · "
                + $"{Get(state, "consolidation.combined")} birleştirildi · okunamayan yapı içeren {Get(state, "ir.workflowsWithUnmapped")}"));
            Line(text, "Önce oku", Path.Combine(state.RunRoot, Crm.Extract.Runs.RunPaths.Report.Replace('/', Path.DirectorySeparatorChar)));
        }

        Block(text, "UYARILAR", state.Warnings);
        Block(text, "BAŞARISIZLIKLAR", state.Failures);
        Line(text, "Durum", $"{state.Status}   çıkış kodu {(int)state.ExitCode} ({state.ExitCode})");
        return text.ToString();
    }

    public static string Markdown(RunState state, IReadOnlyList<WorkflowInventoryRecord> records)
    {
        StringBuilder text = new();
        text.AppendLine("# Envanter — " + state.RunId).AppendLine();
        text.AppendLine("```").Append(Summary(state)).AppendLine("```").AppendLine();

        text.AppendLine("## Kategori, tür ve duruma göre kayıtlar").AppendLine();
        text.AppendLine("| Kategori | Tür | Durum | Kayıt |").AppendLine("|---|---|---|---:|");
        foreach (IGrouping<(string Category, string Type, string State), WorkflowInventoryRecord> group in records
            .GroupBy(record => (Category: record.Category.Label, Type: record.Type.Label, State: record.State.Label))
            .OrderBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Type, StringComparer.Ordinal)
            .ThenBy(group => group.Key.State, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Key.Category} | {group.Key.Type} | {group.Key.State} | {group.Count()} |").AppendLine();
        }
        text.AppendLine();

        text.AppendLine("## Seçenek değerleri: şartname tablosu ile sunucu etiketi").AppendLine();
        text.AppendLine("Görülen her farklı değer; §3.1 tablosundaki etiket ile sunucunun kendi döndürdüğü etiket yan yana. "
            + "Sunucu etiketinin farklı sözcüklerle yazılması olağandır; farklı bir *anlam* taşıması plana kaydedilmesi gereken bir uyuşmazlıktır.").AppendLine();
        text.AppendLine("| Sütun | Ham değer | Şartname etiketi | Sunucu etiketi | Kayıt |").AppendLine("|---|---:|---|---|---:|");
        foreach (IGrouping<(string Column, int Raw, string Tool, string Server), ObservedOption> group in records
            .SelectMany(record => record.ObservedOptions)
            .GroupBy(option => (Column: option.Column, Raw: option.Raw, Tool: option.ToolLabel, Server: option.ServerLabel ?? "(yok)"))
            .OrderBy(group => group.Key.Column, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Raw)
            .ThenBy(group => group.Key.Server, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Key.Column} | {group.Key.Raw} | {group.Key.Tool} | {group.Key.Server} | {group.Count()} |").AppendLine();
        }
        return text.ToString();
    }

    /// <summary>The privilege verdicts as the console prints them; the manifest keeps the enum name for tooling.</summary>
    private static string Verdict(PrivilegeVerdict verdict)
    {
        return verdict switch
        {
            PrivilegeVerdict.Sufficient => "Yeterli",
            PrivilegeVerdict.Insufficient => "Yetersiz",
            PrivilegeVerdict.Missing => "Yok",
            _ => "Doğrulanamadı"
        };
    }

    private static void Line(StringBuilder text, string label, string value)
    {
        text.Append(CultureInfo.InvariantCulture, $"{label + ":",-24}{value}").AppendLine();
    }

    private static void Block(StringBuilder text, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }
        const int shown = 25;
        text.Append(CultureInfo.InvariantCulture, $"{title} ({lines.Count}):").AppendLine();
        foreach (string line in lines.Take(shown))
        {
            text.Append("  ! ").AppendLine(line);
        }
        if (lines.Count > shown)
        {
            text.Append(CultureInfo.InvariantCulture, $"  … {lines.Count - shown} kayıt daha: {Crm.Extract.Runs.RunPaths.WarningLog} ve {Crm.Extract.Runs.RunPaths.Report}").AppendLine();
        }
    }

    private static int Get(RunState state, string key)
    {
        return state.Counts.TryGetValue(key, out int value) ? value : 0;
    }
}
