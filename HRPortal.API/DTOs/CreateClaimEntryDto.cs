using System.ComponentModel.DataAnnotations;

namespace HRPortal.API.DTOs
{
    public class CreateClaimEntryDto
    {
        // Vehicle
        public int VehicleTypeId { get; set; }

        public int? FuelTypeId { get; set; }

        public int? VehicleDetailId { get; set; }

        public string? VehicleNumber { get; set; }


        // Travel
        public int? TravelId { get; set; }

        public DateTime TravelDate { get; set; }

        public string Purpose { get; set; } = string.Empty;

        public string FromLocation { get; set; } = string.Empty;

        public string ToLocation { get; set; } = string.Empty;

        public int? KmRun { get; set; }

        public decimal TravelAmount { get; set; }


        // Food
        // 1 = Breakfast
        // 2 = Lunch
        // 3 = Dinner
        // 4 = Whole Day
        public int? FoodTypeId { get; set; }

        public decimal FoodAmount { get; set; }


        // Expense
        // Toll / Other
        public List<ClaimExpenseDto>? Expenses { get; set; }
    }
}