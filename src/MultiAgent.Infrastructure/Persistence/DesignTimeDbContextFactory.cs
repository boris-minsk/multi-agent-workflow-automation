using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MultiAgent.Infrastructure.Persistence;

/// <summary>
/// Allows `dotnet ef` tooling to construct the DbContext without needing the API project wired up.
/// Used only at design time (migrations); runtime uses DI.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=multiagent-design.db")
            .Options;
        return new AppDbContext(options);
    }
}
