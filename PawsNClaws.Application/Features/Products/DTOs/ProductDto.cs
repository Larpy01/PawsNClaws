namespace PawsNClaws.Application.Features.Products.DTOs;

public sealed record ProductDto(
    Guid Id,
    string SKU,
    string Name,
    decimal Price,
    bool IsActive,
    DateTime CreatedAtUtc);
