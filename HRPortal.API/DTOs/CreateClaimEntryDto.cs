using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.DTOs
{
    public class CreateClaimEntryDto
    {
        // Vehicle
        public int VehicleTypeId { get; set; }

        public int FuelTypeId { get; set; }

        public int? VehicleDetailId { get; set; }

        public string VehicleNumber { get; set; }

        [Column("remarks")]
        public string? Remarks { get; set; }

        // Travel
        public int? TravelId { get; set; }
        public DateTime TravelDate { get; set; }

        public string Purpose { get; set; }

        public string FromLocation { get; set; }

        public string ToLocation { get; set; }

        public string? BusorTrainInvoiceNumber { get; set; }
        public string? TaxiInvoiceNumber { get; set; }

        public int KmRun { get; set; }

        public decimal TravelAmount { get; set; }

        public string TravelRemarks { get; set; }

        // Food
        public decimal Breakfast { get; set; }

        public decimal Lunch { get; set; }

        public decimal Dinner { get; set; }

        public decimal FoodAmount { get; set; }

        public string? BreakfastInvoiceNumber { get; set; }

        public string? LunchInvoiceNumber { get; set; }

        public string? DinnerInvoiceNumber { get; set; }

        // Expense
        public decimal? TollAmount { get; set; }

        public decimal? AutoAmount { get; set; }

        public decimal? OtherAmount { get; set; }

        public string ExpenseRemarks { get; set; }
    }
}