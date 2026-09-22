using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Crm.Extract.Inventory;

namespace Crm.Extract.Xaml;

/// <summary>A definition whose XAML differs from the activation that is actually executing.</summary>
public sealed record DriftFinding(Guid DefinitionId, string Name, Guid ActivationId, bool StructureDiffers);

public sealed record DriftReport(
    int PairsCompared,
    IReadOnlyList<DriftFinding> Drifted,
    IReadOnlyList<WorkflowInventoryRecord> DraftDefinitions,
    IReadOnlyList<WorkflowInventoryRecord> DefinitionsWithoutActivation);

/// <summary>
/// §3.2 definition/activation drift. Two comparisons, reported separately: bytes (anything differs) and structure
/// (after removing formatting, attribute order and the per-record <c>XrmWorkflow&lt;guid&gt;</c> class names, which an
/// activation copy is expected to change). Also lists Draft definitions, because on-premises CRM only lets a workflow
/// be edited while deactivated — so "edited and never shipped" may show up there rather than as drift (plan §2.6).
/// </summary>
public static partial class DriftAnalyzer
{
    public static DriftReport Analyze(IReadOnlyList<WorkflowInventoryRecord> records, IReadOnlyList<XamlEntry> entries, Func<string, string> readFile)
    {
        Dictionary<Guid, XamlEntry> byId = entries.ToDictionary(entry => entry.WorkflowId);
        List<WorkflowInventoryRecord> definitions = [.. records.Where(record => record.Type.Raw == WorkflowOptionSets.TypeDefinition).OrderBy(record => record.WorkflowId)];
        HashSet<Guid> parentsOfActivations = [.. records
            .Where(record => record.Type.Raw == WorkflowOptionSets.TypeActivation && record.ParentWorkflowId is not null)
            .Select(record => record.ParentWorkflowId!.Value)];

        int compared = 0;
        List<DriftFinding> drifted = [];
        foreach (WorkflowInventoryRecord definition in definitions)
        {
            if (definition.ActiveWorkflowId is not Guid activationId
                || !byId.TryGetValue(definition.WorkflowId, out XamlEntry? left)
                || !byId.TryGetValue(activationId, out XamlEntry? right))
            {
                continue;
            }
            compared++;
            if (string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal))
            {
                continue;
            }
            bool structureDiffers = !string.Equals(Canonical(readFile(left.File)), Canonical(readFile(right.File)), StringComparison.Ordinal);
            drifted.Add(new DriftFinding(definition.WorkflowId, definition.Name, activationId, structureDiffers));
        }

        List<WorkflowInventoryRecord> drafts = [.. definitions.Where(record => record.State.Raw == 0)];
        List<WorkflowInventoryRecord> withoutActivation = [.. definitions.Where(record => record.ActiveWorkflowId is null && !parentsOfActivations.Contains(record.WorkflowId))];
        return new DriftReport(compared, drifted, drafts, withoutActivation);
    }

    /// <summary>XAML with formatting, attribute order and per-record class names removed. Unparseable XAML is compared as text.</summary>
    public static string Canonical(string xaml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.None);
        }
        catch (XmlException)
        {
            return ClassName().Replace(xaml, "XrmWorkflow");
        }
        foreach (XElement element in document.Descendants())
        {
            List<XAttribute> ordered = [.. element.Attributes().OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)];
            element.ReplaceAttributes(ordered);
        }
        return ClassName().Replace(document.ToString(SaveOptions.DisableFormatting), "XrmWorkflow");
    }

    public static string Markdown(DriftReport report)
    {
        StringBuilder text = new();
        text.AppendLine("# Tanım ile çalışan kopya arasındaki sapma").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Karşılaştırılan tanım–etkinleştirme çifti: **{report.PairsCompared}**. Sapan: **{report.Drifted.Count}** "
            + $"(yapısı farklı olan: {report.Drifted.Count(finding => finding.StructureDiffers)}).").AppendLine();
        text.AppendLine("*Yapı* farkı, çalışan mantığın düzenleyicideki mantık olmadığı anlamına gelir. Yalnızca bayt düzeyindeki fark "
            + "biçimlendirme veya tanımlayıcı farkıdır ve bütünlük için listelenmiştir.").AppendLine();
        text.AppendLine("| İş akışı | Tanım | Etkinleştirme | Yapı farklı |").AppendLine("|---|---|---|---|");
        foreach (DriftFinding finding in report.Drifted)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {finding.Name} | `{finding.DefinitionId:D}` | `{finding.ActivationId:D}` | {(finding.StructureDiffers ? "**evet**" : "hayır")} |");
        }
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Taslak tanımlar ({report.DraftDefinitions.Count})").AppendLine();
        text.AppendLine("Devre dışı bırakılmış ya da hiç etkinleştirilmemiş. Şirket içi CRM bir iş akışını yalnızca devre dışıyken düzenlemeye izin verdiği için, "
            + "yapılıp hiç yayına alınmamış değişiklikler genellikle burada durur.").AppendLine();
        foreach (WorkflowInventoryRecord record in report.DraftDefinitions)
        {
            string activation = record.ActiveWorkflowId is null ? "etkinleştirmesi yok" : "etkinleştirmesi var";
            text.AppendLine(CultureInfo.InvariantCulture, $"- {record.Name} (`{record.WorkflowId:D}`) — {activation}");
        }
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Hiç etkinleştirme kaydı olmayan tanımlar ({report.DefinitionsWithoutActivation.Count})").AppendLine();
        foreach (WorkflowInventoryRecord record in report.DefinitionsWithoutActivation)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {record.Name} (`{record.WorkflowId:D}`)");
        }
        return text.ToString();
    }

    [GeneratedRegex("XrmWorkflow[0-9a-fA-F]{32}")]
    private static partial Regex ClassName();
}
