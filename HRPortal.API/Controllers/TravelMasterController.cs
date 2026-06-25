
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
using System.Net;
namespace HRPortal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TravelMasterController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;




        public TravelMasterController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;

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

        //[HttpPost("create-claim")]
        //public async Task<IActionResult> CreateClaim([FromBody] ClaimMaster claim)
        //{
        //    try
        //    {
        //        // Default values
        //        claim.CreatedAt = DateTime.Now;
        //        claim.ClaimYear = DateTime.Now.Year;
        //        claim.ClaimNo = $"CLM-{DateTime.Now:yyyyMMddHHmmss}";
        //        claim.TotalAmount = 0;
        //        claim.Status = "Pending";
        //        claim.IsActive = true;

        //        _context.ClaimMasters.Add(claim);
        //        await _context.SaveChangesAsync();

        //        return Ok(new
        //        {
        //            message = "Claim created successfully",
        //            data = claim
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = ex.Message,
        //            innerError = ex.InnerException?.Message,
        //            stackTrace = ex.StackTrace
        //        });
        //    }
        //}

        [HttpPost("create-complete-claim")]
        public async Task<IActionResult> CreateCompleteClaim(CreateClaimEntryDto dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Create Claim
                var claim = new ClaimMaster
                {
                    EmployeeId = dto.EmployeeId,
                    ClaimMonth = dto.ClaimMonth,
                    VehicleTypeId = dto.VehicleTypeId,
                    FuelTypeId = dto.FuelTypeId,
                    VehicleNumber = dto.VehicleNumber,
                    CreatedAt = DateTime.Now,
                    IsActive = true
                };

                _context.ClaimMasters.Add(claim);
                await _context.SaveChangesAsync();

                // Generate Claim Number
                claim.ClaimNo = $"CLM-{DateTime.Now.Year}-{claim.ClaimId:D4}";
                claim.ClaimYear = DateTime.Now.Year;
                claim.Status = "Pending";

                await _context.SaveChangesAsync();

                // 2. Create Travel
                var travel = new ClaimTravel
                {
                    ClaimId = claim.ClaimId,
                    TravelDate = dto.TravelDate,
                    Purpose = dto.Purpose,
                    FromLocation = dto.FromLocation,
                    ToLocation = dto.ToLocation,
                    KmRun = dto.KmRun,
                    Amount = dto.TravelAmount,
                    Remarks = dto.TravelRemarks
                };

                _context.ClaimTravels.Add(travel);
                await _context.SaveChangesAsync();

                // 3. Create Food
                var food = new ClaimFood
                {
                    ClaimId = claim.ClaimId,
                    FoodDate = dto.TravelDate,
                    Breakfast = dto.Breakfast,
                    Lunch = dto.Lunch,
                    Dinner = dto.Dinner,
                    Amount = dto.FoodAmount
                };

                _context.ClaimFoods.Add(food);

                // 4. Create Expense (Single Row)
                var expense = new ClaimExpense
                {
                    ClaimId = claim.ClaimId,
                    TravelId = travel.TravelId,
                    TollAmount = dto.TollAmount,
                    AutoAmount = dto.AutoAmount,
                    OtherAmount = dto.OtherAmount,
                    ExpenseDate = dto.TravelDate,
                    Remarks = dto.ExpenseRemarks,
                    IsActive = true,
                    created_at = DateTime.Now
                };

                _context.ClaimExpenses.Add(expense);

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "Claim Created Successfully",
                    claimId = claim.ClaimId,
                    claimNo = claim.ClaimNo,
                    travelId = travel.TravelId
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    message = "Error while creating claim",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [HttpGet("get-all-claims")]
        public async Task<IActionResult> GetAllClaims()
        {
            try
            {
                var claims = await _context.Set<ClaimListDto>()
                    .FromSqlRaw("EXEC sp_GetAllClaims")
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    count = claims.Count,
                    data = claims
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching claims",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        //-----Dropdown api for vehicle types and fueltypes ------>
        [HttpGet("vehicle-types")]
        public async Task<IActionResult> GetVehicleTypes()
        {
            try
            {
                var vehicles = await _context.VehicleMaster
                    .Where(v => v.IsActive)
                    .OrderBy(v => v.VehicleName)
                    .Select(v => new
                    {
                        vehicleTypeId = v.VehicleId,
                        vehicleTypeName = v.VehicleName
                    })
                    .ToListAsync();

                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error while fetching vehicle types",
                    error = ex.Message
                });
            }
        }



        // Fuel Type dropdown
        // GET: /api/TravelMaster/fuel-types
        [HttpGet("fuel-types")]
        public async Task<IActionResult> GetFuelTypes()
        {
            try
            {
                var fuels = await _context.FuelTypeMasters
                    .Where(f => f.IsActive)
                    .OrderBy(f => f.FuelTypeName)
                    .Select(f => new
                    {
                        fuelTypeId = f.FuelTypeId,
                        fuelTypeName = f.FuelTypeName
                    })
                    .ToListAsync();

                return Ok(fuels);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error while fetching fuel types",
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


        //[HttpPost("add-travel")]
        //public async Task<IActionResult> AddTravel(ClaimTravel travel)
        //{
        //    try
        //    {
        //        travel.TravelId = 0;

        //        travel.FromLocation = travel.FromLocation ?? "-";
        //        travel.ToLocation = travel.ToLocation ?? "-";
        //        travel.Purpose = travel.Purpose ?? "-";
        //        travel.Remarks = travel.Remarks ?? "-";

        //        _context.ClaimTravels.Add(travel);
        //        await _context.SaveChangesAsync();

        //        return Ok(travel);
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(new
        //        {
        //            message = "An error occurred while adding travel claim",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}


        //[HttpPost("add-food")]
        //public async Task<IActionResult> AddFood(ClaimFood food)
        //{
        //    try
        //    {
        //        _context.ClaimFoods.Add(food);
        //        await _context.SaveChangesAsync();

        //        return Ok(food);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "An error occurred while adding food claim",
        //            error = ex.Message
        //        });
        //    }
        //}

        //[HttpPost("add-expense")]
        //public async Task<IActionResult> AddExpense(ClaimExpense expense)
        //{
        //    try
        //    {
        //        _context.ClaimExpenses.Add(expense);
        //        await _context.SaveChangesAsync();

        //        return Ok(expense);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "An error occurred while adding expense claim",    
        //            error = ex.Message
        //        });
        //    }
        //}

        //[HttpPost("add-claim-entry")]
        //public async Task<IActionResult> AddClaimEntry(CreateClaimEntryDto dto)
        //{
        //    await using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // 1. Validate Claim
        //        var claim = await _context.ClaimMasters
        //            .FirstOrDefaultAsync(x => x.ClaimId == dto.ClaimId);

        //        if (claim == null)
        //        {
        //            return BadRequest(new
        //            {
        //                message = "Invalid Claim Id"
        //            });
        //        }

        //        // 2. Travel Entry
        //        var travel = new ClaimTravel
        //        {
        //            ClaimId = dto.ClaimId,
        //            TravelDate = dto.TravelDate,
        //            Purpose = dto.Purpose,
        //            FromLocation = dto.FromLocation,
        //            ToLocation = dto.ToLocation,
        //            KmRun = dto.KmRun,
        //            Amount = dto.TravelAmount,
        //            Remarks = dto.TravelRemarks
        //        };

        //        _context.ClaimTravels.Add(travel);
        //        await _context.SaveChangesAsync(); // TravelId generated here

        //        // 3. Food Entry
        //        var food = new ClaimFood
        //        {
        //            ClaimId = dto.ClaimId,
        //            FoodDate = dto.TravelDate,
        //            Breakfast = dto.Breakfast,
        //            Lunch = dto.Lunch,
        //            Dinner = dto.Dinner,
        //            Amount = dto.FoodAmount
        //        };

        //        _context.ClaimFoods.Add(food);
        //        await _context.SaveChangesAsync();

        //        // 4. Expense Helper
        //        void AddExpense(decimal? amount, int typeId)
        //        {
        //            if (amount.HasValue && amount.Value > 0)
        //            {
        //                _context.ClaimExpenses.Add(new ClaimExpense
        //                {
        //                    ClaimId = dto.ClaimId,
        //                    TravelId = travel.TravelId,
        //                    Expense_type_id = typeId,
        //                    ExpenseDate = dto.TravelDate,
        //                    Amount = amount.Value,
        //                    Remarks = dto.ExpenseRemarks,
        //                    created_at = DateTime.Now,
        //                    IsActive = true
        //                });
        //            }
        //        }

        //        // 5. Expenses
        //        AddExpense(dto.TollAmount, 1);   // Toll
        //        AddExpense(dto.AutoAmount, 2);   // Auto
        //        AddExpense(dto.OtherAmount, 3);  // Others

        //        await _context.SaveChangesAsync();

        //        // 6. Commit Transaction
        //        await transaction.CommitAsync();

        //        return Ok(new
        //        {
        //            message = "Claim Entry Added Successfully",
        //            claimId = dto.ClaimId,
        //            travelId = travel.TravelId
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();

        //        return StatusCode(500, new
        //        {
        //            message = "Error while saving claim entry",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}

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


        [HttpPut("submit-claim/{claimId}")]
        public async Task<IActionResult> SubmitClaim(int claimId)
        {
            try
            {
                var claimMaster = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == claimId);

                if (claimMaster == null)
                {
                    return NotFound(new
                    {
                        message = "Claim not found"
                    });
                }

                claimMaster.Status = "Submitted";
                

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Claim submitted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
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



        //--- This is add Amount popup Api ------

        [Authorize]
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

        [Authorize]
        [HttpPost("expense-master-Add_amount")]
        public async Task<IActionResult> ExpenseMasterApi([FromBody] ExpenseMaster model)
        {
            try
            {
                // GET
                if (model.Action?.ToUpper() == "GET")
                {
                    var data = await _context.ExpenseMasterResponse
                        .FromSqlRaw("EXEC SP_ExpenseMaster @Action='GET'")               
                        .ToListAsync();
                    data = data.Where(x => x.Status == "Active").ToList();
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

        [Authorize]
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

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            try
            {
                // Find record using ExpenseId
                var data = await _context.ExpenseMaster
                    .FirstOrDefaultAsync(x => x.ExpenseId == id);

                // Check record exists or not
                if (data == null)
                    return NotFound("Expense not found");

                // Soft Delete
                data.Status = "Inactive";

                // Save changes
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Expense deleted successfully",
                    expenseId = data.ExpenseId,
                    status = data.Status
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


        [Authorize]
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
                        .Where(x => x.Status == "Active")
                        .Where(x => x.CreatedDate.Month == currentMonth &&
                                    x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

                    yearReceived = await _context.ExpenseMaster
                        .Where(x => x.Status == "Active")
                        .Where(x => x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
                }
                else // Own Data Only
                {
                    monthReceived = await _context.ExpenseMaster
                        .Where(x => x.AssignedTo == employeeId &&
                    x.Status == "Active")
                        .Where(x => x.CreatedDate.Month == currentMonth &&
                                    x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

                    yearReceived = await _context.ExpenseMaster
                        .Where(x => x.AssignedTo == employeeId &&
                    x.Status == "Active")
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


        [Authorize]
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





        [Authorize]
        [HttpPost("spent")]
        public async Task<IActionResult> AddSpent([FromForm] SpentCreateDto dto)
        {
            try
            {
                if (dto == null)
                    return BadRequest("DTO is null");

                var model = new SpentAmount
                {
                    Date = dto.Date,
                    PurchaseItem = dto.PurchaseItem,
                    Amount = dto.Amount,
                    Remarks = dto.Remarks,
                    employee_id = dto.employee_id,
                    IsActive = true
                };

                // FILE UPLOAD
                if (dto.billFile != null && dto.billFile.Length > 0)
                {
                    // SAFE WEBROOT
                    var webRoot = _environment.WebRootPath;

                    if (string.IsNullOrEmpty(webRoot))
                    {
                        webRoot = Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot");
                    }

                    // Folder Path
                    var folderPath = Path.Combine(
                        webRoot,
                        "Uploads",
                        "Bills");

                    // Create Folder
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                    // Unique File Name
                    var fileName = Guid.NewGuid().ToString()
                                 + Path.GetExtension(dto.billFile.FileName);

                    // Full File Path
                    var filePath = Path.Combine(folderPath, fileName);

                    // Save File
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.billFile.CopyToAsync(stream);
                    }

                    // DB Path
                    model.BillFile = $"Uploads/Bills/{fileName}";
                }

                _context.SpentAmounts.Add(model);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Spent added successfully",
                    file = model.BillFile
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
        [Authorize]
        [HttpGet("bill/{*fileName}")]
        public IActionResult GetBill(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    return BadRequest("Invalid file name");

                // 🔥 DECODE URL
                fileName = WebUtility.UrlDecode(fileName);

                // FULL PATH
                var path = Path.Combine(
                    _environment.WebRootPath,
                    fileName);

                // CHECK FILE
                if (!System.IO.File.Exists(path))
                {
                    return Ok(new
                    {
                        message = "File not found",
                        searchedPath = path
                    });
                }

                // CONTENT TYPE
                var extension = Path.GetExtension(fileName).ToLower();

                var contentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" => "image/jpeg",
                    ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => "application/octet-stream"
                };

                // VIEW
                return PhysicalFile(path, contentType);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

         [Authorize]
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

        ///-------Accees Controll Api -----///
        //[Authorize]
        //[Authorize]
        ///dei idhu irukatum apdye...enaku spent_access mtum thiruba podu podre   ro
        ///
        [Authorize]
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

        [Authorize]
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

        [Authorize]
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

        //-------------api for purchaase type--------

        // GET: api/purchasetype
        // GET purchase types
        [Authorize]
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

        [Authorize]
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

        //[Authorize]
        //[HttpGet("voucher")]
        //public async Task<IActionResult> GetVoucher(int employeeId, int month, int year)
        //{
        //    try
        //    {
        //        // 🔹 Employee Check
        //        var employee = await _context.EmployeeMasters
        //            .Where(x => x.EmpId == employeeId)
        //            .Select(x => x.FullName)
        //            .FirstOrDefaultAsync();

        //        if (employee == null)
        //        {
        //            return NotFound(new
        //            {
        //                Message = "Employee Not Found"
        //            });
        //        }

        //        // 🔹 Get Selected Month Data
        //        var spentData = await _context.SpentAmounts
        //            .Where(x =>
        //                x.employee_id == employeeId &&
        //                x.IsActive == true &&
        //                x.Date.Month == month &&
        //                x.Date.Year == year
        //            )
        //            .ToListAsync();

        //        // 🔹 No Data
        //        if (!spentData.Any())
        //        {
        //            return Ok(new
        //            {
        //                success = false,
        //                message = "No Voucher Found For Selected Month",
        //                data = (object)null
        //            });
        //        }

        //        // 🔹 Total Amount
        //        var totalAmount = spentData.Sum(x => x.Amount);

        //        // 🔹 Purchase Types
        //        var purchaseTypes = await (
        //            from s in _context.SpentAmounts

        //            join p in _context.PurchaseTypes
        //            on s.PurchaseItem equals p.Id

        //            where s.employee_id == employeeId
        //                  && s.IsActive == true
        //                  && s.Date.Month == month
        //                  && s.Date.Year == year

        //            select p.Name

        //        ).Distinct().ToListAsync();

        //        // 🔹 Convert List to String
        //        var towardsText = string.Join(", ", purchaseTypes);

        //        // 🔹 Final Response
        //        var result = new VoucherDto
        //        {
        //            PaidTo = employee,

        //            Amount = totalAmount,

        //            RupeesInWords = NumberToWords((long)totalAmount) + " Rupees Only",

        //            Towards = towardsText,

        //            Date = DateTime.Now
        //        };

        //        return Ok(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            Message = ex.Message
        //        });
        //    }
        //}

        [Authorize]
        [HttpGet("voucher")]
        public async Task<IActionResult> GetVoucher(int employeeId, int month, int year)
        {
            try
            {
                // Employee + Personal Details
                var employee = await (
                    from e in _context.EmployeeMasters
                    join p in _context.EmployeePersonalDetails
                        on e.EmpId equals p.EmpId
                    where e.EmpId == employeeId
                    select new
                    {
                        e.FullName,
                        p.Gender,
                        p.MaritalStatus
                    }
                ).FirstOrDefaultAsync();

                if (employee == null)
                {
                    return NotFound(new
                    {
                        Message = "Employee Not Found"
                    });
                }

                // Generate Title
                string title = "Mr";

                if (!string.IsNullOrEmpty(employee.Gender))
                {
                    var gender = employee.Gender.ToLower();
                    var maritalStatus = employee.MaritalStatus?.ToLower();

                    if (gender == "female" || gender == "f")
                    {
                        title = maritalStatus == "married"
                            ? "Mrs"
                            : "Ms";
                    }
                    else
                    {
                        title = "Mr";
                    }
                }

                // Get Selected Month Data
                var spentData = await _context.SpentAmounts
                    .Where(x =>
                        x.employee_id == employeeId &&
                        x.IsActive == true &&
                        x.Date.Month == month &&
                        x.Date.Year == year
                    )
                    .ToListAsync();

                if (!spentData.Any())
                {
                    return Ok(new
                    {
                        success = false,
                        message = "No Voucher Found For Selected Month",
                        data = (object)null
                    });
                }

                // Total Amount
                var totalAmount = spentData.Sum(x => x.Amount);

                // Purchase Types
                var purchaseTypes = await (
                    from s in _context.SpentAmounts
                    join p in _context.PurchaseTypes
                        on s.PurchaseItem equals p.Id
                    where s.employee_id == employeeId
                          && s.IsActive == true
                          && s.Date.Month == month
                          && s.Date.Year == year
                    select p.Name
                ).Distinct().ToListAsync();

                // Convert List To String
                var towardsText = string.Join(", ", purchaseTypes);

                // Final Response
                var result = new VoucherDto
                {
                    PaidTo = $"{title} {employee.FullName}",
                    Amount = totalAmount,
                    RupeesInWords = NumberToWords((long)totalAmount) + " Rupees Only",
                    Towards = towardsText,
                    Date = DateTime.Now
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = ex.Message
                });
            }
        }


        // 🔹 NUMBER TO WORDS METHOD
        private static string NumberToWords(long number)
        {
            if (number == 0)
                return "Zero";

            if (number < 0)
                return "Minus " + NumberToWords(Math.Abs(number));

            string words = "";

            if ((number / 1000000) > 0)
            {
                words += NumberToWords(number / 1000000) + " Million ";
                number %= 1000000;
            }

            if ((number / 1000) > 0)
            {
                words += NumberToWords(number / 1000) + " Thousand ";
                number %= 1000;
            }

            if ((number / 100) > 0)
            {
                words += NumberToWords(number / 100) + " Hundred ";
                number %= 100;
            }

            if (number > 0)
            {
                if (words != "")
                    words += "and ";

                string[] unitsMap =
                {
            "Zero","One","Two","Three","Four","Five","Six","Seven",
            "Eight","Nine","Ten","Eleven","Twelve","Thirteen",
            "Fourteen","Fifteen","Sixteen","Seventeen","Eighteen","Nineteen"
        };

                string[] tensMap =
                {
            "Zero","Ten","Twenty","Thirty","Forty","Fifty",
            "Sixty","Seventy","Eighty","Ninety"
        };

                if (number < 20)
                    words += unitsMap[number];
                else
                {
                    words += tensMap[number / 10];

                    if ((number % 10) > 0)
                        words += " " + unitsMap[number % 10];
                }
            }

            return words;
        }
    }


}


