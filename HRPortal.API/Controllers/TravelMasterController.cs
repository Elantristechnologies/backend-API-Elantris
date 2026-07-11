
using ClosedXML.Excel;
using DocumentFormat.OpenXml.InkML;
using HRPortal.API.Data;
using HRPortal.API.DTOs;
using HRPortal.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace HRPortal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TravelMasterController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;



        public TravelMasterController(ApplicationDbContext context, IWebHostEnvironment environment, IConfiguration configuration, HttpClient httpClient)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
            _httpClient = httpClient;
        }
        [HttpPost("save")]
        public async Task<IActionResult> SaveVehicle([FromBody] Vehicle vehicle)
        {
            try
            {
                if (vehicle == null)
                    return BadRequest("Invalid vehicle payload");

                if (vehicle.EmployeeId <= 0)
                    return BadRequest("EmployeeId is required.");

                // Check employee exists
                var employeeExists = await _context.EmployeeMaster
                    .AnyAsync(e => e.EmpId == vehicle.EmployeeId);

                if (!employeeExists)
                    return BadRequest("Invalid EmployeeId.");

                vehicle.VehicleId = 0;

                _context.Vehicles.Add(vehicle);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Vehicle saved successfully",
                    data = vehicle
                });
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

        [Authorize]
        [HttpGet("my-vehicles")]
        public async Task<IActionResult> GetMyVehicles()
        {
            try
            {
                //=========================================
                // Get Employee Id From JWT Token
                //=========================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;
                 
                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee Id not found in token."
                    });
                }

                int employeeId = Convert.ToInt32(empIdClaim);

                //=========================================
                // Get Employee Vehicles
                //=========================================

                var vehicles = await _context.Vehicles
                    .Where(v => v.EmployeeId == employeeId && v.IsActive)
                    .Select(v => new
                    {
                        v.VehicleId,
                        v.VehicleNumber,
                        v.VehicleType,
                        v.FuelType
                    })
                    .ToListAsync();

                if (!vehicles.Any())
                {
                    return Ok(new
                    {
                        success = true,
                        message = "No vehicles found.",
                        data = new List<object>()
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Vehicles fetched successfully.",
                    data = vehicles
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


        //-----------Travel form FROm ANd To localtion and calculate distonse//------//


        [HttpGet("location-search")]
        public async Task<IActionResult> SearchLocation(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return Ok(new List<LocationResponseDto>());

                var apiKey = _configuration["OpenRouteService:ApiKey"];

                var url =
                    $"https://api.openrouteservice.org/geocode/autocomplete" +
                    $"?text={Uri.EscapeDataString(text)}" +
                    $"&boundary.country=IND" +
                    $"&size=20";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                // IMPORTANT: OpenRouteService expects Authorization header as plain API key
                request.Headers.TryAddWithoutValidation("Authorization", apiKey);

                var response = await _httpClient.SendAsync(request);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = content
                    });
                }

                using JsonDocument doc = JsonDocument.Parse(content);

                var list = new List<LocationResponseDto>();

                foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
                {
                    var coord = feature.GetProperty("geometry").GetProperty("coordinates");
                    var prop = feature.GetProperty("properties");

                    list.Add(new LocationResponseDto
                    {
                        Name = prop.GetProperty("label").GetString(),
                        Latitude = coord[1].GetDouble(),
                        Longitude = coord[0].GetDouble()
                    });
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.ToString()
                });
            }
        }

        private int GetLocationPriority(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return 3;

            location = location.ToLower();

            if (location.Contains("tamil nadu"))
                return 1;

            if (location.Contains("chennai") ||
                location.Contains("coimbatore") ||
                location.Contains("madurai") ||
                location.Contains("salem") ||
                location.Contains("trichy") ||
                location.Contains("tiruchirappalli") ||
                location.Contains("vellore") ||
                location.Contains("erode") ||
                location.Contains("tiruppur") ||
                location.Contains("chengalpattu") ||
                location.Contains("cuddalore") ||
                location.Contains("kanchipuram") ||
                location.Contains("tambaram") ||
                location.Contains("guindy") ||
                location.Contains("velachery") ||
                location.Contains("anna nagar") ||
                location.Contains("adyar") ||
                location.Contains("t nagar"))
                return 1;

            return 2;
        }
        //-----------------ENd--------------------------------------//

        //-------------------Admin VehicleRate ditails-------------//

        [HttpPost("VehicleRateMasters")]
        public async Task<IActionResult> AddVehicleRate([FromBody] VehicleRateDto dto)
        {
            try
            {
                var empIdClaim = User.FindFirst("EmpId");

                if (empIdClaim == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee Id not found in token."
                    });
                }

                int empId = Convert.ToInt32(empIdClaim.Value);

                var exists = await _context.VehicleRateMasters.AnyAsync(x =>
                    x.VehicleTypeId == dto.VehicleTypeId &&
                    x.FuelTypeId == dto.FuelTypeId &&
                    x.ValidFrom.Date == dto.ValidFrom.Date &&
                    x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Vehicle rate already exists."
                    });
                }

                var vehicleRate = new VehicleRateMaster
                {
                    VehicleTypeId = dto.VehicleTypeId,
                    FuelTypeId = dto.FuelTypeId,
                    AmountPerLiter = dto.AmountPerLiter,
                    AmountPerKm = dto.AmountPerKm,
                    ValidFrom = dto.ValidFrom,
                    CreatedBy = empId,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };

                _context.VehicleRateMasters.Add(vehicleRate);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Vehicle rate added successfully."
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

        [HttpGet("VehicleRateMasters")]
        public async Task<IActionResult> GetVehicleRates()
        {
            try
            {
                
                var data = await (
                    from vr in _context.VehicleRateMasters
                    join v in _context.VehicleMaster
                        on vr.VehicleTypeId equals v.VehicleId
                    join f in _context.FuelTypeMasters
                        on vr.FuelTypeId equals f.FuelTypeId
                    where vr.IsActive
                    select new
                    {
                        vr.VehicleRateId,
                        vr.VehicleTypeId,
                        VehicleName = v.VehicleName,

                        vr.FuelTypeId,
                        FuelName = f.FuelTypeName,

                        vr.AmountPerLiter,
                        vr.AmountPerKm,
                        vr.ValidFrom,
                        vr.IsActive
                    }).ToListAsync();

                return Ok(new
                {
                    success = true,
                    data
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


        [HttpPut("VehicleRateMasters/{id}")]
        public async Task<IActionResult> UpdateVehicleRate(int id, [FromBody] VehicleRateDto dto)
        {
            try
            {
                var vehicleRate = await _context.VehicleRateMasters
                    .FirstOrDefaultAsync(x => x.VehicleRateId == id && x.IsActive);

                if (vehicleRate == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Vehicle Rate not found."
                    });
                }

                vehicleRate.VehicleTypeId = dto.VehicleTypeId;
                vehicleRate.FuelTypeId = dto.FuelTypeId;
                vehicleRate.AmountPerLiter = dto.AmountPerLiter;
                vehicleRate.AmountPerKm = dto.AmountPerKm;
                vehicleRate.ValidFrom = dto.ValidFrom;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Vehicle Rate updated successfully."
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


        [HttpDelete("VehicleRateMasters/{id}")]
        public async Task<IActionResult> DeleteVehicleRate(int id)
        {
            try
            {
                var vehicleRate = await _context.VehicleRateMasters
                    .FirstOrDefaultAsync(x => x.VehicleRateId == id && x.IsActive);

                if (vehicleRate == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Vehicle Rate not found."
                    });
                }

                vehicleRate.IsActive = false;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Vehicle Rate deleted successfully."
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
        //---------------------------End Admin Api vehicleRate--------------//


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



        //[HttpPost("create-complete-claim")]
        //public async Task<IActionResult> CreateCompleteClaim(CreateClaimEntryDto dto)
        //{
        //    await using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // 1. Create Claim
        //        var claim = new ClaimMaster
        //        {
        //            EmployeeId = dto.EmployeeId,
        //            ClaimMonth = dto.ClaimMonth,
        //            VehicleTypeId = dto.VehicleTypeId,
        //            FuelTypeId = dto.FuelTypeId,
        //            VehicleNumber = dto.VehicleNumber,
        //            CreatedAt = DateTime.Now,
        //            IsActive = true
        //        };

        //        _context.ClaimMasters.Add(claim);
        //        await _context.SaveChangesAsync();

        //        // Generate Claim Number
        //        claim.ClaimNo = $"CLM-{DateTime.Now.Year}-{claim.ClaimId:D4}";
        //        claim.ClaimYear = DateTime.Now.Year;
        //        claim.Status = "Pending";

        //        await _context.SaveChangesAsync();

        //        // 2. Create Travel
        //        var travel = new ClaimTravel
        //        {
        //            ClaimId = claim.ClaimId,
        //            TravelDate = dto.TravelDate,
        //            Purpose = dto.Purpose,
        //            FromLocation = dto.FromLocation,
        //            ToLocation = dto.ToLocation,
        //            KmRun = dto.KmRun,
        //            Amount = dto.TravelAmount,
        //            Remarks = dto.TravelRemarks
        //        };

        //        _context.ClaimTravels.Add(travel);
        //        await _context.SaveChangesAsync();

        //        // 3. Create Food
        //        var food = new ClaimFood
        //        {
        //            ClaimId = claim.ClaimId,
        //            FoodDate = dto.TravelDate,
        //            Breakfast = dto.Breakfast,
        //            Lunch = dto.Lunch,
        //            Dinner = dto.Dinner,
        //            Amount = dto.FoodAmount
        //        };

        //        _context.ClaimFoods.Add(food);

        //        // 4. Create Expense (Single Row)
        //        var expense = new ClaimExpense
        //        {
        //            ClaimId = claim.ClaimId,
        //            TravelId = travel.TravelId,
        //            TollAmount = dto.TollAmount,
        //            AutoAmount = dto.AutoAmount,
        //            OtherAmount = dto.OtherAmount,
        //            ExpenseDate = dto.TravelDate,
        //            Remarks = dto.ExpenseRemarks,
        //            IsActive = true,
        //            created_at = DateTime.Now
        //        };

        //        _context.ClaimExpenses.Add(expense);

        //        await _context.SaveChangesAsync();

        //        await transaction.CommitAsync();

        //        return Ok(new
        //        {
        //            message = "Claim Created Successfully",
        //            claimId = claim.ClaimId,
        //            claimNo = claim.ClaimNo,
        //            travelId = travel.TravelId
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();

        //        return StatusCode(500, new
        //        {
        //            message = "Error while creating claim",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}

        [HttpPost("create-claim")]
        public async Task<IActionResult> CreateClaim(
          [FromBody] CreateClaimDto dto,
          [FromQuery] bool isSubmit = false)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Request data is required."
                    });
                }

                if (dto.EmployeeId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid Employee."
                    });
                }

                if (dto.ClaimEntries == null || !dto.ClaimEntries.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "At least one claim entry is required."
                    });
                }

                // ===========================
                // Calculate Total Amount
                // ===========================

                decimal totalAmount = dto.ClaimEntries.Sum(x =>
                    x.TravelAmount +
                    x.FoodAmount +
                    (x.TollAmount ?? 0) +
                    (x.AutoAmount ?? 0) +
                    (x.OtherAmount ?? 0));

                // ===========================
                // Create Claim Master
                // ===========================

                var claim = new ClaimMaster
                {
                    EmployeeId = dto.EmployeeId,
                    ClaimMonth = dto.ClaimMonth,
                    ClaimYear = dto.ClaimMonth.Year,
                    CreatedAt = DateTime.Now,
                    LastModifiedAt = DateTime.Now,
                    TotalAmount = totalAmount,
                    IsActive = true,
                    Status = isSubmit ? "Submitted" : "Draft",
                    SubmittedAt = isSubmit ? DateTime.Now : null
                };

                _context.ClaimMasters.Add(claim);
                await _context.SaveChangesAsync();

                claim.ClaimNo = $"CLM-{DateTime.Now.Year}-{claim.ClaimId:D4}";
                await _context.SaveChangesAsync();

                // ===========================
                // Save All Entries
                // ===========================

                foreach (var entry in dto.ClaimEntries)
                {
                    var travel = new ClaimTravel
                    {
                        ClaimId = claim.ClaimId,

                        VehicleTypeId = entry.VehicleTypeId,
                        FuelTypeId = entry.FuelTypeId,
                        VehicleDetailId = entry.VehicleDetailId,
                        VehicleNumber = entry.VehicleNumber,

                        TravelDate = entry.TravelDate,
                        Purpose = entry.Purpose,
                        FromLocation = entry.FromLocation,
                        ToLocation = entry.ToLocation,
                        KmRun = entry.KmRun,
                        Amount = entry.TravelAmount,
                        Remarks = entry.TravelRemarks,
                        DocumentPath = string.Empty
                    };

                    _context.ClaimTravels.Add(travel);
                    await _context.SaveChangesAsync();

                    var food = new ClaimFood
                    {
                        TravelId = travel.TravelId,

                        ClaimId = claim.ClaimId,
                        FoodDate = entry.TravelDate,
                        Breakfast = entry.Breakfast,
                        Lunch = entry.Lunch,
                        Dinner = entry.Dinner,
                        Amount = entry.FoodAmount,
                        InvoiceNumber = string.Empty,
                        DocumentPath = string.Empty
                    };

                    _context.ClaimFoods.Add(food);
                    await _context.SaveChangesAsync();

                    var expense = new ClaimExpense
                    {
                        ClaimId = claim.ClaimId,
                        TravelId = travel.TravelId,

                        TollAmount = entry.TollAmount,
                        AutoAmount = entry.AutoAmount,
                        OtherAmount = entry.OtherAmount,

                        ExpenseDate = entry.TravelDate,
                        Remarks = entry.ExpenseRemarks,

                        DocumentPath = string.Empty,
                        created_at = DateTime.Now,
                        IsActive = true
                    };

                    _context.ClaimExpenses.Add(expense);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = isSubmit
                        ? "Claim submitted successfully."
                        : "Claim saved as draft successfully.",

                    data = new
                    {
                        claim.ClaimId,
                        claim.ClaimNo,
                        claim.Status,
                        claim.TotalAmount
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while saving claim.",
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

        [HttpGet("my-drafts/{empId}")]
        public async Task<IActionResult> GetMyDrafts(int empId)
        {
            try
            {
                var drafts = await _context.ClaimMasters
                    .Where(x =>
                        x.EmployeeId == empId &&
                        x.Status == "Draft" &&
                        x.IsActive == true)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => new
                    {
                        x.ClaimId,
                        x.ClaimNo,
                        x.ClaimMonth,
                        x.TotalAmount,
                        x.Status,
                        x.CreatedAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = drafts
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
        [HttpDelete("delete-draft/{claimId}")]
        public async Task<IActionResult> DeleteDraft(int claimId)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // ==================================
                // Get Draft Claim
                // ==================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.Status == "Draft" &&
                        x.IsActive == true);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft claim not found."
                    });
                }

                // ==================================
                // Soft Delete Claim Master
                // ==================================

                claim.IsActive = false;
                claim.LastModifiedAt = DateTime.Now;

                // ==================================
                // Soft Delete Travels
                // ==================================

                var travels = await _context.ClaimTravels
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                foreach (var travel in travels)
                {
                    travel.IsActive = false;
                }

                // ==================================
                // Soft Delete Foods
                // ==================================

                var foods = await _context.ClaimFoods
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                foreach (var food in foods)
                {
                    food.IsActive = false;
                }

                // ==================================
                // Soft Delete Expenses
                // ==================================

                var expenses = await _context.ClaimExpenses
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                foreach (var expense in expenses)
                {
                    expense.IsActive = false;
                }

                // ==================================
                // Save
                // ==================================

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Draft deleted successfully."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deleting draft.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [HttpGet("draft-details/{claimId}")]
        public async Task<IActionResult> GetDraftDetails(int claimId)
        {
            try
            {
// ===========================
// Claim Master
// ===========================


    var claim = await _context.ClaimMasters
        .FirstOrDefaultAsync(x =>
            x.ClaimId == claimId &&
            x.Status == "Draft" &&
            x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft not found."});
                }

                // ===========================
                // Get Travels
                // ===========================

                var travels = await _context.ClaimTravels
                    .Where(x => x.ClaimId == claimId)
                    .OrderBy(x => x.TravelId)
                    .ToListAsync();

                // ===========================
                // Get Documents
                // ===========================

                var documents = await _context.ClaimDocuments
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                // ===========================
                // Build Response
                // ===========================

                var claimEntries = new List<object>();

                foreach (var travel in travels)
                {
                    var food = await _context.ClaimFoods
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId);

                    var expense = await _context.ClaimExpenses
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId &&
                            x.IsActive);

                    claimEntries.Add(new
                    {
                        travel.TravelId,

                        travel.VehicleTypeId,
                        travel.FuelTypeId,
                        travel.VehicleDetailId,
                        travel.VehicleNumber,

                        travel.TravelDate,
                        travel.Purpose,
                        travel.FromLocation,
                        travel.ToLocation,
                        travel.KmRun,

                        TravelAmount = travel.Amount,
                        TravelRemarks = travel.Remarks,

                        Breakfast = food?.Breakfast ?? 0,
                        Lunch = food?.Lunch ?? 0,
                        Dinner = food?.Dinner ?? 0,
                        FoodAmount = food?.Amount ?? 0,

                        TollAmount = expense?.TollAmount ?? 0,
                        AutoAmount = expense?.AutoAmount ?? 0,
                        OtherAmount = expense?.OtherAmount ?? 0,
                        ExpenseRemarks = expense?.Remarks ?? "",

                        Documents = documents
                            .Where(d =>
                                d.ReferenceId == travel.TravelId)
                            .Select(d => new
                            {
                                d.DocumentId,
                                d.ModuleType,
                                d.DocumentType,
                                d.FilePath,
                                d.OriginalFileName
                            })
                            .ToList()
                    });
                }

                // ===========================
                // Response
                // ===========================

                return Ok(new
                {
                    success = true,

                    data = new
                    {
                        claimId = claim.ClaimId,
                        claimNo = claim.ClaimNo,
                        employeeId = claim.EmployeeId,
                        claimMonth = claim.ClaimMonth,
                        totalAmount = claim.TotalAmount,
                        status = claim.Status,

                        documents = documents.Select(d => new
                        {
                            d.DocumentId,
                            d.ReferenceId,
                            d.ModuleType,
                            d.DocumentType,
                            d.FilePath,
                            d.OriginalFileName
                        }),

                        claimEntries = claimEntries
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching draft details.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }


}

        [HttpPut("update-draft/{claimId}")]         
        public async Task<IActionResult> UpdateDraft(
      int claimId,
      [FromBody] CreateClaimDto dto)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.Status == "Draft" &&
                        x.IsActive == true);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft not found."
                    });
                }

                // =====================
                // Recalculate Total
                // =====================

                decimal totalAmount =
                    dto.ClaimEntries.Sum(x =>
                        x.TravelAmount +
                        x.FoodAmount +
                        (x.TollAmount ?? 0) +
                        (x.AutoAmount ?? 0) +
                        (x.OtherAmount ?? 0));

                // =====================
                // Update Claim Master
                // =====================

                claim.ClaimMonth = dto.ClaimMonth;
                claim.TotalAmount = totalAmount;
                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // =====================
                // Delete Old Rows
                // =====================

                var oldTravels = _context.ClaimTravels
                    .Where(x => x.ClaimId == claimId);

                _context.ClaimTravels.RemoveRange(oldTravels);

                var oldFoods = _context.ClaimFoods
                    .Where(x => x.ClaimId == claimId);

                _context.ClaimFoods.RemoveRange(oldFoods);

                var oldExpenses = _context.ClaimExpenses
                    .Where(x => x.ClaimId == claimId);

                _context.ClaimExpenses.RemoveRange(oldExpenses);

                await _context.SaveChangesAsync();

                // =====================
                // Insert New Rows
                // =====================

                foreach (var entry in dto.ClaimEntries)
                {
                    var travel = new ClaimTravel
                    {
                        ClaimId = claimId,

                        VehicleTypeId = entry.VehicleTypeId,
                        FuelTypeId = entry.FuelTypeId,
                        VehicleDetailId = entry.VehicleDetailId,
                        VehicleNumber = entry.VehicleNumber,

                        TravelDate = entry.TravelDate,
                        Purpose = entry.Purpose,
                        FromLocation = entry.FromLocation,
                        ToLocation = entry.ToLocation,
                        KmRun = entry.KmRun,
                        Amount = entry.TravelAmount,
                        Remarks = entry.TravelRemarks,
                        DocumentPath = string.Empty
                    };

                    _context.ClaimTravels.Add(travel);
                    await _context.SaveChangesAsync();

                    var food = new ClaimFood
                    {
             
                        ClaimId = claimId,
                        FoodDate = entry.TravelDate,
                        Breakfast = entry.Breakfast,
                        Lunch = entry.Lunch,
                        Dinner = entry.Dinner,
                        Amount = entry.FoodAmount,
                        InvoiceNumber = string.Empty,
                        DocumentPath = string.Empty
                    };

                    _context.ClaimFoods.Add(food);

                    var expense = new ClaimExpense
                    {
                        ClaimId = claimId,
                        TravelId = travel.TravelId,

                        TollAmount = entry.TollAmount,
                        AutoAmount = entry.AutoAmount,
                        OtherAmount = entry.OtherAmount,

                        ExpenseDate = entry.TravelDate,
                        Remarks = entry.ExpenseRemarks,

                        DocumentPath = string.Empty,
                        created_at = DateTime.Now,
                        IsActive = true
                    };

                    _context.ClaimExpenses.Add(expense);
                }

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Draft updated successfully."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while updating draft.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        [HttpPut("submit-draft/{claimId}")]
        public async Task<IActionResult> SubmitDraft(int claimId)
        {
            try
            {
                // ==========================================
                // Get Draft Claim
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.Status == "Draft" &&
                        x.IsActive == true);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft not found."
                    });
                }

                // ==========================================
                // Update Status
                // ==========================================

                claim.Status = "Submitted";

                claim.SubmittedAt = DateTime.Now;

                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Claim submitted successfully.",
                    data = new
                    {
                        claim.ClaimId,
                        claim.ClaimNo,
                        claim.Status,
                        claim.SubmittedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while submitting claim.",
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

        [HttpPost("upload-document")]
        public async Task<IActionResult> UploadDocument(
    [FromForm] UploadDocumentDto model)
        {
            try
            {
                // ==========================
                // Validation
                // ==========================

                if (model.File == null || model.File.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "File is required"
                    });
                }

                // ==========================
                // Get Claim Details
                // ==========================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == model.ClaimId);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found"
                    });
                }

                // ==========================
                // Create Claim Folder
                // Uploads/Claims/CLM20260001
                // ==========================

                var claimNumber = claim.ClaimNo;

                var folderPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "Uploads",
                    "Claims",
                    claimNumber);

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // ==========================
                // Generate File Name
                // ==========================

                var extension =
                    Path.GetExtension(model.File.FileName);

                var storedFileName =
                    $"{model.DocumentType}_{DateTime.Now.Ticks}{extension}";

                // ==========================
                // Physical Path
                // ==========================

                var physicalPath =
                    Path.Combine(folderPath, storedFileName);

                // ==========================
                // Save File
                // ==========================

                using (var stream = new FileStream(
                    physicalPath,
                    FileMode.Create))
                {
                    await model.File.CopyToAsync(stream);
                }

                // ==========================
                // Relative Path
                // ==========================

                var relativePath =
                    $"Uploads/Claims/{claimNumber}/{storedFileName}";

                // ==========================
                // Save Document Entry
                // ==========================

                var document = new ClaimDocument
                {
                    ClaimId = model.ClaimId,
                    ReferenceId = model.ReferenceId,
                    ModuleType = model.ModuleType,
                    DocumentType = model.DocumentType,
                    OriginalFileName = model.File.FileName,
                    StoredFileName = storedFileName,
                    FilePath = relativePath,
                    FileExtension = extension,
                    UploadedDate = DateTime.Now,
                    IsActive = true
                };

                _context.ClaimDocuments.Add(document);

                await _context.SaveChangesAsync();

                // ==========================
                // Response
                // ==========================

                return Ok(new
                {
                    success = true,
                    message = "Document uploaded successfully",
                    documentId = document.DocumentId,
                    filePath = relativePath
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("claim-documents/{claimId}")]
        public async Task<IActionResult> GetClaimDocuments(
    int claimId)
        {
            var documents = await _context.ClaimDocuments
                .Where(x => x.ClaimId == claimId
                         && x.IsActive)
                .ToListAsync();

            return Ok(documents);
        }

        [HttpDelete("document/{documentId}")]
        public async Task<IActionResult> DeleteDocument(
    int documentId)
        {
            var document = await _context.ClaimDocuments
                .FirstOrDefaultAsync(x =>
                    x.DocumentId == documentId);

            if (document == null)
            {
                return NotFound();
            }

            document.IsActive = false;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                Success = true
            });
        }
        //[HttpPost("add-travel")]

        //[HttpPost("upload")]
        //public async Task<IActionResult> UploadFile(IFormFile file)
        //{
        //    try
        //    {
        //        if (file == null || file.Length == 0)
        //            return BadRequest("Invalid file");

        //        var folder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");

        //        if (!Directory.Exists(folder))
        //            Directory.CreateDirectory(folder);

        //        var filePath = Path.Combine(folder, file.FileName);

        //        using (var stream = new FileStream(filePath, FileMode.Create))
        //        {
        //            await file.CopyToAsync(stream);
        //        }

        //        return Ok(new
        //        {
        //            path = file.FileName
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "An error occurred while uploading file",
        //            error = ex.Message
        //        });
        //    }
        //}
        //--------------------------------LOcal Purchase Page--------------------//

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


