using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Application.Features.Grooming;

public sealed class GroomingServiceForm : IValidatableObject
{
    public Guid Version { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    [Display(Name = "Duration in minutes")]
    [Range(1, int.MaxValue)]
    public int? DurationMinutes { get; set; }

    [StringLength(80)]
    public string? Species { get; set; }

    [StringLength(100)]
    public string? Breed { get; set; }

    [StringLength(60)]
    public string? Size { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Breed) && string.IsNullOrWhiteSpace(Species))
            yield return new ValidationResult("Specify the species when restricting a service to a breed.", [nameof(Species)]);
        if (IsActive && DurationMinutes is null)
            yield return new ValidationResult("Enter a duration before activating the service.", [nameof(DurationMinutes)]);
    }
}

public sealed record GroomingCatalogEntry(Guid Id, GroomingServiceForm Service);

public enum CatalogSaveResult { Saved, NotFound, Conflict }

public interface IGroomingCatalog
{
    Task<IReadOnlyList<GroomingCatalogEntry>> ListAsync(CancellationToken token);
    Task<GroomingServiceForm?> FindAsync(Guid id, CancellationToken token);
    Task<CatalogSaveResult> SaveAsync(Guid? id, GroomingServiceForm form, CancellationToken token);
}
