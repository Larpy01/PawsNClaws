namespace PawsNClaws.Domain.Entities;

public sealed class GroomingPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public List<GroomingPackageService> Services { get; set; } = [];
}

public sealed class GroomingPackageService
{
    public Guid PackageId { get; set; }
    public Guid ServiceId { get; set; }
    public GroomingService Service { get; set; } = null!;
}

public sealed class GroomingPriceRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid? ServiceId { get; set; }
    public Guid? PackageId { get; set; }
    public string? Species { get; set; }
    public string? Breed { get; set; }
    public string? Size { get; set; }
    public string? Condition { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
}

public sealed class GroomingRecipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid ServiceId { get; set; }
    public Guid ProductId { get; set; }
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public bool IsActive { get; set; }
}

public sealed class GroomingQuote
{
    public Guid Id { get; set; }
    public Guid PetId { get; set; }
    public int LatestRevision { get; set; }
}

public sealed class GroomingQuoteRevision
{
    public Guid QuoteId { get; set; }
    public int Number { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Snapshot { get; set; } = "";
}
