using Microsoft.AspNetCore.Identity;

namespace PawsNClaws.Infastructure.Identity.Cloud;

public class CloudApplicationUser : IdentityUser<Guid>
{
    public Guid? CustomerId { get; set; }
}
