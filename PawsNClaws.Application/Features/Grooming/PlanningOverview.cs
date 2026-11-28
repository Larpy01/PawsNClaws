namespace PawsNClaws.Application.Features.Grooming;

public sealed record PlanningOverview(
    IReadOnlyList<PlanningRow<GroomingPackageForm>> Packages,
    IReadOnlyList<PlanningRow<GroomingPriceForm>> Prices,
    IReadOnlyList<PlanningRow<GroomingRecipeForm>> Recipes,
    IReadOnlyList<QuoteVersion> Quotes);
