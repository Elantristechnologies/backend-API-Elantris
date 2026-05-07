
using ClosedXML.Excel;
using DocumentFormat.OpenXml.InkML;
using HRPortal.API.Data;
using HRPortal.API.DTOs;
using HRPortal.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Data.Common;
namespace HRPortal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TravelMasterController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TravelMasterController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveVehicle(Vehicle vehicle)
        {
            try
            {
                if (vehicle == null)
                    return BadRequest("Invalid vehicle payload");

                vehicle.VehicleId = 0;

                _context.Vehicles.Add(vehicle);
                await _context.SaveChangesAsync();

                return Ok(vehicle);
            }
            catch (DbUpdateException dbEx)
            {
                return BadRequest(new
                {
                    message = "Database error saving vehicle",
                    detail = dbEx.InnerException?.Message ?? dbEx.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while saving vehicle",
                    error = ex.Message
                });
            }
        }


        [HttpGet("all")]
        public async Task<IActionResult> GetAllVehicles()
        {
            try
            {
                var vehicles = await _context.Vehicles.ToListAsync();

                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching vehicles",
                    error = ex.Message
                });
            }
        }
        // GET all purposes
        [HttpGet("purposes")]
        public async Task<IActionResult> GetPurposes()
        {
            try
            {
                var purposes = await _context.Purposes.ToListAsync();

                return Ok(purposes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching purposes",
                    error = ex.Message
                });
            }
        }

        // ADD purpose
        [HttpPost("all Purpose")]
        public async Task<IActionResult> AddPurpose(Purpose purpose)
        {
            try
            {
                _context.Purposes.Add(purpose);
                await _context.SaveChangesAsync();

                return Ok(purpose);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while adding purpose",
                    error = ex.Message
                });
            }
        }

        // DELETE purpose
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePurpose(int id)
        {
            try
            {
                var purpose = await _context.Purposes.FindAsync(id);

                if (purpose == null)
                    return NotFound();

                _context.Purposes.Remove(purpose);
                await _context.SaveChangesAsync();

                return Ok("Purpose deleted");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while deleting purpose",
                    error = ex.Message
                });
            }
        }

        [HttpPost("create-claim")]
        public async Task<IActionResult> CreateClaim(ClaimMaster claim)
        {
            try
            {
                _context.ClaimMasters.Add(claim);
                await _context.SaveChangesAsync();

                return Ok(claim);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while creating claim",
                    error = ex.Message
                });
            }
        }

        //[HttpPost("add-travel")]
        //public async Task<IActionResult> AddTravel(ClaimTravel travel)
        //{
        //    _context.ClaimTravels.Add(travel);
        //    await _context.SaveChangesAsync();

        //    return Ok(travel);
        //}


        [HttpPost("add-travel")]
        public async Task<IActionResult> AddTravel(ClaimTravel travel)
        {
            try
            {
                travel.TravelId = 0;

                travel.FromLocation = travel.FromLocation ?? "-";
                travel.ToLocation = travel.ToLocation ?? "-";
                travel.Purpose = travel.Purpose ?? "-";
                travel.Remarks = travel.Remarks ?? "-";

                _context.ClaimTravels.Add(travel);
                await _context.SaveChangesAsync();

                return Ok(travel);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = "An error occurred while adding travel claim",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        [HttpPost("add-food")]
        public async Task<IActionResult> AddFood(ClaimFood food)
        {
            try
            {
                _context.ClaimFoods.Add(food);
                await _context.SaveChangesAsync();

                return Ok(food);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while adding food claim",
                    error = ex.Message
                });
            }
        }

        [HttpPost("add-expense")]
        public async Task<IActionResult> AddExpense(ClaimExpense expense)
        {
            try
            {
                _context.ClaimExpenses.Add(expense);
                await _context.SaveChangesAsync();

                return Ok(expense);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while adding expense claim",
                    error = ex.Message
                });
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("Invalid file");

                var folder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var filePath = Path.Combine(folder, file.FileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return Ok(new
                {
                    path = file.FileName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while uploading file",
                    error = ex.Message
                });
            }
        }


        [HttpGet("month/{month}")]
        public async Task<IActionResult> GetClaimsByMonth(string month)
        {
            try
            {
                var data = await _context.ClaimMasters
                    .Where(x => x.ClaimMonth.ToString().Contains(month))
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching claims by month.",
                    Error = ex.Message
                });
            }
        }


        [HttpGet("get-claims")]
        public async Task<IActionResult> GetClaims(int claimId, DateTime date)
        {
            try
            {
                var claims = await _context.ClaimTravels
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                var filtered = claims
                    .Where(x => x.TravelDate.HasValue &&
                                x.TravelDate.Value.Date == date.Date)
                    .ToList();

                return Ok(filtered);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching claims.",
                    Error = ex.Message
                });
            }
        }
        [HttpGet("get-claims-by-year")]
        public async Task<IActionResult> GetClaimsByYear(int year)
        {
            try
            {
                var data = await _context.ClaimMasters
                    .Where(x => x.ClaimMonth.Year == year)
                    .Select(x => new
                    {
                        claimId = x.ClaimId,
                        claimMonth = x.ClaimMonth,
                        createdAt = x.CreatedAt,
                        totalAmount = 0,
                        approver1 = "Pending",
                        accountsStatus = "Pending"
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching claims by year.",
                    Error = ex.Message
                });
            }
        }

        [HttpDelete("delete-claim/{id}")]
        public async Task<IActionResult> DeleteClaim(int id)
        {
            try
            {
                var claim = await _context.TravelClaims.FindAsync(id);

                if (claim == null)
                    return NotFound("Claim not found");

                _context.TravelClaims.Remove(claim);
                await _context.SaveChangesAsync();

                return Ok("Claim deleted successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while deleting the claim.",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("total-expense")]
        public async Task<IActionResult> GetTotalExpense(int empId, DateTime date)
        {
            try
            {
                var data = await _context.TravelClaims
                    .Where(x => x.EmpId == empId && x.Date.Date == date.Date)
                    .ToListAsync();

                var result = new
                {
                    Car = data.Where(x => x.VehType == "Car").Sum(x => x.Amount),
                    Bike = data.Where(x => x.VehType == "Bike").Sum(x => x.Amount),
                    BusTaxi = data.Where(x => x.VehType == "Bus/Taxi").Sum(x => x.Amount),
                    Food = data.Sum(x => x.Food),
                    TollCharges = data.Sum(x => x.TollCharge),
                    Auto = data.Sum(x => x.Auto),
                    Others = data.Sum(x => x.Others),
                    Total = data.Sum(x => x.Amount + x.Food + x.TollCharge + x.Auto + x.Others)
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching total expense.",
                    Error = ex.Message
                });
            }
        }


        [HttpPut("update-claim/{id}")]
        public async Task<IActionResult> UpdateClaim(int id, [FromBody] TravelClaim updatedClaim)
        {
            try
            {
                if (id != updatedClaim.ClaimId)
                    return BadRequest("Claim ID mismatch");

                var claim = await _context.TravelClaims
                    .FirstOrDefaultAsync(x => x.ClaimId == id);

                if (claim == null)
                    return NotFound("Claim not found");

                // Update fields
                claim.Date = updatedClaim.Date;
                claim.Purpose = updatedClaim.Purpose;
                claim.VehType = updatedClaim.VehType;
                claim.FromLoc = updatedClaim.FromLoc;
                claim.ToLoc = updatedClaim.ToLoc;
                claim.KmRun = updatedClaim.KmRun;
                claim.Amount = updatedClaim.Amount;
                claim.Food = updatedClaim.Food;
                claim.TollCharge = updatedClaim.TollCharge;
                claim.Auto = updatedClaim.Auto;
                claim.Others = updatedClaim.Others;

                await _context.SaveChangesAsync();

                return Ok("Claim updated successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while updating the claim.",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("get-claim/{id}")]
        public async Task<IActionResult> GetClaim(int id)
        {
            try
            {
                var claim = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x => x.ClaimId == id);

                if (claim == null)
                    return NotFound("Claim not found");

                return Ok(claim);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching the claim.",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("get-drafts")]
        public async Task<IActionResult> GetDraftClaims()
        {
            try
            {
                var data = await _context.DraftClaims
                    .OrderByDescending(x => x.DraftId)
                    .Select(x => new
                    {
                        x.DraftId,
                        x.Year,
                        x.ClaimNo,
                        ClaimDateMonth = x.ClaimDateMonth.ToString("dd-MMMM-yyyy"),
                        x.Amount,
                        x.Status
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching draft claims.",
                    Error = ex.Message
                });
            }
        }

        [HttpDelete("delete-draft/{id}")]
        public async Task<IActionResult> DeleteDraft(int id)
        {
            try
            {
                var draft = await _context.DraftClaims.FindAsync(id);

                if (draft == null)
                    return NotFound("Draft not found");

                _context.DraftClaims.Remove(draft);
                await _context.SaveChangesAsync();

                return Ok("Draft deleted successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while deleting the draft.",
                    Error = ex.Message
                });
            }
        }

        //[HttpPost("create-local-purchase")]
        //public async Task<IActionResult> CreateLocalPurchase(LocalPurchase purchase)
        //{
        //    var nextClaim = await _context.LocalPurchases
        //        .Where(x => x.Year == purchase.Year)
        //        .MaxAsync(x => (int?)x.ClaimNo) ?? 0;

        //    purchase.ClaimNo = nextClaim + 1;

        //    _context.LocalPurchases.Add(purchase);
        //    await _context.SaveChangesAsync();

        //    return Ok(purchase);
        //}

        //[Authorize]
        [HttpPost("add-local-purchase")]
        public async Task<IActionResult> AddLocalPurchase(LocalPurchase model)
        {
            try
            {
                _context.LocalPurchases.Add(model);
                await _context.SaveChangesAsync();

                return Ok(model);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //[Authorize]
        [HttpGet("get-all-local-purchases")]
        public async Task<IActionResult> GetAllLocalPurchases()
        {
            try
            {
                var data = await _context.LocalPurchases
                    .OrderByDescending(x => x.PurchaseId)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //[Authorize]
        [HttpGet("get-local-purchase/{id}")]
        public async Task<IActionResult> GetLocalPurchase(int id)
        {
            try
            {
                var data = await _context.LocalPurchases.FindAsync(id);

                if (data == null)
                    return NotFound("Data not found");

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //[Authorize]
        [HttpPut("update-local-purchase/{id}")]
        public async Task<IActionResult> UpdateLocalPurchase(int id, LocalPurchase purchase)
        {
            try
            {
                var data = await _context.LocalPurchases.FindAsync(id);

                if (data == null)
                    return NotFound("Data not found");

                data.CustomerName = purchase.CustomerName;
                data.Location = purchase.Location;
                data.Amount = purchase.Amount;
                data.Remarks = purchase.Remarks;

                await _context.SaveChangesAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //[Authorize]
        [HttpDelete("delete-local-purchase/{id}")]
        public async Task<IActionResult> DeleteLocalPurchase(int id)
        {
            try
            {
                var data = await _context.LocalPurchases.FindAsync(id);

                if (data == null)
                    return NotFound("Data not found");

                _context.LocalPurchases.Remove(data);
                await _context.SaveChangesAsync();

                return Ok("Deleted successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //[Authorize]
        [HttpGet("get-local-purchase-by-year/{year}")]
        public async Task<IActionResult> GetLocalPurchaseByYear(int year)
        {
            try
            {
                var data = await _context.LocalPurchases
                    .Where(x => x.Year == year)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }


        //---------------Expense Tracker------------------------------//



        // Add Received Amount
        //[HttpPost("received")]
        //public async Task<IActionResult> AddReceived(ReceivedAmount model)
        //{
        //    _context.ReceivedAmounts.Add(model);
        //    await _context.SaveChangesAsync();
        //    return Ok(model);
        //}

        //[Authorize]
        //[HttpPost("received")]
        //public async Task<IActionResult> AddReceived(ReceivedAmount model)
        //{
        //    model.Id = 0;

        //    var adminId = User.FindFirst("id")?.Value;
        //    model.Admin_Id = Convert.ToInt32(adminId);

        //    _context.ReceivedAmounts.Add(model);
        //    await _context.SaveChangesAsync();
        //    return Ok(model);

        //}

        //--- This is add Amount popup Api ------

        [HttpGet("GetEmployees")]
        public IActionResult GetEmployees()
        {
            try
            {
                var employees = _context.EmployeeMaster
       .Select(emp => new EmployeemasterDto
       {
           EmpId = emp.EmpId,
           EmpCode = emp.EmployeeCode,
           FullName = emp.FullName
       })
       .ToList();

                return Ok(employees);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching employee data",
                    error = ex.Message
                });
            }
        }

        [HttpPost("expense-master-Add_amount")]
        public async Task<IActionResult> ExpenseMasterApi([FromBody] ExpenseMaster model)
        {
            try
            {
                // GET
                if (model.Action?.ToUpper() == "GET")
                {
                    var data = await _context.ExpenseMaster
                        .FromSqlRaw("EXEC SP_ExpenseMaster @Action='GET'")
                        .ToListAsync();

                    return Ok(new
                    {
                        message = "Data fetched successfully",
                        data = data
                    });
                }

                // INSERT
                else if (model.Action?.ToUpper() == "INSERT")
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "EXEC SP_ExpenseMaster @Action='INSERT', @AssignedBy={0}, @AssignedTo={1}, @Amount={2}, @Purpose={3}, @Remarks={4},@CreatedDate={5}",
                        model.AssignedBy,
                        model.AssignedTo,
                        model.ExpenseAmount,
                        model.Purpose,
                        model.Remarks,
                        model.CreatedDate
                    );

                    return Ok(new
                    {
                        message = "Inserted Successfully"
                    });
                }

                else
                {
                    return BadRequest("Invalid Action");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        [HttpGet("received")]
        public async Task<IActionResult> GetReceived()
        {
            try
            {
                var data = await _context.ReceivedAmounts
                    .Where(x => x.IsActive == true)
                    .OrderByDescending(x => x.Id)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching received amounts.",
                    Error = ex.Message
                });
            }
        }

        //[Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteReceived(int id)
        {
            try
            {
                var data = await _context.ReceivedAmounts.FindAsync(id);

                if (data == null)
                    return NotFound("Data not found");

                data.IsActive = false;   // soft delete
                await _context.SaveChangesAsync();

                return Ok("Deleted successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }


        //[Au[Authorize]
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(int designationId, int employeeId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;

                bool action = await _context.CompanyexpenseAccesses
                    .Where(x => x.DesignationId == designationId)
                    .Select(x => x.Action)
                    .FirstOrDefaultAsync();

                decimal monthReceived = 0;
                decimal yearReceived = 0;

                // Advance / ExpenseMaster Summary
                if (!action) // Full Access
                {
                    monthReceived = await _context.ExpenseMaster
                        .Where(x => x.CreatedDate.Month == currentMonth &&
                                    x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

                    yearReceived = await _context.ExpenseMaster
                        .Where(x => x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
                }
                else // Own Data Only
                {
                    monthReceived = await _context.ExpenseMaster
                        .Where(x => x.AssignedTo == employeeId)
                        .Where(x => x.CreatedDate.Month == currentMonth &&
                                    x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

                    yearReceived = await _context.ExpenseMaster
                        .Where(x => x.AssignedTo == employeeId)
                        .Where(x => x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
                }

                decimal monthExpense = 0;
                decimal yearExpense = 0;

                // Spent Summary
                if (!action) // Full Access
                {
                    monthExpense = await _context.SpentAmounts
                        .Where(x => x.IsActive == true)
                        .Where(x => x.Date.Month == currentMonth &&
                                    x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;

                    yearExpense = await _context.SpentAmounts
                        .Where(x => x.IsActive == true)
                        .Where(x => x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;
                }
                else // Own Data Only
                {
                    monthExpense = await _context.SpentAmounts
                        .Where(x => x.IsActive == true &&
                                    x.employee_id == employeeId)
                        .Where(x => x.Date.Month == currentMonth &&
                                    x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;

                    yearExpense = await _context.SpentAmounts
                        .Where(x => x.IsActive == true &&
                                    x.employee_id == employeeId)
                        .Where(x => x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;
                }

                //var monthBalance = monthReceived - monthExpense;
                //var yearBalance = yearReceived - yearExpense;

                var monthBalance = (monthReceived - monthExpense) < 0
    ? (monthReceived - monthExpense)
    : 0;

                var yearBalance = (yearReceived - yearExpense) < 0
                    ? (yearReceived - yearExpense)
                    : 0;

                return Ok(new
                {
                    MonthAdvance = monthReceived,
                    YearAdvance = yearReceived,
                    MonthSpent = monthExpense,
                    YearSpent = yearExpense,
                    MonthBalance = monthBalance,
                    YearBalance = yearBalance
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }



        [HttpGet("spent")]
        public async Task<IActionResult> GetSpents()
        {
            try
            {
                var data = await _context.SpentAmounts
                    .Where(x => x.IsActive)
                    .OrderByDescending(x => x.Date)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while fetching spent amounts.",
                    Error = ex.Message
                });
            }
        }


        //[HttpGet("spent_new")]
        //public async Task<IActionResult> GetSpent()
        //{
        //    try
        //    {
        //        var data = await _context.SpentDetailsDto//ipo breakpoint vechu check
        //            .FromSqlRaw("SELECT * FROM dbo.get_spent_details_new()")
        //            .ToListAsync();

        //        return Ok(data);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "Error fetching spent details",
        //            error = ex.Message,
        //            inner = ex.InnerException?.Message
        //        });
        //    }
        //}
        [HttpGet("spent_new")]
        public async Task<IActionResult> GetSpent()
        {
            try
            {
                var data = await _context.SpentDetailsDto
                    .FromSqlRaw("SELECT * FROM dbo.get_spent_details_new()")
                    .ToListAsync();

                var result = data.Select(x => new
                {
                    x.Id,
                    x.Date,
                    x.Amount,
                    x.Remarks,
                    x.Purchase_Item,
                    x.EmpCode,
                    x.EmployeeName,
                    x.EmployeeID,
                    x.PurchaseTypeName,

                    // ✅ Correct mapping
                    BillUrl = string.IsNullOrEmpty(x.billFile)
                        ? null
                        : $"/api/bill/{x.billFile}"
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching spent details",
                    error = ex.Message
                });
            }
        }


        [HttpGet("bill/{fileName}")]
        public IActionResult GetBill(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    return BadRequest("Invalid file name");

                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Uploads", 
                    "Bills",
                    fileName);

                if (!System.IO.File.Exists(path))
                    return NotFound("File not found");

                // Optional: content type detect
                var contentType = "application/octet-stream";

                if (fileName.EndsWith(".pdf"))
                    contentType = "application/pdf";
                else if (fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg"))
                    contentType = "image/jpeg";
                else if (fileName.EndsWith(".png"))
                    contentType = "image/png";

                return PhysicalFile(path, contentType, fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error while fetching file",
                    error = ex.Message
                });
            }
        }


        ///-------Accees Controll Api -----///
        //[Authorize]
        //[Authorize]
        ///dei idhu irukatum apdye...enaku spent_access mtum thiruba podu podre   ro
        ///
        [HttpGet("Total_Access")]
        public async Task<IActionResult> GetTotalAccess(int designationId, int employeeId)
        {
            try
            {
                var spent = await _context.SpentDetails
                    .FromSqlRaw(
                        "EXEC dbo.get_spent_details_with_access @DesignationId, @EmployeeId",
                        new SqlParameter("@DesignationId", designationId),
                        new SqlParameter("@EmployeeId", employeeId)
                    )
                    .ToListAsync();

                var advance = await _context.ExpenseMaster
                    .FromSqlRaw(
                        "EXEC dbo.get_advance_details_with_access @DesignationId, @EmployeeId",
                        new SqlParameter("@DesignationId", designationId),
                        new SqlParameter("@EmployeeId", employeeId)
                    )
                    .ToListAsync();

                return Ok(new
                {
                    spentDetails = spent,
                    advanceDetails = advance
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message
                });
            }
        }


        [HttpGet("spent_Access")]
        public async Task<IActionResult> SpentAccess(int designationId, int employeeId)
        {
            try
            {
                var spent = await _context.SpentDetails
                    .FromSqlRaw(
                        "EXEC dbo.get_spent_details_with_access @DesignationId, @EmployeeId",
                        new SqlParameter("@DesignationId", designationId),
                        new SqlParameter("@EmployeeId", employeeId)
                    )
                    .ToListAsync();

                return Ok(new
                {
                    spentDetails = spent
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message
                });
            }
        }


        [HttpDelete("spent/{id}")]
        public async Task<IActionResult> DeleteSpent(int id)
        {
            try
            {
                var data = await _context.SpentAmounts.FindAsync(id);

                if (data == null)
                    return NotFound("Spent record not found");

                data.IsActive = false; // soft delete

                await _context.SaveChangesAsync();

                return Ok("Spent record deleted successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An error occurred while deleting the spent record.",
                    Error = ex.Message
                });
            }
        }

        //   [HttpPost("spent_amount_add")]
        //   public async Task<IActionResult> AddSpent(
        //[FromForm] IFormFile file,
        //[FromForm] SpentAmount model)
        //   {
        //       try
        //       {
        //           // Excel Upload
        //           if (file != null && file.Length > 0)
        //           {
        //               using var stream = new MemoryStream();
        //               await file.CopyToAsync(stream);

        //               using var workbook = new XLWorkbook(stream);
        //               var ws = workbook.Worksheet(1);

        //               int row = 2;

        //               while (!ws.Cell(row, 1).IsEmpty())
        //               {
        //                   var item = new SpentAmount();

        //                   item.Date = ws.Cell(row, 1).GetDateTime();

        //                   // Purchase Type Column B
        //                   string purchaseTypeName = ws.Cell(row, 2).GetString();

        //                   item.PurchaseItem = _context.PurchaseTypes
        //                       .Where(x => x.Name == purchaseTypeName)
        //                       .Select(x => x.Id)
        //                       .FirstOrDefault();

        //                   item.Amount = Convert.ToDecimal(ws.Cell(row, 3).Value.ToString());

        //                   item.BillFile = ws.Cell(row, 4).GetString();

        //                   item.Remarks = ws.Cell(row, 5).GetString();

        //                   item.employee_id = model.employee_id;

        //                   item.IsActive = true;

        //                   _context.SpentAmounts.Add(item);

        //                   row++;
        //               }

        //               await _context.SaveChangesAsync();

        //               return Ok("Excel Uploaded Successfully");
        //           }

        //           // Single Form Save
        //           model.IsActive = true;

        //           _context.SpentAmounts.Add(model);
        //           await _context.SaveChangesAsync();

        //           return Ok(model);
        //       }
        //       catch (Exception ex)
        //       {
        //           return BadRequest(ex.Message);
        //       }
        //   }

        //[HttpGet("spent-template")]
        //public async Task<IActionResult> DownloadSpentTemplate()
        //{
        //    try
        //    {
        //        using var workbook = new XLWorkbook();

        //        // Main Sheet
        //        var ws = workbook.Worksheets.Add("Expense");

        //        ws.Cell("A1").Value = "Date";
        //        ws.Cell("B1").Value = "Purchase Type";
        //        ws.Cell("C1").Value = "Amount";
        //        ws.Cell("D1").Value = "Bill File";
        //        ws.Cell("E1").Value = "Remarks";

        //        ws.Row(1).Style.Font.Bold = true;

        //        // Date Format
        //        ws.Range("A2:A500").Style.NumberFormat.Format = "dd/MM/yyyy";

        //        // Purchase Type Values from DB
        //        var purchaseTypes = await _context.PurchaseTypes
        //            .OrderBy(x => x.Name)
        //            .ToListAsync();

        //        // Hidden Sheet for Dropdown
        //        var listSheet = workbook.Worksheets.Add("Lists");

        //        for (int i = 0; i < purchaseTypes.Count; i++)
        //        {
        //            listSheet.Cell(i + 1, 1).Value = purchaseTypes[i].Name;
        //        }

        //        listSheet.Hide();

        //        // Dropdown in Column B
        //        var validation = ws.Range("B2:B500").CreateDataValidation();
        //        validation.List(listSheet.Range($"A1:A{purchaseTypes.Count}"));

        //        // Auto Width
        //        ws.Columns().AdjustToContents();

        //        using var stream = new MemoryStream();
        //        workbook.SaveAs(stream);
        //        stream.Position = 0;

        //        return File(
        //            stream.ToArray(),
        //            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        //            "SpentTemplate.xlsx"
        //        );
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(ex.Message);
        //    }
        //}

        // ================================
        // 1. SINGLE ADD EXPENSE
        // POST: api/spent
        // ================================
        //[HttpPost("spent")]
        //public async Task<IActionResult> AddSpent([FromBody] SpentAmount model)
        //{
        //    try
        //    {
        //        model.IsActive = true;

        //        _context.SpentAmounts.Add(model);
        //        await _context.SaveChangesAsync();

        //        return Ok(model);
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(ex.Message);
        //    }
        //}
        [HttpPost("spent")]
        public async Task<IActionResult> AddSpent([FromForm] SpentCreateDto dto)
        {
            try
            {
                var model = new SpentAmount
                {
                    Date = dto.Date,
                    PurchaseItem = dto.PurchaseItem,
                    Amount = dto.Amount,
                    Remarks = dto.Remarks,
                    employee_id = dto.employee_id,
                    IsActive = true
                };

                // ✅ File Upload
                if (dto.billFile != null && dto.billFile.Length > 0)
                {
                    string folderPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Uploads",
                        "Bills");

                    // create folder if not exists
                    if (!Directory.Exists(folderPath))
                        Directory.CreateDirectory(folderPath);

                    // 🔹 Original file name (clean)
                    var originalName = Path.GetFileNameWithoutExtension(dto.billFile.FileName);
                    var extension = Path.GetExtension(dto.billFile.FileName);

                    // remove spaces & invalid chars
                    originalName = string.Concat(originalName.Split(Path.GetInvalidFileNameChars()))
                                         .Replace(" ", "_");

                    // 🔹 Employee ID (or EmpCode use pannalaam)
                    var empId = dto.employee_id;

                    // 🔹 Timestamp
                    var timeStamp = DateTime.Now.ToString("yyyyMMddHHmmss");

                    // 🔹 Extra safety (same second duplicate avoid)
                    var shortId = Guid.NewGuid().ToString().Substring(0, 4);

                    // ✅ Final file name format
                    // Example: xray_EMP101_20260504113055_ab12.jpg
                    string fileName = $"{originalName}_{empId}_{timeStamp}_{shortId}{extension}";

                    string fullPath = Path.Combine(folderPath, fileName);

                    // save file
                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await dto.billFile.CopyToAsync(stream);
                    }

                    // save to DB
                    model.BillFile = fileName;
                }

                _context.SpentAmounts.Add(model);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Spent added successfully",
                    data = model
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error while saving spent",
                    error = ex.Message
                });
            }
        }


        [HttpGet("spent-template")]
        public async Task<IActionResult> DownloadSpentTemplate()
        {
            try
            {
                using var workbook = new XLWorkbook();

                // ======================================
                // MAIN SHEET
                // ======================================
                var ws = workbook.Worksheets.Add("Expense");

                ws.Cell("A1").Value = "Date";
                ws.Cell("B1").Value = "Purchase Type";
                ws.Cell("C1").Value = "Amount";
                ws.Cell("D1").Value = "Bill File";
                ws.Cell("E1").Value = "Remarks";
                ws.Cell("F1").Value = "PurchaseTypeId";

                // ======================================
                // HEADER STYLE (YELLOW)
                // ======================================
                var headerRange = ws.Range("A1:F1");

                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontColor = XLColor.Black;
                headerRange.Style.Fill.BackgroundColor = XLColor.Yellow;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                ws.Row(1).Height = 25;

                // ======================================
                // DATE FORMAT
                // ======================================
                ws.Range("A2:A500").Style.DateFormat.Format = "dd-MM-yyyy";

                // ======================================
                // FETCH PURCHASE TYPES
                // ======================================
                var purchaseTypes = await _context.PurchaseTypes
                    .OrderBy(x => x.Name)
                    .ToListAsync();

                // ======================================
                // LIST SHEET
                // A = ID
                // B = NAME
                // ======================================
                var listSheet = workbook.Worksheets.Add("Lists");

                for (int i = 0; i < purchaseTypes.Count; i++)
                {
                    listSheet.Cell(i + 1, 1).Value = purchaseTypes[i].Id;
                    listSheet.Cell(i + 1, 2).Value = purchaseTypes[i].Name;
                }

                listSheet.Hide();

                // ======================================
                // DROPDOWN
                // ======================================
                var validation = ws.Range("B2:B500").CreateDataValidation();

                validation.IgnoreBlanks = true;
                validation.InCellDropdown = true;
                validation.ShowErrorMessage = true;
                validation.ErrorTitle = "Invalid Selection";
                validation.ErrorMessage = "Please select from dropdown only.";

                validation.List($"=Lists!$B$1:$B${purchaseTypes.Count}");

                // ======================================
                // AUTO FILL PURCHASE TYPE ID IN COLUMN F
                // ======================================
                for (int row = 2; row <= 500; row++)
                {
                    ws.Cell(row, 6).FormulaA1 =
                        $"=IFERROR(INDEX(Lists!A:A,MATCH(B{row},Lists!B:B,0)),\"\")";
                }

                // Hide Column F
                ws.Column("F").Hide();

                // ======================================
                // DATA AREA STYLE
                // ======================================
                var dataRange = ws.Range("A2:F500");

                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // Amount align right
                ws.Range("C2:C500").Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Right;

                // Remarks wrap text
                ws.Range("E2:E500").Style.Alignment.WrapText = true;

                // ======================================
                // COLUMN WIDTH
                // ======================================
                ws.Column("A").Width = 15;
                ws.Column("B").Width = 28;
                ws.Column("C").Width = 15;
                ws.Column("D").Width = 20;
                ws.Column("E").Width = 35;
                ws.Column("F").Width = 15;

                // Freeze header
                ws.SheetView.FreezeRows(1);

                // ======================================
                // RETURN FILE
                // ======================================
                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "SpentTemplate.xlsx");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("spent-upload")]
        public async Task<IActionResult> UploadSpentExcel([FromForm] SpentUploadRequest request)
        {
            try
            {
                if (request?.File == null || request.File.Length == 0)
                    return BadRequest("Please select excel file");

                using var stream = new MemoryStream();
                await request.File.CopyToAsync(stream);
                stream.Position = 0;

                using var workbook = new XLWorkbook(stream);
                var ws = workbook.Worksheet(1);

                int row = 2;
                int count = 0;

                while (!ws.Cell(row, 1).IsEmpty())
                {
                    var item = new SpentAmount();

                    // DATE
                    var dateText = ws.Cell(row, 1).GetValue<string>().Trim();

                    if (string.IsNullOrWhiteSpace(dateText))
                        return BadRequest($"Date missing at row {row}");

                    item.Date = Convert.ToDateTime(dateText);

                    // PURCHASE TYPE ID (Hidden Column F)
                    var purchaseTypeId = ws.Cell(row, 6).GetValue<int>();

                    if (purchaseTypeId == 0)
                        return BadRequest($"Invalid Purchase Type at row {row}");

                    item.PurchaseItem = purchaseTypeId;
                   // return;
                    // AMOUNT
                    var amountText = ws.Cell(row, 3).GetValue<string>().Trim();

                    if (string.IsNullOrWhiteSpace(amountText))
                        return BadRequest($"Amount missing at row {row}");

                    item.Amount = Convert.ToDecimal(amountText);

                    // BILL FILE
                    item.BillFile = ws.Cell(row, 4).GetValue<string>().Trim();

                    // REMARKS
                    item.Remarks = ws.Cell(row, 5).GetValue<string>().Trim();

                    item.employee_id = request.employee_id;
                    item.IsActive = true;

                    _context.SpentAmounts.Add(item);

                    count++;
                    row++;
                }

                await _context.SaveChangesAsync();

                return Ok($"Inserted Rows: {count}");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        //-------------api for purchaase type--------

        // GET: api/purchasetype
        // GET purchase types
        //[Authorize]
        [HttpGet("purchase-types")]
        public async Task<IActionResult> Get()
        {
            try
            {
                var data = await _context.PurchaseTypes
                                         .OrderBy(x => x.Name)
                                         .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        //[Authorize]
        [HttpPost("purchase-type")]
        public async Task<IActionResult> Post([FromBody] PurchaseType model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.Name))
                    return BadRequest("Name is required");

                _context.PurchaseTypes.Add(model);
                await _context.SaveChangesAsync();

                return Ok(model);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }



    }
}

