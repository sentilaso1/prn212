using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using WorkFlowPro.Auth;

namespace WorkFlowPro.Data;

/// <summary>EF migrations / design-time — không có HttpContext.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorkFlowProDbContext>
{
    public WorkFlowProDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=WorkFlowPro;Trusted_Connection=True;MultipleActiveResultSets=true";

        var options = new DbContextOptionsBuilder<WorkFlowProDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new WorkFlowProDbContext(
            options,
            new DesignTimeCurrentWorkspaceService(),
            new DesignTimeCurrentUserAccessor());
    }

    private sealed class DesignTimeCurrentWorkspaceService : ICurrentWorkspaceService
    {
        public Guid? CurrentWorkspaceId => null;
    }

    private sealed class DesignTimeCurrentUserAccessor : ICurrentUserAccessor
    {
        public string? UserId => null;
    }
}
