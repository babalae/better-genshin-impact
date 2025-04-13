using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.EntityFrameworkCore;

namespace BetterGenshinImpact.Model.Database;

public class ApplicationDbContext : DbContext
{
    public DbSet<TaskGroup> TaskGroups { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(Global.Absolute("User\\Db"), "bgi_user.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}