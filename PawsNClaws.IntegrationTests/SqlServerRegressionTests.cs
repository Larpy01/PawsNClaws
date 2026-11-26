using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class SqlServerRegressionTests : IAsyncLifetime
{
    private readonly string _connectionString = new SqlConnectionStringBuilder
    {
        // This endpoint is reserved for tests. Never read application or user connection strings.
        DataSource = @"(localdb)\PawsNClawsPhase2RTests",
        InitialCatalog = $"PawsNClawsTests_{Guid.NewGuid():N}",
        IntegratedSecurity = true,
        TrustServerCertificate = true,
        Pooling = false,
        ConnectTimeout = 5
    }.ConnectionString;
    private bool _ownsDatabase;

    public async ValueTask InitializeAsync()
    {
        await using var db = CreateContext();
        var creator = db.GetService<IRelationalDatabaseCreator>();

        // CREATE DATABASE fails if this catalog already exists. Claim ownership only after it succeeds.
        await creator.CreateAsync();
        _ownsDatabase = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsDatabase)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task Sql_server_enforces_both_product_unique_indexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(cancellationToken);
        db.Products.Add(NewProduct("TEST-01", "Test product"));
        await db.SaveChangesAsync(cancellationToken);

        foreach (var duplicate in new[]
        {
            NewProduct("TEST-01", "Another product"),
            NewProduct("TEST-02", "Test product")
        })
        {
            db.Products.Add(duplicate);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
            var sqlError = Assert.IsType<SqlException>(failure.InnerException);
            Assert.Contains(sqlError.Number, new[] { 2601, 2627 });
            db.Entry(duplicate).State = EntityState.Detached;
        }

        Assert.Equal(1, await db.Products.CountAsync(cancellationToken));
    }

    [SqlServerFact]
    public async Task An_admin_cannot_create_or_modify_privileged_employees()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LocalDbContext>(options => options.UseSqlServer(_connectionString));
        services.AddIdentityCore<LocalApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<LocalDbContext>();
        services.AddScoped<IEmployeeManagementService, EmployeeManagementService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeManagementService>();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await LocalRoleSeeder.SeedAsync(roles);
        var actor = await TestEmployee.CreateAsync(users, LocalRoles.Admin);
        var target = await TestEmployee.CreateAsync(users, LocalRoles.SuperAdmin);
        const string attemptedEmail = "privileged@example.test";

        var create = await employees.CreateAsync(new(
            "Test", "Employee", attemptedEmail, TestEmployee.GeneratePassword(), LocalRoles.Admin, actor.Id),
            cancellationToken);
        var status = await employees.SetActiveStatusAsync(target.Id, actor.Id, false, cancellationToken);
        var role = await employees.ChangeRoleAsync(new(target.Id, actor.Id, LocalRoles.Cashier), cancellationToken);

        Assert.False(create.Succeeded);
        Assert.Equal("You are not authorized to assign this role.", Assert.Single(create.Errors));
        Assert.False(status.Succeeded);
        Assert.Equal("Administrator accounts cannot be modified here.", Assert.Single(status.Errors));
        Assert.False(role.Succeeded);
        Assert.Equal("Privileged administrator accounts cannot be modified here.", Assert.Single(role.Errors));
        Assert.Null(await users.FindByEmailAsync(attemptedEmail));
        var employee = await employees.GetByIdAsync(target.Id, cancellationToken);
        Assert.NotNull(employee);
        Assert.True(employee.IsActive);
        Assert.Equal(LocalRoles.SuperAdmin, Assert.Single(employee.Roles));
        Assert.Empty(await db.EmployeeAuditLogs.ToListAsync(cancellationToken));
    }

    [SqlServerFact]
    public async Task Stock_out_upgrade_preserves_history_and_enforces_request_movement_relationship()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(StockOutMigrationTests.PreviousMigration, cancellationToken);

        var product = NewProduct("TEST-01", "Test product");
        var otherProduct = NewProduct("TEST-02", "Other product");
        var requester = new LocalApplicationUser { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Requester" };
        var reviewer = new LocalApplicationUser { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Reviewer" };
        var legacyMovementId = Guid.NewGuid();
        var legacyEvidenceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AddRange(requester, reviewer);
        foreach (var legacyProduct in new[] { product, otherProduct })
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO [Products] ([Id], [SKU], [Name], [Price], [IsActive], [CreatedAtUtc])
                VALUES ({legacyProduct.Id}, {legacyProduct.SKU}, {legacyProduct.Name},
                    {legacyProduct.Price}, {legacyProduct.IsActive}, {legacyProduct.CreatedAtUtc})
                """, cancellationToken);
        }
        db.InventoryStocks.Add(new InventoryStock { ProductId = product.Id, QuantityOnHand = 3 });
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [InventoryMovements]
                ([Id], [ProductId], [MovementType], [StockOutReason], [Reason], [QuantityBefore],
                 [QuantityChange], [QuantityAfter], [PerformedByUserId], [CreatedAtUtc], [OperationId])
            VALUES ({legacyMovementId}, {product.Id}, 'StockOut', 'Defective', 'Legacy disposal',
                4, -1, 3, {requester.Id}, {now}, {Guid.NewGuid()})
            """, cancellationToken);
        db.InventoryMovementEvidence.Add(new InventoryMovementEvidence
        {
            Id = legacyEvidenceId, InventoryMovementId = legacyMovementId, UploadedByUserId = requester.Id,
            OriginalFileName = "legacy.jpg", StorageKey = "legacy-evidence",
            ContentType = "image/jpeg", FileSizeBytes = 1, Sha256Hash = new string('a', 64)
        });
        await db.SaveChangesAsync(cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);
        db.ChangeTracker.Clear();
        var legacy = await db.InventoryMovements.Include(movement => movement.Evidence)
            .SingleAsync(movement => movement.Id == legacyMovementId, cancellationToken);
        Assert.Null(legacy.StockOutRequestId);
        Assert.Equal(-1, legacy.QuantityChange);
        Assert.Equal(legacyEvidenceId, Assert.Single(legacy.Evidence).Id);
        Assert.Equal(3, (await db.InventoryStocks.SingleAsync(cancellationToken)).QuantityOnHand);

        var selfApproval = new StockOutRequest
        {
            ProductId = product.Id, SubmittedByUserId = requester.Id,
            Quantity = 1, ReasonCategory = StockOutReason.Defective, Reason = "Test disposal",
            Status = StockOutRequestStatus.Approved, SubmittedAtUtc = now,
            DecidedByUserId = requester.Id, DecidedAtUtc = now
        };
        db.StockOutRequests.Add(selfApproval);
        var invalidDecision = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync(cancellationToken));
        Assert.Equal(547, Assert.IsType<SqlException>(invalidDecision.InnerException).Number);
        db.Entry(selfApproval).State = EntityState.Detached;

        var request = new StockOutRequest
        {
            ProductId = product.Id, SubmittedByUserId = requester.Id,
            Quantity = 1, ReasonCategory = StockOutReason.Defective, Reason = "Test disposal",
            Status = StockOutRequestStatus.Approved, SubmittedAtUtc = now,
            DecidedByUserId = reviewer.Id, DecidedAtUtc = now,
            Evidence =
            [
                new StockOutRequestEvidence
                {
                    UploadedByUserId = requester.Id, OriginalFileName = "request.jpg",
                    StorageKey = "request-evidence", ContentType = "image/jpeg",
                    FileSizeBytes = 1, Sha256Hash = new string('b', 64)
                }
            ]
        };
        db.StockOutRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        Assert.NotEmpty(request.RowVersion);
        db.ChangeTracker.Clear();

        var repeatedSubmission = new StockOutRequest
        {
            ProductId = product.Id, SubmittedByUserId = requester.Id,
            Quantity = 1, ReasonCategory = StockOutReason.Defective, Reason = "Repeated submission",
            SubmissionOperationId = request.SubmissionOperationId
        };
        db.StockOutRequests.Add(repeatedSubmission);
        var duplicateSubmission = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync(cancellationToken));
        Assert.Contains(Assert.IsType<SqlException>(duplicateSubmission.InnerException).Number, new[] { 2601, 2627 });
        db.Entry(repeatedSubmission).State = EntityState.Detached;

        var wrongProduct = RequestMovement(request.Id, otherProduct.Id, reviewer.Id);
        db.InventoryMovements.Add(wrongProduct);
        var foreignKey = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
        Assert.Equal(547, Assert.IsType<SqlException>(foreignKey.InnerException).Number);
        db.Entry(wrongProduct).State = EntityState.Detached;

        db.InventoryMovements.Add(RequestMovement(request.Id, product.Id, reviewer.Id));
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        var movement = await db.InventoryMovements
            .Include(movement => movement.StockOutRequest).ThenInclude(request => request!.Evidence)
            .SingleAsync(movement => movement.StockOutRequestId == request.Id, cancellationToken);
        Assert.Equal("request-evidence", Assert.Single(movement.StockOutRequest!.Evidence).StorageKey);
        db.ChangeTracker.Clear();

        db.InventoryMovements.Add(RequestMovement(request.Id, product.Id, reviewer.Id));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
        Assert.Contains(Assert.IsType<SqlException>(duplicate.InnerException).Number, new[] { 2601, 2627 });
    }

    private static InventoryMovement RequestMovement(Guid requestId, Guid productId, Guid reviewerId) => new()
    {
        ProductId = productId,
        StockOutRequestId = requestId,
        PerformedByUserId = reviewerId,
        MovementType = InventoryMovementType.StockOut,
        StockOutReason = StockOutReason.Defective,
        Reason = "Test disposal",
        QuantityBefore = 3,
        QuantityChange = -1,
        QuantityAfter = 2
    };

    private LocalDbContext CreateContext() => new(
        new DbContextOptionsBuilder<LocalDbContext>().UseSqlServer(_connectionString).Options);

    private static Product NewProduct(string sku, string name) => new()
    {
        Id = Guid.NewGuid(),
        SKU = sku,
        Name = name,
        Price = 1m,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow
    };
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("PAWSNCLAWS_RUN_SQLSERVER_TESTS") != "1")
        {
            Skip = "Requires the dedicated PawsNClawsPhase2RTests LocalDB instance and PAWSNCLAWS_RUN_SQLSERVER_TESTS=1.";
        }
    }
}
