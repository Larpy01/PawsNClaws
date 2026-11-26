using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Customers;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Customers;

public sealed class CustomerRecords(LocalDbContext db) : ICustomerRecords
{
    public async Task<IReadOnlyList<CustomerRow>> SearchAsync(string? query, int page, CancellationToken token)
    {
        var owners = db.Set<CustomerRecord>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            owners = owners.Where(x => (x.FirstName + " " + x.LastName).Contains(term) ||
                (x.Phone != null && x.Phone.Contains(term)) || (x.Email != null && x.Email.Contains(term)) ||
                db.Set<PetRecord>().Any(p => p.CustomerId == x.Id && p.Name.Contains(term)));
        }
        return await owners.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id)
            .Skip((Math.Clamp(page, 1, 1000000) - 1) * 25).Take(26)
            .Select(x => new CustomerRow(x.Id, x.FirstName, x.LastName, x.Phone, x.Email)).ToListAsync(token);
    }

    public async Task<CustomerProfile?> FindAsync(Guid id, CancellationToken token)
    {
        var owner = await db.Set<CustomerRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (owner is null)
            return null;

        var pets = await db.Set<PetRecord>().AsNoTracking().Where(x => x.CustomerId == id)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new PetRow(x.Id, new PetForm
            {
                Version = x.Version,
                Name = x.Name,
                Species = x.Species,
                Breed = x.Breed,
                Sex = x.Sex,
                DateOfBirth = x.DateOfBirth,
                Size = x.Size,
                WeightKg = x.WeightKg,
                VaccinationStatus = x.VaccinationStatus,
                Allergies = x.Allergies,
                MedicalNotes = x.MedicalNotes,
                BehaviorNotes = x.BehaviorNotes,
                SpecialInstructions = x.SpecialInstructions
            })).ToListAsync(token);
        return new CustomerProfile(id, new CustomerForm
        {
            Version = owner.Version,
            FirstName = owner.FirstName,
            LastName = owner.LastName,
            Phone = owner.Phone,
            Email = owner.Email,
            Address = owner.Address,
            RecordConsent = owner.RecordConsent
        }, pets);
    }

    public async Task<Guid?> SaveCustomerAsync(Guid? id, CustomerForm form, Guid actor, CancellationToken token)
    {
        var record = id.HasValue
            ? await db.Set<CustomerRecord>().SingleOrDefaultAsync(x => x.Id == id, token)
            : new CustomerRecord { Id = Guid.NewGuid() };
        if (record is null)
            return null;
        if (id.HasValue && record.Version != form.Version)
            throw new ProfileConflictException();
        if (!id.HasValue)
            db.Add(record);
        record.FirstName = form.FirstName.Trim();
        record.LastName = form.LastName.Trim();
        record.Phone = form.Phone?.Trim();
        record.Email = form.Email?.Trim();
        record.Address = form.Address?.Trim();
        record.RecordConsent = form.RecordConsent;
        record.Version = Guid.NewGuid();
        db.Add(new ProfileChange
        {
            CustomerId = record.Id,
            EmployeeId = actor,
            RecordVersion = record.Version,
            Action = id.HasValue ? "CustomerUpdated" : "CustomerCreated"
        });
        await SaveAsync(token);
        return record.Id;
    }

    public async Task<Guid?> SavePetAsync(Guid ownerId, Guid? id, PetForm form, Guid actor, CancellationToken token)
    {
        if (!await db.Set<CustomerRecord>().AnyAsync(x => x.Id == ownerId, token))
            return null;
        var record = id.HasValue
            ? await db.Set<PetRecord>().SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == ownerId, token)
            : new PetRecord { Id = Guid.NewGuid(), CustomerId = ownerId };
        if (record is null)
            return null;
        if (id.HasValue && record.Version != form.Version)
            throw new ProfileConflictException();
        if (!id.HasValue)
            db.Add(record);
        record.Name = form.Name.Trim();
        record.Species = form.Species.Trim();
        record.Breed = form.Breed?.Trim();
        record.Sex = form.Sex?.Trim();
        record.DateOfBirth = form.DateOfBirth;
        record.Size = form.Size?.Trim();
        record.WeightKg = form.WeightKg;
        record.VaccinationStatus = form.VaccinationStatus?.Trim();
        record.Allergies = form.Allergies?.Trim();
        record.MedicalNotes = form.MedicalNotes?.Trim();
        record.BehaviorNotes = form.BehaviorNotes?.Trim();
        record.SpecialInstructions = form.SpecialInstructions?.Trim();
        record.Version = Guid.NewGuid();
        db.Add(new ProfileChange
        {
            CustomerId = ownerId,
            PetId = record.Id,
            EmployeeId = actor,
            RecordVersion = record.Version,
            Action = id.HasValue ? "PetUpdated" : "PetCreated"
        });
        await SaveAsync(token);
        return record.Id;
    }

    private async Task SaveAsync(CancellationToken token)
    {
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProfileConflictException();
        }
    }
}
