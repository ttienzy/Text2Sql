using Microsoft.AspNetCore.Identity;

namespace TextToSqlAgent.API.Data;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
}
