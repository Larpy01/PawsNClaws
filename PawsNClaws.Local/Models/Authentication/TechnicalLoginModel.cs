using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Local.Models.Authentication;

public sealed class TechnicalLoginModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(256)]
    public string Password { get; set; } = "";
}
