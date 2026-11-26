
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Infastructure.Identity.Cloud;
using PawsNClaws.Infastructure.Persistence.Cloud;

var builder = WebApplication.CreateBuilder(args);

// Controllers and OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// SQL Server database
builder.Services.AddDbContext<CloudDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString(
            "CloudConnection")));

// Required for Identity token providers
builder.Services.AddDataProtection();

// Cloud Identity
builder.Services
    .AddIdentityCore<CloudApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<CloudDbContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
