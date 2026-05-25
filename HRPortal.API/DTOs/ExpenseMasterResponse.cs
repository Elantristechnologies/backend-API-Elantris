using System.ComponentModel.DataAnnotations.Schema;

[NotMapped]
public class ExpenseMasterResponse
{
    public int ExpenseId { get; set; }

    public int AssignedBy { get; set; }

    public int AssignedTo { get; set; }
    [Column("Expense_Amount")]
    public decimal ExpenseAmount { get; set; }

    public string Purpose { get; set; }

    public string Remarks { get; set; }

    public string Status { get; set; }

    public DateTime CreatedDate { get; set; }

    public string credited_by_empcode { get; set; }

    public int? credited_by_empId { get; set; }

    public string credited_by_empname { get; set; }

    public string credited_to_empcode { get; set; }

    public int? credited_to_empId { get; set; }

    public string credited_to_empname { get; set; }
}