using Crm.Ir.Parsing;

namespace Crm.Ir.Reports;

/// <summary>
/// §4.4: what the parser made of one workflow — every construct it met, marked mapped, support or unmapped. The
/// run turns these into the <c>Okunamayan yapılar</c> and <c>Yapı sıklığı</c> sheets; nothing is dropped silently,
/// so the reader can see exactly how much of a diagram to trust.
/// </summary>
public sealed record WorkflowCoverage(Guid WorkflowId, string Name, IReadOnlyList<CoverageObservation> Observations);
