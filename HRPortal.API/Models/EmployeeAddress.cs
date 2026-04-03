 
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace HRPortal.API.Models 
{
    [Table("employee_address")]
    public class EmployeeAddress
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        // 🔹 Current Address
        [Column("current_door_no")]
        public string CurrentDoorNo { get; set; }

        [Column("current_street")]
        public string CurrentStreet { get; set; }

        [Column("current_area")]
        public string CurrentArea { get; set; }

        [Column("current_city")]
        public string CurrentCity { get; set; }

        [Column("current_state")]
        public string CurrentState { get; set; }

        [Column("current_pincode")]
        public string CurrentPincode { get; set; }

        [Column("current_country")]
        public string CurrentCountry { get; set; }

        // 🔹 Permanent Address
        [Column("permanent_door_no")]
        public string PermanentDoorNo { get; set; }

        [Column("permanent_street")]
        public string PermanentStreet { get; set; }

        [Column("permanent_area")]
        public string PermanentArea { get; set; }

        [Column("permanent_city")]
        public string PermanentCity { get; set; }

        [Column("permanent_state")]
        public string PermanentState { get; set; }

        [Column("permanent_pincode")]
        public string PermanentPincode { get; set; }

        [Column("permanent_country")]
        public string PermanentCountry { get; set; }

        // 🔥 Navigation
        [ForeignKey("EmpId")]
        public EmployeeMaster Employee { get; set; }
    }
}