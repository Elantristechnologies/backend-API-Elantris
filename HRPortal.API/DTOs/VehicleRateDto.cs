namespace HRPortal.API.DTOs
{
    public class VehicleRateDto
    {
        public int VehicleTypeId { get; set; }

        public int FuelTypeId { get; set; }

        public decimal AmountPerLiter { get; set; }

        public decimal AmountPerKm { get; set; }

        public DateTime ValidFrom { get; set; }
    }
}
