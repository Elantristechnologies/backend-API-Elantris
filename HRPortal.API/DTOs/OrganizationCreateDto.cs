namespace HRPortal.API.DTOs
//{
//    public class OrganizationCreateDto
//    {
//        public string? OrganizationName { get; set; }

//        public string? OrganizationCode { get; set; }

//        public string? Email { get; set; }

//        public string? Phone { get; set; }

//        public string? Address { get; set; }

//    }
//}
{ 
   public class OrganizationCreateDto
{
    public string OrgName { get; set; }

    public string AddressStreet1 { get; set; }

    public string AddressStreet2 { get; set; }

    public string City { get; set; }

    public string PostalCode { get; set; }

    public string StateId { get; set; }

    public string CountryId { get; set; }

    public string IndustryId { get; set; }

    public string UserId { get; set; }

    public string GSTNo { get; set; }

    public string TimezoneId { get; set; }

    public string CurrencyId { get; set; }

    public string DBName { get; set; }

    public string? SelectedModules { get; set; }

    public string? RequestedModules { get; set; }
}
}