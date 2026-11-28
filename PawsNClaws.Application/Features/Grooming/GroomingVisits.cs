namespace PawsNClaws.Application.Features.Grooming;

public sealed record GroomingVisitDetails(Guid Id, Guid EstimateId, int EstimateVersion, DateTime StartedAtUtc,
    QuoteSnapshot Estimate, Guid RevisionToken, DateTime? PickedUpAtUtc = null, string? CollectedBy = null);

public sealed record GroomingJobOrder(VisitChecklist Checklist, string? Phone, string? Address, string? Instructions);

public interface IGroomingVisits
{
    Task<GroomingJobOrder?> JobOrderAsync(Guid id, CancellationToken token);
    Task<bool> RecordPickupAsync(Guid id, Guid revision, Guid employee, string collectedBy, CancellationToken token);
    Task<IReadOnlyList<AssignedTask>> MyTasksAsync(Guid employee, CancellationToken token);
    Task<bool> UpdateTaskAsync(Guid employee, Guid id, Guid revision, int index, bool complete,
        string? observations, string? aftercare, CancellationToken token);
    Task<bool> UpdateManagedTaskAsync(Guid employee, Guid id, Guid revision, int index, bool complete,
        string? observations, string? aftercare, CancellationToken token);
    Task<VisitChecklist?> ChecklistAsync(Guid id, CancellationToken token);
    Task<IReadOnlyList<ChecklistGroomer>> ActiveGroomersAsync(CancellationToken token);
    Task<AssignmentResult> AssignAsync(Guid id, Guid revision, IReadOnlyList<IReadOnlyList<Guid>> assignments, CancellationToken token);
    Task<GroomingVisitDetails?> FindAsync(Guid id, CancellationToken token);
    Task<GroomingVisitDetails?> FindByEstimateAsync(Guid estimateId, CancellationToken token);
    Task<Guid?> StartAsync(Guid estimateId, int estimateVersion, Guid employeeId, CancellationToken token, Guid? appointmentId = null);
    Task<GroomingCheckInDetails?> FindCheckInAsync(Guid visitId, CancellationToken token);
    Task<CheckInResult> CheckInAsync(Guid visitId, Guid employeeId, GroomingCheckInForm form,
        IReadOnlyCollection<Guid> productIds, Stream waiver,
        CancellationToken token);
    Task<GroomingWaiver?> FindWaiverAsync(Guid visitId, Guid? checkInId, CancellationToken token);
    Task<IReadOnlyList<CancelledGroomingCheckIn>> FindCancelledCheckInsAsync(Guid visitId, CancellationToken token);
    Task<EstimateChangeResult> ChangeEstimateAsync(Guid visitId, Guid expectedRevision, int version, Guid employeeId,
        string? reason, CancellationToken token);
}

public sealed record CancelledGroomingCheckIn(Guid Id, int? EstimateVersion, DateTime? CancelledAtUtc, string? Reason,
    GroomingCheckInForm Assessment);
public enum EstimateChangeResult { Changed, VisitMissing, InvalidVersion, ReasonRequired, SuppliesPosted, BillIssued, Conflict }
