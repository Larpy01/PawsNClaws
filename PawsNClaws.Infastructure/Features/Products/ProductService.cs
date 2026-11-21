using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Products.DTOs;
using PawsNClaws.Application.Features.Products.Interfaces;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Products;

public sealed class ProductService : IProductService
{
    private readonly LocalDbContext _db;

    public ProductService(LocalDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Products
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ThenBy(p => p.SKU)
            .Select(p => new ProductDto(
                p.Id,
                p.SKU,
                p.Name,
                p.Price,
                p.IsActive,
                p.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDto(
                p.Id,
                p.SKU,
                p.Name,
                p.Price,
                p.IsActive,
                p.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, IReadOnlyList<string> Errors)>
        CreateAsync(
            CreateProductRequest request,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sku = request.SKU?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();

        var errors = ValidateProduct(sku, name, request.Price);

        if (errors.Count > 0)
            return (false, errors);

        if (await _db.Products.AnyAsync(
            p => p.SKU == sku,
            cancellationToken))
        {
            return (false, ["The product SKU already exists."]);
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            SKU = sku!,
            Name = name!,
            Price = request.Price,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Products.Add(product);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(product).State = EntityState.Detached;
            return (false, ["The product SKU already exists."]);
        }

        return (true, Array.Empty<string>());
    }

    public async Task<(bool Succeeded, IReadOnlyList<string> Errors)>
        UpdateAsync(
            UpdateProductRequest request,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sku = request.SKU?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();

        var errors = ValidateProduct(sku, name, request.Price);

        if (errors.Count > 0)
            return (false, errors);

        var product = await _db.Products
            .FirstOrDefaultAsync(
                p => p.Id == request.Id,
                cancellationToken);

        if (product is null)
            return (false, ["Product not found."]);

        bool duplicateSku = await _db.Products.AnyAsync(
            p => p.SKU == sku && p.Id != request.Id,
            cancellationToken);

        if (duplicateSku)
            return (false, ["The product SKU already exists."]);

        product.SKU = sku!;
        product.Name = name!;
        product.Price = request.Price;
        product.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Do not leave invalid changes tracked by this context.
            _db.Entry(product).State = EntityState.Detached;
            return (false, ["The product SKU already exists."]);
        }

        return (true, Array.Empty<string>());
    }

    private static List<string> ValidateProduct(
        string? sku,
        string? name,
        decimal price)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(sku))
            errors.Add("SKU is required.");
        else if (sku.Length > 50)
            errors.Add("SKU cannot exceed 50 characters.");

        if (string.IsNullOrWhiteSpace(name))
            errors.Add("Product name is required.");
        else if (name.Length > 200)
            errors.Add("Product name cannot exceed 200 characters.");

        if (price < 0)
            errors.Add("Product price cannot be negative.");

        if (price > 9999999999999999.99m)
            errors.Add("Product price exceeds the supported range.");

        if (decimal.Round(price, 2) != price)
            errors.Add("Product price cannot exceed two decimal places.");

        return errors;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
    }
}
