using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Models;

public sealed record WorkspaceLink(string Label, string Controller, string Action, string? Permission = null,
    string? Role = null, bool ManagementOnly = false, string? AdditionalPermission = null, string? Path = null);

public sealed record WorkspaceItem(string Icon, string Label, string Group, params WorkspaceLink[] Links);

public static class WorkspaceNavigation
{
    public static IReadOnlyList<WorkspaceItem> Employee { get; } =
    [
        new("dashboard", "Dashboard", "General", new WorkspaceLink("Dashboard", "Dashboard", "Index", Permissions.DashboardView)),
        new("chart", "Reports", "General", new WorkspaceLink("Operations reports", "OperationsReports", "Index", Permissions.ReportsView, ManagementOnly: true)),
        new("wallet", "Cash drawer", "General", new WorkspaceLink("Cash drawer", "CashDrawer", "Index", Permissions.PosAccess)),
        new("gauge", "Grooming bills", "General", new WorkspaceLink("Grooming bills", "VisitBilling", "Index", Permissions.PosAccess)),
        new("bag", "Retail", "General",
            new("Retail checkout", "RetailCheckout", "Index", Permissions.PosCreateSale, AdditionalPermission: Permissions.PosAccess),
            new("Sale history", "RetailCheckout", "Sales", Permissions.PosAccess),
            new("Products", "Products", "Index", Permissions.ProductsView)),
        new("academy", "Grooming", "General",
            new("Grooming services", "GroomingCatalog", "Index", Permissions.GroomingCatalogManage),
            new("Packages & estimates", "GroomingPlanning", "Index", Permissions.GroomingCatalogManage),
            new("My grooming tasks", "GroomerTasks", "Index", Role: LocalRoles.Groomer)),
        new("logistics", "Inventory & transport", "General",
            new("Inventory", "Inventory", "Index", Permissions.InventoryView),
            new("Inventory alerts", "Inventory", "Alerts", Permissions.InventoryView, ManagementOnly: true),
            new("Off-site bookings", "OffsiteBookings", "Index", Permissions.GroomingCatalogManage)),
        new("mail", "SMS delivery queue", "Apps", new WorkspaceLink("SMS delivery queue", "OperationsReports", "Sms", Permissions.ReportsView, ManagementOnly: true)),
        new("calendar", "Appointments", "Apps", new WorkspaceLink("Appointments", "GroomingAppointments", "Index", Permissions.CustomerRecordsManage)),
        new("kanban", "Walk-in queue", "Apps", new WorkspaceLink("Walk-in queue", "WalkInQueue", "Index", Permissions.CustomerRecordsManage)),
        new("users", "Customers & staff", "Account",
            new("Customers & Pets", "Customers", "Index", Permissions.CustomerRecordsManage),
            new("Employees", "Employees", "Index", Permissions.UsersView)),
        new("lock", "Employee audit log", "Account", new WorkspaceLink("Employee audit log", "EmployeeAuditLogs", "Index", Permissions.UsersView, LocalRoles.Admin))
    ];

    public static IReadOnlyList<WorkspaceItem> Technical { get; } =
    [
        new("dashboard", "System Administration", "General",
            new WorkspaceLink("System Administration", "TechnicalDashboard", "Index", Permissions.TechnicalDashboardView))
    ];

    public static IReadOnlyList<WorkspaceLink> Create { get; } =
    [
        new("Add customer", "Customers", "Owner", Permissions.CustomerRecordsManage, Path: "~/Customers/New"),
        new("Add product", "Products", "Create", Permissions.ProductsCreate),
        new("Add grooming service", "GroomingCatalog", "Edit", Permissions.GroomingCatalogManage, Path: "~/GroomingCatalog/New"),
        new("Start retail checkout", "RetailCheckout", "Index", Permissions.PosCreateSale, AdditionalPermission: Permissions.PosAccess)
    ];
}

public sealed record WorkspaceAccount(bool IsTechnical, string Name, string Email, string Initial);
