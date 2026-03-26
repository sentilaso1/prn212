using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Auth;

namespace WorkFlowPro.Data;

public static class AdminUserSeed
{
    public static async Task EnsureSeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var email = config["SeedAdmin:Email"]?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return;

        var password = config["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password))
            password = "Admin!234";

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminUserSeed");

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!existing.IsPlatformAdmin)
            {
                existing.IsPlatformAdmin = true;
                existing.AccountStatus = AccountStatus.Approved;
                existing.AwaitingPmWorkspaceApproval = false;
                await userManager.UpdateAsync(existing);
                logger.LogInformation("Updated existing user {Email} as platform admin.", email);
            }

            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = "Platform Admin",
            EmailConfirmed = true,
            IsPlatformAdmin = true,
            AccountStatus = AccountStatus.Approved,
            AwaitingPmWorkspaceApproval = false
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Failed to seed admin {Email}: {Errors}",
                email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        logger.LogInformation("Seeded platform admin {Email}.", email);
    }
}
