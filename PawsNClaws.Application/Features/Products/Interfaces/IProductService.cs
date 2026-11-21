
using PawsNClaws.Application.Features.Products.DTOs;

namespace PawsNClaws.Application.Features.Products.Interfaces;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<ProductDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, IReadOnlyList<string> Errors)> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, IReadOnlyList<string> Errors)> UpdateAsync(
        UpdateProductRequest request,
        CancellationToken cancellationToken = default);
}
