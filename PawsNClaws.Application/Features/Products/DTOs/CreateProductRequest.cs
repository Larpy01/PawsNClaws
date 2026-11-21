namespace PawsNClaws.Application.Features.Products.DTOs;

public sealed record CreateProductRequest(
    string SKU,
    string Name,
    decimal Price);
