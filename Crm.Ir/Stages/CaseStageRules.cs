namespace Crm.Ir.Stages;

/// <summary>
/// The rules on a stage besides where it goes next: SLA, assignment, documents, SMS and closing behaviour. Each is
/// something a new product has to reproduce for the process to behave the same, which is why the chief analyst
/// asked for all of it (2026-10-09).
///
/// <para>
/// One table, read by the stage sheet for its columns and by the stage map for each stage's documentation, so the
/// two can never disagree about what a stage does. The logical names are this organisation's own, read off its
/// metadata. The user a stage assigns to is deliberately NOT here: the sheet is handed to an outsource partner, and
/// a person's name is not a rule — whether a stage assigns to a named person at all is shown instead.
/// </para>
/// </summary>
public static class CaseStageRules
{
    /// <summary>The field that names the person a stage assigns to. Reported as present or absent, never by name.</summary>
    public const string AssignedUser = "ps_systemuserid";

    public static readonly IReadOnlyList<CaseStageRule> All =
    [
        new("sms_gonderilsin", "SMS gönderilsin mi", "ps_sendsms"),
        new("kuyruk", "Kuyruk", "ps_queueid"),
        new("takim", "Takım", "ps_teamid"),
        new("sla_periyodu", "SLA periyodu", "ps_slaperiod"),
        new("tpg_sla_periyodu", "TPG SLA periyodu", "ps_tpgslaperiod"),
        new("calisilan_gun", "Çalışılan gün mü", "ps_isworkingdays"),
        new("sla_hesaplama_turu", "SLA hesaplama türü", "ps_slacalculationtype"),
        new("sla_is_birimi", "SLA iş birimi", "ps_slabusinessunitid"),
        new("asama_sla", "Aşama SLA", "ps_stepslaid"),
        new("sla_icin_son_asama", "SLA için son aşama mı", "ps_islaststepofsla"),
        new("sla_asiminda_kapat", "SLA aşıldığında kapatılacak mı", "ps_autoforwardonnoncompliance"),
        new("sla_asiminda_statu", "SLA aşımında kapatılacak statü", "ps_autoforwardonnoncomliancestatus"),
        new("sla_asiminda_mail", "SLA aşımında yöneticiye mail", "ps_sentinfomailonnoncompliancecode"),
        new("gecis_dokuman_tipi", "Aşamaya geçiş doküman tipi", "ps_documentrequiredtypecode"),
        new("dokuman_yuklenebilir", "Doküman yüklenebilir mi", "ps_isdocumentuploadable"),
        new("dokuman_gelince_kapat", "Doküman geldiğinde kapatılacak mı", "ps_autocloseondocreceived"),
        new("dokuman_gelince_statu", "Doküman geldiğinde kapatılacak statü", "ps_autocloseondocreceivedstatus"),
        new("otomatik_cozum_suresi", "Otomatik çözüm süresi aşaması", "ps_autoresolutiondurationstep"),
        new("otomatik_cozum_statu", "Otomatik çözüm süresi statüsü", "ps_autoresolutiondurationstepstatus"),
        new("geri_donus", "Geri dönüş mü", "ps_iscallback"),
        new("ana_talep_yazilabilir", "Ana servis talebi yazılabilir", "ps_setparentcase"),
        new("bu_asama_kapanis", "Bu aşama kapanışı", "ps_thisstepclose"),
        new("atlanarak_gecilebilir", "Atlanarak geçilebilir mi", "ps_isrunskiptostep"),
        new("servis_talebi_limiti", "Servis talebi limiti", "ps_caselimit"),
        new("kampanya_grubu", "Kampanya grubu", "ps_campaigngroup"),
        new("step_code", "Step code", "ps_stepcode"),
        new("tanim", "Tanım", "ps_description"),
        new("mobil_sube_aciklama", "Mobil şube açıklaması", "ps_mobilebranchdescription"),
        new("mobil_sube_detay", "Mobil şube detaylı açıklaması", "ps_mobilebranchdetaildescription")
    ];
}

/// <summary>One rule: the sheet's column, the words a reader knows it by, and the field it is read from.</summary>
public sealed record CaseStageRule(string Column, string Label, string Field);
