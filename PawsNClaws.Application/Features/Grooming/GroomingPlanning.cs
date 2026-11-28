using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Application.Features.Grooming;

public sealed class GroomingPackageForm
{
    public Guid Version { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; }
    [Display(Name = "Included services")] public List<Guid> ServiceIds { get; set; } = [];
}

public sealed class GroomingPriceForm
{
    public Guid Version { get; set; }
    [Display(Name = "Service or package")] public Guid TargetId { get; set; }
    [StringLength(80)] public string? Species { get; set; }
    [StringLength(100)] public string? Breed { get; set; }
    [StringLength(60)] public string? Size { get; set; }
    [Display(Name = "Coat condition"), StringLength(100)] public string? Condition { get; set; }
    [Display(Name = "Price in pesos"), Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
}

public sealed class GroomingRecipeForm
{
    public Guid Version { get; set; }
    [Display(Name = "Service")] public Guid ServiceId { get; set; }
    [Display(Name = "Supply")] public Guid ProductId { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999999.999")] public decimal Quantity { get; set; }
    public bool IsActive { get; set; }
}

public sealed class GroomingQuoteForm
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int Revision { get; set; }
    [Display(Name = "Customer and pet"), Required(ErrorMessage = "Choose a customer and pet.")]
    public Guid? PetId { get; set; }
    [Display(Name = "Service or package"), Required(ErrorMessage = "Choose a service or package.")]
    public Guid? TargetId { get; set; }
    [Display(Name = "Optional add-ons")] public List<Guid>? AddOnIds { get; set; }
    [Display(Name = "Coat condition"), StringLength(100)] public string? Condition { get; set; }
    [Display(Name = "Custom price estimate total (₱)"), Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal? OverrideTotal { get; set; }
    [Display(Name = "Reason for changing the total"), StringLength(500)] public string? OverrideReason { get; set; }
}

public sealed record PlanningOption(Guid Id, string Label, bool Available);
public sealed record PlanningRow<T>(Guid Id, T Form);
public sealed record QuoteLine(Guid TargetId, string Name, IReadOnlyList<string> Services, decimal Amount);
public sealed record QuoteSupply(Guid ProductId, string Name, decimal Quantity, string Unit);
public sealed record QuoteSnapshot(string Owner, string Pet, string Species, string? Breed, string? Size,
    string? Condition, IReadOnlyList<QuoteLine> Lines, IReadOnlyList<QuoteSupply> Supplies,
    decimal BaseTotal, decimal Total, decimal? OverrideTotal, string? OverrideReason);
public sealed record QuoteVersion(Guid Id, Guid PetId, int Number, DateTime CreatedAtUtc, QuoteSnapshot Snapshot);
public sealed record PlanningResult(Guid? Id = null, string? Error = null, bool Missing = false, bool Conflict = false);

public interface IGroomingPlanning
{
    Task<IReadOnlyList<PlanningOption>> ServicesAsync(CancellationToken token);
    Task<IReadOnlyList<PlanningOption>> TargetsAsync(CancellationToken token);
    Task<IReadOnlyList<PlanningOption>> ProductsAsync(CancellationToken token);
    Task<IReadOnlyList<PlanningOption>> PetsAsync(CancellationToken token);
    Task<IReadOnlyList<PlanningRow<GroomingPackageForm>>> PackagesAsync(CancellationToken token);
    Task<IReadOnlyList<PlanningRow<GroomingPriceForm>>> PricesAsync(CancellationToken token);
    Task<IReadOnlyList<PlanningRow<GroomingRecipeForm>>> RecipesAsync(CancellationToken token);
    Task<IReadOnlyList<QuoteVersion>> QuotesAsync(Guid? id, CancellationToken token, Guid? petId = null);
    Task<PlanningResult> SavePackageAsync(Guid? id, GroomingPackageForm form, CancellationToken token);
    Task<PlanningResult> SavePriceAsync(Guid? id, GroomingPriceForm form, CancellationToken token);
    Task<PlanningResult> SaveRecipeAsync(Guid? id, GroomingRecipeForm form, CancellationToken token);
    Task<PlanningResult> SaveQuoteAsync(Guid? id, GroomingQuoteForm form, Guid employee, CancellationToken token);
}
