using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Products.DTOs;
using PawsNClaws.Application.Features.Products.Interfaces;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class ProductPersistenceTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Theory]
    [InlineData(true, "The product SKU already exists.")]
    [InlineData(false, "The product name already exists.")]
    public async Task Create_and_update_reject_a_duplicate_after_normalization(
        bool duplicateSku, string expectedError)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var existingSku = $"FIRST-{suffix.ToUpperInvariant()}";
        var existingName = $"First product {suffix}";
        var otherSku = $"SECOND-{suffix.ToUpperInvariant()}";
        var otherName = $"Second product {suffix}";

        await using var scope = app.Services.CreateAsyncScope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.True((await products.CreateAsync(new(existingSku.ToLowerInvariant(), existingName, 9m), cancellationToken)).Succeeded);
        Assert.True((await products.CreateAsync(new(otherSku, otherName, 15m), cancellationToken)).Succeeded);
        var original = await db.Products.AsNoTracking().SingleAsync(p => p.SKU == otherSku, cancellationToken);

        var sku = duplicateSku ? $" {existingSku.ToLowerInvariant()} " : $"NEW-{suffix}";
        var name = duplicateSku ? $"New product {suffix}" : $"  First   product\t{suffix}  ";
        var create = await products.CreateAsync(new CreateProductRequest(sku, name, 10m), cancellationToken);
        var update = await products.UpdateAsync(new UpdateProductRequest(original.Id, sku, name, 20m, false), cancellationToken);

        Assert.False(create.Succeeded);
        Assert.Equal(expectedError, Assert.Single(create.Errors));
        Assert.False(update.Succeeded);
        Assert.Equal(expectedError, Assert.Single(update.Errors));
        Assert.Equal(2, await db.Products.CountAsync(p => p.Name.EndsWith(suffix), cancellationToken));
        var persisted = await db.Products.AsNoTracking().SingleAsync(p => p.Id == original.Id, cancellationToken);
        Assert.Equal(otherSku, persisted.SKU);
        Assert.Equal(otherName, persisted.Name);
        Assert.Equal(15m, persisted.Price);
        Assert.True(persisted.IsActive);
    }

    [Fact]
    public async Task Updating_a_product_can_retain_its_own_sku_and_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        await using var scope = app.Services.CreateAsyncScope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.True((await products.CreateAsync(new(suffix, $"Test product {suffix}", 0m), cancellationToken)).Succeeded);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.SKU == suffix, cancellationToken);

        var result = await products.UpdateAsync(
            new(product.Id, product.SKU.ToLowerInvariant(), $"  Test   product {suffix}  ", 12.34m, false), cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(12.34m, await db.Products.Where(p => p.Id == product.Id).Select(p => p.Price).SingleAsync(cancellationToken));
    }
}
