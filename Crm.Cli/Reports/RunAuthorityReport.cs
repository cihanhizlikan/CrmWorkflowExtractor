using Crm.Extract.Security;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>
/// Who may start a process by hand, and whose identity it runs under.
///
/// <para>
/// <b>CRM grants this estate-wide, not per workflow.</b> There is no record saying "role X may run workflow Y".
/// Starting one by hand needs <c>prvExecuteWorkflowJob</c>, which a security role either carries or does not, plus
/// the right to read the process record. So the answer comes in two halves and the workbook keeps them apart: the
/// roles that carry the right, and — per workflow — whether a human can start it at all and whose privileges its
/// steps then use. Joining the two into one "these people can run this workflow" column would read as a grant the
/// system does not make.
/// </para>
/// </summary>
public static class RunAuthorityReport
{
    /// <summary>CRM's privilege depth mask. A role holds one depth per privilege; the labels are the ones CRM shows.</summary>
    public static string Depth(int mask)
    {
        return mask switch
        {
            0 => "yok",
            1 => "Kullanıcı",
            2 => "İş birimi",
            4 => "İş birimi ve altı",
            8 => "Kurum",
            _ => "Kullanıcı"
        };
    }

    /// <summary>
    /// The roles that carry the right to start a process by hand, busiest first.
    ///
    /// <para>
    /// A page with no rows tells a reader nothing and looks like a fault in the file rather than in the run, so
    /// where there are no roles the page says WHY on itself. The reason is also on the guide page, but a reader
    /// who opened this tab is looking here, and sending them to another tab to find out that this one is empty
    /// on purpose is the kind of thing that gets a whole workbook mistrusted.
    /// </para>
    /// </summary>
    public static Sheet Roles(RunAuthority authority)
    {
        Sheet sheet = new(SheetNames.RunRoles, "rol", "kullanici_sayisi", "ekip_sayisi", "ekipler",
            "calistirma_derinligi", "surec_gorme_derinligi", "rolun_is_birimi", "rol_id");
        if (authority.Roles.Count == 0)
        {
            sheet.Row(authority.Note ?? "Elle çalıştırma yetkisini taşıyan rol bulunamadı.", 0, 0, "", "", "", "", "");
            return sheet;
        }
        foreach (RunRole role in authority.Roles)
        {
            sheet.Row(role.Name, role.Users, role.Teams.Count, Sheet.List(role.Teams),
                Depth(role.RunDepthMask), Depth(role.ProcessDepthMask), role.BusinessUnit, role.RoleId);
        }
        return sheet;
    }

    /// <summary>
    /// One row per workflow in the plan: the authority facts that really are per workflow. A workflow nobody can
    /// start by hand is not an omission — it is the answer, and the column says so in words rather than leaving a
    /// blank the reader has to interpret.
    /// </summary>
    public static Sheet Authority(IEnumerable<WorkflowIr> inScope, RunAuthority authority, IReadOnlyDictionary<Guid, string> bpmnFiles)
    {
        IReadOnlySet<Guid> teams = Teams(authority);
        Sheet sheet = new(SheetNames.RunAuthority, "is_akisi", "kategori", "birincil_varlik", "elle_baslatilabilir",
            "calisma_kimligi", "sahip", "sahip_turu", "kaydin_is_birimi", "bpmn_dosyasi", "is_akisi_id");
        foreach (WorkflowIr document in inScope.OrderBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            WorkflowIdentity identity = document.Identity;
            sheet.Row(identity.Name, identity.Category, identity.PrimaryEntity,
                identity.IsOnDemand == true ? "evet" : "hayır — yalnızca CRM tetikler",
                document.Trigger.RunAs, identity.OwnerName, OwnerKind(identity, teams),
                identity.OwningBusinessUnit,
                bpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "",
                identity.WorkflowId);
        }
        return sheet;
    }

    /// <summary>
    /// A user or a team. CRM's owner lookup is polymorphic and the export carries only the formatted name, so the
    /// kind is settled by asking whether that owner is one of the teams read from the server — evidence, not a
    /// guess from the name.
    /// </summary>
    private static string OwnerKind(WorkflowIdentity identity, IReadOnlySet<Guid> teams)
    {
        if (identity.Owner is not Guid owner)
        {
            return "";
        }
        return teams.Contains(owner) ? "ekip" : "kullanıcı";
    }

    private static IReadOnlySet<Guid> Teams(RunAuthority authority)
    {
        return authority.Teams.Select(team => team.TeamId).ToHashSet();
    }
}
