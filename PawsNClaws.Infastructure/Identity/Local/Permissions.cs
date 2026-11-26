
using System.Reflection;

namespace PawsNClaws.Infastructure.Identity.Local;

public static class Permissions
{
    public const string CustomerRecordsManage = "Customers.Manage";
    public const string GroomingCatalogManage = "GroomingCatalog.Manage";
    public const string DashboardView = "Dashboard.View";

    public const string ProductsView = "Products.View";
    public const string ProductsCreate = "Products.Create";
    public const string ProductsUpdate = "Products.Update";

    public const string InventoryView = "Inventory.View";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string InventoryReceive = "Inventory.Receive";
    public const string InventorySubmit = "Inventory.SubmitDisposal";
    public const string InventoryReview = "Inventory.ReviewDisposal";
    public const string InventoryUse = "Inventory.GroomingUsage";

    public const string PosAccess = "POS.Access";
    public const string PosCreateSale = "POS.CreateSale";
    public const string PosVoidSale = "POS.VoidSale";

    public const string ReportsView = "Reports.View";

    public const string UsersView = "Users.View";
    public const string UsersCreate = "Users.Create";
    public const string UsersAssignRoles = "Users.AssignRoles";

    public const string SettingsManage = "Settings.Manage";

    public const string UsersChangeStatus = "Users.ChangeStatus";
    public const string TechnicalDashboardView = "TechnicalDashboard.View";

    public static IReadOnlyList<string> All { get; } =
        typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral &&
                        !f.IsInitOnly &&
                        f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();
}
