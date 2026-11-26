using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Products.DTOs;
using PawsNClaws.Infastructure.Features.Products;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.UnitTests;

public sealed class ProductValidationTests
{
    public static TheoryData<string, string, decimal, string> InvalidProducts => new()
    {
        { " ", "Test product", 10m, "SKU is required." },
        { new string('S', 51), "Test product", 10m, "SKU cannot exceed 50 characters." },
        { "TEST-1", " \t ", 10m, "Product name is required." },
        { "TEST-1", new string('N', 201), 10m, "Product name cannot exceed 200 characters." },
        { "TEST-1", "Test product", -0.01m, "Product price cannot be negative." },
        { "TEST-1", "Test product", 10000000000000000m, "Product price exceeds the supported range." },
        { "TEST-1", "Test product", 1.001m, "Product price cannot exceed two decimal places." }
    };

    [Theory]
    [MemberData(nameof(InvalidProducts))]
    public async Task Create_and_update_reject_invalid_products_before_accessing_storage(
        string sku, string name, decimal price, string expectedError)
    {
        // A context without a provider fails if validation reaches persistence.
        await using var db = new LocalDbContext(new DbContextOptions<LocalDbContext>());
        var service = new ProductService(db);

        var create = await service.CreateAsync(new CreateProductRequest(sku, name, price), TestContext.Current.CancellationToken);
        var update = await service.UpdateAsync(
            new UpdateProductRequest(Guid.NewGuid(), sku, name, price, true), TestContext.Current.CancellationToken);

        Assert.False(create.Succeeded);
        Assert.Equal(expectedError, Assert.Single(create.Errors));
        Assert.False(update.Succeeded);
        Assert.Equal(expectedError, Assert.Single(update.Errors));
    }
}
