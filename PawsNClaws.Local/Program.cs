using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Authentication.Interfaces;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Application.Features.Products;
using PawsNClaws.Application.Features.Products.Interfaces;
using PawsNClaws.Infastructure.Features.Products;
using PawsNClaws.Infastructure.Features.Inventory;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;
using PawsNClaws.Local;
using PawsNClaws.Local.Controllers;
using PawsNClaws.Local.Notifications;
using PawsNClaws.Infastructure.Features.Notifications;

var isTechnicalCommand = args.FirstOrDefault() == TechnicalAccountCommands.ResetPassword;
var builder = WebApplication.CreateBuilder(isTechnicalCommand ? [] : args);
builder.Services.AddSingleton(services =>
    new RetailTax(services.GetRequiredService<IConfiguration>().GetValue<bool>("RetailTax:VatRegistered")));

// Add services to the container.

builder.Services
    .AddIdentity<LocalApplicationUser, IdentityRole<Guid>>(
        options =>
        {
            options.User.RequireUniqueEmail = true;

            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
        })
    .AddEntityFrameworkStores<LocalDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<
    IEmployeeAuthenticationService,
    EmployeeAuthenticationService>();
builder.Services.AddScoped<TechnicalAuthenticationService>();


// Configure Identity lockout settings before builder.Build().
builder.Services.Configure<IdentityOptions>(options =>
{
    options.Lockout.DefaultLockoutTimeSpan =
        TimeSpan.FromMinutes(15);

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
});

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<LocalDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("LocalConnection")));


builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.Cookie.Name = "PawsNClaws.Local.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<LocalApplicationUser>>();

        var user = await userManager.GetUserAsync(context.Principal);

        if (user is null || !user.IsActive || await userManager.IsInRoleAsync(user, LocalRoles.SuperAdmin))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(
                IdentityConstants.ApplicationScheme);
        }
    };
});

builder.Services.AddAuthentication().AddCookie(TechnicalAccountController.Scheme, options =>
{
    options.LoginPath = "/system-admin/login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "__Host-PawsNClaws.Technical";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        var authentication = context.HttpContext.RequestServices.GetRequiredService<TechnicalAuthenticationService>();
        var principal = await authentication.RevalidateAsync(context.Principal);
        if (principal is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(TechnicalAccountController.Scheme);
            return;
        }

        context.ReplacePrincipal(principal);
    };
});


builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (permission == Permissions.GroomingCatalogManage)
                policy.RequireRole(LocalRoles.Admin, LocalRoles.Manager);
            if (permission == Permissions.CustomerRecordsManage)
                policy.RequireRole(LocalRoles.Admin, LocalRoles.Manager, LocalRoles.Cashier);
            if (permission is Permissions.ProductsView or Permissions.ProductsCreate or Permissions.ProductsUpdate)
                policy.RequireRole(LocalRoles.Manager, LocalRoles.Admin);
            if (permission is Permissions.UsersView or Permissions.UsersCreate or Permissions.UsersAssignRoles or Permissions.UsersChangeStatus)
                policy.RequireRole(LocalRoles.Admin);
            if (permission == Permissions.TechnicalDashboardView)
                policy.AddAuthenticationSchemes(TechnicalAccountController.Scheme);

            policy.AddRequirements(
                new PermissionRequirement(permission));
        });
    }

    // Require authentication unless an endpoint
    // explicitly allows anonymous access.
    options.FallbackPolicy =
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
});

builder.Services.AddSingleton<
    IAuthorizationHandler,
    PermissionAuthorizationHandler>();

builder.Services.AddScoped<
    IEmployeeManagementService,
    EmployeeManagementService>();


builder.Services.Configure<SecurityStampValidatorOptions>(
    options =>
    {
        // Revalidate on every authenticated request.
        // This prioritizes immediate revocation over
        // database-query performance.
        options.ValidationInterval = TimeSpan.Zero;
    });

builder.Services.AddScoped<
    IEmployeeAuditLogService,
    EmployeeAuditLogService>();

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Customers.ICustomerRecords, PawsNClaws.Infastructure.Features.Customers.CustomerRecords>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<IInventoryService>(services => services.GetRequiredService<InventoryService>());
builder.Services.AddScoped<ICashDrawer, CashDrawer>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Reports.IOperationsReports,
    PawsNClaws.Infastructure.Features.Reports.OperationsReports>();
builder.Services.AddScoped<IRetailCheckout>(services => services.GetRequiredService<InventoryService>());
builder.Services.AddScoped<SmsDispatcher>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Notifications.ISmsQueueStatus, SmsQueueStatus>();
builder.Services.AddHttpClient<TextBeeClient>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddHostedService<SmsDeliveryWorker>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IGroomingCatalog,
    PawsNClaws.Infastructure.Features.Grooming.GroomingCatalog>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IGroomingPlanning,
    PawsNClaws.Infastructure.Features.Grooming.GroomingPlanning>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IGroomingVisits,
    PawsNClaws.Infastructure.Features.Grooming.GroomingVisits>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IVisitBilling,
    PawsNClaws.Infastructure.Features.Grooming.VisitBilling>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IOffsiteBookings,
    PawsNClaws.Infastructure.Features.Grooming.OffsiteBookings>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IGroomingAppointments,
    PawsNClaws.Infastructure.Features.Grooming.GroomingAppointments>();
builder.Services.AddScoped<PawsNClaws.Application.Features.Grooming.IWalkInQueue,
    PawsNClaws.Infastructure.Features.Grooming.WalkInQueue>();
builder.Services.AddSingleton(_ => new InventoryEvidenceStore(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawsNClaws", "InventoryEvidence")));




var app = builder.Build();

if (isTechnicalCommand)
{
    await TechnicalAccountCommands.RunAsync(app, args);
    return;
}

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    await LocalRoleSeeder.SeedAsync(roleManager);
}


if (app.Environment.IsDevelopment())
{
    foreach (var sectionName in new[] { "BootstrapAdmin", "BootstrapBusinessAdmin" })
    {
        var section = app.Configuration.GetSection(sectionName);
        if (!section.GetValue<bool>("Enabled"))
            continue;

        var email = section["Email"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException($"Credentials for {sectionName} are missing.");

        await using var scope = app.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        if (sectionName == "BootstrapBusinessAdmin")
            await LocalIdentitySeeder.SeedBusinessAdminAsync(userManager, roleManager, email.Trim(), password);
        else
            await LocalIdentitySeeder.SeedSuperAdminAsync(userManager, roleManager, email.Trim(), password);

        await transaction.CommitAsync();
    }
}



// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
