using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Application.Features.Grooming;

public enum AssessmentStatus
{
    [Display(Name = "Not assessed")] NotAssessed,
    Normal,
    [Display(Name = "Needs attention")] NeedsAttention
}

public sealed class GroomingCheckInForm
{
    public Guid? VisitRevision { get; set; }
    [EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus Skin { get; set; }
    [EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus Coat { get; set; }
    [EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus Ears { get; set; }
    [EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus Nails { get; set; }
    [EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus Teeth { get; set; }
    [EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus Gums { get; set; }
    [Display(Name = "Anal glands"), EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus AnalGlands { get; set; }
    [Display(Name = "Ticks and fleas"), EnumDataType(typeof(AssessmentStatus))]
    public AssessmentStatus TicksAndFleas { get; set; }
    [Display(Name = "Tick and flea severity or other findings"), StringLength(1000)]
    public string? Findings { get; set; }
    [Display(Name = "Owner’s instructions"), StringLength(1000)]
    public string? OwnerInstructions { get; set; }
    [Display(Name = "Other products or solutions used"), StringLength(500)]
    public string? SolutionsUsed { get; set; }
    public IReadOnlyList<CheckInProduct>? ProductsUsed { get; set; }
    [Display(Name = "Staff notes"), StringLength(1000)]
    public string? StaffNotes { get; set; }
    [Display(Name = "Name on the signed waiver"), Required, StringLength(150)]
    public string Signer { get; set; } = "";
    [Display(Name = "Date signed"), Required, DataType(DataType.Date)]
    public DateOnly? SignedOn { get; set; }
}

public sealed record CheckInProduct(Guid Id, string Name);
public sealed record GroomingCheckInDetails(GroomingCheckInForm Assessment, DateTime RecordedAtUtc);
public sealed record GroomingWaiver(byte[] Content, string ContentType);
public enum CheckInResult { Saved, AlreadyRecorded, VisitMissing, InvalidWaiver, VisitChanged, UnavailableProduct, WaitingInQueue }
