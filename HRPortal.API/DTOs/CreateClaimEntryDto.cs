namespace HRPortal.API.DTOs
{
    public class CreateClaimEntryDto
    {
        public int EmployeeId { get; set; }
        public DateTime ClaimMonth { get; set; }
        public int VehicleTypeId { get; set; }
        public string VehicleNumber { get; set; }
        public int FuelTypeId { get; set; }
        // Travel
        public DateTime TravelDate { get; set; }
        public string Purpose { get; set; }
        public string FromLocation { get; set; }
        public string ToLocation { get; set; }
        public int KmRun { get; set; }
        public decimal TravelAmount { get; set; }
        public string TravelRemarks { get; set; }

        // Food
        public decimal Breakfast { get; set; }
        public decimal Lunch { get; set; }
        public decimal Dinner { get; set; }
        public decimal FoodAmount { get; set; }

        // Expense
        public decimal? TollAmount { get; set; }
        public decimal? AutoAmount { get; set; }
        public decimal? OtherAmount { get; set; }
        public string ExpenseRemarks { get; set; }
    }
}