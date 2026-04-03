using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("shift_master")]
    public class Shift
    {
        [Key]
        [Column("shift_id")]
        public int ShiftId { get; set; }

        [Column("shift_name")]
        public string ShiftName { get; set; }

        [Column("shift_start")]
        public TimeSpan ShiftStart { get; set; }

        [Column("shift_end")]
        public TimeSpan ShiftEnd { get; set; }

        public ICollection<AttendanceMaster> Attendances { get; set; }
    }
}