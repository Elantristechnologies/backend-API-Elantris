namespace HRPortal.API.DTOs
{
    public class ClaimListDto
    {
        public int ClaimId { get; set; }
        public string ClaimNo { get; set; }
        public int EmployeeId { get; set; }
        public DateTime ClaimMonth { get; set; }
        public string VehicleType { get; set; }
        public string Status { get; set; }

        public decimal TravelAmount { get; set; }
        public decimal FoodAmount { get; set; }
        public decimal TollAmount { get; set; }
        public decimal AutoAmount { get; set; }
        public decimal OtherAmount { get; set; }

        public decimal TotalAmount { get; set; }
    }
}
