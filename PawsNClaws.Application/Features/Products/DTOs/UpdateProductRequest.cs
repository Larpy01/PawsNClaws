namespace PawsNClaws.Application.Features.Products.DTOs;

public sealed record UpdateProductRequest(
    Guid Id,
    string SKU,
    string Name,
    decimal Price,
    bool IsActive);
