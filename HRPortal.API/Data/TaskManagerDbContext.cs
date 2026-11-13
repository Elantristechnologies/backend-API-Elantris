
using HRPortal.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.API.Data
{
    public class TaskManagerDbContext : DbContext
    {
        public TaskManagerDbContext(
            DbContextOptions<TaskManagerDbContext> options)
            : base(options)
        {
        }

        public DbSet<TaskManagerTask> Tasks { get; set; }

        public DbSet<TaskCreatorPermission> TaskCreatorPermissions { get; set; }
    }
}
