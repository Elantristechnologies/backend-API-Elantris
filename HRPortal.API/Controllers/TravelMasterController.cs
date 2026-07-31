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
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Options;




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
        private readonly EmailService _emailService;



        public TravelMasterController(ApplicationDbContext context, IWebHostEnvironment environment, IConfiguration configuration, HttpClient httpClient, EmailService emailService)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
            _httpClient = httpClient;
            _emailService = emailService;
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
                // Get Employee Id from JWT Token
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

                // Get Active Vehicles
                var vehicles = await _context.Vehicles
                    .Where(v => v.EmployeeId == employeeId && v.IsActive)
                    .Select(v => new
                    {
                        v.VehicleId,
                        v.VehicleType,
                        v.VehicleNumber,
                        v.GearType,
                        v.FuelType,
                        v.CCRange
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = vehicles.Any()
                        ? "Vehicles fetched successfully."
                        : "No vehicles found.",
                    data = vehicles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "An error occurred while fetching vehicles.",
                    error = ex.Message
                });
            }
        }


        [HttpPut("update/{vehicleId}")]
        public async Task<IActionResult> UpdateVehicle(int vehicleId, [FromBody] Vehicle model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid request."
                    });
                }

                var vehicle = await _context.Vehicles
                    .FirstOrDefaultAsync(x => x.VehicleId == vehicleId && x.IsActive);

                if (vehicle == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Vehicle not found."
                    });
                }

                // Duplicate check
                var duplicate = await _context.Vehicles.AnyAsync(v =>
                    v.VehicleId != vehicleId &&
                    v.EmployeeId == model.EmployeeId &&
                    v.VehicleType == model.VehicleType &&
                    v.IsActive);

                if (duplicate)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Employee already has a {model.VehicleType}."
                    });
                }

                vehicle.VehicleType = model.VehicleType;
                vehicle.VehicleNumber = model.VehicleNumber;
                vehicle.GearType = model.GearType;
                vehicle.FuelType = model.FuelType;
                vehicle.CCRange = model.CCRange;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Vehicle updated successfully.",
                    data = vehicle
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

       [HttpDelete("soft-delete/{vehicleId}")]
