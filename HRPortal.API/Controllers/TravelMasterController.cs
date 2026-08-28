using ClosedXML.Excel;
using DocumentFormat.OpenXml.InkML;
using HRPortal.API.Data;
using HRPortal.API.DTOs;
using HRPortal.API.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
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

                // Get active vehicles
                var vehicles = await _context.Vehicles
                    .Where(v => v.EmployeeId == employeeId && v.IsActive)
                    .ToListAsync();

                var result = new List<object>();

                foreach (var vehicle in vehicles)
                {
                    // Find Vehicle Type ID using Vehicle Type Name
                    var vehicleType = await _context.VehicleMaster
                        .FirstOrDefaultAsync(x =>
                            x.VehicleName == vehicle.VehicleType &&
                            x.IsActive);

                    // Find Fuel Type ID using Fuel Type Name
                    var fuelType = await _context.FuelTypeMasters
                        .FirstOrDefaultAsync(x =>
                            x.FuelTypeName == vehicle.FuelType &&
                            x.IsActive);

                    result.Add(new
                    {
                        vehicleDetailId = vehicle.VehicleId,

                        vehicleTypeId = vehicleType?.VehicleId ?? 0,

                        fuelTypeId = fuelType?.FuelTypeId ?? 0,

                        vehicleNumber = vehicle.VehicleNumber,

                        vehicleType = vehicle.VehicleType,

                        fuelType = vehicle.FuelType,

                        gearType = vehicle.GearType,

                        ccRange = vehicle.CCRange
                    });
                }

                return Ok(new
                {
                    success = true,

                    message = result.Any()
                        ? "Vehicles fetched successfully."
                        : "No vehicles found.",

                    data = result
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

        [Authorize]
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
        [Authorize]
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



        //-----------NumberSeriesConfiguration----------------------//
        [Authorize]
        [HttpGet("number-series-configurations")]
        public async Task<IActionResult> GetNumberSeriesConfigurations()
        {
            try
            {
                var data = await _context.NumberSeriesConfigurations
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.ModuleName)
                    .ToListAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Active configurations fetched successfully.",
                    Data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Success = false,
                    Message = "An error occurred while fetching configurations.",
                    Error = ex.Message,
                    InnerException = ex.InnerException?.Message
                });
            }
        }
        [Authorize]
        [HttpGet("number-series-configurations/{id}")]
        public async Task<IActionResult> GetNumberSeriesConfigurationById(int id)
        {
            try
            {
                var configuration = await _context.NumberSeriesConfigurations
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (configuration == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        Message = "Configuration not found."
                    });
                }

                return Ok(new
                {
                    Success = true,
                    Message = "Configuration fetched successfully.",
                    Data = configuration
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Success = false,
                    Message = "An error occurred while fetching the configuration.",
                    Error = ex.Message,
                    InnerException = ex.InnerException?.Message
                });
            }
        }
        [Authorize]
        [HttpPost("number-series-configurations")]
        public async Task<IActionResult> AddNumberSeriesConfiguration(
         [FromBody] NumberSeriesConfigurationDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Request data is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.ModuleName))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Module name is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.Prefix))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Prefix is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.CurrentNumber))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Current number is required."
                    });
                }

                var employeeIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(employeeIdClaim, out int employeeId))
                {
                    return Unauthorized(new
                    {
                        Success = false,
                        Message = "Invalid employee information."
                    });
                }

                var exists = await _context.NumberSeriesConfigurations
                    .AnyAsync(x =>
                        x.ModuleName == dto.ModuleName &&
                        x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Configuration already exists for this module."
                    });
                }

                var configuration = new NumberSeriesConfiguration
                {
                    ModuleName = dto.ModuleName.Trim(),

                    Prefix = dto.Prefix.Trim(),

                    Suffix = string.IsNullOrWhiteSpace(dto.Suffix)
                        ? null
                        : dto.Suffix.Trim(),

                    // Exact value preserved
                    // "01" -> "01"
                    // "36" -> "36"
                    // "0036" -> "0036"
                    CurrentNumber = dto.CurrentNumber.Trim(),

                    IncludeFinancialYear = dto.IncludeFinancialYear,

                    IsActive = true,

                    CreatedBy = employeeId,
                    CreatedDate = DateTime.Now,

                    ModifiedBy = null,
                    ModifiedDate = null
                };

                _context.NumberSeriesConfigurations.Add(configuration);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Configuration created successfully.",
                    Data = configuration
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        Success = false,
                        Message = "An error occurred while creating the configuration.",
                        Error = ex.Message,
                        InnerException = ex.InnerException?.Message
                    });
            }
        }
        [Authorize]
        [HttpPut("number-series-configurations/{id}")]
        public async Task<IActionResult> UpdateNumberSeriesConfiguration(int id, NumberSeriesConfigurationDto dto)
        {
            try
            {
                var employeeId = Convert.ToInt32(User.FindFirst("EmpId")?.Value);

                var configuration = await _context.NumberSeriesConfigurations
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (configuration == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        Message = "Configuration not found."
                    });
                }

                configuration.ModuleName = dto.ModuleName;
                configuration.Prefix = dto.Prefix;
                configuration.Suffix = dto.Suffix;
              
                configuration.CurrentNumber = dto.CurrentNumber;
            
                configuration.IncludeFinancialYear = dto.IncludeFinancialYear;
                configuration.IsActive = dto.IsActive;
                configuration.ModifiedDate = DateTime.Now;
                configuration.CreatedDate = DateTime.Now;
                configuration.CreatedBy = employeeId;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Configuration updated successfully.",
                    Data = configuration
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while updating the configuration.",
                    Error = ex.Message,
                    InnerException = ex.InnerException?.Message
                });
            }
        }
        [Authorize]
        [HttpDelete("number-series-configurations/{id}")]
        public async Task<IActionResult> DeleteNumberSeriesConfiguration(int id)
        {
            try
            {
                var configuration = await _context.NumberSeriesConfigurations
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (configuration == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        Message = "Configuration not found."
                    });
                }

                if (!configuration.IsActive)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Configuration is already deleted."
                    });
                }

                configuration.IsActive = false;
                configuration.ModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Configuration deleted successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Success = false,
                    Message = "An error occurred while deleting the configuration.",
                    Error = ex.Message,
                    InnerException = ex.InnerException?.Message
                });
            }
        }
        private async Task<string> GenerateClaimNumber(string moduleName)
        {
            var configuration = await _context.NumberSeriesConfigurations
                .FirstOrDefaultAsync(x =>
                    x.ModuleName == moduleName &&
                    x.IsActive);

            if (configuration == null)
            {
                throw new Exception(
                    $"Number series configuration not found for module '{moduleName}'.");
            }

            string runningNumber = configuration.CurrentNumber;

            if (string.IsNullOrWhiteSpace(runningNumber))
            {
                throw new Exception(
                    $"Current number is empty for module '{moduleName}'.");
            }

            // Prefix
            // DB: LEC-  -> LEC
            string prefix = configuration.Prefix?
                .Trim()
                .TrimEnd('-') ?? "";

            // Suffix
            // DB: -26/27 -> 26/27
            string suffix = configuration.Suffix?
                .Trim()
                .Trim('-') ?? "";

            string claimNumber;

            if (configuration.IncludeFinancialYear)
            {
                claimNumber = $"{prefix}-{runningNumber}-{suffix}";
            }
            else
            {
                claimNumber = $"{prefix}-{runningNumber}";
            }

            // Increment CurrentNumber

            if (!int.TryParse(
                configuration.CurrentNumber,
                out int currentNumber))
            {
                throw new Exception(
                    $"Invalid current number '{configuration.CurrentNumber}' " +
                    $"for module '{moduleName}'.");
            }

            int digitLength = configuration.CurrentNumber.Length;

            currentNumber++;

            configuration.CurrentNumber =
                currentNumber.ToString($"D{digitLength}");

            configuration.ModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            return claimNumber;
        }

        //-------------------Admin VehicleRate ditails-------------//
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
        public async Task<IActionResult> GetNextClaimNumber(
     [FromQuery] string moduleName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Module name is required."
                    });
                }

                var configuration = await _context.NumberSeriesConfigurations
                    .FirstOrDefaultAsync(x =>
                        x.ModuleName == moduleName &&
                        x.IsActive);

                if (configuration == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"Configuration not found for module '{moduleName}'."
                    });
                }

                // CurrentNumber exact format is preserved
                // "01" -> "01"
                // "36" -> "36"
                // "0036" -> "0036"
                string runningNumber = configuration.CurrentNumber;

                string financialYear = string.Empty;

                if (configuration.IncludeFinancialYear)
                {
                    var today = DateTime.Now;

                    if (today.Month >= 4)
                    {
                        financialYear =
                            $"{today.Year}/{today.Year + 1}";
                    }
                    else
                    {
                        financialYear =
                            $"{today.Year - 1}/{today.Year}";
                    }
                }

                string claimNumber;

                // "-" is fixed separator
                if (configuration.IncludeFinancialYear)
                {
                    claimNumber =
                        $"{configuration.Prefix}-" +
                        $"{runningNumber}-" +
                        $"{financialYear}";
                }
                else
                {
                    claimNumber =
                        $"{configuration.Prefix}-" +
                        $"{runningNumber}";
                }

                return Ok(new
                {
                    success = true,
                    claimNumber = claimNumber
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message = "Error while generating claim number.",
                        error = ex.Message,
                        innerException = ex.InnerException?.Message
                    });
            }
        }
        [Authorize]
        [HttpGet("food-types")]
        public async Task<IActionResult> GetFoodTypes()
        {
            try
            {
                var foodTypes = await _context.FoodTypeMasters
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.FoodTypeId)
                    .Select(x => new
                    {
                        foodTypeId = x.FoodTypeId,
                        foodTypeName = x.FoodTypeName
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = foodTypes
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching food types.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpPost("create-claim")]
        public async Task<IActionResult> CreateClaim(
            [FromBody] CreateClaimDto dto,
            [FromQuery] bool isSubmit = false)
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

                // ============================================
                // ONE EMPLOYEE = ONE CLAIM PER MONTH
                // ============================================

                var claimMonth = dto.ClaimMonth.Month;
                var claimYear = dto.ClaimMonth.Year;

                var existingClaim = await _context.ClaimMasters
       .FirstOrDefaultAsync(x =>
           x.EmployeeId == dto.EmployeeId &&
           x.ClaimMonth.Month == claimMonth &&
           x.ClaimYear == claimYear &&
           x.IsActive);

                if (existingClaim != null &&
                    existingClaim.Status != "Draft" &&
                    existingClaim.Status != "Rejected")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Claim already exists for {dto.ClaimMonth:MMMM yyyy}."
                    });
                }



                // ============================================
                // CALCULATE TOTAL AMOUNT
                // ============================================

                decimal totalAmount = dto.ClaimEntries.Sum(x =>
                    x.TravelAmount +
                    x.FoodAmount +
                    (x.Expenses?.Sum(e => e.ExpenseAmount) ?? 0)
                );

                // ============================================
                // CREATE CLAIM MASTER
                // ============================================

                ClaimMaster claim;

                if (existingClaim != null)
                {
                    // Existing monthly draft
                    claim = existingClaim;

                    // Add this save's amount to existing claim total
                    claim.TotalAmount = (claim.TotalAmount ?? 0) + totalAmount;

                    claim.LastModifiedAt = DateTime.Now;
                }
                else
                {
                    // First save for this month
                    claim = new ClaimMaster
                    {
                        EmployeeId = dto.EmployeeId,
                        ClaimMonth = dto.ClaimMonth,
                        ClaimYear = dto.ClaimMonth.Year,
                        CreatedAt = DateTime.Now,
                        LastModifiedAt = DateTime.Now,
                        TotalAmount = totalAmount,
                        IsActive = true,

                        // CreateClaim API always saves as Draft
                        Status = "Draft",

                        SubmittedAt = null,
                        CurrentApprovalLevel = null
                    };

                    _context.ClaimMasters.Add(claim);

                    await _context.SaveChangesAsync();

                    // Generate claim number only once
                    claim.ClaimNo =
                        await GenerateClaimNumber("Local Expense Claim");

                    await _context.SaveChangesAsync();
                }

                // ============================================
                // EXISTING APPROVAL FLOW STARTS HERE
                // DON'T CHANGE THIS SECTION
                // ============================================

                if (isSubmit)
                {
                    var employee = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == dto.EmployeeId);

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
                var savedTravelIds = new List<object>();

                foreach (var entry in dto.ClaimEntries)
                {
                    // ===========================
                    // GET VEHICLE RATE
                    // ===========================

                    decimal? appliedRatePerKm = null;
                    decimal travelAmount = 0;

                    // Bike = 2
                    // Car = 3
                    // Rate required only for Bike and Car
                    if (entry.VehicleTypeId == 2 || entry.VehicleTypeId == 3)
                    {
                        // FuelTypeId mandatory for Bike / Car
                        if (!entry.FuelTypeId.HasValue || entry.FuelTypeId.Value <= 0)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message = $"Fuel Type is required for VehicleTypeId {entry.VehicleTypeId}."
                            });
                        }

                        var currentRate = await _context.VehicleRateMasters
                            .Where(x =>
                                x.VehicleTypeId == entry.VehicleTypeId &&
                                x.FuelTypeId == entry.FuelTypeId.Value &&
                                x.IsActive)
                            .OrderByDescending(x => x.ValidFrom)
                            .FirstOrDefaultAsync();

                        if (currentRate == null)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message =
                                    $"Vehicle rate is not configured for VehicleTypeId {entry.VehicleTypeId} " +
                                    $"and FuelTypeId {entry.FuelTypeId.Value}."
                            });
                        }

                        // ===========================
                        // RATE USED FOR THIS CLAIM
                        // ===========================

                        appliedRatePerKm = currentRate.AmountPerKm;

                        // ===========================
                        // CALCULATE TRAVEL AMOUNT
                        // ===========================

                        travelAmount =
                            entry.KmRun * currentRate.AmountPerKm?? 0;
                    }
                    else
                    {
                        // Bus / Train / other vehicles
                        // No vehicle rate required

                        appliedRatePerKm = null;

                        // If manually entered amount is allowed
                        travelAmount = entry.TravelAmount;
                    }


                    // ===========================
                    // SAVE TRAVEL
                    // ===========================

                    var travel = new ClaimTravel
                    {
                        ClaimId = claim.ClaimId,

                        VehicleTypeId = entry.VehicleTypeId,

                        FuelTypeId =
                            entry.FuelTypeId.HasValue &&
                            entry.FuelTypeId.Value > 0
                                ? entry.FuelTypeId.Value
                                : null,

                        VehicleDetailId =
                            entry.VehicleDetailId.HasValue &&
                            entry.VehicleDetailId.Value > 0
                                ? entry.VehicleDetailId.Value
                                : null,

                        VehicleNumber =
                            string.IsNullOrWhiteSpace(entry.VehicleNumber)
                                ? null
                                : entry.VehicleNumber,

                        TravelDate = entry.TravelDate,

                        Purpose = entry.Purpose,

                        FromLocation = entry.FromLocation,

                        ToLocation = entry.ToLocation,

                        KmRun = entry.KmRun,

                        // ⭐ CALCULATED AMOUNT
                        Amount = travelAmount,

                        // ⭐ RATE USED
                        AppliedRatePerKm = appliedRatePerKm,

                        IsActive = true
                    };

                    _context.ClaimTravels.Add(travel);

                    await _context.SaveChangesAsync();

                    // Save so TravelId gets generated
                    await _context.SaveChangesAsync();

                    

                    savedTravelIds.Add(new
                    {
                        travelId = travel.TravelId,
                        travelDate = travel.TravelDate
                    });



                    // ============================================
                    // FOOD DETAILS
                    // OPTIONAL
                    // ============================================

                    if (entry.FoodTypeId.HasValue &&
                        entry.FoodTypeId.Value > 0 &&
                        entry.FoodAmount > 0)
                    {
                        var food = new ClaimFood
                        {
                            TravelId = travel.TravelId,

                            ClaimId = claim.ClaimId,

                            FoodDate = entry.TravelDate,

                            FoodTypeId = entry.FoodTypeId.Value,

                            Amount = entry.FoodAmount,

                            IsActive = true
                        };

                        _context.ClaimFoods.Add(food);

                        await _context.SaveChangesAsync();
                    }


                    // ============================================
                    // MULTIPLE EXPENSES
                    // ============================================

                    if (entry.Expenses != null && entry.Expenses.Any())
                    {
                        foreach (var expenseItem in entry.Expenses)
                        {
                            // Ignore empty expense
                            if (string.IsNullOrWhiteSpace(expenseItem.ExpenseType) ||
                                expenseItem.ExpenseAmount <= 0)
                            {
                                continue;
                            }

                            var expense = new ClaimExpense
                            {
                                ClaimId = claim.ClaimId,

                                // IMPORTANT
                                // Expense belongs to this Travel
                                TravelId = travel.TravelId,

                                ExpenseDate = entry.TravelDate,

                                ExpenseType = expenseItem.ExpenseType,

                                InvoiceNo = expenseItem.InvoiceNo,

                                Description = expenseItem.Description,

                                Purpose = expenseItem.ExpensePurpose,

                                Amount = expenseItem.ExpenseAmount,

                                created_at = DateTime.Now,

                                IsActive = true
                            };

                            _context.ClaimExpenses.Add(expense);
                        }

                        await _context.SaveChangesAsync();
                    }
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
                        claim.TotalAmount,
                        claimEntries = savedTravelIds
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




        [Authorize]
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

        [Authorize]
        [HttpDelete("delete-draft-entry/{claimId}/{travelId}")]
        public async Task<IActionResult> DeleteDraftEntry(
     int claimId,
     int travelId)
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
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft claim not found."
                    });
                }

                // ==================================
                // Get ONLY Selected Travel Entry
                // ==================================

                var travel = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.TravelId == travelId &&
                        x.IsActive);

                if (travel == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft travel entry not found."
                    });
                }

                // ==================================
                // Soft Delete Selected Travel
                // ==================================

                travel.IsActive = false;

                // ==================================
                // Soft Delete Food
                // ONLY for this Travel Entry
                // ==================================

                var foods = await _context.ClaimFoods
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.TravelId == travelId &&
                        x.IsActive)
                    .ToListAsync();

                foreach (var food in foods)
                {
                    food.IsActive = false;
                }

                // ==================================
                // Soft Delete Expenses
                // ONLY for this Travel Entry
                // ==================================

                var expenses = await _context.ClaimExpenses
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.TravelId == travelId &&
                        x.IsActive)
                    .ToListAsync();

                foreach (var expense in expenses)
                {
                    expense.IsActive = false;
                }

                // ==================================
                // Soft Delete Documents
                // ONLY for this Travel Entry
                // ==================================

                var documents = await _context.ClaimDocuments
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.ReferenceId == travelId &&
                        x.IsActive)
                    .ToListAsync();

                foreach (var document in documents)
                {
                    document.IsActive = false;
                }

                // ==================================
                // Recalculate Claim Total
                // ==================================

                var remainingTravels = await _context.ClaimTravels
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                var remainingFoods = await _context.ClaimFoods
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                var remainingExpenses = await _context.ClaimExpenses
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                decimal totalAmount = 0;

                totalAmount += remainingTravels
                    .Sum(x => x.Amount ?? 0m);

                totalAmount += remainingFoods
                    .Sum(x => x.Amount);

                totalAmount += remainingExpenses
                    .Sum(x => x.Amount ?? 0m);

                claim.TotalAmount = totalAmount;
                claim.LastModifiedAt = DateTime.Now;

                // ==================================
                // Save
                // ==================================

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Draft entry deleted successfully.",
                    data = new
                    {
                        claimId = claimId,
                        deletedTravelId = travelId,
                        remainingClaimAmount = totalAmount
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deleting draft entry.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpGet("draft-details/{claimId}")]
        public async Task<IActionResult> GetDraftDetails(int claimId)
        {
            try
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // =====================================================
                // CLAIM MASTER
                // =====================================================

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
                        message = "Draft not found."
                    });
                }

                // =====================================================
                // TRAVELS
                // =====================================================

                var travels = await _context.ClaimTravels
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .OrderBy(x => x.TravelId)
                    .ToListAsync();

                // =====================================================
                // DOCUMENTS
                // =====================================================

                var documents = await _context.ClaimDocuments
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                // =====================================================
                // CLAIM ENTRIES
                // =====================================================

                var claimEntries = new List<object>();

                // ⭐ GRAND TOTAL FOR THIS DRAFT
                decimal grandTotalAmount = 0;

                foreach (var travel in travels)
                {
                    // =================================================
                    // FOOD
                    // =================================================

                    var food = await _context.ClaimFoods
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId &&
                            x.IsActive);

                    // =================================================
                    // ALL EXPENSES FOR THIS TRAVEL
                    // =================================================

                    var expenses = await _context.ClaimExpenses
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId &&
                            x.IsActive)
                        .ToListAsync();

                    // =================================================
                    // VEHICLE NAME
                    // =================================================

                    var vehicleName = await _context.VehicleMaster
                        .Where(v => v.VehicleId == travel.VehicleTypeId)
                        .Select(v => v.VehicleName)
                        .FirstOrDefaultAsync();

                    // =================================================
                    // EXPENSE LIST
                    // =================================================

                    var expenseList = expenses
                        .Select(e => new
                        {
                            expenseId = e.ExpenseId,
                            expenseType = e.ExpenseType,
                            invoiceNo = e.InvoiceNo,
                            description = e.Description,
                            expensePurpose = e.Purpose,
                            expenseAmount = e.Amount ?? 0
                        })
                        .ToList();

                    // =================================================
                    // TOTAL OTHER EXPENSE
                    // =================================================

                    decimal otherAmount = expenses
                        .Where(e =>
                            !string.Equals(
                                e.ExpenseType,
                                "Toll",
                                StringComparison.OrdinalIgnoreCase))
                        .Sum(e => e.Amount ?? 0);

                    // =================================================
                    // TOTAL TOLL
                    // =================================================

                    decimal tollAmount = expenses
                        .Where(e =>
                            string.Equals(
                                e.ExpenseType,
                                "Toll",
                                StringComparison.OrdinalIgnoreCase))
                        .Sum(e => e.Amount ?? 0);

                    // =================================================
                    // TRAVEL AMOUNT
                    // =================================================

                    decimal travelAmount = travel.Amount ?? 0;

                    // =================================================
                    // FOOD AMOUNT
                    // =================================================

                    decimal foodAmount = food?.Amount ?? 0;

                    // =================================================
                    // ENTRY TOTAL
                    // Travel + Food + Toll + Others
                    // =================================================

                    decimal entryTotalAmount =
                        travelAmount +
                        foodAmount +
                        tollAmount +
                        otherAmount;

                    // ⭐ ADD THIS ENTRY TOTAL TO GRAND TOTAL
                    grandTotalAmount += entryTotalAmount;

                    // =================================================
                    // DOCUMENTS
                    // =================================================

                    var travelDocuments = documents
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
                        .ToList();

                    // =================================================
                    // ADD CLAIM ENTRY
                    // =================================================

                    claimEntries.Add(new
                    {
                        // -----------------------------
                        // TRAVEL
                        // -----------------------------

                        travelId = travel.TravelId,

                        vehicleTypeId = travel.VehicleTypeId,

                        vehicleName = vehicleName,

                        fuelTypeId = travel.FuelTypeId,

                        vehicleDetailId = travel.VehicleDetailId,

                        vehicleNumber = travel.VehicleNumber,

                        appliedRatePerKm = travel.AppliedRatePerKm,

                        travelDate = travel.TravelDate,

                        purpose = travel.Purpose,

                        fromLocation = travel.FromLocation,

                        toLocation = travel.ToLocation,

                        kmRun = travel.KmRun,

                        travelAmount = travelAmount,

                        // -----------------------------
                        // FOOD
                        // -----------------------------

                        foodTypeId = food?.FoodTypeId,

                        foodAmount = foodAmount,

                        // -----------------------------
                        // EXPENSE TOTALS
                        // -----------------------------

                        tollAmount = tollAmount,

                        otherAmount = otherAmount,

                        // -----------------------------
                        // ENTRY TOTAL
                        // -----------------------------

                        totalAmount = entryTotalAmount,

                        // -----------------------------
                        // ALL EXPENSES
                        // -----------------------------

                        expenses = expenseList,

                        // -----------------------------
                        // DOCUMENTS
                        // -----------------------------

                        documents = travelDocuments
                    });
                }

                // =====================================================
                // FINAL RESPONSE
                // =====================================================

                return Ok(new
                {
                    success = true,

                    data = new
                    {
                        claimId = claim.ClaimId,

                        claimNo = claim.ClaimNo,

                        employeeId = claim.EmployeeId,

                        claimMonth = claim.ClaimMonth,

                        // ⭐ DO NOT USE claim.TotalAmount
                        // Use calculated draft total
                        totalAmount = grandTotalAmount,

                        status = claim.Status,

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
                // ============================================================
                // 1. BASIC VALIDATION
                // ============================================================

                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Request data is required."
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

                if (claimId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid claim ID."
                    });
                }


                // ============================================================
                // 2. GET LOGIN EMPLOYEE FROM JWT
                // ============================================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrWhiteSpace(empIdClaim) ||
                    !int.TryParse(empIdClaim, out int loginEmpId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }


                // ============================================================
                // 3. GET CLAIM
                // Draft / Rejected only can be edited
                // ============================================================

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
                        message = "Draft claim not found."
                    });
                }


                // ============================================================
                // 4. OWNER VALIDATION
                // ============================================================

                if (claim.EmployeeId != loginEmpId)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "You are not authorized to update this claim."
                    });
                }


                // ============================================================
                // 5. CLAIM MONTH VALIDATION
                // ============================================================

                if (dto.ClaimMonth == default)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim month is required."
                    });
                }


                // ============================================================
                // 6. TRACK INCOMING TRAVEL IDs
                //
                // Existing TravelId -> update
                // TravelId = 0 -> new travel
                // ============================================================

                var incomingTravelIds = new HashSet<int>();


                // ============================================================
                // 7. CALCULATED TOTAL
                // ============================================================

                decimal calculatedTotalAmount = 0;


                // ============================================================
                // 8. PROCESS EACH TRAVEL ENTRY
                // ============================================================

                foreach (var entry in dto.ClaimEntries)
                {
                    ClaimTravel travel;


                    // ========================================================
                    // 8.1 TRAVEL VALIDATION
                    // ========================================================

                    if (entry.TravelDate == default)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Travel date is required."
                        });
                    }

                    if (string.IsNullOrWhiteSpace(entry.Purpose))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Travel purpose is required."
                        });
                    }

                    if (string.IsNullOrWhiteSpace(entry.FromLocation) ||
                        string.IsNullOrWhiteSpace(entry.ToLocation))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "From and To locations are required."
                        });
                    }

                    // ========================================================
                    // 8.2 VEHICLE RATE + TRAVEL AMOUNT
                    // ========================================================

                    decimal? appliedRatePerKm = null;
                    decimal travelAmount = 0;


                    // ========================================================
                    // BIKE / CAR
                    // VehicleTypeId 2 = Bike
                    // VehicleTypeId 3 = Car
                    // ========================================================

                    if (entry.VehicleTypeId == 2 ||
                        entry.VehicleTypeId == 3)
                    {
                        // ----------------------------------------------------
                        // DISTANCE IS OPTIONAL
                        // ----------------------------------------------------

                        if (entry.KmRun > 0)
                        {
                            // ------------------------------------------------
                            // Distance entered
                            // Fuel Type required only for rate calculation
                            // ------------------------------------------------

                            if (!entry.FuelTypeId.HasValue ||
      entry.FuelTypeId.Value <= 0)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "Fuel Type is required when distance is entered."
                                });
                            }


                            // ------------------------------------------------
                            // GET VEHICLE RATE
                            // ------------------------------------------------

                            var currentRate = await _context.VehicleRateMasters
                                .Where(x =>
                                    x.VehicleTypeId == entry.VehicleTypeId &&
                                  x.FuelTypeId == entry.FuelTypeId.Value &&
                                    x.IsActive)
                                .OrderByDescending(x => x.ValidFrom)
                                .FirstOrDefaultAsync();


                            // ------------------------------------------------
                            // RATE NOT FOUND
                            // ------------------------------------------------

                            if (currentRate == null)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "Vehicle rate is not configured."
                                });
                            }


                            // ------------------------------------------------
                            // RATE SNAPSHOT
                            // ------------------------------------------------

                            appliedRatePerKm =
                                Convert.ToDecimal(currentRate.AmountPerKm);


                            // ------------------------------------------------
                            // CALCULATE TRAVEL AMOUNT
                            // KM × RATE
                            // ------------------------------------------------

                            travelAmount =
                                Convert.ToDecimal(entry.KmRun) *
                                Convert.ToDecimal(currentRate.AmountPerKm);
                        }
                        else
                        {
                            // ------------------------------------------------
                            // NO DISTANCE ENTERED
                            // No error
                            // No rate calculation
                            // Amount = 0
                            // ------------------------------------------------

                            appliedRatePerKm = null;

                            travelAmount = 0;
                        }
                    }


                    // ========================================================
                    // BUS / TAXI / OTHER VEHICLES
                    // User-entered amount
                    // ========================================================

                    else
                    {
                        appliedRatePerKm = null;

                        travelAmount =
        Convert.ToDecimal(entry.TravelAmount);
                    }


                    // ========================================================
                    // 8.3 EXISTING TRAVEL -> UPDATE
                    // ========================================================

                    if (entry.TravelId.HasValue &&
                        entry.TravelId.Value > 0)
                    {
                        travel = await _context.ClaimTravels
                            .FirstOrDefaultAsync(x =>
                                x.TravelId == entry.TravelId.Value &&
                                x.ClaimId == claimId &&
                                x.IsActive);

                        if (travel == null)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message =
                                    $"Travel entry {entry.TravelId.Value} not found for this claim."
                            });
                        }


                        // --------------------------------------------
                        // Vehicle
                        // --------------------------------------------

                        travel.VehicleTypeId =
                            entry.VehicleTypeId;

                        travel.FuelTypeId =
                            entry.FuelTypeId;

                        travel.VehicleDetailId =
                            entry.VehicleDetailId;

                        travel.VehicleNumber =
                            entry.VehicleNumber;


                        // --------------------------------------------
                        // Travel Details
                        // --------------------------------------------

                        travel.TravelDate =
                            entry.TravelDate;

                        travel.Purpose =
                            entry.Purpose;

                        travel.FromLocation =
                            entry.FromLocation;

                        travel.ToLocation =
                            entry.ToLocation;


                        // --------------------------------------------
                        // KM
                        // --------------------------------------------

                        travel.KmRun =
                            entry.KmRun;


                        // --------------------------------------------
                        // Amount
                        // --------------------------------------------

                        travel.Amount =
                            travelAmount;


                        // --------------------------------------------
                        // Rate Snapshot
                        // --------------------------------------------

                        travel.AppliedRatePerKm =
                            appliedRatePerKm;


                        _context.ClaimTravels.Update(travel);
                    }


                    // ========================================================
                    // 8.5 NEW TRAVEL -> INSERT
                    // ========================================================

                    else
                    {
                        travel = new ClaimTravel
                        {
                            ClaimId = claimId,


                            // --------------------------------------------
                            // Vehicle
                            // --------------------------------------------

                            VehicleTypeId =
                                entry.VehicleTypeId,

                            FuelTypeId =
                                entry.FuelTypeId,

                            VehicleDetailId =
                                entry.VehicleDetailId,

                            VehicleNumber =
                                entry.VehicleNumber,


                            // --------------------------------------------
                            // Travel Details
                            // --------------------------------------------

                            TravelDate =
                                entry.TravelDate,

                            Purpose =
                                entry.Purpose,

                            FromLocation =
                                entry.FromLocation,

                            ToLocation =
                                entry.ToLocation,


                            // --------------------------------------------
                            // Distance
                            // --------------------------------------------

                            KmRun =
                                entry.KmRun,


                            // --------------------------------------------
                            // Amount
                            // --------------------------------------------

                            Amount =
                                travelAmount,


                            // --------------------------------------------
                            // Applied Rate
                            // --------------------------------------------

                            AppliedRatePerKm =
                                appliedRatePerKm,


                            // --------------------------------------------
                            // Active
                            // --------------------------------------------

                            IsActive = true
                        };


                        _context.ClaimTravels.Add(travel);


                        // Need TravelId before Food / Expense
                        await _context.SaveChangesAsync();
                    }


                    // ========================================================
                    // 8.6 TRACK TRAVEL ID
                    // ========================================================

                    incomingTravelIds.Add(travel.TravelId);


                    // ========================================================
                    // 8.7 FOOD
                    // One Food per Travel
                    // ========================================================

                    var existingFood = await _context.ClaimFoods
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId &&
                            x.IsActive);


                    // --------------------------------------------
                    // Food exists
                    // --------------------------------------------

                    if (entry.FoodTypeId.HasValue &&
                        entry.FoodTypeId.Value > 0 &&
                        entry.FoodAmount > 0)
                    {
                        if (existingFood != null)
                        {
                            existingFood.FoodDate =
                                entry.TravelDate;

                            existingFood.FoodTypeId =
                                entry.FoodTypeId.Value;

                            existingFood.Amount =
                                entry.FoodAmount;

                            _context.ClaimFoods.Update(existingFood);
                        }
                        else
                        {
                            var food = new ClaimFood
                            {
                                ClaimId =
                                    claimId,

                                TravelId =
                                    travel.TravelId,

                                FoodDate =
                                    entry.TravelDate,

                                FoodTypeId =
                                    entry.FoodTypeId.Value,

                                Amount =
                                    entry.FoodAmount,

                                IsActive = true
                            };

                            _context.ClaimFoods.Add(food);
                        }
                    }


                    // --------------------------------------------
                    // Food removed
                    // --------------------------------------------

                    else
                    {
                        if (existingFood != null)
                        {
                            existingFood.IsActive = false;
                        }
                    }


                    // ========================================================
                    // 8.8 MULTIPLE EXPENSES
                    // ========================================================

                    var incomingExpenseIds = new HashSet<int>();


                    if (entry.Expenses != null &&
                        entry.Expenses.Any())
                    {
                        foreach (var expenseDto in entry.Expenses)
                        {
                            // --------------------------------------------
                            // Ignore empty expense
                            // --------------------------------------------

                            if (string.IsNullOrWhiteSpace(
                                    expenseDto.ExpenseType) ||
                                expenseDto.ExpenseAmount <= 0)
                            {
                                continue;
                            }


                            // --------------------------------------------
                            // Existing Expense ID
                            // --------------------------------------------

                            if (expenseDto.ExpenseId > 0)
                            {
                                incomingExpenseIds.Add(
                                    expenseDto.ExpenseId);


                                var existingExpense =
                                    await _context.ClaimExpenses
                                        .FirstOrDefaultAsync(x =>
                                            x.ExpenseId ==
                                                expenseDto.ExpenseId &&

                                            x.ClaimId ==
                                                claimId &&

                                            x.TravelId ==
                                                travel.TravelId &&

                                            x.IsActive);


                                if (existingExpense == null)
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message =
                                            $"Expense {expenseDto.ExpenseId} not found for Travel {travel.TravelId}."
                                    });
                                }


                                // ----------------------------------------
                                // UPDATE EXISTING EXPENSE
                                // ----------------------------------------

                                existingExpense.ExpenseDate =
                                    entry.TravelDate;

                                existingExpense.ExpenseType =
                                    expenseDto.ExpenseType;

                                existingExpense.InvoiceNo =
                                    expenseDto.InvoiceNo;

                                existingExpense.Description =
                                    expenseDto.Description;

                                existingExpense.Purpose =
                                    expenseDto.ExpensePurpose;

                                existingExpense.Amount =
                                    expenseDto.ExpenseAmount;

                                _context.ClaimExpenses.Update(
                                    existingExpense);
                            }


                            // --------------------------------------------
                            // New Expense
                            // ExpenseId = 0
                            // --------------------------------------------

                            else
                            {
                                var expense = new ClaimExpense
                                {
                                    ClaimId =
                                        claimId,

                                    TravelId =
                                        travel.TravelId,

                                    ExpenseDate =
                                        entry.TravelDate,

                                    ExpenseType =
                                        expenseDto.ExpenseType,

                                    InvoiceNo =
                                        expenseDto.InvoiceNo,

                                    Description =
                                        expenseDto.Description,

                                    Purpose =
                                        expenseDto.ExpensePurpose,

                                    Amount =
                                        expenseDto.ExpenseAmount,

                                    created_at =
                                        DateTime.Now,

                                    IsActive = true
                                };

                                _context.ClaimExpenses.Add(expense);
                            }
                        }
                    }


                    // ========================================================
                    // 8.9 DEACTIVATE REMOVED EXPENSES
                    //
                    // Example:
                    // DB:
                    // Toll     ID 10
                    // Others   ID 11
                    // Parking  ID 12
                    //
                    // Request:
                    // ID 10
                    // ID 11
                    //
                    // ID 12 -> inactive
                    // ========================================================

                    var existingExpenses =
                        await _context.ClaimExpenses
                            .Where(x =>
                                x.ClaimId == claimId &&
                                x.TravelId == travel.TravelId &&
                                x.IsActive)
                            .ToListAsync();


                    foreach (var existingExpense in existingExpenses)
                    {
                        if (!incomingExpenseIds.Contains(
                                existingExpense.ExpenseId))
                        {
                            existingExpense.IsActive = false;
                        }
                    }


                    // ========================================================
                    // 8.10 CALCULATE TOTAL
                    // ========================================================

                    decimal expenseTotal =
                        entry.Expenses?
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(
                                    x.ExpenseType) &&
                                x.ExpenseAmount > 0)
                            .Sum(x =>
                                x.ExpenseAmount)
                        ?? 0;


                    calculatedTotalAmount +=
                        travelAmount +
                        entry.FoodAmount +
                        expenseTotal;
                }


                // ============================================================
                // 9. DELETE / DEACTIVATE REMOVED TRAVELS
                //
                // If old TravelId is not present in request,
                // that travel was removed from UI.
                // ============================================================

                var removedTravels =
                    await _context.ClaimTravels
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.IsActive &&
                            !incomingTravelIds.Contains(
                                x.TravelId))
                        .ToListAsync();


                foreach (var removedTravel in removedTravels)
                {
                    // --------------------------------------------
                    // Deactivate Travel
                    // --------------------------------------------

                    removedTravel.IsActive = false;


                    // --------------------------------------------
                    // Deactivate Food
                    // --------------------------------------------

                    var foods =
                        await _context.ClaimFoods
                            .Where(x =>
                                x.ClaimId == claimId &&
                                x.TravelId ==
                                    removedTravel.TravelId &&
                                x.IsActive)
                            .ToListAsync();


                    foreach (var food in foods)
                    {
                        food.IsActive = false;
                    }


                    // --------------------------------------------
                    // Deactivate Expenses
                    // --------------------------------------------

                    var expenses =
                        await _context.ClaimExpenses
                            .Where(x =>
                                x.ClaimId == claimId &&
                                x.TravelId ==
                                    removedTravel.TravelId &&
                                x.IsActive)
                            .ToListAsync();


                    foreach (var expense in expenses)
                    {
                        expense.IsActive = false;
                    }


                    // --------------------------------------------
                    // Deactivate Documents
                    // --------------------------------------------

                    var documents =
                        await _context.ClaimDocuments
                            .Where(x =>
                                x.ClaimId == claimId &&
                                x.ReferenceId ==
                                    removedTravel.TravelId &&
                                x.IsActive)
                            .ToListAsync();


                    foreach (var document in documents)
                    {
                        document.IsActive = false;
                    }
                }


                // ============================================================
                // 10. UPDATE CLAIM MASTER TOTAL
                // ============================================================

                claim.EmployeeId =
                    loginEmpId;

                claim.ClaimMonth =
                    dto.ClaimMonth;

                claim.ClaimYear =
                    dto.ClaimMonth.Year;

                claim.TotalAmount =
                    calculatedTotalAmount;

                claim.LastModifiedAt =
                    DateTime.Now;


                _context.ClaimMasters.Update(claim);


                // ============================================================
                // 11. FINAL SAVE
                // ============================================================

                await _context.SaveChangesAsync();


                // ============================================================
                // 12. COMMIT TRANSACTION
                // ============================================================

                await transaction.CommitAsync();


                // ============================================================
                // 13. SUCCESS RESPONSE
                // ============================================================

                return Ok(new
                {
                    success = true,
                    message = "Draft updated successfully.",
                    data = new
                    {
                        claimId = claim.ClaimId,
                        claimNo = claim.ClaimNo,
                        status = claim.Status,
                        totalAmount = claim.TotalAmount,
                        travelCount = incomingTravelIds.Count
                    }
                });
            }
            catch (Exception ex)
            {
                // ============================================================
                // ROLLBACK
                // ============================================================

                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while updating draft.",
                    error = ex.InnerException?.Message ??
                            ex.Message
                });
            }
        }

        [Authorize]
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
                //----for validation next month only submit 
                //var today = DateTime.Today;

                //var nextMonthFirstDay = new DateTime(
                //    claim.ClaimMonth.Year,
                //    claim.ClaimMonth.Month,
                //    1
                //).AddMonths(1);

                //if (today != nextMonthFirstDay)
                //{
                //    return BadRequest(new
                //    {
                //        success = false,
                //        message = $"This claim can be submitted only on {nextMonthFirstDay:dd-MM-yyyy}."
                //    });
                //}
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
        [Authorize]
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
        [Authorize]
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
        [Authorize]
        [HttpPost("upload-document")]
        public async Task<IActionResult> UploadDocument(
    [FromForm] UploadDocumentDto model)
        {
            try
            {
                // =====================================================
                // VALIDATION
                // =====================================================

                if (model.File == null || model.File.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "File is required."
                    });
                }

                if (model.ClaimId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "ClaimId is required."
                    });
                }

                if (model.ReferenceId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "ReferenceId is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(model.ModuleType))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "ModuleType is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(model.DocumentType))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "DocumentType is required."
                    });
                }

                // =====================================================
                // GET CLAIM
                // =====================================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == model.ClaimId &&
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                // =====================================================
                // CLAIM NUMBER
                // Example:
                // LEC-012-26/27
                //
                // Folder:
                // LEC-012-26-27
                // =====================================================

                var claimNumber = claim.ClaimNo;

                if (string.IsNullOrWhiteSpace(claimNumber))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim number is not available."
                    });
                }

                var folderClaimNumber = claimNumber
                    .Replace("/", "-")
                    .Replace("\\", "-");

                // =====================================================
                // WWWROOT
                // =====================================================

                var uploadsRoot = Path.Combine(
                    _environment.WebRootPath,
                    "Uploads",
                    "Claims"
                );

                // =====================================================
                // TRAVEL ENTRY FOLDER
                //
                // Example:
                // Travel_66
                //
                // ReferenceId = TravelId
                // =====================================================

                var travelFolder = $"Travel_{model.ReferenceId}";

                // =====================================================
                // CLAIM + TRAVEL FOLDER
                //
                // wwwroot/Uploads/Claims/LEC-012-26-27/Travel_66
                // =====================================================

                var claimFolder = Path.Combine(
                    uploadsRoot,
                    folderClaimNumber,
                    travelFolder
                );

                // =====================================================
                // CREATE FOLDER
                // =====================================================

                if (!Directory.Exists(claimFolder))
                {
                    Directory.CreateDirectory(claimFolder);
                }

                // =====================================================
                // FIND EXISTING DOCUMENT
                //
                // Same:
                // ClaimId
                // ReferenceId
                // ModuleType
                // DocumentType
                //
                // means user is replacing old bill
                // =====================================================

                var existingDocument = await _context.ClaimDocuments
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == model.ClaimId &&
                        x.ReferenceId == model.ReferenceId &&
                        x.ModuleType == model.ModuleType &&
                        x.DocumentType == model.DocumentType &&
                        x.IsActive);

                // =====================================================
                // DELETE OLD PHYSICAL FILE
                // =====================================================

                if (existingDocument != null)
                {
                    if (!string.IsNullOrWhiteSpace(existingDocument.FilePath))
                    {
                        var oldRelativePath = existingDocument.FilePath
                            .Replace("/", Path.DirectorySeparatorChar.ToString())
                            .Replace("\\", Path.DirectorySeparatorChar.ToString());

                        var oldPhysicalPath = Path.Combine(
                            _environment.WebRootPath,
                            oldRelativePath
                        );

                        if (System.IO.File.Exists(oldPhysicalPath))
                        {
                            System.IO.File.Delete(oldPhysicalPath);
                        }
                    }

                    // =================================================
                    // MAKE OLD DB RECORD INACTIVE
                    // =================================================

                    existingDocument.IsActive = false;
                }

                // =====================================================
                // FILE EXTENSION
                // =====================================================

                var extension =
                    Path.GetExtension(model.File.FileName);

                if (string.IsNullOrWhiteSpace(extension))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "File extension is missing."
                    });
                }

                // =====================================================
                // UNIQUE FILE NAME
                // =====================================================

                var storedFileName =
                    $"{model.DocumentType}_{Guid.NewGuid():N}{extension}";

                // =====================================================
                // PHYSICAL FILE PATH
                //
                // Example:
                // wwwroot/Uploads/Claims/
                // LEC-012-26-27/
                // Travel_66/
                // FoodBill_xxx.jpg
                // =====================================================

                var physicalPath = Path.Combine(
                    claimFolder,
                    storedFileName
                );

                // =====================================================
                // SAVE FILE
                // =====================================================

                await using (var stream = new FileStream(
                    physicalPath,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    await model.File.CopyToAsync(stream);
                }

                // =====================================================
                // VERIFY FILE
                // =====================================================

                if (!System.IO.File.Exists(physicalPath))
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "File was not saved."
                    });
                }

                // =====================================================
                // RELATIVE PATH FOR DATABASE
                // =====================================================

                var relativePath =
                    $"Uploads/Claims/{folderClaimNumber}/{travelFolder}/{storedFileName}";

                // =====================================================
                // SAVE NEW DOCUMENT
                // =====================================================

                var document = new ClaimDocument
                {
                    ClaimId = model.ClaimId,

                    ReferenceId = model.ReferenceId,

                    ModuleType = model.ModuleType.Trim(),

                    DocumentType = model.DocumentType.Trim(),

                    OriginalFileName = model.File.FileName,

                    StoredFileName = storedFileName,

                    FilePath = relativePath,

                    FileExtension = extension,

                    UploadedDate = DateTime.Now,

                    IsActive = true
                };

                _context.ClaimDocuments.Add(document);

                await _context.SaveChangesAsync();

                // =====================================================
                // FILE URL
                // =====================================================

                var baseUrl =
                    $"{Request.Scheme}://{Request.Host}";

                var fileUrl =
                    $"{baseUrl}/{relativePath.Replace("\\", "/")}";

                // =====================================================
                // RESPONSE
                // =====================================================

                return Ok(new
                {
                    success = true,

                    message = existingDocument != null
                        ? "Document replaced successfully."
                        : "Document uploaded successfully.",

                    data = new
                    {
                        documentId = document.DocumentId,

                        claimId = document.ClaimId,

                        referenceId = document.ReferenceId,

                        claimNo = claim.ClaimNo,

                        moduleType = document.ModuleType,

                        documentType = document.DocumentType,

                        originalFileName = document.OriginalFileName,

                        storedFileName = document.StoredFileName,

                        filePath = document.FilePath,

                        fileUrl = fileUrl
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,

                    message = "Error while uploading document.",

                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
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
        [Authorize]
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
        //[Authorize]
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
        //[Authorize]
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
        //[Authorize]
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
        //[Authorize]
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

        //[Authorize]
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
        [Authorize]
        [HttpPut("resubmit-claim/{claimId}")]
        public async Task<IActionResult> ResubmitClaim(
         int claimId,
         [FromBody] CreateClaimDto dto)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // ==========================================
                // VALIDATION
                // ==========================================

                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Request data is required."
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

                // ==========================================
                // GET LOGGED-IN EMPLOYEE
                // ==========================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrEmpty(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                if (!int.TryParse(empIdClaim, out int loginEmpId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid EmployeeId in token."
                    });
                }

                // ==========================================
                // GET CLAIM
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                // ==========================================
                // EMPLOYEE VALIDATION
                // ==========================================

                if (claim.EmployeeId != loginEmpId)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "You are not authorized to edit this claim."
                    });
                }

                // ==========================================
                // ONLY REJECTED CLAIM CAN BE RESUBMITTED
                // ==========================================

                if (claim.Status != "Rejected")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Only rejected claims can be resubmitted."
                    });
                }

                // ==========================================
                // CALCULATE TOTAL
                // ==========================================

                decimal totalAmount = 0;

                foreach (var item in dto.ClaimEntries)
                {
                    // ======================================
                    // AMOUNT VARIABLES
                    // ======================================

                    decimal travelAmount = 0;
                    decimal foodAmount = item.FoodAmount;
                    decimal expenseAmount = 0;


                    // ======================================
                    // BIKE / CAR
                    // VehicleTypeId 2 = Bike
                    // VehicleTypeId 3 = Car
                    // ======================================

                    if (item.VehicleTypeId == 2 ||
                        item.VehicleTypeId == 3)
                    {
                        // ----------------------------------
                        // DISTANCE IS OPTIONAL
                        // ----------------------------------

                        if (item.KmRun > 0)
                        {
                            // ----------------------------------
                            // FUEL TYPE REQUIRED ONLY
                            // WHEN DISTANCE IS ENTERED
                            // ----------------------------------

                            if (item.FuelTypeId <= 0)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        "Fuel Type is required when distance is entered."
                                });
                            }


                            // ----------------------------------
                            // GET RATE ONLY FOR BIKE / CAR
                            // ----------------------------------

                            var currentRate = await _context.VehicleRateMasters
                                .Where(x =>
                                    x.VehicleTypeId == item.VehicleTypeId &&
                                    x.FuelTypeId == item.FuelTypeId &&
                                    x.IsActive)
                                .OrderByDescending(x => x.ValidFrom)
                                .FirstOrDefaultAsync();


                            // ----------------------------------
                            // RATE NOT FOUND
                            // ----------------------------------

                            if (currentRate == null)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        $"Rate not configured for VehicleType {item.VehicleTypeId} and FuelType {item.FuelTypeId}."
                                });
                            }


                            // ----------------------------------
                            // KM × RATE
                            // ----------------------------------

                            decimal rate =
                                currentRate.AmountPerKm;

                          

                            travelAmount = (item.KmRun * rate) ?? 0;
                        }
                        else
                        {
                            // ----------------------------------
                            // NO DISTANCE
                            // NO ERROR
                            // NO RATE
                            // ----------------------------------

                            travelAmount = 0;
                        }
                    }


                    // ======================================
                    // TAXI / BUS / TRAIN / OTHER
                    // USER ENTERED AMOUNT
                    // ======================================

                    else
                    {
                        travelAmount = item.TravelAmount;
                    }


                    // ======================================
                    // FOOD
                    // ======================================

                    if (foodAmount < 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Food amount cannot be negative."
                        });
                    }


                    // ======================================
                    // EXPENSE
                    // ======================================

                    if (item.Expenses != null)
                    {
                        expenseAmount = item.Expenses
                            .Where(x => x.ExpenseAmount > 0)
                            .Sum(x => x.ExpenseAmount);
                    }


                    if (expenseAmount < 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Expense amount cannot be negative."
                        });
                    }


                    // ======================================
                    // GRAND TOTAL
                    // ======================================

                    totalAmount +=
                        travelAmount +
                        foodAmount +
                        expenseAmount;
                }

                // ==========================================
                // UPDATE CLAIM MASTER
                // ==========================================

                claim.ClaimMonth = dto.ClaimMonth;
                claim.ClaimYear = dto.ClaimMonth.Year;

                claim.TotalAmount = totalAmount;

                // No Remarks

                claim.Status = "Pending";

                claim.SubmittedAt = DateTime.Now;

                claim.ApprovedAt = null;
                claim.RejectedAt = null;

                claim.CurrentApprovalLevel = 1;

                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();


                // ==========================================
                // DELETE OLD TRAVEL / FOOD / EXPENSE
                // ==========================================

                var existingTravels = await _context.ClaimTravels
    .Where(x =>
        x.ClaimId == claim.ClaimId &&
        x.IsActive)
    .ToDictionaryAsync(x => x.TravelId);


                var oldFoods = await _context.ClaimFoods
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                var oldExpenses = await _context.ClaimExpenses
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                _context.ClaimExpenses.RemoveRange(oldExpenses);

                _context.ClaimFoods.RemoveRange(oldFoods);

                //_context.ClaimTravels.RemoveRange(oldTravels);

                await _context.SaveChangesAsync();


                // ==========================================
                // INSERT / UPDATE CLAIM DETAILS
                // ==========================================

                foreach (var item in dto.ClaimEntries)
                {
                    decimal travelAmount = 0;
                    decimal? appliedRatePerKm = null;

                    // ======================================
                    // 1. CALCULATE TRAVEL AMOUNT
                    // ======================================

                    // --------------------------------------
                    // BIKE / CAR
                    // VehicleTypeId 2 = Bike
                    // VehicleTypeId 3 = Car
                    // --------------------------------------

                    if (item.VehicleTypeId == 2 ||
                        item.VehicleTypeId == 3)
                    {
                        // Distance is optional
                        if (item.KmRun > 0)
                        {
                            // Fuel required only when KM is entered
                            if (item.FuelTypeId <= 0)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        "Fuel Type is required when distance is entered."
                                });
                            }

                            // ----------------------------------
                            // GET VEHICLE RATE
                            // ----------------------------------

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
                                    message =
                                        $"Rate not configured for VehicleType {item.VehicleTypeId} and FuelType {item.FuelTypeId}."
                                });
                            }

                            // AmountPerKm is decimal
                            decimal rate = currentRate.AmountPerKm;

                            // KM × RATE
                            travelAmount = (item.KmRun * rate) ?? 0;

                            // Rate snapshot
                            appliedRatePerKm =
                                currentRate.AmountPerKm;
                        }
                        else
                        {
                            // No distance
                            // No error
                            // No rate calculation

                            travelAmount = 0;

                            appliedRatePerKm = null;
                        }
                    }

                    // --------------------------------------
                    // TAXI / BUS / TRAIN / OTHER
                    // User entered amount
                    // --------------------------------------

                    else
                    {
                        travelAmount =
                            item.TravelAmount;

                        appliedRatePerKm = null;
                    }


                    // ======================================
                    // 2. EXISTING TRAVEL -> UPDATE
                    // ======================================

                    ClaimTravel travel;

                    if (item.TravelId.HasValue &&
                        item.TravelId.Value > 0)
                    {
                        // ----------------------------------
                        // FIND EXISTING TRAVEL
                        // ----------------------------------

                        travel = await _context.ClaimTravels
                            .FirstOrDefaultAsync(x =>
                                x.TravelId == item.TravelId.Value &&
                                x.ClaimId == claim.ClaimId &&
                                x.IsActive);

                        if (travel == null)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message =
                                    $"TravelId {item.TravelId.Value} not found for this claim."
                            });
                        }


                        // ----------------------------------
                        // UPDATE VEHICLE DETAILS
                        // ----------------------------------

                        travel.VehicleTypeId =
                            item.VehicleTypeId;

                        travel.FuelTypeId =
                            item.FuelTypeId;

                        travel.VehicleDetailId =
                            item.VehicleDetailId;

                        travel.VehicleNumber =
                            item.VehicleNumber;


                        // ----------------------------------
                        // UPDATE TRAVEL DETAILS
                        // ----------------------------------

                        travel.TravelDate =
                            item.TravelDate;

                        travel.Purpose =
                            item.Purpose;

                        travel.FromLocation =
                            item.FromLocation;

                        travel.ToLocation =
                            item.ToLocation;


                        // ----------------------------------
                        // UPDATE KM
                        // ----------------------------------

                        travel.KmRun =
                            item.KmRun;


                        // ----------------------------------
                        // UPDATE AMOUNT
                        // ----------------------------------

                        travel.Amount =
                            travelAmount;


                        // ----------------------------------
                        // UPDATE RATE SNAPSHOT
                        // ----------------------------------

                        travel.AppliedRatePerKm =
                            appliedRatePerKm;

                        travel.IsActive = true;

                        _context.ClaimTravels.Update(travel);
                    }


                    // ======================================
                    // 3. NEW TRAVEL -> INSERT
                    // ======================================

                    else
                    {
                        travel = new ClaimTravel
                        {
                            ClaimId =
                                claim.ClaimId,

                            // Vehicle
                            VehicleTypeId =
                                item.VehicleTypeId,

                            FuelTypeId =
                                item.FuelTypeId,

                            VehicleDetailId =
                                item.VehicleDetailId,

                            VehicleNumber =
                                item.VehicleNumber,

                            // Travel
                            TravelDate =
                                item.TravelDate,

                            Purpose =
                                item.Purpose,

                            FromLocation =
                                item.FromLocation,

                            ToLocation =
                                item.ToLocation,

                            // Distance
                            KmRun =
                                item.KmRun,

                            // Amount
                            Amount =
                                travelAmount,

                            // Rate
                            AppliedRatePerKm =
                                appliedRatePerKm,

                            IsActive = true
                        };

                        _context.ClaimTravels.Add(travel);

                        // TravelId required for Food / Expense
                        await _context.SaveChangesAsync();
                    }


                    // ======================================
                    // 4. FOOD
                    // ======================================

                    if (item.FoodTypeId.HasValue ||
                        item.FoodAmount > 0)
                    {
                        var food = new ClaimFood
                        {
                            ClaimId =
                                claim.ClaimId,

                            TravelId =
                                travel.TravelId,

                            FoodDate =
                                item.TravelDate,

                            FoodTypeId =
                                item.FoodTypeId,

                            Amount =
                                item.FoodAmount,

                            IsActive = true
                        };

                        _context.ClaimFoods.Add(food);
                    }


                    // ======================================
                    // 5. EXPENSE
                    // ======================================

                    if (item.Expenses != null &&
                        item.Expenses.Any())
                    {
                        foreach (var expenseItem in item.Expenses)
                        {
                            if (string.IsNullOrWhiteSpace(
                                    expenseItem.ExpenseType) ||
                                expenseItem.ExpenseAmount <= 0)
                            {
                                continue;
                            }

                            var expense = new ClaimExpense
                            {
                                ClaimId =
                                    claim.ClaimId,

                                TravelId =
                                    travel.TravelId,

                                ExpenseDate =
                                    item.TravelDate,

                                ExpenseType =
                                    expenseItem.ExpenseType,

                                InvoiceNo =
                                    expenseItem.InvoiceNo,

                                Description =
                                    expenseItem.Description,

                                Purpose =
                                    expenseItem.ExpensePurpose,

                                Amount =
                                    expenseItem.ExpenseAmount,

                                created_at =
                                    DateTime.Now,

                                IsActive = true
                            };

                            _context.ClaimExpenses.Add(expense);
                        }
                    }
                }


                // ==========================================
                // SAVE UPDATED DETAILS
                // ==========================================

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

        [Authorize]
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

        [Authorize]
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


        [Authorize]
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
        [Authorize]
        [HttpGet("claim-details/{claimId}")]
        public async Task<IActionResult> GetClaimDetails(int claimId)
        {
            try
            {
                // =====================================
                // CLAIM MASTER
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
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
                // EMPLOYEE DETAILS
                // =====================================

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId);

                string? designationName = null;


                if (employee?.DesignationId != null)
                {
                    designationName = await _context.Designation
                        .Where(d => d.Id == employee.DesignationId)
                        .Select(d => d.Name)
                        .FirstOrDefaultAsync();
                }

                // =====================================
                // TRAVEL DETAILS
                // Only IsActive = true
                // =====================================
                var travels = await (
                    from t in _context.ClaimTravels

                    join v in _context.VehicleMaster
                        on t.VehicleTypeId equals v.VehicleId
                        into vehicleGroup
                    from v in vehicleGroup.DefaultIfEmpty()

                    join f in _context.FuelTypeMasters
                        on t.FuelTypeId equals f.FuelTypeId
                        into fuelGroup
                    from f in fuelGroup.DefaultIfEmpty()

                    where t.ClaimId == claimId
                          && t.IsActive

                    select new
                    {
                        t.TravelId,

                        t.VehicleTypeId,
                        VehicleType = v != null
                            ? v.VehicleName
                            : null,

                        t.FuelTypeId,
                        FuelType = f != null
                            ? f.FuelTypeName
                            : null,

                        t.VehicleDetailId,
                        t.VehicleNumber,

                        t.AppliedRatePerKm,

                        t.TravelDate,
                        t.Purpose,
                        t.FromLocation,
                        t.ToLocation,
                        t.KmRun,
                        t.Amount
                    })
                    .ToListAsync();

                // =====================================
                // FOOD DETAILS
                // Only IsActive = true
                // =====================================

                var foods = await _context.ClaimFoods
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                // =====================================
                // EXPENSE DETAILS
                // Only IsActive = true
                // =====================================

                var expenses = await _context.ClaimExpenses
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                // =====================================
                // DOCUMENTS
                // Only IsActive = true
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
                // APPROVAL HISTORY
                // No IsActive condition
                // =====================================

                var approvals = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == claimId)
                    .OrderBy(x => x.ApprovalLevel)
                    .Select(x => new
                    {
                        x.ApprovalLevel,
                        x.ApproverEmpId,

                        ApproverName = _context.EmployeeMasters
                            .Where(e =>
                                e.EmpId == x.ApproverEmpId)
                            .Select(e => e.FullName)
                            .FirstOrDefault(),

                        x.Status,
                        x.Remarks,
                        x.ActionDate
                    })
                    .ToListAsync();

                // =====================================
                // RESPONSE
                // =====================================

                return Ok(new
                {
                    success = true,

                    claim = new
                    {
                        claim.ClaimId,

                        claim.ClaimNo,

                        claim.EmployeeId,

                        EmployeeName = employee?.FullName,

                        Designation = designationName,

                        employeeCode = employee?.EmployeeCode,

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

        [HttpGet("claim-report/{claimId}")]
        public async Task<IActionResult> GetClaimReport(int claimId)
        {
            try
            {
                // ============================================
                // CLAIM MASTER
                // ============================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }


                // ============================================
                // EMPLOYEE
                // ============================================

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found."
                    });
                }


                // ============================================
                // DESIGNATION
                // ============================================

                string? designationName = null;

                if (employee.DesignationId.HasValue)
                {
                    designationName = await _context.Designation
                        .Where(x =>
                            x.Id == employee.DesignationId.Value)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();
                }


                // ============================================
                // TRAVEL DETAILS
                // ONLY ACTIVE RECORDS
                // ============================================

                var travelDetails = await
                    (
                        from t in _context.ClaimTravels

                        join v in _context.VehicleMaster
                            on t.VehicleTypeId equals v.VehicleId

                        // LEFT JOIN FUEL TYPE
                        // Because Taxi / Bus / Train may not need fuel
                        join f in _context.FuelTypeMasters
                            on t.FuelTypeId equals f.FuelTypeId
                            into fuelGroup

                        from f in fuelGroup.DefaultIfEmpty()

                        where
                            t.ClaimId == claimId &&
                            t.IsActive

                        select new TravelReportDto
                        {
                            // =====================================
                            // TRAVEL
                            // =====================================

                            TravelId = t.TravelId,

                            TravelDate = t.TravelDate,

                            FromLocation = t.FromLocation,

                            ToLocation = t.ToLocation,

                            Purpose = t.Purpose,


                            // =====================================
                            // VEHICLE TYPE
                            // =====================================

                            VehicleType = v.VehicleName,


                            // =====================================
                            // FUEL TYPE
                            //
                            // Bike = 2
                            // Car  = 3
                            //
                            // Taxi / Bus / Train = "-"
                            // =====================================

                            FuelType =
                                (t.VehicleTypeId == 2 ||
                                 t.VehicleTypeId == 3)
                                    ? (f != null
                                        ? f.FuelTypeName
                                        : "-")
                                    : "-",


                            // =====================================
                            // VEHICLE NUMBER
                            //
                            // Bike / Car:
                            // Actual number -> show number
                            //
                            // Taxi:
                            // "Taxi" -> "-"
                            //
                            // Bus / Train:
                            // "Bus / Train" -> "-"
                            // =====================================

                            VehicleNumber =
                                string.IsNullOrWhiteSpace(t.VehicleNumber) ||
                                t.VehicleNumber.Trim()
                                    .Equals(
                                        v.VehicleName,
                                        StringComparison.OrdinalIgnoreCase)
                                    ? null
                                    : t.VehicleNumber,


                            // =====================================
                            // OTHER DETAILS
                            // =====================================

                            KmRun = t.KmRun,

                            Amount = t.Amount,

                            AppliedRatePerKm = t.AppliedRatePerKm
                        }
                    )
                    .ToListAsync();


                // ============================================
                // VEHICLE TYPE + VEHICLE NUMBER
                // ============================================

                var vehicleDetails = travelDetails
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.VehicleType) &&
                        !string.IsNullOrWhiteSpace(x.VehicleNumber))
                    .Select(x =>
                        $"{x.VehicleType} - {x.VehicleNumber}")
                    .Distinct()
                    .ToList();


                string vehicleNo =
                    vehicleDetails.Any()
                        ? string.Join(" / ", vehicleDetails)
                        : "-";


                // ============================================
                // FOOD DETAILS
                // ONLY ACTIVE RECORDS
                // ============================================

                var foodDetails = await _context.ClaimFoods
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .Select(x => new FoodReportDto
                    {
                        FoodDate = x.FoodDate,

                        FoodTypeId = x.FoodTypeId,

                        Amount = x.Amount
                    })
                    .ToListAsync();


                // ============================================
                // EXPENSE DETAILS
                // ONLY ACTIVE RECORDS
                // ============================================

                var expenseDetails = await _context.ClaimExpenses
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .Select(x => new ExpenseReportDto
                    {
                        ExpenseId = x.ExpenseId,

                        ExpenseDate = x.ExpenseDate,

                        ExpenseType = x.ExpenseType,

                        InvoiceNo = x.InvoiceNo,

                        Description = x.Description,

                        Purpose = x.Purpose,

                        Amount = x.Amount ?? 0
                    })
                    .ToListAsync();


                // ============================================
                // APPROVAL STATUS
                // ============================================

                var approvalStatus = await
                    (
                        from a in _context.ClaimApprovals

                        join e in _context.EmployeeMasters
                            on a.ApproverEmpId equals e.EmpId
                            into approverGroup

                        from e in approverGroup.DefaultIfEmpty()

                        where a.ClaimId == claimId

                        orderby a.ApprovalLevel

                        select new ApprovalReportDto
                        {
                            ApprovalLevel = a.ApprovalLevel,

                            ApproverName = e != null
                                ? e.FullName
                                : "-",

                            Status = a.Status,

                            Remarks = a.Remarks,

                            ActionDate = a.ActionDate
                        }
                    )
                    .ToListAsync();


                // ============================================
                // MONTH FORMAT
                // Example: Jul-26
                // ============================================

                string monthText =
                    claim.ClaimMonth.ToString("MMM-yy");


                // ============================================
                // BUILD REPORT
                // ============================================

                var report = new ClaimReportDto
                {
                    // =========================================
                    // EMPLOYEE
                    // =========================================

                    Employee = new EmployeeReportDto
                    {
                        Name = employee.FullName,

                        EmployeeCode = employee.EmployeeCode,

                        Designation = designationName,

                        VehicleNo = vehicleNo
                    },


                    // =========================================
                    // CLAIM
                    // =========================================

                    Claim = new ClaimReportHeaderDto
                    {
                        ClaimId = claim.ClaimId,

                        ClaimNo = claim.ClaimNo,

                        ClaimMonth = claim.ClaimMonth,

                        Month = monthText,

                        TotalAmount = claim.TotalAmount ?? 0
                    },


                    // =========================================
                    // APPROVAL
                    // =========================================

                    ApprovalStatus = approvalStatus,


                    // =========================================
                    // TRAVEL
                    // =========================================

                    TravelDetails = travelDetails,


                    // =========================================
                    // FOOD
                    // =========================================

                    FoodDetails = foodDetails,


                    // =========================================
                    // EXPENSE
                    // =========================================

                    ExpenseSummary = expenseDetails
                };


                // ============================================
                // RESPONSE
                // ============================================

                return Ok(new
                {
                    success = true,

                    message =
                        "Claim report details fetched successfully.",

                    data = report
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,

                    message =
                        "Error while fetching claim report.",

                    error =
                        ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }

        //---------------Expense Tracker  Module..------------------------------//



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


