
using HRPortal.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)

            : base(options)
        {
        }

        public DbSet<EmployeeMaster> EmployeeMasters { get; set; }
        public DbSet<EmployeeAddress> EmployeeAddresses { get; set; }
        public DbSet<EmployeePersonalDetails> EmployeePersonalDetails { get; set; }
        public DbSet<EmployeeEducation> EmployeeEducations { get; set; }
        public DbSet<EmployeeCompensation> EmployeeCompensations { get; set; }
        public DbSet<EmployeePreviousEmployment> EmployeePreviousEmployments { get; set; }
        public DbSet<EmployeeDocuments> EmployeeDocuments { get; set; }
        public DbSet<HrAdmin> HrAdmins { get; set; }
        public DbSet<AttendanceMaster> AttendanceMasters { get; set; }
        public DbSet<Shift> Shifts { get; set; }
        public DbSet<WeekOff> WeekOffs { get; set; }
        public DbSet<PayrollHead> PayrollHeads { get; set; }
        public DbSet<PayElement> PayElements { get; set; }
        public DbSet<Payroll> Payrolls { get; set; }
        public DbSet<EmployeePayrollSummary> EmployeePayrollSummaries { get; set; }
        public DbSet<CompanyPayrollOverview> CompanyPayrollOverviews { get; set; }


        //-----------------------------Travles -----------------------------

        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<Purpose> Purposes { get; set; }
        public DbSet<ClaimMaster> ClaimMasters { get; set; }
        public DbSet<ClaimTravel> ClaimTravels { get; set; }
        public DbSet<ClaimFood> ClaimFoods { get; set; }
        public DbSet<ClaimExpense> ClaimExpenses { get; set; }
        public DbSet<TravelClaim> TravelClaims { get; set; }
        public DbSet<DraftClaimView> DraftClaims { get; set; }
        public DbSet<LocalPurchase> LocalPurchases { get; set; }

  

        //--------------------Expenses tracker ----------


        public DbSet<ReceivedAmount> ReceivedAmounts { get; set; }
        public DbSet<SpentAmount> SpentAmounts { get; set; }
        public DbSet<SpentDetails> SpentDetails { get; set; }

        //-----------Purchasetype ----------
        public DbSet<PurchaseType> PurchaseTypes { get; set; }


        //---------CashVoucher----------

        public DbSet<CashVoucher> CashVouchers { get; set; }



      
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            modelBuilder.Entity<EmployeeAddress>()
                .HasOne(a => a.Employee)
                .WithOne(e => e.Address)
                .HasForeignKey<EmployeeAddress>(a => a.EmpId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmployeePersonalDetails>()
                .HasOne(p => p.Employee)
                .WithOne(e => e.PersonalDetails)
                .HasForeignKey<EmployeePersonalDetails>(p => p.EmpId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmployeeMaster>()
                .HasOne<EmployeeCompensation>()
                .WithOne(c => c.Employee)
                .HasForeignKey<EmployeeCompensation>(c => c.EmpId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmployeeMaster>()
                .HasMany<EmployeePreviousEmployment>()
                .WithOne(e => e.Employee)
                .HasForeignKey(e => e.EmpId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmployeeMaster>()
               .HasOne(e => e.EmployeeDocuments)
               .WithOne(d => d.EmployeeMaster)
               .HasForeignKey<EmployeeDocuments>(d => d.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);

            // Employee → Attendance
            modelBuilder.Entity<AttendanceMaster>()
                .HasOne(a => a.Employee)
                .WithMany(e => e.Attendances)
                .HasForeignKey(a => a.EmpId);

            // Shift → Attendance
            modelBuilder.Entity<AttendanceMaster>()
                .HasOne(a => a.Shift)
                .WithMany(s => s.Attendances)
                .HasForeignKey(a => a.ShiftId);

            // WeekOff → Attendance
            modelBuilder.Entity<AttendanceMaster>()
                .HasOne(a => a.WeekOff)
                .WithMany(w => w.Attendances)
                .HasForeignKey(a => a.WeekOffId);

            // Payroll summary primary key
            modelBuilder.Entity<EmployeePayrollSummary>()
                .HasKey(e => e.SummaryId);

            // Vehicle identity configuration: ensure EF treats vehicle_id as database-generated on add
            modelBuilder.Entity<Vehicle>()
                .Property(v => v.VehicleId)
                .ValueGeneratedOnAdd();
        }
    }
}   