public async Task<IActionResult> DeleteVehicle(int vehicleId)
{
    try
    {
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(x => x.VehicleId == vehicleId && x.IsActive);

        if (vehicle == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Vehicle not found."
            });
        }

        vehicle.IsActive = false;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Vehicle deleted successfully."
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
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in token."
                    });

                int empId = Convert.ToInt32(empIdClaim);

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x => x.EmpId == empId);

                if (employee == null)
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found."
                    });

                // Director = 1, Manager = 2
                if (employee.DesignationId != 1 && employee.DesignationId != 2)
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message = "You don't have permission."
                    });
                }

                bool exists = await _context.VehicleRateMasters.AnyAsync(x =>
                    x.VehicleTypeId == dto.VehicleTypeId &&
                    x.FuelTypeId == dto.FuelTypeId &&
                    x.ValidFrom.Date == dto.ValidFrom.Date &&
                    x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Vehicle Rate already exists."
                    });
                }

                var rate = new VehicleRateMaster
                {
                    VehicleTypeId = dto.VehicleTypeId,
                    FuelTypeId = dto.FuelTypeId,
                    AmountPerLiter = dto.AmountPerLiter,
                    AmountPerKm = dto.AmountPerKm,
                    ValidFrom = dto.ValidFrom,
                    CreatedBy = empId,
                    CreatedDate = DateTime.Now,
                    IsActive = true
                };

                _context.VehicleRateMasters.Add(rate);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Vehicle Rate added successfully."
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
                var data =
                    await (
                        from vr in _context.VehicleRateMasters
                        join vt in _context.VehicleMaster
                            on vr.VehicleTypeId equals vt.VehicleId
                        join ft in _context.FuelTypeMasters
                            on vr.FuelTypeId equals ft.FuelTypeId
                        where vr.IsActive
                        orderby vt.VehicleName
                        select new
                        {
                            vr.VehicleRateId,
                            vr.VehicleTypeId,
                            VehicleType = vt.VehicleName,

                            vr.FuelTypeId,
                            FuelType = ft.FuelTypeName,

                            vr.AmountPerLiter,
                            vr.AmountPerKm,
                            vr.ValidFrom
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

        [HttpGet("VehicleRate")]
        public async Task<IActionResult> GetVehicleRate(
    int vehicleTypeId,
    int fuelTypeId)
        {
            try
            {
                var rate = await (
                    from vr in _context.VehicleRateMasters
                    join v in _context.VehicleMaster
                        on vr.VehicleTypeId equals v.VehicleId
                    join f in _context.FuelTypeMasters
                        on vr.FuelTypeId equals f.FuelTypeId
                    where vr.IsActive
                          && vr.VehicleTypeId == vehicleTypeId
                          && vr.FuelTypeId == fuelTypeId
                    select new
                    {
                        vr.VehicleRateId,
                        vr.VehicleTypeId,
                        VehicleType = v.VehicleName,

                        vr.FuelTypeId,
                        FuelType = f.FuelTypeName,

                        vr.AmountPerLiter,
                        vr.AmountPerKm,
                        vr.ValidFrom
                    }).FirstOrDefaultAsync();

                if (rate == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Rate not configured."
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = rate
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
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in token."
                    });
                }

                int empId = Convert.ToInt32(empIdClaim);

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x => x.EmpId == empId);

                if (employee == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found."
                    });
                }

                // Only Director & Manager
                if (employee.DesignationId != 1 && employee.DesignationId != 2)
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message = "You don't have permission."
                    });
                }

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

                // Duplicate Check
                bool exists = await _context.VehicleRateMasters.AnyAsync(x =>
                    x.VehicleRateId != id &&
                    x.VehicleTypeId == dto.VehicleTypeId &&
                    x.FuelTypeId == dto.FuelTypeId &&
                    x.ValidFrom.Date == dto.ValidFrom.Date &&
                    x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Vehicle Rate already exists."
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
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in token."
                    });
                }

                int empId = Convert.ToInt32(empIdClaim);

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x => x.EmpId == empId);

                if (employee == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found."
                    });
                }

                // Only Director & Manager
                if (employee.DesignationId != 1 && employee.DesignationId != 2)
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message = "You don't have permission."
                    });
                }

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

                // Soft Delete
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




        [HttpGet("next-claim-number")]
        public async Task<IActionResult> GetNextClaimNumber()
        {
            try
            {
                var lastClaim = await _context.ClaimMasters
                    .OrderByDescending(x => x.ClaimId)
                    .FirstOrDefaultAsync();

                int nextNumber = (lastClaim?.ClaimId ?? 0) + 1;

                var today = DateTime.Now;

                string financialYear;

                if (today.Month >= 4)
                {
                    financialYear = $"{today.Year}/{today.Year + 1}";
                }
                else
                {
                    financialYear = $"{today.Year - 1}/{today.Year}";
                }

                var claimNumber = $"LEC-{nextNumber:D4}-{financialYear}";

                return Ok(new
                {
                    success = true,
                    claimNumber = claimNumber
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while generating claim number.",
                    error = ex.Message
                });
            }
        }

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
                    Status = isSubmit ? "Pending" : "Draft",
                    SubmittedAt = isSubmit ? DateTime.Now : null,
                    CurrentApprovalLevel = isSubmit ? 1 : null,
                    Remarks = dto.ClaimEntries.FirstOrDefault()?.Remarks
                };

                _context.ClaimMasters.Add(claim);

                await _context.SaveChangesAsync();

    //---------for claim number genrate ---//

                var lastClaim = await _context.ClaimMasters
    .Where(x => !string.IsNullOrEmpty(x.ClaimNo))
    .OrderByDescending(x => x.ClaimId)
    .FirstOrDefaultAsync();

                int nextNumber = 1;

                if (lastClaim != null)
                {
                    var parts = lastClaim.ClaimNo.Split('-');

                    if (parts.Length >= 2 &&
                        int.TryParse(parts[1], out int lastNumber))
                    {
                        nextNumber = lastNumber + 1;
                    }
                }

                var today = DateTime.Now;

                string financialYear;

                if (today.Month >= 4)
                {
                    financialYear = $"{today.Year}/{today.Year + 1}";
                }
                else
                {
                    financialYear = $"{today.Year - 1}/{today.Year}";
                }

                claim.ClaimNo = $"LEC-{claim.ClaimId:D4}-{financialYear}";

                await _context.SaveChangesAsync();


                if (isSubmit)
                {
                    var employee = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x => x.EmpId == dto.EmployeeId);

                    if (employee == null)
                    {
                        throw new Exception("Employee not found.");
                    }

                    // ==========================
                    // Level 1 - Reporting Manager
                    // ==========================

                    if (employee.ReportingManagerId.HasValue)
                    {
                        _context.ClaimApprovals.Add(new ClaimApproval
                        {
                            ClaimId = claim.ClaimId,
                            ApprovalLevel = 1,
                            ApproverEmpId = employee.ReportingManagerId.Value,
                            Status = "Pending",
                            CreatedAt = DateTime.Now,
                            ApprovalToken = Guid.NewGuid()
                        });
                    }

                    // ==========================
                    // Level 2 - Manager's Manager
                    // ==========================

                    if (employee.ReportingManagerId.HasValue)
                    {
                        var manager = await _context.EmployeeMasters
                            .FirstOrDefaultAsync(x => x.EmpId == employee.ReportingManagerId.Value);

                        if (manager != null &&
                            manager.ReportingManagerId.HasValue &&
                            manager.ReportingManagerId.Value > 0)
                        {
                            _context.ClaimApprovals.Add(new ClaimApproval
                            {
                                ClaimId = claim.ClaimId,
                                ApprovalLevel = 2,
                                ApproverEmpId = manager.ReportingManagerId.Value,
                                Status = "Pending",
                                CreatedAt = DateTime.Now,
                                ApprovalToken = Guid.NewGuid()
                            });
                        }
                    }

                    // ==========================
                    // HR1 / HR2 / Accounts
                    // ==========================

                    var approvalMatrix = await _context.ApprovalMatrix
                        .Where(x =>
                            x.OrganizationId == employee.OrganizationId &&
                            x.IsActive)
                        .OrderBy(x => x.ApprovalLevel)
                        .ToListAsync();

                    foreach (var item in approvalMatrix)
                    {
                        var approver = await _context.EmployeeMasters
                            .FirstOrDefaultAsync(x =>
                                x.OrganizationId == employee.OrganizationId &&
                                x.DesignationId == item.DesignationId);

                        _context.ClaimApprovals.Add(new ClaimApproval
                        {
                            ClaimId = claim.ClaimId,
                            ApprovalLevel = item.ApprovalLevel,
                            ApproverEmpId = approver?.EmpId,   // Employee illana NULL save aagum
                            Status = "Pending",
                            CreatedAt = DateTime.Now,
                            ApprovalToken = Guid.NewGuid()
                        });
                    }

                    await _context.SaveChangesAsync();
                    await _context.SaveChangesAsync();

                    // =====================================
                    // Send Mail To First Approver
                    // =====================================

                    var firstApproval = await _context.ClaimApprovals
                        .Where(x => x.ClaimId == claim.ClaimId)
                        .OrderBy(x => x.ApprovalLevel)
                        .FirstOrDefaultAsync();

                    if (firstApproval != null)
                    {
                        var approver = await _context.EmployeeMasters
                            .FirstOrDefaultAsync(x => x.EmpId == firstApproval.ApproverEmpId);

                        if (approver != null &&
                            !string.IsNullOrWhiteSpace(approver.EmailId))
                        {

                            string departmentName = "-";

                            if (employee.DepartmentId.HasValue)
                            {
                                var department = await _context.Department_Master
                                    .FirstOrDefaultAsync(x => x.DepartmentId == employee.DepartmentId.Value);

                                if (department != null)
                                {
                                    departmentName = department.DepartmentName;
                                }
                            }

                            string purpose = dto.ClaimEntries
                                .FirstOrDefault()?.Purpose ?? "-";


                            var baseUrl = _configuration["AppSettings:BaseUrl"];

                            var approveUrl =
                                             $"{baseUrl}/api/TravelMaster/mail-approve?token={firstApproval.ApprovalToken}";

                            var rejectUrl =
                                             $"{baseUrl}/api/TravelMaster/reject-form?token={firstApproval.ApprovalToken}";

                            string body = $@"

Dear {approver.FullName},

<br/><br/>

A new Local expense claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {employee.FullName}</li>
    <li>Employee ID : {employee.EmployeeCode}</li>
    <li>Department : {departmentName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
</ul>

<b>Purpose</b>

<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Approve

</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Reject

</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

                            await _emailService.SendEmailAsync(
                                approver.EmailId,
                                $"Claim Approval Required - {claim.ClaimNo}",
                                body);
                        }
                    }
                }

                // ===========================
                // Save All Entries
                // ===========================

                foreach (var entry in dto.ClaimEntries)
                {
                    // ===========================
                    // Get Current Vehicle Rate
                    // ===========================

                    var currentRate = await _context.VehicleRateMasters
                        .Where(x =>
                            x.VehicleTypeId == entry.VehicleTypeId &&
                            x.FuelTypeId == entry.FuelTypeId &&
                            x.IsActive)
                        .OrderByDescending(x => x.ValidFrom)
                        .FirstOrDefaultAsync();

                    if (currentRate == null)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Rate not configured for VehicleType {entry.VehicleTypeId} and FuelType {entry.FuelTypeId}"
                        });
                    }

                    // ===========================
                    // Travel
                    // ===========================

                    var travel = new ClaimTravel
                    {
                        ClaimId = claim.ClaimId,

                        VehicleTypeId = entry.VehicleTypeId,
                        FuelTypeId = entry.FuelTypeId,
                        VehicleDetailId = entry.VehicleDetailId,
                        VehicleNumber = entry.VehicleNumber,

                        // 👇 Snapshot of rate at claim creation time
                        AppliedRatePerKm = currentRate.AmountPerKm,

                        TravelDate = entry.TravelDate,
                        Purpose = entry.Purpose,
                        FromLocation = entry.FromLocation,
                        ToLocation = entry.ToLocation,
                        BusorTrainInvoiceNumber = entry.BusorTrainInvoiceNumber,
                        TaxiInvoiceNumber = entry.TaxiInvoiceNumber,
                        KmRun = entry.KmRun,
                        Amount = entry.TravelAmount,
                        Remarks = entry.TravelRemarks,
                        IsActive = true
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
                        BreakfastInvoiceNumber = entry.BreakfastInvoiceNumber,
                        LunchInvoiceNumber = entry.LunchInvoiceNumber,
                        DinnerInvoiceNumber = entry.DinnerInvoiceNumber,
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
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // ===========================
                // Claim Master
                // ===========================

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

                // ===========================
                // Travels
                // ===========================

                var travels = await _context.ClaimTravels
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .OrderBy(x => x.TravelId)
                    .ToListAsync();

                // ===========================
                // Documents
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
                        AppliedRatePerKm = travel.AppliedRatePerKm,
                        travel.TravelDate,
                        travel.Purpose,
                        travel.FromLocation,
                        travel.ToLocation,
                        travel.BusorTrainInvoiceNumber,
                        travel.TaxiInvoiceNumber,
                        travel.KmRun,

                        TravelAmount = travel.Amount,
                        TravelRemarks = travel.Remarks,

                        Breakfast = food?.Breakfast ?? 0,
                        Lunch = food?.Lunch ?? 0,
                        Dinner = food?.Dinner ?? 0,
                        FoodAmount = food?.Amount ?? 0,
                        BreakfastInvoiceNumber = food.BreakfastInvoiceNumber,
                        LunchInvoiceNumber = food.LunchInvoiceNumber,
                        DinnerInvoiceNumber = food.DinnerInvoiceNumber,

                        TollAmount = expense?.TollAmount ?? 0,
                        AutoAmount = expense?.AutoAmount ?? 0,
                        OtherAmount = expense?.OtherAmount ?? 0,
                        ExpenseRemarks = expense?.Remarks ?? "",

                        Documents = documents
                            .Where(d => d.ReferenceId == travel.TravelId)
                            .Select(d => new
                            {
                                d.DocumentId,
                                d.ReferenceId,
                                d.ModuleType,
                                d.DocumentType,

                                FilePath = string.IsNullOrEmpty(d.FilePath)
                                    ? ""
                                    : $"{baseUrl}/{d.FilePath.Replace("\\", "/")}",

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
                        remarks = claim.Remarks,


                        claimEntries
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

        //  [HttpPut("update-draft/{claimId}")]
        //  public async Task<IActionResult> UpdateDraft(
        //int claimId,
        //[FromBody] CreateClaimDto dto)
        //  {
        //      await using var transaction =
        //          await _context.Database.BeginTransactionAsync();

        //      try
        //      {
        //          var claim = await _context.ClaimMasters
        //          .FirstOrDefaultAsync(x =>
        //          x.ClaimId == claimId &&
        //           (x.Status == "Draft" || x.Status == "Rejected") &&
        //          x.IsActive == true);


        //          if (claim == null)
        //          {
        //              return NotFound(new
        //              {
        //                  success = false,
        //                  message = "Draft not found."
        //              });
        //          }

        //          decimal totalAmount =
        //              dto.ClaimEntries.Sum(x =>
        //                  x.TravelAmount +
        //                  x.FoodAmount +
        //                  (x.TollAmount ?? 0) +
        //                  (x.AutoAmount ?? 0) +
        //                  (x.OtherAmount ?? 0));

        //          claim.ClaimMonth = dto.ClaimMonth;
        //          claim.TotalAmount = totalAmount;
        //          claim.Remarks = dto.ClaimEntries.FirstOrDefault()?.Remarks;
        //          claim.LastModifiedAt = DateTime.Now;

        //          await _context.SaveChangesAsync();

        //          foreach (var entry in dto.ClaimEntries)
        //          {
        //              // ==================================
        //              // Existing Travel
        //              // ==================================

        //              if (entry.TravelId.HasValue)
        //              {
        //                  var travel = await _context.ClaimTravels
        //                      .FirstOrDefaultAsync(x =>
        //                          x.TravelId == entry.TravelId &&
        //                          x.ClaimId == claimId);

        //                  if (travel != null)
        //                  {
        //                      travel.VehicleTypeId = entry.VehicleTypeId;
        //                      travel.FuelTypeId = entry.FuelTypeId;
        //                      travel.VehicleDetailId = entry.VehicleDetailId;
        //                      travel.VehicleNumber = entry.VehicleNumber;

        //                      travel.TravelDate = entry.TravelDate;
        //                      travel.Purpose = entry.Purpose;
        //                      travel.FromLocation = entry.FromLocation;
        //                      travel.ToLocation = entry.ToLocation;
        //                      travel.KmRun = entry.KmRun;
        //                      travel.Amount = entry.TravelAmount;
        //                      travel.Remarks = entry.TravelRemarks;

        //                      var food = await _context.ClaimFoods
        //                          .FirstOrDefaultAsync(x =>
        //                              x.ClaimId == claimId &&
        //                              x.TravelId == travel.TravelId);

        //                      if (food != null)
        //                      {
        //                          food.FoodDate = entry.TravelDate;
        //                          food.Breakfast = entry.Breakfast;
        //                          food.Lunch = entry.Lunch;
        //                          food.Dinner = entry.Dinner;
        //                          food.Amount = entry.FoodAmount;
        //                      }

        //                      var expense = await _context.ClaimExpenses
        //                          .FirstOrDefaultAsync(x =>
        //                              x.ClaimId == claimId &&
        //                              x.TravelId == travel.TravelId &&
        //                              x.IsActive == true);

        //                      if (expense != null)
        //                      {
        //                          expense.TollAmount = entry.TollAmount;
        //                          expense.AutoAmount = entry.AutoAmount;
        //                          expense.OtherAmount = entry.OtherAmount;

        //                          expense.ExpenseDate = entry.TravelDate;
        //                          expense.Remarks = entry.ExpenseRemarks;

        //                      }
        //                  }
        //              }
        //              else
        //              {
        //                  // ==================================
        //                  // New Travel
        //                  // ==================================

        //                  var travel = new ClaimTravel
        //                  {
        //                      ClaimId = claimId,

        //                      VehicleTypeId = entry.VehicleTypeId,
        //                      FuelTypeId = entry.FuelTypeId,
        //                      VehicleDetailId = entry.VehicleDetailId,
        //                      VehicleNumber = entry.VehicleNumber,

        //                      TravelDate = entry.TravelDate,
        //                      Purpose = entry.Purpose,
        //                      FromLocation = entry.FromLocation,
        //                      ToLocation = entry.ToLocation,
        //                      KmRun = entry.KmRun,
        //                      Amount = entry.TravelAmount,
        //                      Remarks = entry.TravelRemarks,

        //                  };

        //                  _context.ClaimTravels.Add(travel);

        //                  await _context.SaveChangesAsync();

        //                  var food = new ClaimFood
        //                  {
        //                      TravelId = travel.TravelId,
        //                      ClaimId = claimId,

        //                      FoodDate = entry.TravelDate,
        //                      Breakfast = entry.Breakfast,
        //                      Lunch = entry.Lunch,
        //                      Dinner = entry.Dinner,
        //                      Amount = entry.FoodAmount,

        //                      InvoiceNumber = string.Empty,
        //                      DocumentPath = string.Empty
        //                  };

        //                  _context.ClaimFoods.Add(food);

        //                  var expense = new ClaimExpense
        //                  {
        //                      ClaimId = claimId,
        //                      TravelId = travel.TravelId,

        //                      TollAmount = entry.TollAmount,
        //                      AutoAmount = entry.AutoAmount,
        //                      OtherAmount = entry.OtherAmount,

        //                      ExpenseDate = entry.TravelDate,
        //                      Remarks = entry.ExpenseRemarks,                   
        //                      created_at = DateTime.Now,
        //                      IsActive = true
        //                  };

        //                  _context.ClaimExpenses.Add(expense);
        //              }
        //          }

        //          await _context.SaveChangesAsync();

        //          await transaction.CommitAsync();

        //          return Ok(new
        //          {
        //              success = true,
        //              message = "Draft updated successfully."
        //          });
        //      }
        //      catch (Exception ex)
        //      {
        //          await transaction.RollbackAsync();

        //          return StatusCode(500, new
        //          {
        //              success = false,
        //              message = "Error while updating draft.",
        //              error = ex.InnerException?.Message ?? ex.Message
        //          });
        //      }
        //  }

        [Authorize]
        [HttpPut("update-draft/{claimId}")]
        public async Task<IActionResult> UpdateDraft(
    int claimId,
    [FromBody] CreateClaimDto dto)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

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

                //------------------------------------------
                // Login Employee
                //------------------------------------------

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                int loginEmpId = int.Parse(empIdClaim);

                //------------------------------------------
                // Claim
                //------------------------------------------

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        (x.Status == "Draft" || x.Status == "Rejected") &&
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                //------------------------------------------
                // Owner Validation
                //------------------------------------------

                if (claim.EmployeeId != loginEmpId)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "You are not authorized to update this claim."
                    });
                }

                //------------------------------------------
                // Total Amount
                //------------------------------------------

                decimal totalAmount =
                    dto.ClaimEntries.Sum(x =>
                        x.TravelAmount +
                        x.FoodAmount +
                        (x.TollAmount ?? 0) +
                        (x.AutoAmount ?? 0) +
                        (x.OtherAmount ?? 0));

                //------------------------------------------
                // Claim Master Update
                //------------------------------------------

                claim.EmployeeId = dto.EmployeeId;
                claim.ClaimMonth = dto.ClaimMonth;
                claim.ClaimYear = dto.ClaimMonth.Year;
                claim.TotalAmount = totalAmount;
                claim.Remarks = dto.ClaimEntries.FirstOrDefault()?.Remarks;
                claim.LastModifiedAt = DateTime.Now;

                _context.Entry(claim).State = EntityState.Modified;

                await _context.SaveChangesAsync();

                //------------------------------------------------
                // PART-2
                // Existing Travel Update + New Travel Insert
                //------------------------------------------------
                // ==================================
                // Travel Update / Insert
                // ==================================

                foreach (var entry in dto.ClaimEntries)
                {
                    ClaimTravel travel;

                    //=========================================
                    // EXISTING TRAVEL
                    //=========================================
                    var currentRate = await _context.VehicleRateMasters
    .Where(x =>
        x.VehicleTypeId == entry.VehicleTypeId &&
        x.FuelTypeId == entry.FuelTypeId &&
        x.IsActive)
    .OrderByDescending(x => x.ValidFrom)
    .FirstOrDefaultAsync();

                    if (currentRate == null)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Vehicle rate not configured."
                        });
                    }

                    if (entry.TravelId.HasValue && entry.TravelId.Value > 0)
                    {
                        travel = await _context.ClaimTravels
                            .FirstOrDefaultAsync(x =>
                                x.TravelId == entry.TravelId.Value &&
                                x.ClaimId == claimId &&
                                x.IsActive);

                        if (travel == null)
                        {
                            continue;
                        }

                        travel.VehicleTypeId = entry.VehicleTypeId;
                        travel.FuelTypeId = entry.FuelTypeId;
                        travel.VehicleDetailId = entry.VehicleDetailId;
                        travel.VehicleNumber = entry.VehicleNumber;

                        travel.TravelDate = entry.TravelDate;
                        travel.Purpose = entry.Purpose;
                        travel.FromLocation = entry.FromLocation;
                        travel.ToLocation = entry.ToLocation;
                        travel.KmRun = entry.KmRun;
                        travel.Amount = entry.TravelAmount;
                        travel.Remarks = entry.TravelRemarks;
                        travel.AppliedRatePerKm = currentRate.AmountPerKm;

                        travel.BusorTrainInvoiceNumber = entry.BusorTrainInvoiceNumber;
                        travel.TaxiInvoiceNumber = entry.TaxiInvoiceNumber;


                        _context.ClaimTravels.Update(travel);

                        //------------------------------------
                        // FOOD
                        //------------------------------------

                        var food = await _context.ClaimFoods
                            .FirstOrDefaultAsync(x =>
                                x.ClaimId == claimId &&
                                x.TravelId == travel.TravelId);

                        if (food != null)
                        {
                            food.FoodDate = entry.TravelDate;
                            food.Breakfast = entry.Breakfast;
                            food.Lunch = entry.Lunch;
                            food.Dinner = entry.Dinner;
                            food.Amount = entry.FoodAmount;
                            food.BreakfastInvoiceNumber = entry.BreakfastInvoiceNumber;
                            food.LunchInvoiceNumber = entry.LunchInvoiceNumber;
                            food.DinnerInvoiceNumber = entry.DinnerInvoiceNumber;
                            _context.ClaimFoods.Update(food);
                        }

                        //------------------------------------
                        // EXPENSE
                        //------------------------------------

                        var expense = await _context.ClaimExpenses
                            .FirstOrDefaultAsync(x =>
                                x.ClaimId == claimId &&
                                x.TravelId == travel.TravelId &&
                                x.IsActive);

                        if (expense != null)
                        {
                            expense.TollAmount = entry.TollAmount;
                            expense.AutoAmount = entry.AutoAmount;
                            expense.OtherAmount = entry.OtherAmount;

                            expense.ExpenseDate = entry.TravelDate;
                            expense.Remarks = entry.ExpenseRemarks;

                            _context.ClaimExpenses.Update(expense);
                        }
                        else
                        {
                            expense = new ClaimExpense
                            {
                                ClaimId = claimId,
                                TravelId = travel.TravelId,

                                TollAmount = entry.TollAmount,
                                AutoAmount = entry.AutoAmount,
                                OtherAmount = entry.OtherAmount,

                                ExpenseDate = entry.TravelDate,
                                Remarks = entry.ExpenseRemarks,
                                created_at = DateTime.Now,
                                IsActive = true
                            };

                            _context.ClaimExpenses.Add(expense);
                        }
                    }

                    //=========================================
                    // NEW TRAVEL
                    //=========================================

                    else
                    {
                        travel = new ClaimTravel
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
                            BusorTrainInvoiceNumber = entry.BusorTrainInvoiceNumber,
                            TaxiInvoiceNumber = entry.TaxiInvoiceNumber,
                            Amount = entry.TravelAmount,
                            Remarks = entry.TravelRemarks,
                            AppliedRatePerKm = currentRate.AmountPerKm,
                            IsActive = true
                        };

                        _context.ClaimTravels.Add(travel);

                        await _context.SaveChangesAsync();

                        var food = new ClaimFood
                        {
                            ClaimId = claimId,
                            TravelId = travel.TravelId,

                            FoodDate = entry.TravelDate,
                            Breakfast = entry.Breakfast,
                            Lunch = entry.Lunch,
                            Dinner = entry.Dinner,
                            Amount = entry.FoodAmount,

                            BreakfastInvoiceNumber = entry.BreakfastInvoiceNumber,
                            LunchInvoiceNumber = entry.LunchInvoiceNumber,
                            DinnerInvoiceNumber = entry.DinnerInvoiceNumber,
                            DocumentPath = ""
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
                            created_at = DateTime.Now,
                            IsActive = true
                        };

                        _context.ClaimExpenses.Add(expense);
                    }
                }

                await _context.SaveChangesAsync();
                //=====================================================
                // DELETE REMOVED TRAVELS
                //=====================================================

                var incomingTravelIds = dto.ClaimEntries
                    .Where(x => x.TravelId.HasValue && x.TravelId.Value > 0)
                    .Select(x => x.TravelId.Value)
                    .ToList();

                var removedTravels = await _context.ClaimTravels
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive &&
                        !incomingTravelIds.Contains(x.TravelId))
                    .ToListAsync();

                foreach (var travel in removedTravels)
                {
                    travel.IsActive = false;

                    var foods = await _context.ClaimFoods
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId)
                        .ToListAsync();

                    foreach (var food in foods)
                    {
                        food.IsActive = false;
                    }

                    var expenses = await _context.ClaimExpenses
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId)
                        .ToListAsync();

                    foreach (var expense in expenses)
                    {
                        expense.IsActive = false;
                    }

                    var documents = await _context.ClaimDocuments
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.ReferenceId == travel.TravelId &&
                            x.IsActive)
                        .ToListAsync();

                    foreach (var doc in documents)
                    {
                        doc.IsActive = false;
                    }
                }

                //=====================================================
                // SAVE
                //=====================================================

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
                // =====================================
                // Get Draft / Rejected Claim
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        (x.Status == "Draft" || x.Status == "Rejected") &&
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                // =====================================
                // Check Resubmit
                // =====================================

                bool isResubmit = claim.Status == "Rejected";

                // =====================================
                // Remove Old Approval Flow
                // =====================================

                var oldApprovals = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                if (oldApprovals.Any())
                {
                    _context.ClaimApprovals.RemoveRange(oldApprovals);
                    await _context.SaveChangesAsync();
                }

                // =====================================
                // Reset Claim Status
                // =====================================

                claim.Status = "Pending";
                
                claim.CurrentApprovalLevel = 1;
                claim.SubmittedAt = DateTime.Now;
                claim.RejectedAt = null;
                claim.ApprovedAt = null;
                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // =====================================
                // Employee Details
                // =====================================

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x => x.EmpId == claim.EmployeeId);

                if (employee == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee not found."
                    });
                }

                // =====================================
                // Level 1 - Reporting Manager
                // =====================================

                if (employee.ReportingManagerId.HasValue)
                {
                    _context.ClaimApprovals.Add(new ClaimApproval
                    {
                        ClaimId = claim.ClaimId,
                        ApprovalLevel = 1,
                        ApproverEmpId = employee.ReportingManagerId.Value,
                        Status = "Pending",
                        CreatedAt = DateTime.Now,
                        ApprovalToken = Guid.NewGuid()
                    });
                }

                // =====================================
                // Level 2 - Reporting Manager's Manager
                // =====================================

                if (employee.ReportingManagerId.HasValue)
                {
                    var manager = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == employee.ReportingManagerId.Value);

                    if (manager != null &&
                        manager.ReportingManagerId.HasValue &&
                        manager.ReportingManagerId.Value > 0)
                    {
                        _context.ClaimApprovals.Add(new ClaimApproval
                        {
                            ClaimId = claim.ClaimId,
                            ApprovalLevel = 2,
                            ApproverEmpId = manager.ReportingManagerId.Value,
                            Status = "Pending",
                            CreatedAt = DateTime.Now,
                            ApprovalToken = Guid.NewGuid()
                        });
                    }
                }

                // =====================================
                // Approval Matrix
                // =====================================

                var approvalMatrix = await _context.ApprovalMatrix
                    .Where(x =>
                        x.OrganizationId == employee.OrganizationId &&
                        x.IsActive)
                    .OrderBy(x => x.ApprovalLevel)
                    .ToListAsync();

                foreach (var item in approvalMatrix)
                {
                    var approver = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == employee.OrganizationId &&
                            x.DesignationId == item.DesignationId);

                    _context.ClaimApprovals.Add(new ClaimApproval
                    {
                        ClaimId = claim.ClaimId,
                        ApprovalLevel = item.ApprovalLevel,
                        ApproverEmpId = approver?.EmpId,   // NULL if no employee
                        Status = "Pending",
                        CreatedAt = DateTime.Now,
                        ApprovalToken = approver != null ? Guid.NewGuid() : null
                    });
                }

                await _context.SaveChangesAsync();

                // =====================================
                // Department Name
                // =====================================

                string departmentName = "-";

                if (employee.DepartmentId.HasValue)
                {
                    var department = await _context.Department_Master
                        .FirstOrDefaultAsync(x =>
                            x.DepartmentId == employee.DepartmentId.Value);

                    if (department != null)
                    {
                        departmentName = department.DepartmentName;
                    }
                }

                // =====================================
                // Purpose
                // =====================================

                var firstTravel = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x => x.ClaimId == claim.ClaimId);

                string purpose = firstTravel?.Purpose ?? "-";

                // =====================================
                // Get First Approver
                // =====================================

                var firstApproval = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .OrderBy(x => x.ApprovalLevel)
                    .FirstOrDefaultAsync();

                if (firstApproval != null)
                {
                    var approver = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == firstApproval.ApproverEmpId);

                    if (approver != null &&
                        !string.IsNullOrWhiteSpace(approver.EmailId))
                    {
                        var baseUrl = _configuration["AppSettings:BaseUrl"];

                        var approveUrl =
                            $"{baseUrl}/api/TravelMaster/mail-approve?token={firstApproval.ApprovalToken}";

                        var rejectUrl =
                            $"{baseUrl}/api/TravelMaster/reject-form?token={firstApproval.ApprovalToken}";

                        string body = $@"

Dear {approver.FullName},

<br/><br/>

A new Local expense claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {employee.FullName}</li>
    <li>Employee ID : {employee.EmployeeCode}</li>
    <li>Department : {departmentName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
</ul>

<b>Purpose</b>

<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Approve

</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Reject

</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

                        await _emailService.SendEmailAsync(
                            approver.EmailId,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = isResubmit
                        ? "Claim resubmitted successfully."
                        : "Claim submitted successfully.",

                    data = new
                    {
                        claim.ClaimId,
                        claim.ClaimNo,
                        claim.Status,
                        claim.CurrentApprovalLevel,
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


        //------------APPROVAL FOR CLAIM----------------//

        [HttpPost("approve-claim")]
        public async Task<IActionResult> ApproveClaim(
      [FromBody] ClaimApprovalActionDto dto)
        {
            try
            {
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                int empId = int.Parse(empIdClaim);

                // =====================================
                // Current Pending Approval
                // =====================================

                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == dto.ClaimId &&
                        x.ApproverEmpId == empId &&
                        x.Status == "Pending");

                if (approval == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval record not found."
                    });
                }

                // =====================================
                // Update Current Approval
                // =====================================

                approval.Status = "Approved";
                approval.Remarks = dto.Remarks;
                approval.ActionDate = DateTime.Now;

                await _context.SaveChangesAsync();

                // =====================================
                // Get Claim
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == dto.ClaimId);

                if (claim == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                ClaimApproval? nextPending = null;

                // =====================================
                // COMPANY FLOW
                // Level 2 = Final Manual Approval
                // =====================================

                if (approval.ApprovalLevel == 2)
                {
                    claim.Status = "Approved";
                    claim.ApprovedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;

                    // Auto Approve Remaining Levels

                    var remainingApprovals = await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == dto.ClaimId &&
                            x.Status == "Pending")
                        .ToListAsync();

                    foreach (var item in remainingApprovals)
                    {
                        item.Status = "Approved";
                        item.Remarks = "Auto Approved";
                        item.ActionDate = DateTime.Now;
                    }
                }
                else
                {
                    // Move To Next Approval

                    nextPending = await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == dto.ClaimId &&
                            x.Status == "Pending")
                        .OrderBy(x => x.ApprovalLevel)
                        .FirstOrDefaultAsync();

                    if (nextPending != null)
                    {
                        claim.CurrentApprovalLevel = nextPending.ApprovalLevel;
                        claim.LastModifiedAt = DateTime.Now;
                    }
                    else
                    {
                        claim.Status = "Approved";
                        claim.ApprovedAt = DateTime.Now;
                        claim.CurrentApprovalLevel = null;
                        claim.LastModifiedAt = DateTime.Now;
                    }
                }

                await _context.SaveChangesAsync();
                // =====================================
                // SEND MAIL TO NEXT APPROVER
                // =====================================

                if (nextPending != null)
                {
                    var nextApprover = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x => x.EmpId == nextPending.ApproverEmpId);

                    var employee = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x => x.EmpId == claim.EmployeeId);

                    string departmentName = "-";

                    if (employee?.DepartmentId != null)
                    {
                        var department = await _context.Department_Master
                            .FirstOrDefaultAsync(x => x.DepartmentId == employee.DepartmentId.Value);

                        if (department != null)
                        {
                            departmentName = department.DepartmentName;
                        }
                    }

                    var firstTravel = await _context.ClaimTravels
                        .FirstOrDefaultAsync(x => x.ClaimId == claim.ClaimId);

                    string purpose = firstTravel?.Purpose ?? "-";

                    if (nextApprover != null &&
                        !string.IsNullOrWhiteSpace(nextApprover.EmailId))
                    {
                        var baseUrl = _configuration["AppSettings:BaseUrl"];

                        var approveUrl =
                            $"{baseUrl}/api/TravelMaster/mail-approve?token={nextPending.ApprovalToken}";

                        var rejectUrl =
                            $"{baseUrl}/api/TravelMaster/reject-form?token={nextPending.ApprovalToken}";
                        string body = $@"

Dear {nextApprover.FullName},

<br/><br/>

A new Local expense claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {employee.FullName}</li>
    <li>Employee ID : {employee.EmployeeCode}</li>
    <li>Department : {departmentName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
</ul>

<b>Purpose</b>

<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Approve

</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Reject

</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

                        await _emailService.SendEmailAsync(
                            nextApprover.EmailId,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);
                    }
                }

                    return Ok(new
                    {
                        success = true,
                        message = "Claim approved successfully."
                    });
                }
            
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while approving claim.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [HttpGet("mail-approve")]
        public async Task<IActionResult> MailApprove(Guid token)
        {
            try
            {
                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ApprovalToken == token &&
                        x.Status == "Pending");

                if (approval == null)
                {
                    return Content("Invalid Approval Link");
                }

                // =====================================
                // Current Approval Update
                // =====================================

                approval.Status = "Approved";
                approval.Remarks = "Approved From Email";
                approval.ActionDate = DateTime.Now;

                await _context.SaveChangesAsync();

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == approval.ClaimId);

                if (claim == null)
                {
                    return Content("Claim Not Found");
                }

                // =====================================
                // COMPANY FLOW
                // =====================================

                ClaimApproval? nextPending = null;

                if (approval.ApprovalLevel == 2)
                {
                    claim.Status = "Approved";
                    claim.ApprovedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;

                    // Auto Approve Remaining Levels

                    var remainingApprovals = await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claim.ClaimId &&
                            x.Status == "Pending")
                        .ToListAsync();

                    foreach (var item in remainingApprovals)
                    {
                        item.Status = "Approved";
                        item.Remarks = "Auto Approved";
                        item.ActionDate = DateTime.Now;
                    }
                }
                else
                {
                    nextPending = await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claim.ClaimId &&
                            x.Status == "Pending")
                        .OrderBy(x => x.ApprovalLevel)
                        .FirstOrDefaultAsync();

                    if (nextPending != null)
                    {
                        claim.CurrentApprovalLevel = nextPending.ApprovalLevel;
                        claim.LastModifiedAt = DateTime.Now;
                    }
                    else
                    {
                        claim.Status = "Approved";
                        claim.ApprovedAt = DateTime.Now;
                        claim.CurrentApprovalLevel = null;
                        claim.LastModifiedAt = DateTime.Now;
                    }
                }

                await _context.SaveChangesAsync();
                // =====================================
                // SEND MAIL TO NEXT APPROVER
                // =====================================

                if (nextPending != null)
                {
                    var nextApprover = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == nextPending.ApproverEmpId);

                    var employee = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x => x.EmpId == claim.EmployeeId);

                    string departmentName = "-";

                    if (employee?.DepartmentId != null)
                    {
                        var department = await _context.Department_Master
                            .FirstOrDefaultAsync(x =>
                                x.DepartmentId == employee.DepartmentId.Value);

                        if (department != null)
                        {
                            departmentName = department.DepartmentName;
                        }
                    }

                    string purpose = "-";

                    if (nextApprover != null &&
                        !string.IsNullOrWhiteSpace(nextApprover.EmailId))
                    {
                        var baseUrl = _configuration["AppSettings:BaseUrl"];

                        var approveUrl =
                            $"{baseUrl}/api/TravelMaster/mail-approve?token={nextPending.ApprovalToken}";

                        var rejectUrl =
                            $"{baseUrl}/api/TravelMaster/reject-form?token={nextPending.ApprovalToken}";

                        string body = $@"

Dear {nextApprover.FullName},

<br/><br/>

A new Local expense claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {employee.FullName}</li>
    <li>Employee ID : {employee.EmployeeCode}</li>
    <li>Department : {departmentName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
</ul>

<b>Purpose</b>

<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Approve

</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Reject

</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

             

                        await _emailService.SendEmailAsync(
                            nextApprover.EmailId,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);
                    }
                }
                else
                {
                    claim.Status = "Approved";
                    claim.ApprovedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                return Content(@"
<!DOCTYPE html>
<html>
<head>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<meta charset=""utf-8"">
<title>Claim Approved</title>
<style>
body{
    font-family:Arial,sans-serif;
    background:#f4f6f9;
    display:flex;
    justify-content:center;
    align-items:center;
    height:100vh;
    margin:0;
}
.card{
    background:white;
    padding:30px;
    border-radius:12px;
    box-shadow:0 2px 10px rgba(0,0,0,0.15);
    text-align:center;
    width:90%;
    max-width:400px;
}
.icon{
    font-size:60px;
}
.title{
    color:green;
    font-size:24px;
    font-weight:bold;
    margin-top:10px;
}
.message{
    margin-top:10px;
    color:#555;
}
</style>
</head>
<body>

<div class='card'>
    <div class='icon' style='color:green;'>✔</div>
    <div class='title'>Claim Approved</div>
    <div class='message'>
        The claim has been approved successfully.
    </div>
</div>

</body>
</html>
", "text/html");
            }
            catch (Exception ex)
            {
                return Content(ex.Message);
            }
        }

        [HttpPost("reject-claim")]
        public async Task<IActionResult> RejectClaim([FromBody] ClaimApprovalActionDto dto)
        {
            try
            {
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                int empId = int.Parse(empIdClaim);

                // =====================================
                // Get Current Pending Approval
                // =====================================

                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == dto.ClaimId &&
                        x.ApproverEmpId == empId &&
                        x.Status == "Pending");

                if (approval == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval record not found."
                    });
                }

                // =====================================
                // Get Claim
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == dto.ClaimId);

                if (claim == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                // =====================================
                // Reject Current Approval
                // =====================================

                string remarks = string.IsNullOrWhiteSpace(dto.Remarks)
                    ? "NA"
                    : dto.Remarks.Trim();

                approval.Status = "Rejected";
                approval.Remarks = remarks;
                approval.ActionDate = DateTime.Now;

                // =====================================
                // Update Claim
                // =====================================

                claim.Status = "Rejected";
                claim.Remarks = remarks;          // Save latest rejection reason
                claim.RejectedAt = DateTime.Now;
                claim.CurrentApprovalLevel = null;
                claim.LastModifiedAt = DateTime.Now;

                // =====================================
                // Cancel Remaining Pending Approvals
                // =====================================

                var futureApprovals = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == dto.ClaimId &&
                        x.ApprovalLevel > approval.ApprovalLevel &&
                        x.Status == "Pending")
                    .ToListAsync();

                foreach (var item in futureApprovals)
                {
                    item.Status = "Cancelled";
                    item.Remarks = "Cancelled due to previous level rejection";
                    item.ActionDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Claim rejected successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while rejecting claim.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [HttpGet("mail-reject")]
            public async Task<IActionResult> MailReject(Guid token)
            {
                try
                {
                    var approval = await _context.ClaimApprovals
                        .FirstOrDefaultAsync(x =>
                            x.ApprovalToken == token &&
                            x.Status == "Pending");

                if (approval == null)
                {
                    return Content("Invalid Rejection Link");
                }

                // =====================================
                // Update Approval
                // =====================================

                string remarks = "Rejected From Email";

                approval.Status = "Rejected";
                approval.Remarks = remarks;
                approval.ActionDate = DateTime.Now;

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == approval.ClaimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.Remarks = remarks;      // Save latest rejection reason
                    claim.RejectedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;

                    // =====================================
                    // Cancel Remaining Pending Approvals
                    // =====================================

                    var futureApprovals = await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == approval.ClaimId &&
                            x.ApprovalLevel > approval.ApprovalLevel &&
                            x.Status == "Pending")
                        .ToListAsync();

                    foreach (var item in futureApprovals)
                    {
                        item.Status = "Cancelled";
                        item.Remarks = "Cancelled due to previous level rejection";
                        item.ActionDate = DateTime.Now;
                    }
                }

                await _context.SaveChangesAsync();

                return Content(@"
<!DOCTYPE html>
<html>
<head>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<meta charset=""utf-8"">
<title>Claim Rejected</title>
<style>
body{
    font-family:Arial,sans-serif;
    background:#f4f6f9;
    display:flex;
    justify-content:center;
    align-items:center;
    height:100vh;
    margin:0;
}
.card{
    background:white;
    padding:30px;
    border-radius:12px;
    box-shadow:0 2px 10px rgba(0,0,0,0.15);
    text-align:center;
    width:90%;
    max-width:400px;
}
.icon{
    font-size:60px;
}
.title{
    color:red;
    font-size:24px;
    font-weight:bold;
    margin-top:10px;
}
.message{
    margin-top:10px;
    color:#555;
}
</style>
</head>
<body>

<div class='card'>
    <div class='icon' style='color:red;'>✖</div>
    <div class='title'>Claim Rejected</div>
    <div class='message'>
        The claim has been rejected successfully.
    </div>
</div>

</body>
</html>
", "text/html");
            }
            catch (Exception ex)
            {
                return Content(ex.Message);
            }
        }


        [HttpGet("reject-form")]
        public IActionResult RejectForm(Guid token)
        {
            return Content($@"
<!DOCTYPE html>
<html>
<head>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Reject Claim</title>

<style>
body {{
    margin:0;
    padding:20px;
    background:#f5f5f5;
    font-family:Segoe UI,Arial,sans-serif;
}}

.card {{
    max-width:500px;
    margin:40px auto;
    background:white;
    padding:25px;
    border-radius:12px;
    box-shadow:0 2px 10px rgba(0,0,0,.15);
}}

h2 {{
    text-align:center;
    color:#dc3545;
}}

label {{
    font-weight:600;
}}

textarea {{
    width:100%;
    min-height:120px;
    margin-top:10px;
    padding:10px;
    border:1px solid #ccc;
    border-radius:8px;
    resize:none;
}}

button {{
    width:100%;
    margin-top:20px;
    padding:12px;
    background:#dc3545;
    color:white;
    border:none;
    border-radius:8px;
    font-size:16px;
}}

button:hover {{
    background:#c82333;
}}
</style>
</head>

<body>

<div class='card'>

<h2>Reject Claim</h2>

<form method='post' action='/api/TravelMaster/mail-reject'>

<input type='hidden'
       name='token'
       value='{token}' />

<label>Reason For Rejection</label>

<textarea name='reason'>NA</textarea>

<button type='submit'>
Reject Claim
</button>

</form>

</div>

</body>
</html>",
        "text/html");
        }

        [HttpPost("mail-reject")]
        public async Task<IActionResult> MailReject(
    [FromForm] Guid token,
    [FromForm] string reason)
        {
            try
            {
                // =====================================
                // Find Pending Approval
                // =====================================

                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ApprovalToken == token &&
                        x.Status == "Pending");

                if (approval == null)
                {
                    return Content(@"
<!DOCTYPE html>
<html>
<head>
<meta name='viewport' content='width=device-width, initial-scale=1'>
</head>

<body style='font-family:Segoe UI;text-align:center;padding-top:70px;background:#f5f5f5'>

<div style='max-width:500px;margin:auto;background:white;padding:30px;border-radius:12px;box-shadow:0 2px 10px rgba(0,0,0,.15)'>

<h2 style='color:red'>
Invalid or Expired Rejection Link
</h2>

<p>
This approval link has already been used or is no longer valid.
</p>

</div>

</body>
</html>",
        "text/html");
                }

                // =====================================
                // Reject Current Approval
                // =====================================

                approval.Status = "Rejected";

                approval.Remarks =
                    string.IsNullOrWhiteSpace(reason)
                    ? "NA"
                    : reason.Trim();

                approval.ActionDate = DateTime.Now;

                // =====================================
                // Update Claim
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == approval.ClaimId);

                if (claim == null)
                {
                    return Content("Claim Not Found");
                }

                claim.Status = "Rejected";
                claim.RejectedAt = DateTime.Now;
                claim.CurrentApprovalLevel = null;
                claim.LastModifiedAt = DateTime.Now;

                // =====================================
                // Cancel Remaining Pending Approvals
                // =====================================

                var futureApprovals = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == approval.ClaimId &&
                        x.ApprovalLevel > approval.ApprovalLevel &&
                        x.Status == "Pending")
                    .ToListAsync();

                foreach (var item in futureApprovals)
                {
                    item.Status = "Cancelled";
                    item.Remarks = "Cancelled due to previous level rejection";
                    item.ActionDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                // =====================================
                // Success Page
                // =====================================

                return Content(@"
<!DOCTYPE html>
<html>
<head>
<meta name='viewport' content='width=device-width, initial-scale=1'>
</head>

<body style='font-family:Segoe UI;background:#f5f5f5'>

<div style='max-width:500px;
margin:80px auto;
background:white;
padding:40px;
border-radius:12px;
box-shadow:0 4px 15px rgba(0,0,0,.15);
text-align:center;'>

<div style='font-size:70px;color:#d32f2f'>
❌
</div>

<h2 style='color:#d32f2f;margin-top:10px'>
Claim Rejected Successfully
</h2>

<p style='font-size:16px;color:#555'>
The claim has been rejected successfully.
</p>

<hr>

<p>
<b>Reason</b>
</p>

<div style='background:#fff3f3;
padding:15px;
border-left:5px solid #d32f2f;
border-radius:6px;
font-size:15px;'>

" + (string.IsNullOrWhiteSpace(reason) ? "NA" : reason) + @"

</div>

<br>

<p style='color:#888'>
You may now close this window.
</p>

</div>

</body>
</html>",
        "text/html");
            }
            catch (Exception ex)
            {
                return Content($@"
<html>
<body style='font-family:Segoe UI;text-align:center;padding-top:60px'>
<h2 style='color:red'>Error</h2>
<p>{ex.Message}</p>
</body>
</html>",
        "text/html");
            }
        }


        //-------Reject resubmit Apiii-------------//

        [HttpPut("resubmit-claim/{claimId}")]
        public async Task<IActionResult> ResubmitClaim(
    int claimId,
    [FromBody] CreateClaimDto dto)
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

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                int loginEmpId = int.Parse(empIdClaim);

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == claimId);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                if (claim.EmployeeId != loginEmpId)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "You are not authorized to edit this claim."
                    });
                }

                if (claim.Status != "Rejected")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Only rejected claims can be resubmitted."
                    });
                }

                decimal totalAmount = dto.ClaimEntries.Sum(x =>
                    x.TravelAmount +
                    x.FoodAmount +
                    (x.TollAmount ?? 0) +
                    (x.AutoAmount ?? 0) +
                    (x.OtherAmount ?? 0));

                // ===============================
                // Update Claim Master
                // ===============================

                claim.ClaimMonth = dto.ClaimMonth;
                claim.ClaimYear = dto.ClaimMonth.Year;
                claim.TotalAmount = totalAmount;
                claim.Remarks = dto.ClaimEntries.FirstOrDefault()?.Remarks;

                claim.Status = "Pending";
                claim.SubmittedAt = DateTime.Now;
                claim.ApprovedAt = null;
                claim.RejectedAt = null;
                claim.CurrentApprovalLevel = 1;
                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();
                // ==========================================
                // Delete Old Travel / Food / Expense
                // ==========================================

                var oldTravels = await _context.ClaimTravels
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                var oldFoods = await _context.ClaimFoods
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                var oldExpenses = await _context.ClaimExpenses
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                _context.ClaimExpenses.RemoveRange(oldExpenses);
                _context.ClaimFoods.RemoveRange(oldFoods);
                _context.ClaimTravels.RemoveRange(oldTravels);

                await _context.SaveChangesAsync();

                // ==========================================
                // Insert Updated Claim Entries
                // ==========================================

                foreach (var item in dto.ClaimEntries)
                {
                    var currentRate = await _context.VehicleRateMasters
    .Where(x =>
        x.VehicleTypeId == item.VehicleTypeId &&
        x.FuelTypeId == item.FuelTypeId &&
        x.IsActive)
    .OrderByDescending(x => x.ValidFrom)
    .FirstOrDefaultAsync();

                    if (currentRate == null)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Rate not configured for VehicleType {item.VehicleTypeId} and FuelType {item.FuelTypeId}"
                        });
                    }
                    // ----------------------------
                    // Travel
                    // ----------------------------

                    var travel = new ClaimTravel
                    {
                        VehicleTypeId = item.VehicleTypeId,
                        FuelTypeId = item.FuelTypeId,
                        VehicleDetailId = item.VehicleDetailId,
                        VehicleNumber = item.VehicleNumber,
                        AppliedRatePerKm = currentRate.AmountPerKm,
                        ClaimId = claim.ClaimId,
                        TravelDate = item.TravelDate,
                        Purpose = item.Purpose,
                        FromLocation = item.FromLocation,
                        ToLocation = item.ToLocation,
                        KmRun = item.KmRun,
                        BusorTrainInvoiceNumber = item.BusorTrainInvoiceNumber,
                        TaxiInvoiceNumber = item.TaxiInvoiceNumber,
                        Amount = item.TravelAmount,
                        Remarks = item.TravelRemarks,
                        IsActive = true
                    };

                    _context.ClaimTravels.Add(travel);

                    await _context.SaveChangesAsync();

                    // ----------------------------
                    // Food
                    // ----------------------------

                    var food = new ClaimFood
                    {
                        ClaimId = claim.ClaimId,
                        TravelId = travel.TravelId,
                        FoodDate = item.TravelDate,
                        Breakfast = item.Breakfast,
                        Lunch = item.Lunch,
                        Dinner = item.Dinner,
                        Amount = item.FoodAmount,
                        BreakfastInvoiceNumber = item.BreakfastInvoiceNumber,
                        LunchInvoiceNumber = item.LunchInvoiceNumber,
                        DinnerInvoiceNumber = item.DinnerInvoiceNumber,
                        IsActive = true
                    };

                    _context.ClaimFoods.Add(food);

                    // ----------------------------
                    // Expense
                    // ----------------------------

                    if ((item.TollAmount ?? 0) > 0)
                    {
                        _context.ClaimExpenses.Add(new ClaimExpense
                        {
                            ClaimId = claim.ClaimId,
                            TravelId = travel.TravelId,
                           
                            ExpenseDate = item.TravelDate,
                            TollAmount = item.TollAmount ?? 0,
                            Remarks = item.ExpenseRemarks,
                            IsActive = true,
                            created_at = DateTime.Now
                        });
                    }

                    if ((item.AutoAmount ?? 0) > 0)
                    {
                        _context.ClaimExpenses.Add(new ClaimExpense
                        {
                            ClaimId = claim.ClaimId,
                            TravelId = travel.TravelId,
                            
                            ExpenseDate = item.TravelDate,
                            AutoAmount = item.AutoAmount ?? 0,
                            Remarks = item.ExpenseRemarks,
                            IsActive = true,
                            created_at = DateTime.Now
                        });
                    }

                    if ((item.OtherAmount ?? 0) > 0)
                    {
                        _context.ClaimExpenses.Add(new ClaimExpense
                        {
                            ClaimId = claim.ClaimId,
                            TravelId = travel.TravelId,
                            
                            ExpenseDate = item.TravelDate,
                            OtherAmount = item.OtherAmount ?? 0,
                            Remarks = item.ExpenseRemarks,
                            IsActive = true,
                            created_at = DateTime.Now
                        });
                    }
                }

                await _context.SaveChangesAsync();
                // ==========================================
                // Delete Old Approval Records
                // ==========================================

                var oldApprovals = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                _context.ClaimApprovals.RemoveRange(oldApprovals);

                await _context.SaveChangesAsync();

                // ==========================================
                // Recreate Approval Flow
                // ==========================================

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x => x.EmpId == claim.EmployeeId);

                if (employee == null)
                {
                    throw new Exception("Employee not found.");
                }

                // ==========================
                // Level 1 - Reporting Manager
                // ==========================

                if (employee.ReportingManagerId.HasValue)
                {
                    _context.ClaimApprovals.Add(new ClaimApproval
                    {
                        ClaimId = claim.ClaimId,
                        ApprovalLevel = 1,
                        ApproverEmpId = employee.ReportingManagerId.Value,
                        Status = "Pending",
                        CreatedAt = DateTime.Now,
                        ApprovalToken = Guid.NewGuid()
                    });
                }

                // ==========================
                // Level 2 - Manager's Manager
                // ==========================

                if (employee.ReportingManagerId.HasValue)
                {
                    var manager = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x => x.EmpId == employee.ReportingManagerId.Value);

                    if (manager != null &&
                        manager.ReportingManagerId.HasValue &&
                        manager.ReportingManagerId.Value > 0)
                    {
                        _context.ClaimApprovals.Add(new ClaimApproval
                        {
                            ClaimId = claim.ClaimId,
                            ApprovalLevel = 2,
                            ApproverEmpId = manager.ReportingManagerId.Value,
                            Status = "Pending",
                            CreatedAt = DateTime.Now,
                            ApprovalToken = Guid.NewGuid()
                        });
                    }
                }

                // ==========================
                // HR / Accounts
                // ==========================

                var approvalMatrix = await _context.ApprovalMatrix
                    .Where(x =>
                        x.OrganizationId == employee.OrganizationId &&
                        x.IsActive)
                    .OrderBy(x => x.ApprovalLevel)
                    .ToListAsync();

                foreach (var item in approvalMatrix)
                {
                    var approver = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == employee.OrganizationId &&
                            x.DesignationId == item.DesignationId);

                    _context.ClaimApprovals.Add(new ClaimApproval
                    {
                        ClaimId = claim.ClaimId,
                        ApprovalLevel = item.ApprovalLevel,
                        ApproverEmpId = approver?.EmpId,
                        Status = "Pending",
                        CreatedAt = DateTime.Now,
                        ApprovalToken = Guid.NewGuid()
                    });
                }

                await _context.SaveChangesAsync();

                // ==========================================
                // First Approver
                // ==========================================

                var firstApproval = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .OrderBy(x => x.ApprovalLevel)
                    .FirstOrDefaultAsync();
                // ==========================================
                // Send Mail To Level 1
                // ==========================================

                if (firstApproval != null)
                {
                    var approver = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x => x.EmpId == firstApproval.ApproverEmpId);

                    if (approver != null &&
                        !string.IsNullOrWhiteSpace(approver.EmailId))
                    {
                        string departmentName = "-";

                        if (employee.DepartmentId.HasValue)
                        {
                            var department = await _context.Department_Master
                                .FirstOrDefaultAsync(x => x.DepartmentId == employee.DepartmentId.Value);

                            if (department != null)
                            {
                                departmentName = department.DepartmentName;
                            }
                        }

                        string purpose = dto.ClaimEntries
                            .FirstOrDefault()?.Purpose ?? "-";

                        var baseUrl = _configuration["AppSettings:BaseUrl"];

                        var approveUrl =
                            $"{baseUrl}/api/TravelMaster/mail-approve?token={firstApproval.ApprovalToken}";

                        var rejectUrl =
                            $"{baseUrl}/api/TravelMaster/reject-form?token={firstApproval.ApprovalToken}";

                        string body = $@"

Dear {approver.FullName},

<br/><br/>

A new Local expense claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {employee.FullName}</li>
    <li>Employee ID : {employee.EmployeeCode}</li>
    <li>Department : {departmentName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
</ul>

<b>Purpose</b>

<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Approve

</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;
color:white;
padding:10px 20px;
text-decoration:none;
border-radius:5px;'>

Reject

</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

                        try
                        {
                            await _emailService.SendEmailAsync(
                                approver.EmailId,
                                $"Resubmitted Claim Approval Required - {claim.ClaimNo}",
                                body);
                        }
                        catch
                        {
                            // Mail failure should NOT stop resubmission
                        }
                    }
                }

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Claim resubmitted successfully.",
                    claimId = claim.ClaimId,
                    claimNo = claim.ClaimNo
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("pending-approvals")]
        public async Task<IActionResult> GetPendingApprovals()
        {
            try
            {
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                int empId = int.Parse(empIdClaim);

                var data = await (
                    from ca in _context.ClaimApprovals
                    join cm in _context.ClaimMasters
                        on ca.ClaimId equals cm.ClaimId
                    join emp in _context.EmployeeMasters
                        on cm.EmployeeId equals emp.EmpId
                    where ca.ApproverEmpId == empId
                          && ca.Status == "Pending"
                          && cm.Status == "Pending"
                          && cm.CurrentApprovalLevel == ca.ApprovalLevel
                    select new
                    {
                        cm.ClaimId,
                        cm.ClaimNo,

                        EmployeeId = emp.EmpId,
                        EmployeeName = emp.FullName,
                        EmployeeCode = emp.EmployeeCode,

                        cm.TotalAmount,
                        cm.ClaimMonth,

                        ApprovalLevel = ca.ApprovalLevel,
                        SubmittedDate = cm.SubmittedAt,
                        PendingSince = ca.CreatedAt
                    })
                    .OrderByDescending(x => x.PendingSince)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    count = data.Count,
                    data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching pending approvals.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        [HttpGet("claim-approval-history/{claimId}")]
        public async Task<IActionResult> GetClaimApprovalHistory(int claimId)
        {
            try
            {
                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == claimId);

                if (claim == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                var approvals = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claimId)
                    .OrderBy(x => x.ApprovalLevel)
                    .Select(x => new
                    {
                        x.ApprovalLevel,
                        x.ApproverEmpId,
                        x.Status,
                        x.Remarks,
                        x.ActionDate
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    currentApprovalLevel = claim.CurrentApprovalLevel,
                    claimStatus = claim.Status,
                    data = approvals
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching approval history.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

     

        [HttpGet("my-claims")]
        public async Task<IActionResult> GetMyClaims()
        {
            try
            {
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                int empId = int.Parse(empIdClaim);

                var claims = await _context.ClaimMasters
                    .Where(x =>
                        x.EmployeeId == empId &&
                        x.Status != "Draft")
                    .OrderByDescending(x => x.ClaimId)
                    .ToListAsync();

                var result = new List<MyClaimDto>();

                foreach (var claim in claims)
                {
                    var approvals = await _context.ClaimApprovals
                        .Where(x => x.ClaimId == claim.ClaimId)
                        .OrderBy(x => x.ApprovalLevel)
                        .ToListAsync();

                    // ===========================================
                    // Last Action
                    // ===========================================

                    string lastActionBy = "-";
                    string lastAction = "-";

                    var lastActionRecord = approvals
                        .Where(x =>
                            x.Status == "Approved" ||
                            x.Status == "Rejected")
                        .OrderByDescending(x => x.ApprovalLevel)
                        .FirstOrDefault();

                    if (lastActionRecord != null)
                    {
                        lastAction = lastActionRecord.Status;

                        var approver = await _context.EmployeeMasters
                            .FirstOrDefaultAsync(x =>
                                x.EmpId == lastActionRecord.ApproverEmpId);

                        if (approver != null)
                        {
                            lastActionBy = approver.FullName;
                        }
                    }

                    // ===========================================
                    // Approval Status
                    // ===========================================

                    string GetStatus(int level)
                    {
                        var approval = approvals
                            .FirstOrDefault(x => x.ApprovalLevel == level);

                        if (approval == null)
                            return "-";

                        switch (approval.Status)
                        {
                            case "Approved":
                                return "Approved";

                            case "Rejected":
                                return "Rejected";

                            case "Cancelled":
                                return "Cancelled";

                            case "Pending":
                                if (claim.CurrentApprovalLevel == level)
                                    return "Waiting For Approval";
                                return "Pending";

                            default:
                                if (claim.Status == "Approved")
                                    return "Approved";
                                return "Pending";
                        }
                    }

                    result.Add(new MyClaimDto
                    {
                        ClaimId = claim.ClaimId,
                        ClaimNo = claim.ClaimNo ?? "",
                        ClaimMonth = claim.ClaimMonth,
                        TotalAmount = claim.TotalAmount ?? 0,
                        Status = claim.Status,
                        Remarks = claim.Remarks,
                        CurrentApprovalLevel = claim.CurrentApprovalLevel,

                        LastActionBy = lastActionBy,
                        LastAction = lastAction,

                        Approver1 = GetStatus(1),
                        Approver2 = GetStatus(2),
                        HrLevel1 = GetStatus(3),
                        HrLevel2 = GetStatus(4),
                        Accounts = GetStatus(5)
                    });
                }

                return Ok(new
                {
                    success = true,
                    count = result.Count,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("claim-details/{claimId}")]
        public async Task<IActionResult> GetClaimDetails(int claimId)
        {
            try
            {
                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x => x.ClaimId == claimId);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                var employee = await _context.EmployeeMasters
                 .FirstOrDefaultAsync(x => x.EmpId == claim.EmployeeId);

                string? designationName = null;

                if (employee?.DesignationId != null)
                {
                    designationName = await _context.Designation
                        .Where(d => d.Id == employee.DesignationId)
                        .Select(d => d.Name)
                        .FirstOrDefaultAsync();
                }


                // =====================================
                // Travel Details
                // =====================================

                var travels = await (
    from t in _context.ClaimTravels
    join v in _context.VehicleMaster
        on t.VehicleTypeId equals v.VehicleId
    join f in _context.FuelTypeMasters
        on t.FuelTypeId equals f.FuelTypeId
    where t.ClaimId == claimId
    select new
    {
        t.TravelId,

        t.VehicleTypeId,
        VehicleType = v.VehicleName,

        t.FuelTypeId,
        FuelType = f.FuelTypeName,

        t.VehicleDetailId,
        t.VehicleNumber,

        t.AppliedRatePerKm,

        t.TravelDate,
        t.Purpose,
        t.FromLocation,
        t.ToLocation,
        t.BusorTrainInvoiceNumber,
        t.TaxiInvoiceNumber,
        t.KmRun,
        t.Amount,
        t.Remarks
    }).ToListAsync();

                // =====================================
                // Food Details
                // =====================================

                var foods = await _context.ClaimFoods
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                // =====================================
                // Expense Details
                // =====================================

                var expenses = await _context.ClaimExpenses
                    .Where(x => x.ClaimId == claimId)
                    .ToListAsync();

                // =====================================
                // Documents
                // =====================================

                var documents = await _context.ClaimDocuments
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .Select(x => new
                    {
                        x.DocumentId,
                        x.ReferenceId,
                        x.ModuleType,
                        x.DocumentType,
                        x.OriginalFileName,
                        x.StoredFileName,
                        x.FileExtension,
                        x.FilePath,
                        x.UploadedDate
                    })
                    .ToListAsync();

                // =====================================
                // Approval History
                // =====================================

                var approvals = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claimId)
                    .OrderBy(x => x.ApprovalLevel)
                    .Select(x => new
                    {
                        x.ApprovalLevel,
                        x.ApproverEmpId,
                        ApproverName = _context.EmployeeMasters
            .Where(e => e.EmpId == x.ApproverEmpId)
            .Select(e => e.FullName)
            .FirstOrDefault(),

                        x.Status,
                        x.Remarks,
                        x.ActionDate
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,

                    claim = new
                    {
                        claim.ClaimId,
                        claim.Remarks,
                        claim.ClaimNo,
                        claim.EmployeeId,
                        EmployeeName = employee?.FullName,
                        Designation = designationName,


                        claim.ClaimMonth,
                        claim.TotalAmount,
                        claim.Status,
                        claim.CurrentApprovalLevel,
                        claim.CreatedAt,
                        claim.SubmittedAt,
                        claim.ApprovedAt,
                        claim.RejectedAt
                    },

                    travels,
                    foods,
                    expenses,
                    documents,
                    approvals
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching claim details.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        //----------------EMail set up-------------------/

        public class EmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendEmailAsync(
            string toEmail,
            string subject,
            string body)
        {
            try
            {
                var email = new MimeMessage();

                email.From.Add(
                    new MailboxAddress(
                        _settings.DisplayName,
                        _settings.Email));

                email.To.Add(
                    MailboxAddress.Parse(toEmail));

                email.Subject = subject;

                email.Body = new TextPart("html")
                {
                    Text = body
                };

                using var smtp = new SmtpClient();

                await smtp.ConnectAsync(
                    _settings.Host,
                    _settings.Port,
                    SecureSocketOptions.StartTls);

                await smtp.AuthenticateAsync(
                    _settings.Email,
                    _settings.Password);

                await smtp.SendAsync(email);

                await smtp.DisconnectAsync(true);
            }
            catch
            {
                throw;
            }
        }
}

        


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


