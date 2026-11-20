using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Local.Models.Employees;

public sealed class CreateEmployeeModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Temporary Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Employee Role")]
    public string Role { get; set; } = string.Empty;
}
