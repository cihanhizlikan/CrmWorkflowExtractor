namespace Crm.Ir.Model;

/// <summary>
/// CRM's condition operators in the words a reader uses. The definitions carry the .NET enum name —
/// <c>NotNull</c>, <c>In</c>, <c>OlderThanXMonths</c> — and a diagram showing <c>lead.leadid NotNull</c> left a
/// reader asking whether that was a null check at all.
///
/// <para>
/// This is the fourth table carrying the output's Turkish, beside <c>RunPaths</c>, <c>RunStages</c> and
/// <c>ProcessLabels</c>, and it sits here because the predicate's text is composed here and read from the diagram
/// and the sheets alike. The operator's own NAME is kept untranslated on <see cref="Predicate.Operator"/>, which
/// is a comparison key and must not move when the wording does.
/// </para>
///
/// <para>
/// An operator with no match keeps CRM's name. That is the honest fallback for a list CRM can extend: a word
/// nobody translated reads as a word nobody translated, where a guessed one would read as fact.
/// </para>
/// </summary>
public static class ConditionWords
{
    private static readonly IReadOnlyDictionary<string, string> Turkish = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Equal"] = "=",
        ["NotEqual"] = "≠",
        ["GreaterThan"] = ">",
        ["GreaterEqual"] = "≥",
        ["LessThan"] = "<",
        ["LessEqual"] = "≤",
        ["Null"] = "boş",
        ["NotNull"] = "dolu",
        ["In"] = "şunlardan biri:",
        ["NotIn"] = "şunlardan hiçbiri:",
        ["Between"] = "şu aralıkta:",
        ["NotBetween"] = "şu aralıkta değil:",
        ["Like"] = "şuna benziyor:",
        ["NotLike"] = "şuna benzemiyor:",
        ["Contains"] = "içeriyor:",
        ["DoesNotContain"] = "içermiyor:",
        ["ContainValues"] = "şu değerleri içeriyor:",
        ["DoesNotContainValues"] = "şu değerleri içermiyor:",
        ["BeginsWith"] = "ile başlıyor:",
        ["DoesNotBeginWith"] = "ile başlamıyor:",
        ["EndsWith"] = "ile bitiyor:",
        ["DoesNotEndWith"] = "ile bitmiyor:",
        ["On"] = "şu tarihte:",
        ["OnOrBefore"] = "şu tarihte ya da öncesinde:",
        ["OnOrAfter"] = "şu tarihte ya da sonrasında:",
        ["Today"] = "bugün",
        ["Yesterday"] = "dün",
        ["Tomorrow"] = "yarın",
        ["Last7Days"] = "son 7 gün içinde",
        ["Next7Days"] = "önümüzdeki 7 gün içinde",
        ["LastWeek"] = "geçen hafta",
        ["ThisWeek"] = "bu hafta",
        ["NextWeek"] = "önümüzdeki hafta",
        ["LastMonth"] = "geçen ay",
        ["ThisMonth"] = "bu ay",
        ["NextMonth"] = "önümüzdeki ay",
        ["LastYear"] = "geçen yıl",
        ["ThisYear"] = "bu yıl",
        ["NextYear"] = "önümüzdeki yıl",
        ["LastXDays"] = "son şu kadar gün içinde:",
        ["NextXDays"] = "önümüzdeki şu kadar gün içinde:",
        ["LastXMonths"] = "son şu kadar ay içinde:",
        ["NextXMonths"] = "önümüzdeki şu kadar ay içinde:",
        ["LastXYears"] = "son şu kadar yıl içinde:",
        ["NextXYears"] = "önümüzdeki şu kadar yıl içinde:",
        ["LastXHours"] = "son şu kadar saat içinde:",
        ["NextXHours"] = "önümüzdeki şu kadar saat içinde:",
        ["OlderThanXDays"] = "şu kadar günden eski:",
        ["OlderThanXMonths"] = "şu kadar aydan eski:",
        ["OlderThanXYears"] = "şu kadar yıldan eski:",
        ["EqualUserId"] = "= çalıştıran kullanıcı",
        ["NotEqualUserId"] = "≠ çalıştıran kullanıcı",
        ["EqualBusinessId"] = "= kullanıcının iş birimi",
        ["NotEqualBusinessId"] = "≠ kullanıcının iş birimi",
        ["EqualUserTeams"] = "= kullanıcının ekipleri",
        ["Under"] = "şunun altında:",
        ["UnderOrEqual"] = "şu ya da altında:",
        ["NotUnder"] = "şunun altında değil:",
        ["Above"] = "şunun üstünde:",
        ["AboveOrEqual"] = "şu ya da üstünde:"
    };

    /// <summary>The operator in Turkish, or CRM's own name where there is no translation for it.</summary>
    public static string Of(string? crmOperator)
    {
        if (crmOperator is null || crmOperator.Length == 0)
        {
            return "";
        }
        return Turkish.TryGetValue(crmOperator, out string? word) ? word : crmOperator;
    }

    /// <summary>How two conditions are joined. CRM writes <c>And</c> and <c>Or</c>.</summary>
    public static string Join(string? logical)
    {
        return string.Equals(logical, "Or", StringComparison.OrdinalIgnoreCase) ? "VEYA" : "VE";
    }
}
