using HRPortal.API.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("pay_elements")]
public class PayElement
{
    [Key]
    [Column("element_id")]
    public int ElementId { get; set; }

    [Column("head_id")]
    public int HeadId { get; set; }

    [Column("element_value")]
    public decimal ElementValue { get; set; }

    [Column("value_type")]
    public string ValueType { get; set; } = string.Empty;

    [Column("value_calculating")]
    public string? ValueCalculating { get; set; }

    [ForeignKey("HeadId")]
    public PayrollHead? PayrollHead { get; set; }
}