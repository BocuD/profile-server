using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ProfileServer.Database;

public class PerformanceDbContext : DbContext
{
    public DbSet<PerformanceReport> PerformanceReports { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source=performance.db3");
}

public class PerformanceReport
{
    [Key]
    public int id { get; set; }

    public DateTime created { get; set; }
    
    public string csvName { get; set; } = "";
    
    public float averageFrametime;
    public float percentile95;
    public float percentile99;
    public float maxFrameTime;
}