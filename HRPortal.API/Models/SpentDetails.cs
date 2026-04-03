namespace HRPortal.API.Models
{
    public class SpentDetails
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal Amount { get; set; }

        public string Bill_File { get; set; }

        public string Remarks { get; set; }

        public DateTime Date { get; set; }

        public bool IsActive { get; set; }
    }
}
