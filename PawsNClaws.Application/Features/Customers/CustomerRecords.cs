using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Application.Features.Customers;

public sealed class CustomerForm
{
    public Guid Version { get; set; }
    [Display(Name = "First name")]
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = "";
    [Display(Name = "Last name")]
    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = "";
    [Display(Name = "Contact number")]
    [StringLength(40)]
    public string? Phone { get; set; }
    [StringLength(256)]
    [EmailAddress]
    public string? Email { get; set; }
    [StringLength(400)]
    public string? Address { get; set; }
    [Display(Name = "Owner consent for storing this record")]
    public bool RecordConsent { get; set; }
}

public sealed class PetForm : IValidatableObject
{
    public Guid Version { get; set; }
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = "";
    [Required]
    [StringLength(80)]
    public string Species { get; set; } = "";
    [StringLength(100)]
    public string? Breed { get; set; }
    [StringLength(30)]
    public string? Sex { get; set; }
    [Display(Name = "Date of birth")]
    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }
    [StringLength(60)]
    public string? Size { get; set; }
    [Display(Name = "Weight (kg)")]
    [Range(typeof(decimal), "0.001", "99999.999")]
    public decimal? WeightKg { get; set; }
    [Display(Name = "Vaccination status")]
    [StringLength(100)]
    public string? VaccinationStatus { get; set; }
    [StringLength(500)]
    public string? Allergies { get; set; }
    [Display(Name = "Medical notes")]
    [StringLength(1000)]
    public string? MedicalNotes { get; set; }
    [Display(Name = "Behavioral notes")]
    [StringLength(1000)]
    public string? BehaviorNotes { get; set; }
    [Display(Name = "Special instructions")]
    [StringLength(1000)]
    public string? SpecialInstructions { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (DateOfBirth > DateOnly.FromDateTime(DateTime.Today))
            yield return new ValidationResult("Birth date cannot be in the future.", [nameof(DateOfBirth)]);
        if (WeightKg.HasValue && decimal.Round(WeightKg.Value, 3) != WeightKg.Value)
            yield return new ValidationResult("Enter weight to at most three decimal places.", [nameof(WeightKg)]);
    }
}

public sealed record CustomerRow(Guid Id, string FirstName, string LastName, string? Phone, string? Email);
public sealed record PetRow(Guid Id, PetForm Details);
public sealed record CustomerProfile(Guid Id, CustomerForm Details, IReadOnlyList<PetRow> Pets);

public interface ICustomerRecords
{
    Task<IReadOnlyList<CustomerRow>> SearchAsync(string? query, int page, CancellationToken token);
    Task<CustomerProfile?> FindAsync(Guid id, CancellationToken token);
    Task<Guid?> SaveCustomerAsync(Guid? id, CustomerForm form, Guid actor, CancellationToken token);
    Task<Guid?> SavePetAsync(Guid ownerId, Guid? id, PetForm form, Guid actor, CancellationToken token);
}

public sealed class ProfileConflictException : Exception { }
