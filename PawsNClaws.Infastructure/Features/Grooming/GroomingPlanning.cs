using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class GroomingPlanning(LocalDbContext db) : IGroomingPlanning
{
    public async Task<IReadOnlyList<PlanningOption>> ServicesAsync(CancellationToken token) =>
        await db.GroomingServices.OrderBy(x => x.Name)
            .Select(x => new PlanningOption(x.Id, x.Name, x.IsActive)).ToListAsync(token);

    public async Task<IReadOnlyList<PlanningOption>> TargetsAsync(CancellationToken token)
    {
        var services = await ServicesAsync(token);
        var packages = await db.Set<GroomingPackage>().OrderBy(x => x.Name)
            .Select(x => new PlanningOption(x.Id, "Package · " + x.Name,
                x.IsActive && x.Services.Any() && x.Services.All(s => s.Service.IsActive))).ToListAsync(token);
        return services.Concat(packages).ToArray();
    }

    public async Task<IReadOnlyList<PlanningOption>> ProductsAsync(CancellationToken token) =>
        await db.Products.Where(x => x.IsConsumable ||
                db.Set<GroomingRecipe>().Any(r => r.ProductId == x.Id)).OrderBy(x => x.Name)
            .Select(x => new PlanningOption(x.Id, x.SKU + " · " + x.Name + " (" + x.Unit + ")",
                x.IsActive && x.IsConsumable)).ToListAsync(token);

    public async Task<IReadOnlyList<PlanningOption>> PetsAsync(CancellationToken token) =>
        await (from pet in db.Set<PetRecord>()
               join owner in db.Set<CustomerRecord>() on pet.CustomerId equals owner.Id
               orderby owner.LastName, owner.FirstName, pet.Name
               select new PlanningOption(pet.Id, owner.FirstName + " " + owner.LastName + " · " + pet.Name
                   + " (" + pet.Species + ")", true)).ToListAsync(token);

    public async Task<IReadOnlyList<PlanningRow<GroomingPackageForm>>> PackagesAsync(CancellationToken token) =>
        await db.Set<GroomingPackage>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new PlanningRow<GroomingPackageForm>(x.Id, new GroomingPackageForm
            {
                Version = x.Version, Name = x.Name, Description = x.Description,
                IsActive = x.IsActive, ServiceIds = x.Services.Select(s => s.ServiceId).ToList()
            })).ToListAsync(token);

    public async Task<IReadOnlyList<PlanningRow<GroomingPriceForm>>> PricesAsync(CancellationToken token) =>
        await db.Set<GroomingPriceRule>().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new PlanningRow<GroomingPriceForm>(x.Id, new GroomingPriceForm
            {
                Version = x.Version, TargetId = x.ServiceId ?? x.PackageId!.Value, Species = x.Species,
                Breed = x.Breed, Size = x.Size, Condition = x.Condition, Amount = x.Amount, IsActive = x.IsActive
            })).ToListAsync(token);

    public async Task<IReadOnlyList<PlanningRow<GroomingRecipeForm>>> RecipesAsync(CancellationToken token) =>
        await db.Set<GroomingRecipe>().AsNoTracking().OrderBy(x => x.ServiceId).ThenBy(x => x.ProductId)
            .Select(x => new PlanningRow<GroomingRecipeForm>(x.Id, new GroomingRecipeForm
            {
                Version = x.Version, ServiceId = x.ServiceId, ProductId = x.ProductId,
                Quantity = x.Quantity, IsActive = x.IsActive
            })).ToListAsync(token);

    public async Task<IReadOnlyList<QuoteVersion>> QuotesAsync(Guid? id, CancellationToken token, Guid? petId = null)
    {
        var rows = await (from quote in db.Set<GroomingQuote>()
                          join version in db.Set<GroomingQuoteRevision>() on quote.Id equals version.QuoteId
                          where (!petId.HasValue || quote.PetId == petId) &&
                              (id.HasValue ? quote.Id == id : version.Number == quote.LatestRevision)
                          orderby version.CreatedAtUtc descending, quote.Id, version.Number descending
                          select new { quote.Id, quote.PetId, version.Number, version.CreatedAtUtc, version.Snapshot })
            .ToListAsync(token);
        return rows.Select(x => new QuoteVersion(x.Id, x.PetId, x.Number, x.CreatedAtUtc,
            JsonSerializer.Deserialize<QuoteSnapshot>(x.Snapshot)!)).ToArray();
    }

    public async Task<PlanningResult> SavePackageAsync(Guid? id, GroomingPackageForm form, CancellationToken token)
    {
        var package = id.HasValue
            ? await db.Set<GroomingPackage>().Include(x => x.Services).SingleOrDefaultAsync(x => x.Id == id, token)
            : new GroomingPackage();
        if (package is null)
            return new(Missing: true);
        if (id.HasValue && package.Version != form.Version)
            return new(Conflict: true);

        var ids = form.ServiceIds.Distinct().ToArray();
        var services = await db.GroomingServices.Where(x => ids.Contains(x.Id)).ToListAsync(token);
        if (services.Count != ids.Length || ids.Length == 0 ||
            (form.IsActive && services.Any(x => !x.IsActive)))
            return new(Error: "Select at least one existing service and use only active services in an active package.");
        if (form.IsActive && (HasConflictingValues(services.Select(x => x.Species)) ||
            HasConflictingValues(services.Select(x => x.Breed)) || HasConflictingValues(services.Select(x => x.Size))))
            return new(Error: "The selected services have conflicting pet eligibility.");

        package.Name = form.Name.Trim();
        package.Description = Clean(form.Description);
        package.IsActive = form.IsActive;
        package.Version = Guid.NewGuid();
        package.Services.RemoveAll(x => !ids.Contains(x.ServiceId));
        foreach (var service in services.Where(x => package.Services.All(link => link.ServiceId != x.Id)))
            package.Services.Add(new GroomingPackageService { PackageId = package.Id, ServiceId = service.Id, Service = service });
        if (!id.HasValue)
            db.Add(package);
        return await SaveAsync(package.Id, token);
    }

    public async Task<PlanningResult> SavePriceAsync(Guid? id, GroomingPriceForm form, CancellationToken token)
    {
        var rule = id.HasValue ? await db.Set<GroomingPriceRule>().FindAsync([id.Value], token) : new GroomingPriceRule();
        if (rule is null)
            return new(Missing: true);
        if (id.HasValue && rule.Version != form.Version)
            return new(Conflict: true);

        var service = await db.GroomingServices.AnyAsync(x => x.Id == form.TargetId, token);
        if (!service && !await db.Set<GroomingPackage>().AnyAsync(x => x.Id == form.TargetId, token))
            return new(Error: "Choose an existing service or package.");
        if (decimal.Round(form.Amount, 2) != form.Amount)
            return new(Error: "Enter a price with at most two decimal places.");
        var species = PriceFilter(form.Species);
        var breed = PriceFilter(form.Breed);
        if (breed is not null && species is null)
            return new(Error: "Specify the species when pricing by breed.");

        rule.ServiceId = service ? form.TargetId : null;
        rule.PackageId = service ? null : form.TargetId;
        rule.Species = species;
        rule.Breed = breed;
        rule.Size = PriceFilter(form.Size);
        rule.Condition = PriceFilter(form.Condition);
        rule.Amount = form.Amount;
        rule.IsActive = form.IsActive;
        rule.Version = Guid.NewGuid();
        if (!id.HasValue)
            db.Add(rule);
        return await SaveAsync(rule.Id, token);
    }

    public async Task<PlanningResult> SaveRecipeAsync(Guid? id, GroomingRecipeForm form, CancellationToken token)
    {
        var recipe = id.HasValue ? await db.Set<GroomingRecipe>().FindAsync([id.Value], token) : new GroomingRecipe();
        if (recipe is null)
            return new(Missing: true);
        if (id.HasValue && recipe.Version != form.Version)
            return new(Conflict: true);

        var product = await db.Products.FindAsync([form.ProductId], token);
        if (product is null || (form.IsActive && (!product.IsActive || !product.IsConsumable)) ||
            !await db.GroomingServices.AnyAsync(x => x.Id == form.ServiceId, token))
            return new(Error: "Choose an existing service and an active consumable for an active recipe.");
        if (decimal.Round(form.Quantity, 3) != form.Quantity ||
            (product.Unit == "Each" && decimal.Truncate(form.Quantity) != form.Quantity))
            return new(Error: "Use up to three decimal places, or a whole quantity for items measured in Each.");
        if (await db.Set<GroomingRecipe>().AnyAsync(x => x.ServiceId == form.ServiceId && x.ProductId == form.ProductId &&
            x.Id != recipe.Id, token))
            return new(Error: "This service already has a recipe for that product. Edit the existing recipe.");

        recipe.ServiceId = form.ServiceId;
        recipe.ProductId = form.ProductId;
        recipe.Unit = product.Unit;
        recipe.Quantity = form.Quantity;
        recipe.IsActive = form.IsActive;
        recipe.Version = Guid.NewGuid();
        if (!id.HasValue)
            db.Add(recipe);
        return await SaveAsync(recipe.Id, token);
    }

    public async Task<PlanningResult> SaveQuoteAsync(Guid? id, GroomingQuoteForm form, Guid employee, CancellationToken token)
    {
        if (form.PetId is not Guid petId || petId == Guid.Empty)
            return new(Error: "Choose a customer and pet.");
        if (form.TargetId is not Guid targetId || targetId == Guid.Empty)
            return new(Error: "Choose a service or package.");
        if (id is null && form.RequestId == Guid.Empty)
            return new(Error: "Reopen the price estimate form before saving.");
        if (!id.HasValue && await db.Set<GroomingQuote>().AnyAsync(x => x.Id == form.RequestId, token))
            return new(Conflict: true);
        var quote = id.HasValue
            ? await db.Set<GroomingQuote>().FindAsync([id.Value], token)
            : new GroomingQuote { Id = form.RequestId, PetId = petId };
        if (quote is null)
            return new(Missing: true);
        if (id.HasValue && quote.LatestRevision != form.Revision)
            return new(Conflict: true);
        if (quote.PetId != petId)
            return new(Error: "Start a new price estimate to choose a different pet.");
        if (form.OverrideTotal is decimal amount)
        {
            if (Clean(form.OverrideReason) is null)
                return new(Error: "Enter a reason for changing the total.");
            if (decimal.Round(amount, 2) != amount)
                return new(Error: "Enter a custom total with up to two decimal places.");
        }

        var pet = await db.Set<PetRecord>().FindAsync([quote.PetId], token);
        var owner = pet is null ? null : await db.Set<CustomerRecord>().FindAsync([pet.CustomerId], token);
        if (pet is null || owner is null)
            return new(Error: "Choose an existing pet.");

        var addOns = form.AddOnIds ?? [];
        var targets = addOns.Prepend(targetId).Distinct().ToArray();
        var services = await db.GroomingServices.Where(x => targets.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
        var package = await db.Set<GroomingPackage>().Include(x => x.Services).ThenInclude(x => x.Service)
            .SingleOrDefaultAsync(x => x.Id == targetId && x.IsActive, token);
        if (package is null && !services.ContainsKey(targetId))
            return new(Error: "Choose an available service or package.");
        if (addOns.Contains(targetId) || addOns.Any(x => !services.ContainsKey(x)))
            return new(Error: "Add-ons must be individual services separate from the main selection.");

        var rules = await db.Set<GroomingPriceRule>().Where(x => x.IsActive &&
            ((x.ServiceId.HasValue && targets.Contains(x.ServiceId.Value)) ||
             (x.PackageId.HasValue && targets.Contains(x.PackageId.Value)))).ToListAsync(token);
        var lines = new List<QuoteLine>();
        var included = new HashSet<Guid>();
        foreach (var target in targets)
        {
            var isPackage = package is not null && target == package.Id;
            var name = isPackage ? package!.Name : services[target].Name;
            GroomingService[] components = isPackage ? package!.Services.Select(x => x.Service).OrderBy(x => x.Name).ToArray() : [services[target]];
            if (components.Length == 0 || components.Any(x => !x.IsActive ||
                !Matches(x.Species, pet.Species) || !Matches(x.Breed, pet.Breed) || !Matches(x.Size, pet.Size)))
                return new(Error: $"{name} includes a service that is unavailable or does not apply to this pet.");
            if (components.Any(x => !included.Add(x.Id)))
                return new(Error: "An add-on is already included in this price estimate.");

            var matchingRules = rules.Where(x => (x.ServiceId == target || x.PackageId == target) &&
                Matches(PriceFilter(x.Species), pet.Species) && Matches(PriceFilter(x.Breed), pet.Breed) &&
                Matches(PriceFilter(x.Size), pet.Size) && Matches(PriceFilter(x.Condition), form.Condition))
                .GroupBy(x => new[] { x.Species, x.Breed, x.Size, x.Condition }.Count(v => PriceFilter(v) is not null))
                .OrderByDescending(x => x.Key).FirstOrDefault();
            if (matchingRules is null)
                return new(Error: $"No approved price rule matches {name}.");
            if (matchingRules.Count() != 1)
                return new(Error: $"More than one price matches {name} equally well. Update the price rules before saving this price estimate.");

            lines.Add(new(target, name, components.Select(x => x.Name).ToArray(), matchingRules.Single().Amount));
        }

        var recipeRows = await (from recipe in db.Set<GroomingRecipe>()
                                join product in db.Products on recipe.ProductId equals product.Id
                                where recipe.IsActive && included.Contains(recipe.ServiceId)
                                orderby product.Name
                                select new { recipe.ProductId, product.Name, recipe.Quantity, recipe.Unit,
                                    CurrentUnit = product.Unit, Usable = product.IsActive && product.IsConsumable }).ToListAsync(token);
        if (recipeRows.Any(x => !x.Usable || x.Unit != x.CurrentUnit))
            return new(Error: "Review the included services' recipes. A consumable is unavailable or its unit changed.");

        var supplies = recipeRows.GroupBy(x => new { x.ProductId, x.Name, x.Unit })
            .Select(x => new QuoteSupply(x.Key.ProductId, x.Key.Name, x.Sum(r => r.Quantity), x.Key.Unit)).ToArray();
        var baseTotal = lines.Sum(x => x.Amount);
        var snapshot = new QuoteSnapshot($"{owner.FirstName} {owner.LastName}", pet.Name, pet.Species, pet.Breed,
            pet.Size, Clean(form.Condition), lines, supplies, baseTotal, form.OverrideTotal ?? baseTotal,
            form.OverrideTotal, form.OverrideTotal.HasValue ? Clean(form.OverrideReason) : null);

        if (!id.HasValue)
            db.Add(quote);
        quote.LatestRevision++;
        db.Add(new GroomingQuoteRevision
        {
            QuoteId = quote.Id, Number = quote.LatestRevision, EmployeeId = employee,
            CreatedAtUtc = DateTime.UtcNow, Snapshot = JsonSerializer.Serialize(snapshot)
        });
        return await SaveAsync(quote.Id, token);
    }

    private async Task<PlanningResult> SaveAsync(Guid id, CancellationToken token)
    {
        try
        {
            await db.SaveChangesAsync(token);
            return new(Id: id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(Conflict: true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return new(Conflict: true);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? PriceFilter(string? value) =>
        string.Equals(value?.Trim(), "All", StringComparison.OrdinalIgnoreCase) ? null : Clean(value);
    private static bool Matches(string? filter, string? actual) => Clean(filter) is not { } expected ||
        string.Equals(expected, Clean(actual), StringComparison.OrdinalIgnoreCase);
    private static bool HasConflictingValues(IEnumerable<string?> values) => values.Select(Clean)
        .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
}
