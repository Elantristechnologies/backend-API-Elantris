

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ExpenseMaster
{
    [Key]
    public int ExpenseId { get; set; }

    [NotMapped]
    public string Action { get; set; }

    public int? AssignedBy { get; set; }

    public int? AssignedTo { get; set; }

    [Column("Expense_Amount")]
    public decimal? ExpenseAmount { get; set; }

    public string? Purpose { get; set; }

    public string? Remarks { get; set; }

    public string? Status { get; set; }

    public int OrganizationId { get; set; }

    public DateTime CreatedDate { get; set; }

    [Column("AssignedByUserId")]
    public int? AssignedByUserId { get; set; }
}