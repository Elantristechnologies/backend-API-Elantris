using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("weekoff_master")]
    public class WeekOff
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("weekoff_id")]
        public int WeekOffId { get; set; }

        [Column("weekoff_day")]
        public string WeekOffDay { get; set; }

        public ICollection<AttendanceMaster> Attendances { get; set; }

        [NotMapped]
        public string Name { get; internal set; }
    }
}