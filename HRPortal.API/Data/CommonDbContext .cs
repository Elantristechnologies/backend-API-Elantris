using HRPortal.API.DTOs;
using HRPortal.API.Models;
using Microsoft.EntityFrameworkCore;

public class CommonDbContext : DbContext
{
    public CommonDbContext(DbContextOptions<CommonDbContext> options)
        : base(options)
    {
    }
    public DbSet<CompanyUser> CompanyUsers { get; set; }

    public DbSet<UsersMaster> UsersMaster { get; set; }

    //public DbSet<LoginResponseDto> LoginResponseDtos { get; set; }

    public DbSet<OrganizationModule> OrganizationModules { get; set; }

    public DbSet<OrganizationMaster> OrganizationMasters { get; set; }

    public DbSet<ModuleMaster> ModuleMasters { get; set; }

    public DbSet<CommonDepartment> Departments { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

      
    }
}
