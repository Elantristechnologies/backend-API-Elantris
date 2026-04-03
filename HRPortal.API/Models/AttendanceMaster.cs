using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("attendance_master")]
    public class AttendanceMaster
    {
        [Key]
        [Column("attendance_id")]
        public int AttendanceId { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("shift_id")]
        public int ShiftId { get; set; }

        [Column("weekoff_id")]
        public int WeekOffId { get; set; }

        // store only the date (Postgres `date`)
        [Column("attendance_date", TypeName = "date")]
        public DateTime AttendanceDate { get; set; }

        // store only the time part (Postgres `time`)
        [Column("check_in", TypeName = "time without time zone")]
        public TimeSpan? CheckIn { get; set; }

        [Column("check_out", TypeName = "time without time zone")]
        public TimeSpan? CheckOut { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("modified_at")]
        public DateTime? ModifiedAt { get; set; }

        [ForeignKey("EmpId")]
        public EmployeeMaster Employee { get; set; }

        [ForeignKey("ShiftId")]
        public Shift Shift { get; set; }

        [ForeignKey("WeekOffId")]
        public WeekOff WeekOff { get; set; }
    }
}