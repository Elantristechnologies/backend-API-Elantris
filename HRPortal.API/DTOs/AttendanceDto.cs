namespace HRPortal.API.DTOs
{
    public class AttendanceDto
    {
        public int AttendanceId { get; set; }
        public int EmpId { get; set; }
        public string? EmployeeCode { get; set; }
        public string? FullName { get; set; }
        public DateTime AttendanceDate { get; set; }
        public TimeSpan? CheckIn { get; set; }
        public TimeSpan? CheckOut { get; set; }
        public ShiftDto? Shift { get; set; }
        public WeekOffDto? WeekOff { get; set; }
    }
}
