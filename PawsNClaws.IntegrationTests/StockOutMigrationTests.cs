using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class StockOutMigrationTests
{
    internal const string PreviousMigration = "20261125031706_AddInventoryFoundation";
    internal const string ApprovalMigration = "20261126053025_AddStockOutApprovalFoundation";

    [Fact]
    public void Stock_out_migration_generates_additive_SQL_with_a_filtered_unique_movement_link()
    {
        using var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=ScriptOnly;Integrated Security=true;Connect Timeout=1")
            .Options);

        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ApprovalMigration);

        Assert.Contains("CREATE TABLE [StockOutRequests]", script);
        Assert.Contains("CREATE TABLE [StockOutRequestEvidence]", script);
        Assert.Contains("ADD [StockOutRequestId] uniqueidentifier NULL", script);
        Assert.Contains(
            "CREATE UNIQUE INDEX [UX_InventoryMovements_StockOutRequestId] ON [InventoryMovements] ([StockOutRequestId], [ProductId]) WHERE [StockOutRequestId] IS NOT NULL",
            script);
        Assert.Contains("FOREIGN KEY ([StockOutRequestId], [ProductId]) REFERENCES [StockOutRequests] ([Id], [ProductId])", script);
        Assert.Contains("[RowVersion] rowversion NOT NULL", script);
        Assert.Contains("[CK_StockOutRequests_Decision]", script);
        Assert.DoesNotContain("DROP ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER COLUMN", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [InventoryMovements]", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inventory_upgrade_widens_quantities_and_backfills_from_current_stock()
    {
        using var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=ScriptOnly;Integrated Security=true;Connect Timeout=1")
            .Options);
        var script = db.GetService<IMigrator>().GenerateScript(ApprovalMigration);
        Assert.Contains("ALTER COLUMN [QuantityOnHand] decimal(18,3) NOT NULL", script);
        Assert.Contains("CREATE TABLE [InventoryBatches]", script);
        Assert.Contains("CREATE TABLE [InventorySuppliers]", script);
        Assert.Contains("CREATE TABLE [StockReceipts]", script);
        Assert.Contains("CREATE TABLE [GroomingUsageEstimates]", script);
        Assert.Contains("Pending Stock Out exceeds legacy stock", script);
        Assert.Contains("INSERT INTO [dbo].[InventoryBatches]", script);
        Assert.Contains("WHERE stock.[QuantityOnHand] > 0", script);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", script, StringComparison.OrdinalIgnoreCase);
    }
}
