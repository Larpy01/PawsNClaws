namespace PawsNClaws.Domain.Entities;

public sealed class PetRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = "";
    public string Species { get; set; } = "";
    public string? Breed { get; set; }
    public string? Sex { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Size { get; set; }
    public decimal? WeightKg { get; set; }
    public string? VaccinationStatus { get; set; }
    public string? Allergies { get; set; }
    public string? MedicalNotes { get; set; }
    public string? BehaviorNotes { get; set; }
    public string? SpecialInstructions { get; set; }
}
