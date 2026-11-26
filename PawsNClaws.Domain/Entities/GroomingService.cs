namespace PawsNClaws.Domain.Entities;

public sealed class GroomingService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Species { get; set; }
    public string? Breed { get; set; }
    public string? Size { get; set; }
    public bool IsActive { get; set; }
}
