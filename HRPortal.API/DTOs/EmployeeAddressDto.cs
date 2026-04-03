namespace HRPortal.API.DTOs
{
    public class EmployeeAddressDto
    {
        public string CurrentDoorNo { get; set; }
        public string CurrentStreet { get; set; }
        public string CurrentArea { get; set; }
        public string CurrentCity { get; set; }
        public string CurrentState { get; set; }
        public string CurrentPincode { get; set; }
        public string CurrentCountry { get; set; }

        public string PermanentDoorNo { get; set; }
        public string PermanentStreet { get; set; }
        public string PermanentArea { get; set; }
        public string PermanentCity { get; set; }
        public string PermanentState { get; set; }
        public string PermanentPincode { get; set; }
        public string PermanentCountry { get; set; }
    }
}