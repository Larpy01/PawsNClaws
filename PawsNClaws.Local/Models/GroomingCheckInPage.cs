using PawsNClaws.Application.Features.Grooming;

namespace PawsNClaws.Local.Models;

public sealed record GroomingCheckInPage(GroomingVisitDetails Visit, GroomingCheckInForm Form,
    IReadOnlyList<PlanningOption> Products, IReadOnlyCollection<Guid> SelectedProductIds);
