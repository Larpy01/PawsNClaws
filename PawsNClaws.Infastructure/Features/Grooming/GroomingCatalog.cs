using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class GroomingCatalog(LocalDbContext db) : IGroomingCatalog
{
    private static readonly Expression<Func<GroomingService, GroomingCatalogEntry>> ToEntry = x =>
        new GroomingCatalogEntry(x.Id, new GroomingServiceForm
        {
            Version = x.Version, Name = x.Name, Description = x.Description,
            DurationMinutes = x.DurationMinutes, Species = x.Species,
            Breed = x.Breed, Size = x.Size, IsActive = x.IsActive
        });

    public async Task<IReadOnlyList<GroomingCatalogEntry>> ListAsync(CancellationToken token) =>
        await db.GroomingServices.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(ToEntry).ToListAsync(token);

    public async Task<GroomingServiceForm?> FindAsync(Guid id, CancellationToken token) =>
        (await db.GroomingServices.AsNoTracking().Where(x => x.Id == id)
            .Select(ToEntry).SingleOrDefaultAsync(token))?.Service;

    public async Task<CatalogSaveResult> SaveAsync(Guid? id, GroomingServiceForm form, CancellationToken token)
    {
        var service = id.HasValue
            ? await db.GroomingServices.SingleOrDefaultAsync(x => x.Id == id, token)
            : new GroomingService();
        if (service is null)
            return CatalogSaveResult.NotFound;
        if (id.HasValue && service.Version != form.Version)
            return CatalogSaveResult.Conflict;

        service.Version = Guid.NewGuid();
        service.Name = form.Name.Trim();
        service.Description = form.Description?.Trim();
        service.DurationMinutes = form.DurationMinutes;
        service.Species = form.Species?.Trim();
        service.Breed = form.Breed?.Trim();
        service.Size = form.Size?.Trim();
        service.IsActive = form.IsActive;
        if (!id.HasValue)
            db.GroomingServices.Add(service);

        try
        {
            await db.SaveChangesAsync(token);
            return CatalogSaveResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return CatalogSaveResult.Conflict;
        }
    }
}
