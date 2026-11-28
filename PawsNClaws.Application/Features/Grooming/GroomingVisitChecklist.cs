namespace PawsNClaws.Application.Features.Grooming;

public sealed record ChecklistGroomer(Guid Id, string Name);
public sealed record GroomingTask(string Service, ChecklistGroomer Groomer, DateTime? Started = null,
    DateTime? Completed = null, string? Observations = null, string? Aftercare = null,
    Guid? StartedByEmployeeId = null, Guid? CompletedByEmployeeId = null,
    IReadOnlyList<ChecklistGroomer>? AdditionalGroomers = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ChecklistGroomer> AssignedGroomers =>
        AdditionalGroomers is { Count: > 0 } ? [Groomer, ..AdditionalGroomers] : [Groomer];

    public string Status => Completed.HasValue ? "Completed" : Started.HasValue ? "In progress" : "Ready to start";
}
public sealed record AssignedTask(Guid VisitId, Guid Revision, int Index, string Pet, GroomingTask Task);
public sealed record VisitChecklist(GroomingVisitDetails Visit, IReadOnlyList<GroomingTask> Tasks)
{
    public string[] Services => Visit.Estimate.Lines.SelectMany(line =>
        line.Services.Count == 0 ? new[] { line.Name } : line.Services).ToArray();
}
public enum AssignmentResult { Saved, VisitChanged, InvalidSelection }
