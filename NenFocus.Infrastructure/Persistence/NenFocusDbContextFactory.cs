using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NenFocus.Infrastructure.Persistence;

public class NenFocusDbContextFactory : IDesignTimeDbContextFactory<NenFocusDbContext>
{
    public NenFocusDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NenFocusDbContext>();
        // This path doesn't matter much; it's just for the migration tool to read your code
        optionsBuilder.UseSqlite("Data Source=migration_temp.db");

        return new NenFocusDbContext(optionsBuilder.Options);
    }
}