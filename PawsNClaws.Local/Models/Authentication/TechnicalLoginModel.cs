using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Local.Models.Authentication;

public sealed class TechnicalLoginModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(256)]
    public string Password { get; set; } = "";

    [Required, StringLength(64)]
    [Display(Name = "Authenticator or recovery code")]
    public string Code { get; set; } = "";

    [Display(Name = "Use a recovery code")]
    public bool UseRecoveryCode { get; set; }
}
