
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Organization")]
    public class OrganizationMaster
{
        [Key]
        [Column("org_id")]
        public int OrgId { get; set; }

        [Column("org_name")]
        public string OrgName { get; set; }

        [Column("address_street1")]
        public string AddressStreet1 { get; set; }

        [Column("address_street2")]
        public string AddressStreet2 { get; set; }

        [Column("city")]
        public string City { get; set; }

        [Column("postal_code")]
        public string PostalCode { get; set; }

        [Column("state_id")]
        public int? StateId { get; set; }

        [Column("country_id")]
        public int? CountryId { get; set; }

        [Column("industry_id")]
        public int? IndustryId { get; set; }

        [Column("user_id")]
        public string? UserId { get; set; }

        [Column("gst_no")]
        public string GSTNo { get; set; }

        [Column("timezone_id")]
        public int? TimezoneId { get; set; }

        [Column("currency_id")]
        public int? CurrencyId { get; set; }

        [Column("DB_name")]
        public string DBName { get; set; }
    }
}

//{
//    [Table("Organization_Master")]
//    public class OrganizationMaster
//    {
//        [Key]
//        public int OrganizationId { get; set; }

//        public string OrganizationName { get; set; }

//        public string? OrganizationCode { get; set; }

//        public string? Email { get; set; }

//        public string? Phone { get; set; }

//        public string? Address { get; set; }


//        public bool IsActive { get; set; } = true;

//        public DateTime CreatedDate { get; set; }


//    }
//}