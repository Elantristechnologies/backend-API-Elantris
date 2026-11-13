using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.Http.Headers;

using System.Security.Claims;
using System.Text;
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
        private readonly CommonDbContext _commonContext;
        private readonly TaskManagerDbContext _taskManagerDbContext;





        public TravelMasterController(ApplicationDbContext context, IWebHostEnvironment environment, IConfiguration configuration, HttpClient httpClient, EmailService emailService, CommonDbContext commonContext, TaskManagerDbContext taskManagerDbContext)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
            _httpClient = httpClient;
            _emailService = emailService;
            _commonContext = commonContext;
            _taskManagerDbContext = taskManagerDbContext;
        }

        private int GetOrganizationId()
        {
            var organizationIdClaim =
                User.FindFirst("orgId");

            if (organizationIdClaim == null)
            {
                throw new UnauthorizedAccessException(
                    "OrganizationId not found in token.");
            }

            return int.Parse(organizationIdClaim.Value);
        }

        private int? GetEmployeeId()
        {
            var empIdClaim = User.FindFirst("EmpId");

            if (empIdClaim == null)
                return null;

            if (!int.TryParse(empIdClaim.Value, out int empId))
                return null;

            if (empId <= 0)
                return null;

            return empId;
        }

        private string? GetLocalExpenseRole()
        {   
            try
            {
                var portalRolesClaim =
                    User.FindFirst("portal_roles")?.Value;

                if (string.IsNullOrWhiteSpace(portalRolesClaim))
                    return null;

                using var document =
                    JsonDocument.Parse(portalRolesClaim);

                if (document.RootElement.TryGetProperty(
                       "travel_portal",
                        out var localExpenseRole))
                {
                    return localExpenseRole.GetString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        [Authorize]
        [HttpPost("save")]
        public async Task<IActionResult> SaveVehicle([FromBody] Vehicle vehicle)
        {
            try
            {
                // ==========================================
                // 1. Validate request
                // ==========================================
                if (vehicle == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid vehicle payload."
                    });
                }

                // ==========================================
                // 2. OrganizationId from JWT
                // ==========================================
                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 3. EmployeeId from JWT
                // ==========================================
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 4. Check employee in Common DB
                //    company_users ONLY
                // ==========================================
                var companyUser = await _commonContext.CompanyUsers
                .FirstOrDefaultAsync(x =>
                x.EmpId == employeeId &&
                x.OrgId == organizationId &&
                (
                 x.Status == null ||
                 x.Status.ToUpper() == "ACTIVE"
                    ));
                if (companyUser == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 5. Set values from JWT
                //    Do NOT trust body EmployeeId / OrgId
                // ==========================================
                vehicle.VehicleId = 0;
                vehicle.EmployeeId = employeeId;
                vehicle.OrganizationId = organizationId;
                vehicle.IsActive = true;

                // ==========================================
                // 6. Duplicate vehicle check
                // ==========================================
                var duplicate = await _context.Vehicles
                    .AnyAsync(v =>
                        v.EmployeeId == employeeId &&
                        v.OrganizationId == organizationId &&
                        v.VehicleType == vehicle.VehicleType &&
                        v.IsActive);

                if (duplicate)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Employee already has a {vehicle.VehicleType}."
                    });
                }

                // ==========================================
                // 7. Save vehicle
                // ==========================================
                _context.Vehicles.Add(vehicle);

                await _context.SaveChangesAsync();

                // ==========================================
                // 8. Response
                // ==========================================
                return Ok(new
                {
                    success = true,
                    message = "Vehicle saved successfully.",
                    data = new
                    {
                        vehicleId = vehicle.VehicleId,
                        employeeId = vehicle.EmployeeId,
                        organizationId = vehicle.OrganizationId,
                        vehicleType = vehicle.VehicleType,
                        vehicleNumber = vehicle.VehicleNumber,
                        gearType = vehicle.GearType,
                        fuelType = vehicle.FuelType,
                        ccRange = vehicle.CCRange,
                        isActive = vehicle.IsActive
                    }
                });
            }
            catch (DbUpdateException dbEx)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Database error while saving vehicle.",
                    error = dbEx.InnerException?.Message ?? dbEx.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving vehicle.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("my-vehicles")]
        public async Task<IActionResult> GetMyVehicles()
        {
            try
                {
                // ==========================================
                // 1. OrganizationId from JWT
                // ==========================================

                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 2. Local Expense Role from portal_roles
                // ==========================================

                var role = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // ==========================================
                // 3. EmployeeId from JWT
                // ==========================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 4. Validate employee in Common DB
                // ==========================================

                var companyUser =
                    await _commonContext.CompanyUsers
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == employeeId &&
                            x.OrgId == organizationId &&
                            (
                                x.Status == null ||
                                x.Status == "Active"
                            ));

                if (companyUser == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 5. Get employee's own vehicles
                // ==========================================

                var vehicles =
                    await _context.Vehicles
                        .Where(v =>
                            v.EmployeeId == employeeId &&
                            v.OrganizationId == organizationId &&
                            v.IsActive)
                        .ToListAsync();

                var result = new List<object>();

                // ==========================================
                // 6. Vehicle details
                // ==========================================

                foreach (var vehicle in vehicles)
                {
                    var vehicleType =
                        await _context.VehicleMaster
                            .FirstOrDefaultAsync(x =>
                                x.VehicleName == vehicle.VehicleType &&
                                x.IsActive);

                    var fuelType =
                        await _context.FuelTypeMasters
                            .FirstOrDefaultAsync(x =>
                                x.FuelTypeName == vehicle.FuelType &&
                                x.IsActive);

                    result.Add(new
                    {
                        vehicleDetailId = vehicle.VehicleId,

                        employeeId = vehicle.EmployeeId,

                        organizationId = vehicle.OrganizationId,

                        vehicleTypeId =
                            vehicleType?.VehicleId ?? 0,

                        fuelTypeId =
                            fuelType?.FuelTypeId ?? 0,

                        vehicleNumber = vehicle.VehicleNumber,

                        vehicleType = vehicle.VehicleType,

                        fuelType = vehicle.FuelType,

                        gearType = vehicle.GearType,

                        ccRange = vehicle.CCRange
                    });
                }

                // ==========================================
                // 7. Response
                // ==========================================

                return Ok(new
                {
                    success = true,
                    role = role,
                    organizationId = organizationId,
                    employeeId = employeeId,

                    message = result.Any()
                        ? "Vehicles fetched successfully."
                        : "No vehicles found.",

                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "An error occurred while fetching vehicles.",
                    error =
                        ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpPut("update/{vehicleId}")]
        public async Task<IActionResult> UpdateVehicle(
       int vehicleId,
       [FromBody] Vehicle model)
        {
            try
            {
                // ==========================================
                // 1. Validate request
                // ==========================================
                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid request."
                    });
                }

                // ==========================================
                // 2. OrganizationId from JWT
                // ==========================================
                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 3. Get Local Expense role
                //    from portal_roles
                // ==========================================
                var role = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // ==========================================
                // 4. EmployeeId from JWT
                // ==========================================
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 5. Validate employee in Common DB
                //    company_users ONLY
                // ==========================================
                var companyUser = await _commonContext.CompanyUsers
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == employeeId &&
                        x.OrgId == organizationId &&
                        (
                            x.Status == null ||
                            x.Status.Equals(
                                "Active",
                                StringComparison.OrdinalIgnoreCase)
                        ));

                if (companyUser == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 6. Get OWN vehicle
                //    EmployeeId + OrganizationId
                // ==========================================
                var vehicle = await _context.Vehicles
                    .FirstOrDefaultAsync(x =>
                        x.VehicleId == vehicleId &&
                        x.EmployeeId == employeeId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (vehicle == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Vehicle not found for this employee."
                    });
                }

                // ==========================================
                // 7. Duplicate vehicle type check
                // ==========================================
                var duplicate = await _context.Vehicles
                    .AnyAsync(v =>
                        v.VehicleId != vehicleId &&
                        v.EmployeeId == employeeId &&
                        v.OrganizationId == organizationId &&
                        v.VehicleType == model.VehicleType &&
                        v.IsActive);

                if (duplicate)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Employee already has a {model.VehicleType}."
                    });
                }

                // ==========================================
                // 8. Update vehicle fields
                // ==========================================
                vehicle.VehicleType = model.VehicleType;
                vehicle.VehicleNumber = model.VehicleNumber;
                vehicle.GearType = model.GearType;
                vehicle.FuelType = model.FuelType;
                vehicle.CCRange = model.CCRange;

                // Do NOT change:
                // vehicle.EmployeeId
                // vehicle.OrganizationId
                // vehicle.IsActive

                // ==========================================
                // 9. Save
                // ==========================================
                await _context.SaveChangesAsync();

                // ==========================================
                // 10. Response
                // ==========================================
                return Ok(new
                {
                    success = true,
                    message = "Vehicle updated successfully.",
                    role = role,
                    employeeId = employeeId,
                    organizationId = organizationId,
                    data = new
                    {
                        vehicleId = vehicle.VehicleId,
                        employeeId = vehicle.EmployeeId,
                        organizationId = vehicle.OrganizationId,
                        vehicleType = vehicle.VehicleType,
                        vehicleNumber = vehicle.VehicleNumber,
                        gearType = vehicle.GearType,
                        fuelType = vehicle.FuelType,
                        ccRange = vehicle.CCRange,
                        isActive = vehicle.IsActive
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new  
                {
                    success = false,
                    message = "An error occurred while updating vehicle.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpDelete("soft-delete/{vehicleId}")]
        public async Task<IActionResult> DeleteVehicle(int vehicleId)
        {
            try
            {
                // ==========================================
                // 1. Validate VehicleId
                // ==========================================
                if (vehicleId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid VehicleId."
                    });
                }

                // ==========================================
                // 2. OrganizationId from JWT
                // ==========================================
                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 3. Get Local Expense role from portal_roles
                // ==========================================
                var role = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // ==========================================
                // 4. EmpId from JWT
                // ==========================================
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 5. Validate employee in Common DB
                //    company_users ONLY
                // ==========================================
                var companyUserExists = await _commonContext.CompanyUsers
                    .AnyAsync(x =>
                        x.EmpId == employeeId &&
                        x.OrgId == organizationId &&
                        (
                            x.Status == null ||
                            x.Status.Equals(
                                "Active",
                                StringComparison.OrdinalIgnoreCase)
                        ));

                if (!companyUserExists)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 6. Find OWN vehicle
                //    VehicleId + EmpId + OrganizationId
                // ==========================================
                var vehicle = await _context.Vehicles
                    .FirstOrDefaultAsync(x =>
                        x.VehicleId == vehicleId &&
                        x.EmployeeId == employeeId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (vehicle == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Vehicle not found."
                    });
                }

                // ==========================================
                // 7. Soft Delete
                // ==========================================
                vehicle.IsActive = false;

                await _context.SaveChangesAsync();

                // ==========================================
                // 8. Response
                // ==========================================
                return Ok(new
                {
                    success = true,
                    message = "Vehicle deleted successfully.",
                    role = role,    
                    employeeId = employeeId,
                    organizationId = organizationId,
                    vehicleId = vehicleId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while deleting vehicle.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        //-----------Travel form FROm ANd To localtion and calculate distonse//------//


        //[Authorize]
        //[HttpGet("location-search")]
        //public async Task<IActionResult> SearchLocation(string text)
        //{
        //    try
        //    {
        //        // ============================================
        //        // VALIDATION
        //        // ============================================

        //        if (string.IsNullOrWhiteSpace(text))
        //        {
        //            return Ok(new List<string>());
        //        }

        //        text = text.Trim();

        //        // ============================================
        //        // API KEY
        //        // ============================================

        //        var apiKey = _configuration["Geoapify:ApiKey"];

        //        if (string.IsNullOrWhiteSpace(apiKey))
        //        {
        //            return StatusCode(500, new
        //            {
        //                success = false,
        //                message = "Geoapify API key is missing."
        //            });
        //        }

        //        // ============================================
        //        // GEOAPIFY AUTOCOMPLETE
        //        // India only
        //        // City results
        //        // ============================================

        //        var url =
        //            "https://api.geoapify.com/v1/geocode/autocomplete" +
        //            $"?text={Uri.EscapeDataString(text)}" +
        //            "&type=city" +
        //            "&filter=countrycode:in" +
        //            "&lang=en" +
        //            "&limit=20" +
        //            $"&apiKey={Uri.EscapeDataString(apiKey)}";

        //        // ============================================
        //        // API CALL
        //        // ============================================

        //        using var response =
        //            await _httpClient.GetAsync(url);

        //        var content =
        //            await response.Content.ReadAsStringAsync();

        //        // ============================================
        //        // GEOAPIFY ERROR
        //        // ============================================

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            return StatusCode(
        //                (int)response.StatusCode,
        //                new
        //                {
        //                    success = false,
        //                    message = content
        //                });
        //        }

        //        // ============================================
        //        // PARSE RESPONSE
        //        // ============================================

        //        using var doc =
        //            JsonDocument.Parse(content);

        //        var result =
        //            new List<string>();

        //        if (!doc.RootElement.TryGetProperty(
        //                "features",
        //                out var features))
        //        {
        //            return Ok(result);
        //        }

        //        // ============================================
        //        // GET CITY NAMES
        //        // ============================================

        //        foreach (var feature in
        //                 features.EnumerateArray())
        //        {
        //            if (!feature.TryGetProperty(
        //                    "properties",
        //                    out var properties))
        //            {
        //                continue;
        //            }

        //            if (!properties.TryGetProperty(
        //                    "formatted",
        //                    out var formatted))
        //            {
        //                continue;
        //            }

        //            var name =
        //                formatted.GetString();

        //            if (!string.IsNullOrWhiteSpace(name))
        //            {
        //                result.Add(name.Trim());
        //            }
        //        }

        //        // ============================================
        //        // REMOVE DUPLICATES
        //        // ============================================

        //        result = result
        //            .Distinct(StringComparer.OrdinalIgnoreCase)
        //            .ToList();

        //        // ============================================
        //        // TAMIL NADU FIRST
        //        // ============================================

        //        result = result
        //            .OrderBy(x =>
        //                x.Contains(
        //                    "Tamil Nadu",
        //                    StringComparison.OrdinalIgnoreCase)
        //                    ? 0
        //                    : 1)
        //            .ThenBy(x => x)
        //            .Take(20)
        //            .ToList();

        //        // ============================================
        //        // RESPONSE
        //        // ============================================

        //        return Ok(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "Error while searching location.",
        //            error = ex.Message
        //        });
        //    }
        //}

        [Authorize]
        [HttpGet("location-search")]
        public async Task<IActionResult> SearchLocation(string text)
        {
            try
            {
                // ============================================
                // VALIDATION
                // ============================================

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Ok(new List<LocationResponseDto>());
                }

                text = text.Trim();

                // ============================================
                // PHOTON API
                // ============================================

                var url =
                    "https://photon.komoot.io/api/" +
                    $"?q={Uri.EscapeDataString(text)}" +
                    "&countrycode=IN" +
                    "&layer=city" +
                    "&limit=20" +
                    "&lang=en";

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url);

                // Identify your application
                request.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "HRPortal/1.0");

                // ============================================
                // API CALL
                // ============================================

                using var response =
                    await _httpClient.SendAsync(request);

                var content =
                    await response.Content.ReadAsStringAsync();

                // ============================================
                // PHOTON ERROR
                // ============================================

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(
                        (int)response.StatusCode,
                        new
                        {
                            success = false,
                            message = content
                        });
                }

                // ============================================
                // PARSE JSON
                // ============================================

                using var doc =
                    JsonDocument.Parse(content);

                var result =
                    new List<LocationResponseDto>();

                if (!doc.RootElement.TryGetProperty(
                        "features",
                        out var features))
                {
                    return Ok(result);
                }

                // ============================================
                // READ LOCATIONS
                // ============================================

                foreach (var feature in
                         features.EnumerateArray())
                {
                    if (!feature.TryGetProperty(
                            "properties",
                            out var properties))
                    {
                        continue;
                    }

                    if (!feature.TryGetProperty(
                            "geometry",
                            out var geometry))
                    {
                        continue;
                    }

                    if (!geometry.TryGetProperty(
                            "coordinates",
                            out var coordinates))
                    {
                        continue;
                    }

                    if (coordinates.GetArrayLength() < 2)
                    {
                        continue;
                    }

                    // ----------------------------------------
                    // COUNTRY CHECK
                    // ----------------------------------------

                    var country =
                        properties.TryGetProperty(
                            "country",
                            out var countryElement)
                            ? countryElement.GetString()
                            : null;

                    if (!string.Equals(
                            country,
                            "India",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // ----------------------------------------
                    // NAME
                    // ----------------------------------------

                    var name =
                        properties.TryGetProperty(
                            "name",
                            out var nameElement)
                            ? nameElement.GetString()
                            : null;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    // ----------------------------------------
                    // STATE
                    // ----------------------------------------

                    var state =
                        properties.TryGetProperty(
                            "state",
                            out var stateElement)
                            ? stateElement.GetString()
                            : null;

                    // ----------------------------------------
                    // CITY
                    // ----------------------------------------

                    var city =
                        properties.TryGetProperty(
                            "city",
                            out var cityElement)
                            ? cityElement.GetString()
                            : null;

                    // ----------------------------------------
                    // DISPLAY NAME
                    // ----------------------------------------

                    string displayName;

                    if (!string.IsNullOrWhiteSpace(state))
                    {
                        displayName =
                            $"{name}, {state}, India";
                    }
                    else
                    {
                        displayName =
                            $"{name}, India";
                    }

                    // ----------------------------------------
                    // COORDINATES
                    // ----------------------------------------

                    var longitude =
                        coordinates[0].GetDouble();

                    var latitude =
                        coordinates[1].GetDouble();

                    // ----------------------------------------
                    // ADD
                    // ----------------------------------------

                    result.Add(
                        new LocationResponseDto
                        {
                            Name = displayName,
                            Latitude = latitude,
                            Longitude = longitude
                        });
                }

                // ============================================
                // REMOVE DUPLICATES
                // ============================================

                result = result
                    .GroupBy(x =>
                        x.Name?.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                // ============================================
                // TAMIL NADU FIRST
                // ============================================

                result = result
                    .OrderBy(x =>
                        x.Name != null &&
                        x.Name.Contains(
                            "Tamil Nadu",
                            StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                    .ThenBy(x => x.Name)
                    .Take(20)
                    .ToList();

                // ============================================
                // RESPONSE
                // ============================================

                return Ok(result);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(502, new
                {
                    success = false,
                    message =
                        "Photon location service is unavailable.",
                    error = ex.Message
                });
            }
            catch (JsonException ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Invalid response from Photon.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Error while searching location.",
                    error = ex.Message
                });
            }
        }
        private int GetLocationPriority(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return 3;

            location = location.ToLowerInvariant();

            // ============================================
            // TAMIL NADU
            // ============================================

            if (location.Contains("tamil nadu"))
                return 1;


            // ============================================
            // IMPORTANT TAMIL NADU CITIES / AREAS
            // ============================================

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
                location.Contains("t nagar") ||
                location.Contains("kumbakonam") ||
                location.Contains("thanjavur") ||
                location.Contains("nagapattinam") ||
                location.Contains("dindigul") ||
                location.Contains("karur") ||
                location.Contains("namakkal") ||
                location.Contains("hosur") ||
                location.Contains("tirunelveli") ||
                location.Contains("thoothukudi"))
            {
                return 1;
            }


            // ============================================
            // OTHER INDIA LOCATIONS
            // ============================================

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
        public async Task<IActionResult> AddVehicleRate(
            [FromBody] VehicleRateDto dto)
        {
            try
            {
                // ==========================================
                // 1. VALIDATE REQUEST
                // ==========================================

                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Vehicle rate data is required."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION ID FROM JWT
                // ==========================================

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid OrganizationId."
                    });
                }


                // ==========================================
                // 3. GET LOCAL EXPENSE PORTAL ROLE
                // portal_roles.travel_portal
                // ==========================================

                var portalRole = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(portalRole))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Local Expense portal access not found."
                        });
                }


                // ==========================================
                // 4. ADMIN ONLY
                // ==========================================

                if (!portalRole.Equals(
                        "Admin",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Only Local Expense Admin can manage Vehicle Rate Masters."
                        });
                }


                // ==========================================
                // 5. GET OPTIONAL EMPLOYEE ID FROM JWT
                // ==========================================
                // user_admin Admin-ku EmpId irukka vendiya
                // avasiyam illai.
                //
                // Admin access role base panni decide aagum.
                // EmpId CreatedBy purpose-ku mattum use aagum.
                // ==========================================

                int empId = 0;

                var empIdClaim =
                    User.FindFirst("EmpId")?.Value;

                if (!string.IsNullOrWhiteSpace(empIdClaim))
                {
                    if (!int.TryParse(
                            empIdClaim,
                            out empId) ||
                        empId < 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "Invalid EmpId in token."
                        });
                    }
                }


                // ==========================================
                // 6. VALIDATE COMPANY USER IF EMPID EXISTS
                // COMMON DB - company_users
                // ==========================================

                if (empId > 0)
                {
                    var companyUser =
                        await _commonContext.CompanyUsers
                            .FirstOrDefaultAsync(x =>
                                x.EmpId == empId &&
                                x.OrgId == organizationId &&
                                (x.Status == null ||
                                 x.Status == "Active"));

                    if (companyUser == null)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message =
                                "Employee does not belong to this organization."
                        });
                    }
                }


                // ==========================================
                // 7. VALIDATE RATE VALUES
                // ==========================================

                if (dto.VehicleTypeId <= 0 ||
                    dto.FuelTypeId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Valid VehicleTypeId and FuelTypeId are required."
                    });
                }

                if (dto.AmountPerLiter < 0 ||
                    dto.AmountPerKm < 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Vehicle rate amounts cannot be negative."
                    });
                }


                // ==========================================
                // 8. CHECK DUPLICATE RATE
                // ORGANIZATION-WISE
                // ==========================================

                bool exists =
                    await _context.VehicleRateMasters
                        .AnyAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.VehicleTypeId == dto.VehicleTypeId &&
                            x.FuelTypeId == dto.FuelTypeId &&
                            x.ValidFrom.Date == dto.ValidFrom.Date &&
                            x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Vehicle Rate already exists for this organization."
                    });
                }


                // ==========================================
                // 9. CREATE VEHICLE RATE
                // ==========================================

                var rate = new VehicleRateMaster
                {
                    OrganizationId = organizationId,

                    VehicleTypeId = dto.VehicleTypeId,

                    FuelTypeId = dto.FuelTypeId,

                    AmountPerLiter = dto.AmountPerLiter,

                    AmountPerKm = dto.AmountPerKm,

                    ValidFrom = dto.ValidFrom,

                    CreatedBy = empId,

                    CreatedDate = DateTime.Now,

                    IsActive = true
                };


                // ==========================================
                // 10. SAVE
                // ==========================================

                _context.VehicleRateMasters.Add(rate);

                await _context.SaveChangesAsync();


                // ==========================================
                // 11. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message = "Vehicle Rate added successfully.",
                    data = new
                    {
                        rate.VehicleRateId,
                        rate.OrganizationId,
                        rate.VehicleTypeId,
                        rate.FuelTypeId,
                        rate.AmountPerLiter,
                        rate.AmountPerKm,
                        rate.ValidFrom,
                        rate.CreatedBy,
                        rate.CreatedDate,
                        rate.IsActive
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while adding Vehicle Rate.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("VehicleRateMasters")]
        public async Task<IActionResult> GetVehicleRates()
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
                // ==========================================

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid OrganizationId."
                    });
                }


                // ==========================================
                // 2. CHECK LOCAL EXPENSE PORTAL ACCESS
                // portal_roles.travel_portal
                // ==========================================

                var portalRole = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(portalRole))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Local Expense portal access not found."
                        });
                }


                // ==========================================
                // 3. GET VEHICLE RATES
                // ORGANIZATION-WISE FILTER
                // ==========================================

                var data = await (
                    from vr in _context.VehicleRateMasters

                    join vt in _context.VehicleMaster
                        on vr.VehicleTypeId equals vt.VehicleId

                    join ft in _context.FuelTypeMasters
                        on vr.FuelTypeId equals ft.FuelTypeId

                    where vr.IsActive &&
                          vr.OrganizationId == organizationId

                    orderby vt.VehicleName

                    select new
                    {
                        vr.VehicleRateId,

                        vr.OrganizationId,

                        vr.VehicleTypeId,

                        VehicleType = vt.VehicleName,

                        vr.FuelTypeId,

                        FuelType = ft.FuelTypeName,

                        vr.AmountPerLiter,

                        vr.AmountPerKm,

                        vr.ValidFrom
                    }
                ).ToListAsync();


                // ==========================================
                // 4. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,

                    message = data.Any()
                        ? "Vehicle rates fetched successfully."
                        : "No vehicle rates found for this organization.",

                    organizationId,

                    role = portalRole,

                    data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,

                    message = "Error while fetching vehicle rates.",

                    error = ex.InnerException?.Message ?? ex.Message
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
                // ==========================================
                // 1. VALIDATE INPUT
                // ==========================================

                if (vehicleTypeId <= 0 || fuelTypeId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Valid VehicleTypeId and FuelTypeId are required."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION ID FROM JWT
                // ==========================================

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }


                // ==========================================
                // 3. CHECK LOCAL EXPENSE PORTAL ACCESS
                // portal_roles.travel_portal
                // ==========================================

                var portalRole = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(portalRole))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message = "Local Expense portal access not found."
                        });
                }


                // ==========================================
                // 4. GET VEHICLE RATE
                // ORGANIZATION-WISE FILTER
                // ==========================================

                var rate = await (
                    from vr in _context.VehicleRateMasters

                    join v in _context.VehicleMaster
                        on vr.VehicleTypeId equals v.VehicleId

                    join f in _context.FuelTypeMasters
                        on vr.FuelTypeId equals f.FuelTypeId

                    where vr.IsActive
                          && vr.OrganizationId == organizationId
                          && vr.VehicleTypeId == vehicleTypeId
                          && vr.FuelTypeId == fuelTypeId

                    select new
                    {
                        vr.VehicleRateId,

                        vr.OrganizationId,

                        vr.VehicleTypeId,

                        VehicleType = v.VehicleName,

                        vr.FuelTypeId,

                        FuelType = f.FuelTypeName,

                        vr.AmountPerLiter,

                        vr.AmountPerKm,

                        vr.ValidFrom
                    }
                ).FirstOrDefaultAsync();


                // ==========================================
                // 5. RATE NOT FOUND
                // ==========================================

                if (rate == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Rate not configured for this organization."
                    });
                }


                // ==========================================
                // 6. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message = "Vehicle rate fetched successfully.",
                    data = rate
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching vehicle rate.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpPut("VehicleRateMasters/{id}")]
        public async Task<IActionResult> UpdateVehicleRate(
       int id,
       [FromBody] VehicleRateDto dto)
        {
            try
            {
                // ==========================================
                // 1. VALIDATE REQUEST
                // ==========================================

                if (id <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid Vehicle Rate ID."
                    });
                }

                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Vehicle Rate data is required."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION ID FROM JWT
                // ==========================================

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid OrganizationId."
                    });
                }


                // ==========================================
                // 3. GET LOCAL EXPENSE PORTAL ROLE
                // portal_roles.travel_portal
                // ==========================================

                var portalRole = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(portalRole))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Local Expense portal access not found."
                        });
                }


                // ==========================================
                // 4. ADMIN ONLY
                // ==========================================

                if (!portalRole.Equals(
                        "Admin",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Only Local Expense Admin can update Vehicle Rates."
                        });
                }


                // ==========================================
                // 5. VALIDATE RATE VALUES
                // ==========================================

                if (dto.VehicleTypeId <= 0 ||
                    dto.FuelTypeId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Valid VehicleTypeId and FuelTypeId are required."
                    });
                }

                if (dto.AmountPerLiter < 0 ||
                    dto.AmountPerKm < 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Vehicle rate amounts cannot be negative."
                    });
                }


                // ==========================================
                // 6. GET VEHICLE RATE
                // ORGANIZATION-WISE FILTER
                // ==========================================

                var vehicleRate =
                    await _context.VehicleRateMasters
                        .FirstOrDefaultAsync(x =>
                            x.VehicleRateId == id &&
                            x.OrganizationId == organizationId &&
                            x.IsActive);

                if (vehicleRate == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message =
                            "Vehicle Rate not found in this organization."
                    });
                }


                // ==========================================
                // 7. DUPLICATE CHECK
                // SAME ORGANIZATION
                // ==========================================

                bool exists =
                    await _context.VehicleRateMasters
                        .AnyAsync(x =>
                            x.VehicleRateId != id &&
                            x.OrganizationId == organizationId &&
                            x.VehicleTypeId == dto.VehicleTypeId &&
                            x.FuelTypeId == dto.FuelTypeId &&
                            x.ValidFrom.Date == dto.ValidFrom.Date &&
                            x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Vehicle Rate already exists for this organization."
                    });
                }


                // ==========================================
                // 8. UPDATE VEHICLE RATE
                // ==========================================

                vehicleRate.VehicleTypeId =
                    dto.VehicleTypeId;

                vehicleRate.FuelTypeId =
                    dto.FuelTypeId;

                vehicleRate.AmountPerLiter =
                    dto.AmountPerLiter;

                vehicleRate.AmountPerKm =
                    dto.AmountPerKm;

                vehicleRate.ValidFrom =
                    dto.ValidFrom;


                // ==========================================
                // 9. SAVE CHANGES
                // ==========================================

                await _context.SaveChangesAsync();


                // ==========================================
                // 10. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message =
                        "Vehicle Rate updated successfully.",
                    data = new
                    {
                        vehicleRate.VehicleRateId,
                        vehicleRate.OrganizationId,
                        vehicleRate.VehicleTypeId,
                        vehicleRate.FuelTypeId,
                        vehicleRate.AmountPerLiter,
                        vehicleRate.AmountPerKm,
                        vehicleRate.ValidFrom
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Error while updating Vehicle Rate.",
                    error =
                        ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpDelete("VehicleRateMasters/{id}")]
        public async Task<IActionResult> DeleteVehicleRate(int id)
        {
            try
            {
                // ==========================================
                // 1. VALIDATE ID
                // ==========================================

                if (id <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid Vehicle Rate ID."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION ID FROM JWT
                // ==========================================

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid OrganizationId."
                    });
                }


                // ==========================================
                // 3. GET LOCAL EXPENSE PORTAL ROLE
                // portal_roles.travel_portal
                // ==========================================

                var portalRole = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(portalRole))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Local Expense portal access not found."
                        });
                }


                // ==========================================
                // 4. ADMIN ONLY
                // ==========================================

                if (!portalRole.Equals(
                        "Admin",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Only Local Expense Admin can delete Vehicle Rates."
                        });
                }


                // ==========================================
                // 5. GET VEHICLE RATE
                // ORGANIZATION-WISE FILTER
                // ==========================================

                var vehicleRate =
                    await _context.VehicleRateMasters
                        .FirstOrDefaultAsync(x =>
                            x.VehicleRateId == id &&
                            x.OrganizationId == organizationId &&
                            x.IsActive);

                if (vehicleRate == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message =
                            "Active Vehicle Rate not found in this organization."
                    });
                }


                // ==========================================
                // 6. SOFT DELETE
                // ==========================================

                vehicleRate.IsActive = false;

                await _context.SaveChangesAsync();


                // ==========================================
                // 7. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message =
                        "Vehicle Rate deleted successfully.",
                    data = new
                    {
                        vehicleRate.VehicleRateId,
                        vehicleRate.OrganizationId,
                        vehicleRate.IsActive
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Error while deleting Vehicle Rate.",
                    error =
                        ex.InnerException?.Message ?? ex.Message
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

        //------------------------ For Dynamic Approvl method------------------------------//

        private async Task<List<int>> GetApproverEmployeeIdsAsync(
    int? organizationId,
    int claimEmployeeId,
    string approverType,
    int? roleId,
    int? employeeId)
        {
            try
            {
                approverType = approverType?.Trim() ?? string.Empty;

                var approverIds = new List<int>();

                // ==========================================
                // 1. REPORTING MANAGER
                // ==========================================

                if (approverType.Equals(
                        "ReportingManager",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var employee = await _context.EmployeeMasters
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == claimEmployeeId &&
                            x.OrganizationId == organizationId &&
                            x.IsActive);

                    if (employee?.ReportingManagerId.HasValue == true &&
                        employee.ReportingManagerId.Value > 0)
                    {
                        var managerExists = await _context.EmployeeMasters
                            .AnyAsync(x =>
                                x.EmpId == employee.ReportingManagerId.Value &&
                                x.OrganizationId == organizationId &&
                                x.IsActive);

                        if (managerExists)
                        {
                            approverIds.Add(employee.ReportingManagerId.Value);
                        }
                    }
                }

                // ==========================================
                // 2. ROLE
                // ==========================================

                else if (approverType.Equals(
                             "Role",
                             StringComparison.OrdinalIgnoreCase))
                {
                    if (!roleId.HasValue || roleId.Value <= 0)
                    {
                        return approverIds;
                    }

                    var employees = await _context.EmployeeMasters
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.RoleId == roleId.Value &&
                            x.IsActive)
                        .OrderBy(x => x.EmpId)
                        .Select(x => x.EmpId)
                        .ToListAsync();

                    // ALL active employees having this role
                    approverIds.AddRange(employees);
                }

                // ==========================================
                // 3. SPECIFIC EMPLOYEE
                // ==========================================

                else if (approverType.Equals(
                             "Employee",
                             StringComparison.OrdinalIgnoreCase))
                {
                    if (!employeeId.HasValue || employeeId.Value <= 0)
                    {
                        return approverIds;
                    }

                    var employeeExists = await _context.EmployeeMasters
                        .AnyAsync(x =>
                            x.EmpId == employeeId.Value &&
                            x.OrganizationId == organizationId &&
                            x.IsActive);

                    if (employeeExists)
                    {
                        approverIds.Add(employeeId.Value);
                    }
                }

                return approverIds
                    .Distinct()
                    .ToList();
            }
            catch
            {
                throw;
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
                            // ============================================
                            // 1. ORGANIZATION AND REQUEST VALIDATION
                            // ============================================

                            var organizationId = GetOrganizationId();

                            if (organizationId <= 0)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "Invalid organization."
                                });
                            }

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

                            if (dto.ClaimEntries == null ||
                                !dto.ClaimEntries.Any())
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "At least one claim entry is required."
                                });
                            }

                            // ============================================
                            // 2. GET EMPLOYEE FROM COMMON DATABASE
                            // ============================================

                            var companyUser = await _commonContext.CompanyUsers
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x =>
                                    x.EmpId == dto.EmployeeId &&
                                    x.OrgId == organizationId &&
                                    x.Status == "Active");

                            if (companyUser == null)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        "Active employee not found in this organization."
                                });
                            }

                            if (!companyUser.BranchId.HasValue)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        "Employee branch is not configured."
                                });
                            }

                            // ============================================
                            // 3. VALIDATE SELECTED DEPARTMENT
                            // ============================================

                            int? selectedDepartmentId = dto.DepartmentId;

                            if (isSubmit &&
                                (!selectedDepartmentId.HasValue ||
                                 selectedDepartmentId.Value <= 0))
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "Please select a department."
                                });
                            }

                            // CompanyUsers.DepartmentId can contain values like:
                            // "21,15"

                            var employeeDepartmentIds =
                                (companyUser.DepartmentId ?? string.Empty)
                                .Split(
                                    ',',
                                    StringSplitOptions.RemoveEmptyEntries)
                                .Select(value =>
                                {
                                    return int.TryParse(
                                        value.Trim(),
                                        out int departmentId)
                                        ? departmentId
                                        : 0;
                                })
                                .Where(id => id > 0)
                                .Distinct()
                                .ToList();

                            if (selectedDepartmentId.HasValue)
                            {
                                if (selectedDepartmentId.Value <= 0 ||
                                    !employeeDepartmentIds.Contains(
                                        selectedDepartmentId.Value))
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message =
                                            "Selected department is not assigned " +
                                            "to this employee."
                                    });
                                }
                            }

                            // ============================================
                            // 4. ONE EMPLOYEE = ONE CLAIM PER MONTH
                            // ============================================

                            var claimMonth = dto.ClaimMonth.Month;
                            var claimYear = dto.ClaimMonth.Year;

                            var existingClaim = await _context.ClaimMasters
                                .FirstOrDefaultAsync(x =>
                                    x.EmployeeId == dto.EmployeeId &&
                                    x.ClaimMonth.Month == claimMonth &&
                                    x.OrganizationId == organizationId &&
                                    x.ClaimYear == claimYear &&
                                    x.IsActive);

                            if (existingClaim != null &&
                                existingClaim.Status != "Draft" &&
                                existingClaim.Status != "Rejected")
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        $"Claim already exists for " +
                                        $"{dto.ClaimMonth:MMMM yyyy}."
                                });
                            }

                            // Prevent switching department after a claim has
                            // already been submitted or rejected.
                            if (existingClaim != null &&
                                existingClaim.DepartmentId.HasValue &&
                                selectedDepartmentId.HasValue &&
                                existingClaim.DepartmentId.Value !=
                                    selectedDepartmentId.Value)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        "The selected department differs from " +
                                        "the existing claim department."
                                });
                            }

                            // ============================================
                            // 5. CALCULATE TOTAL AMOUNT
                            // ============================================

                            decimal totalAmount = dto.ClaimEntries.Sum(x =>
                                x.TravelAmount +
                                x.FoodAmount +
                                (x.Expenses?.Sum(e => e.ExpenseAmount) ?? 0)
                            );

                            // ============================================
                            // 6. CREATE / UPDATE CLAIM MASTER
                            // ============================================

                            ClaimMaster claim;

                            if (existingClaim != null)
                            {
                                claim = existingClaim;

                                // Preserve existing amount-update behaviour.
                                claim.TotalAmount =
                                    (claim.TotalAmount ?? 0) + totalAmount;

                                // Save the selected department.
                                if (selectedDepartmentId.HasValue)
                                {
                                    claim.DepartmentId =
                                        selectedDepartmentId.Value;
                                }

                                claim.LastModifiedAt = DateTime.Now;
                            }
                            else
                            {
                                claim = new ClaimMaster
                                {
                                    EmployeeId = dto.EmployeeId,

                                    DepartmentId = selectedDepartmentId,

                                    ClaimMonth = dto.ClaimMonth,
                                    ClaimYear = dto.ClaimMonth.Year,
                                    OrganizationId = organizationId,

                                    CreatedAt = DateTime.Now,
                                    LastModifiedAt = DateTime.Now,

                                    TotalAmount = totalAmount,
                                    IsActive = true,

                                    Status = "Draft",
                                    SubmittedAt = null,
                                    CurrentApprovalLevel = null
                                };

                                _context.ClaimMasters.Add(claim);

                                await _context.SaveChangesAsync();

                                claim.ClaimNo =
                                    await GenerateClaimNumber(
                                        "Local Expense Claim");

                                await _context.SaveChangesAsync();
                            }

                            // ============================================
                            // 7. CONFIGURATION-BASED APPROVAL FLOW
                            // ============================================

                            if (isSubmit)
                            {
                                // ----------------------------------------
                                // 7.1 Get branch
                                // ----------------------------------------

                                var branch = await _commonContext.Branches
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(x =>
                                        x.BranchId == companyUser.BranchId.Value &&
                                        x.OrgId == organizationId);

                                if (branch == null ||
                                    string.IsNullOrWhiteSpace(branch.BranchName))
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message = "Branch configuration not found."
                                    });
                                }

                                var branchName = branch.BranchName.Trim();

                                // ----------------------------------------
                                // 7.2 Get selected department configuration
                                // ----------------------------------------

                                const string moduleCode = "travel_portal";

                                var departmentConfig =
                                    await _context.DepartmentApprovalConfigs
                                        .FirstOrDefaultAsync(x =>
                                            x.OrganizationId == organizationId &&
                                            x.ModuleCode == moduleCode &&
                                            x.DepartmentId ==
                                                selectedDepartmentId!.Value &&
                                            x.IsActive &&
                                            x.Branch != null &&
                                            x.Branch.Trim() == branchName);

                                if (departmentConfig == null)
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message =
                                            "Approval configuration not found " +
                                            "for the selected department.",
                                        departmentId = selectedDepartmentId,
                                        branch = branchName
                                    });
                                }

                                if (departmentConfig.TotalApprovalLevels <= 0)
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message =
                                            "Approval levels are not configured " +
                                            "for the selected department."
                                    });
                                }

                                // ----------------------------------------
                                // 7.3 Get employee approval configuration
                                // ----------------------------------------

                                var employeeApprovalConfigs =
                                    await _context.EmployeeApprovalConfigs
                                        .Include(x => x.Approvers)
                                        .Where(x =>
                                            x.OrganizationId == organizationId &&
                                            x.DepartmentApprovalConfigId ==
                                                departmentConfig.Id &&
                                            x.EmployeeId == dto.EmployeeId &&
                                            x.IsActive)
                                        .OrderBy(x => x.ApprovalLevel)
                                        .ToListAsync();

                                if (!employeeApprovalConfigs.Any())
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message =
                                            "No employee approval configuration found " +
                                            $"for employee {dto.EmployeeId} " +
                                            $"in department {selectedDepartmentId}."
                                    });
                                }

                                // ----------------------------------------
                                // 7.4 Validate approval levels
                                // ----------------------------------------

                                var configuredLevels =
                                    employeeApprovalConfigs
                                        .Select(x => x.ApprovalLevel)
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList();

                                var expectedLevels =
                                    departmentConfig.TotalApprovalLevels;

                                var expectedSequence =
                                    Enumerable.Range(1, expectedLevels).ToList();

                                if (!configuredLevels.SequenceEqual(
                                        expectedSequence))
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message =
                                            "Configured approval levels do not match " +
                                            "the department configuration.",
                                        expectedLevels,
                                        configuredLevels
                                    });
                                }

                                // ----------------------------------------
                                // 7.5 Build approval records
                                // ----------------------------------------

                                var newApprovals = new List<ClaimApproval>();

                                foreach (var levelConfig in employeeApprovalConfigs)
                                {
                                    var approverIds = levelConfig.Approvers
                                        .Where(x => x.IsActive)
                                        .OrderBy(x => x.ApprovalOrder)
                                        .Select(x => x.ApproverEmployeeId)
                                        .Distinct()
                                        .ToList();

                                    if (!approverIds.Any())
                                    {
                                        return BadRequest(new
                                        {
                                            success = false,
                                            message =
                                                "No active approver configured for " +
                                                $"approval level {levelConfig.ApprovalLevel}."
                                        });
                                    }

                                    foreach (var approverId in approverIds)
                                    {
                                        if (approverId == dto.EmployeeId)
                                        {
                                            return BadRequest(new
                                            {
                                                success = false,
                                                message =
                                                    "An employee cannot approve " +
                                                    "their own claim."
                                            });
                                        }

                                        var approver = await _commonContext.CompanyUsers
                                            .AsNoTracking()
                                            .FirstOrDefaultAsync(x =>
                                                x.EmpId == approverId &&
                                                x.OrgId == organizationId &&
                                                x.Status == "Active");

                                        if (approver == null)
                                        {
                                            return BadRequest(new
                                            {
                                                success = false,
                                                message =
                                                    $"Approver employee {approverId} " +
                                                    "does not exist or is inactive " +
                                                    "in this organization."
                                            });
                                        }

                                        newApprovals.Add(new ClaimApproval
                                        {
                                            ClaimId = claim.ClaimId,

                                            // Associate this approval with the
                                            // selected department configuration.
                                            DepartmentApprovalConfigId =
                                                departmentConfig.Id,

                                            ApprovalLevel =
                                                levelConfig.ApprovalLevel,

                                            ApproverEmpId = approverId,

                                            Status = "Pending",
                                            Remarks = null,
                                            ActionDate = null,

                                            CreatedAt = DateTime.Now,
                                            ApprovalToken = Guid.NewGuid()
                                        });
                                    }
                                }

                                // ----------------------------------------
                                // 7.6 Remove previous approvals on resubmit
                                // ----------------------------------------

                                var oldApprovals =
                                    await _context.ClaimApprovals
                                        .Where(x => x.ClaimId == claim.ClaimId)
                                        .ToListAsync();

                                if (oldApprovals.Any())
                                {
                                    _context.ClaimApprovals.RemoveRange(oldApprovals);
                                }

                                _context.ClaimApprovals.AddRange(newApprovals);

                                // ----------------------------------------
                                // 7.7 Update claim status
                                // ----------------------------------------

                                var firstLevel = configuredLevels.First();

                                claim.DepartmentId = selectedDepartmentId.Value;
                                claim.Status = "Pending";
                                claim.SubmittedAt = DateTime.Now;
                                claim.CurrentApprovalLevel = firstLevel;
                                claim.LastModifiedAt = DateTime.Now;

                                await _context.SaveChangesAsync();

                    // ----------------------------------------
                    // 7.8 Department display value
                    // ----------------------------------------

                    string departmentName = "N/A";

                    try
                    {
                        if (int.TryParse(companyUser.DepartmentId, out int departmentId))
                        {
                            departmentName = await _commonContext.Departments
                                .Where(d => d.Id == departmentId)
                                .Select(d => d.DepartmentName)
                                .FirstOrDefaultAsync() ?? "N/A";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching department name: {ex.Message}");
                    }

                    // ----------------------------------------
                    // 7.9 Get employee details for email
                    // ----------------------------------------

                    var employeeName =
                                    companyUser.FullName ?? "-";

                                var employeeCode =
                                    companyUser.EmpCode ?? "-";

                                var firstLevelApprovals = newApprovals
                                    .Where(x => x.ApprovalLevel == firstLevel)
                                    .ToList();

                                string purpose = dto.ClaimEntries
                                    .FirstOrDefault()?.Purpose ?? "-";

                                var baseUrl =
                                    _configuration["AppSettings:BaseUrl"];

                                if (string.IsNullOrWhiteSpace(baseUrl))
                                {
                                    throw new Exception(
                                        "AppSettings:BaseUrl is not configured.");
                                }

                                // ----------------------------------------
                                // 7.10 Send email to first-level approvers
                                // ----------------------------------------

                                foreach (var firstApproval in firstLevelApprovals)
                                {
                                    var approver = await _commonContext.CompanyUsers
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(x =>
                                            x.EmpId == firstApproval.ApproverEmpId &&
                                            x.OrgId == organizationId &&
                                            x.Status == "Active");

                                    if (approver == null ||
                                        string.IsNullOrWhiteSpace(approver.Email))
                                    {
                                        continue;
                                    }

                                    var approveUrl =
                                        $"{baseUrl}/api/TravelMaster/mail-approve" +
                                        $"?token={firstApproval.ApprovalToken}";

                                    var rejectUrl =
                                        $"{baseUrl}/api/TravelMaster/reject-form" +
                                        $"?token={firstApproval.ApprovalToken}";

                                    string body = $@"
Dear {approver.FullName ?? "Approver"},

<br/><br/>

A new Local Expense Claim requires your approval.

<br/><br/>

<b>Employee Details</b>
<ul>
    <li>Employee Name: {employeeName}</li>
    <li>Employee ID: {employeeCode}</li>
   <li>Department: {departmentName}</li>
</ul>

<b>Claim Details</b>
<ul>
    <li>Claim Number: {claim.ClaimNo}</li>
    <li>Claim Type: Local Expense Claim</li>
    <li>Claim Month: {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount: ₹{claim.TotalAmount:N2}</li>
    <li>Status: Pending Approval</li>
    <li>Approval Level: {firstApproval.ApprovalLevel}</li>
</ul>

<b>Purpose</b>
<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;color:white;padding:10px 20px;
text-decoration:none;border-radius:5px;'>
Approve
</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;color:white;padding:10px 20px;
text-decoration:none;border-radius:5px;'>
Reject
</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

                                    await _emailService.SendEmailAsync(
                                        approver.Email,
                                        $"Claim Approval Required - {claim.ClaimNo}",
                                        body);
                                }
                            }

                            // ============================================
                            // 8. SAVE TRAVEL ENTRIES
                            // ============================================

                            var savedTravelIds = new List<object>();

                            foreach (var entry in dto.ClaimEntries)
                            {
                                decimal? appliedRatePerKm = null;
                                decimal travelAmount = 0;

                                // Bike = 2; Car = 3.
                                // Fuel rate is required only for Bike and Car.

                                if (entry.VehicleTypeId == 2 ||
                                    entry.VehicleTypeId == 3)
                                {
                                    if (!entry.FuelTypeId.HasValue ||
                                        entry.FuelTypeId.Value <= 0)
                                    {
                                        return BadRequest(new
                                        {
                                            success = false,
                                            message =
                                                $"Fuel Type is required for VehicleTypeId " +
                                                $"{entry.VehicleTypeId}."
                                        });
                                    }

                                    var currentRate =
                                        await _context.VehicleRateMasters
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
                                                "Vehicle rate is not configured for " +
                                                $"VehicleTypeId {entry.VehicleTypeId} " +
                                                $"and FuelTypeId {entry.FuelTypeId.Value}."
                                        });
                                    }

                                    appliedRatePerKm = currentRate.AmountPerKm;

                                    travelAmount =
                                        Convert.ToDecimal(entry.KmRun) *
                                        currentRate.AmountPerKm;
                                }
                                else
                                {
                                    appliedRatePerKm = null;
                                    travelAmount = entry.TravelAmount;
                                }

                                // ----------------------------------------
                                // Save travel
                                // ----------------------------------------

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

                                    Amount = travelAmount,
                                    AppliedRatePerKm = appliedRatePerKm,

                                    IsActive = true
                                };

                                _context.ClaimTravels.Add(travel);

                                await _context.SaveChangesAsync();

                                savedTravelIds.Add(new
                                {
                                    travelId = travel.TravelId,
                                    travelDate = travel.TravelDate
                                });

                                // ========================================
                                // 9. SAVE FOOD DETAILS
                                // ========================================

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

                                // ========================================
                                // 10. SAVE MULTIPLE EXPENSES
                                // ========================================

                                if (entry.Expenses != null &&
                                    entry.Expenses.Any())
                                {
                                    foreach (var expenseItem in entry.Expenses)
                                    {
                                        if (string.IsNullOrWhiteSpace(
                                                expenseItem.ExpenseType) ||
                                            expenseItem.ExpenseAmount <= 0)
                                        {
                                            continue;
                                        }

                                        var expense = new ClaimExpense
                                        {
                                            ClaimId = claim.ClaimId,
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

                            // ============================================
                            // 11. COMMIT TRANSACTION
                            // ============================================

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
                                    claim.DepartmentId,
                                    claim.CurrentApprovalLevel,
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
        [HttpGet("my-drafts")]
        public async Task<IActionResult> GetMyDrafts()
        {
            try
            {
                // ==========================================
                // 1. OrganizationId from JWT
                // ==========================================
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 2. EmployeeId from JWT
                // ==========================================
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 3. Validate employee in Common DB
                //    company_users ONLY
                // ==========================================
                var companyUserExists = await _commonContext.CompanyUsers
                    .AnyAsync(x =>
                        x.EmpId == employeeId &&
                        x.OrgId == organizationId &&
                        (
                            x.Status == null ||
                            x.Status == "Active"
                        ));

                if (!companyUserExists)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 4. Get own drafts
                //    Organization + Employee based
                // ==========================================
                var drafts = await _context.ClaimMasters
                    .Where(x =>
                        x.EmployeeId == employeeId &&
                        x.OrganizationId == organizationId &&
                        x.Status == "Draft" &&
                        x.IsActive)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => new
                    {
                        x.ClaimId,
                        x.ClaimNo,
                        x.ClaimMonth,
                        x.TotalAmount,
                        x.Status,
                        x.CreatedAt,
                        x.OrganizationId,
                        x.EmployeeId
                    })
                    .ToListAsync();

                // ==========================================
                // 5. Response
                // ==========================================
                return Ok(new
                {
                    success = true,
                    organizationId = organizationId,
                    employeeId = employeeId,
                    count = drafts.Count,
                    data = drafts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching my drafts.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpDelete("delete-draft-entry/{claimId}/{travelId}")]
        public async Task<IActionResult> DeleteDraftEntry(
            int claimId,
            int travelId)
        {
            try
            {
                // ==========================================
                // 1. Validate IDs
                // ==========================================
                if (claimId <= 0 || travelId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid ClaimId or TravelId."
                    });
                }

                // ==========================================
                // 2. OrganizationId from JWT
                // ==========================================
                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 3. Local Expense role from JWT
                // ==========================================
                var role = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // ==========================================
                // 4. EmployeeId from JWT
                // ==========================================
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 5. Validate employee in Common DB
                // ==========================================
                var companyUserExists = await _commonContext.CompanyUsers
                    .AnyAsync(x =>
                        x.EmpId == employeeId &&
                        x.OrgId == organizationId &&
                        (
                            x.Status == null ||
                            x.Status == "Active"
                        ));

                if (!companyUserExists)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 6. Start transaction
                // ==========================================
                await using var transaction =
                    await _context.Database.BeginTransactionAsync();

                // ==========================================
                // 7. Get OWN Draft Claim
                //    Organization + Employee based
                // ==========================================
                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.EmployeeId == employeeId &&
                        x.OrganizationId == organizationId &&
                        x.Status == "Draft" &&
                        x.IsActive);

                if (claim == null)
                {
                    await transaction.RollbackAsync();

                    return NotFound(new
                    {
                        success = false,
                        message = "Draft claim not found."
                    });
                }

                // ==========================================
                // 8. Get ONLY selected travel entry
                // ==========================================
                var travel = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.TravelId == travelId &&
                        x.IsActive);

                if (travel == null)
                {
                    await transaction.RollbackAsync();

                    return NotFound(new
                    {
                        success = false,
                        message = "Draft travel entry not found."
                    });
                }

                // ==========================================
                // 9. Soft delete Travel
                // ==========================================
                travel.IsActive = false;

                // ==========================================
                // 10. Soft delete Food
                // ==========================================
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

                // ==========================================
                // 11. Soft delete Expenses
                // ==========================================
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

                // ==========================================
                // 12. Soft delete Documents
                // ==========================================
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

                // ==========================================
                // 13. Recalculate Claim Total
                // ==========================================
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

                decimal totalAmount = 0m;

                totalAmount += remainingTravels
                    .Sum(x => x.Amount ?? 0m);

                totalAmount += remainingFoods
                    .Sum(x => x.Amount);

                totalAmount += remainingExpenses
                    .Sum(x => x.Amount ?? 0m);

                claim.TotalAmount = totalAmount;
                claim.LastModifiedAt = DateTime.Now;

                // ==========================================
                // 14. Save
                // ==========================================
                await _context.SaveChangesAsync();

                // ==========================================
                // 15. Commit
                // ==========================================
                await transaction.CommitAsync();

                // ==========================================
                // 16. Response
                // ==========================================
                return Ok(new
                {
                    success = true,
                    message = "Draft entry deleted successfully.",
                    organizationId = organizationId,
                    employeeId = employeeId,
                    claimId = claimId,
                    deletedTravelId = travelId,
                    remainingClaimAmount = totalAmount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deleting draft entry.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        //[Authorize]
        //[HttpGet("draft-details/{claimId}")]
        //public async Task<IActionResult> GetDraftDetails(int claimId)
        //{
        //    try
        //    {
        //        // ==========================================
        //        // 1. Validate ClaimId
        //        // ==========================================
        //        if (claimId <= 0)
        //        {
        //            return BadRequest(new
        //            {
        //                success = false,
        //                message = "Invalid ClaimId."
        //            });
        //        }

        //        // ==========================================
        //        // 2. OrganizationId from JWT
        //        // ==========================================
        //        int organizationId = GetOrganizationId();

        //        if (organizationId <= 0)
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "OrganizationId not found in token."
        //            });
        //        }

        //        // ==========================================
        //        // 3. Local Expense role from portal_roles
        //        // ==========================================
        //        var role = GetLocalExpenseRole();

        //        if (string.IsNullOrWhiteSpace(role))
        //        {
        //            return Forbid();
        //        }

        //        // ==========================================
        //        // 4. EmployeeId from JWT
        //        // ==========================================
        //        var empIdClaim = User.FindFirst("EmpId")?.Value;

        //        if (!int.TryParse(empIdClaim, out int employeeId) ||
        //            employeeId <= 0)
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "EmployeeId not found in token."
        //            });
        //        }

        //        // ==========================================
        //        // 5. Validate employee in Common DB
        //        //    company_users ONLY
        //        // ==========================================
        //        var companyUserExists = await _commonContext.CompanyUsers
        //            .AnyAsync(x =>
        //                x.EmpId == employeeId &&
        //                x.OrgId == organizationId &&
        //                (
        //                    x.Status == null ||
        //                    x.Status == "Active"
        //                ));

        //        if (!companyUserExists)
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "Employee not found in company_users."
        //            });
        //        }

        //        // ==========================================
        //        // 6. Base URL
        //        // ==========================================
        //        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        //        // ==========================================
        //        // 7. Get OWN Draft Claim
        //        //    Employee + Organization based
        //        // ==========================================
        //        var claim = await _context.ClaimMasters
        //            .FirstOrDefaultAsync(x =>
        //                x.ClaimId == claimId &&
        //                x.EmployeeId == employeeId &&
        //                x.OrganizationId == organizationId &&
        //                x.Status == "Draft" &&
        //                x.IsActive);

        //        if (claim == null)
        //        {
        //            return NotFound(new
        //            {
        //                success = false,
        //                message = "Draft not found."
        //            });
        //        }

        //        // ==========================================
        //        // 8. Get Travels
        //        // ==========================================
        //        var travels = await _context.ClaimTravels
        //            .Where(x =>
        //                x.ClaimId == claimId &&
        //                x.IsActive)
        //            .OrderBy(x => x.TravelId)
        //            .ToListAsync();

        //        // ==========================================
        //        // 9. Get Documents
        //        // ==========================================
        //        var documents = await _context.ClaimDocuments
        //            .Where(x =>
        //                x.ClaimId == claimId &&
        //                x.IsActive)
        //            .ToListAsync();

        //        // ==========================================
        //        // 10. Claim Entries
        //        // ==========================================
        //        var claimEntries = new List<object>();

        //        decimal grandTotalAmount = 0m;

        //        foreach (var travel in travels)
        //        {
        //            // ======================================
        //            // FOOD
        //            // ======================================
        //            var food = await _context.ClaimFoods
        //                .FirstOrDefaultAsync(x =>
        //                    x.ClaimId == claimId &&
        //                    x.TravelId == travel.TravelId &&
        //                    x.IsActive);

        //            // ======================================
        //            // EXPENSES
        //            // ======================================
        //            var expenses = await _context.ClaimExpenses
        //                .Where(x =>
        //                    x.ClaimId == claimId &&
        //                    x.TravelId == travel.TravelId &&
        //                    x.IsActive)
        //                .ToListAsync();

        //            // ======================================
        //            // VEHICLE NAME
        //            // ======================================
        //            var vehicleName = await _context.VehicleMaster
        //                .Where(v =>
        //                    v.VehicleId == travel.VehicleTypeId &&
        //                    v.IsActive)
        //                .Select(v => v.VehicleName)
        //                .FirstOrDefaultAsync();

        //            // ======================================
        //            // EXPENSE LIST
        //            // ======================================
        //            var expenseList = expenses
        //                .Select(e => new
        //                {
        //                    expenseId = e.ExpenseId,
        //                    expenseType = e.ExpenseType,
        //                    invoiceNo = e.InvoiceNo,
        //                    description = e.Description,
        //                    expensePurpose = e.Purpose,
        //                    expenseAmount = e.Amount ?? 0m
        //                })
        //                .ToList();

        //            // ======================================
        //            // OTHER EXPENSE
        //            // ======================================
        //            decimal otherAmount = expenses
        //                .Where(e =>
        //                    !string.Equals(
        //                        e.ExpenseType,
        //                        "Toll",
        //                        StringComparison.OrdinalIgnoreCase))
        //                .Sum(e => e.Amount ?? 0m);

        //            // ======================================
        //            // TOLL
        //            // ======================================
        //            decimal tollAmount = expenses
        //                .Where(e =>
        //                    string.Equals(
        //                        e.ExpenseType,
        //                        "Toll",
        //                        StringComparison.OrdinalIgnoreCase))
        //                .Sum(e => e.Amount ?? 0m);

        //            // ======================================
        //            // TRAVEL AMOUNT
        //            // ======================================
        //            decimal travelAmount = travel.Amount ?? 0m;

        //            // ======================================
        //            // FOOD AMOUNT
        //            // ======================================
        //            decimal foodAmount = food?.Amount ?? 0m;

        //            // ======================================
        //            // ENTRY TOTAL
        //            // ======================================
        //            decimal entryTotalAmount =
        //                travelAmount +
        //                foodAmount +
        //                tollAmount +
        //                otherAmount;

        //            grandTotalAmount += entryTotalAmount;

        //            // ======================================
        //            // DOCUMENTS FOR THIS TRAVEL
        //            // ======================================
        //            var travelDocuments = documents
        //                .Where(d => d.ReferenceId == travel.TravelId)
        //                .Select(d => new
        //                {
        //                    d.DocumentId,
        //                    d.ReferenceId,
        //                    d.ModuleType,
        //                    d.DocumentType,

        //                    FilePath = string.IsNullOrEmpty(d.FilePath)
        //                        ? ""
        //                        : $"{baseUrl}/{d.FilePath.Replace("\\", "/")}",

        //                    d.OriginalFileName
        //                })
        //                .ToList();

        //            // ======================================
        //            // CLAIM ENTRY
        //            // ======================================
        //            claimEntries.Add(new
        //            {
        //                // TRAVEL
        //                travelId = travel.TravelId,
        //                vehicleTypeId = travel.VehicleTypeId,
        //                vehicleName = vehicleName,
        //                fuelTypeId = travel.FuelTypeId,
        //                vehicleDetailId = travel.VehicleDetailId,
        //                vehicleNumber = travel.VehicleNumber,
        //                appliedRatePerKm = travel.AppliedRatePerKm,
        //                travelDate = travel.TravelDate,
        //                purpose = travel.Purpose,
        //                fromLocation = travel.FromLocation,
        //                toLocation = travel.ToLocation,
        //                kmRun = travel.KmRun,
        //                travelAmount = travelAmount,

        //                // FOOD
        //                foodTypeId = food?.FoodTypeId,
        //                foodAmount = foodAmount,

        //                // EXPENSE TOTALS
        //                tollAmount = tollAmount,
        //                otherAmount = otherAmount,

        //                // ENTRY TOTAL
        //                totalAmount = entryTotalAmount,

        //                // ALL EXPENSES
        //                expenses = expenseList,

        //                // DOCUMENTS
        //                documents = travelDocuments
        //            });
        //        }

        //        // ==========================================
        //        // 11. FINAL RESPONSE
        //        // ==========================================
        //        return Ok(new
        //        {
        //            success = true,

        //            data = new
        //            {
        //                claimId = claim.ClaimId,
        //                claimNo = claim.ClaimNo,

        //                employeeId = claim.EmployeeId,
        //                organizationId = claim.OrganizationId,

        //                claimMonth = claim.ClaimMonth,

        //                totalAmount = grandTotalAmount,

        //                status = claim.Status,

        //                claimEntries = claimEntries
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "Error while fetching draft details.",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}


        [Authorize]
        [HttpGet("draft-details/{claimId}")]
        public async Task<IActionResult> GetDraftDetails(int claimId)
        {
            try
            {
                // ==========================================
                // 1. Validate ClaimId
                // ==========================================
                if (claimId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid ClaimId."
                    });
                }

                // ==========================================
                // 2. Get OrganizationId from JWT
                // ==========================================
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 3. Get EmployeeId from JWT
                // ==========================================
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int employeeId) ||
                    employeeId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ==========================================
                // 4. Validate Employee belongs to Organization
                //    Common DB - company_users
                // ==========================================
                var companyUserExists = await _commonContext.CompanyUsers
                    .AnyAsync(x =>
                        x.EmpId == employeeId &&
                        x.OrgId == organizationId &&
                        (
                            x.Status == null ||
                            x.Status == "Active"
                        ));

                if (!companyUserExists)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ==========================================
                // 5. Base URL
                // ==========================================
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // ==========================================
                // 6. Get ONLY the logged-in employee's claim
                //    + Same Organization
                //    + Draft
                //    + Active
                // ==========================================
                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.EmployeeId == employeeId &&
                        x.OrganizationId == organizationId &&
                        x.Status == "Draft" &&
                        x.IsActive);

                // ==========================================
                // 7. Claim not found / not belongs to user
                // ==========================================
                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Draft not found."
                    });
                }

                // ==========================================
                // 8. Get ONLY active travels for this claim
                // ==========================================
                var travels = await _context.ClaimTravels
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .OrderBy(x => x.TravelId)
                    .ToListAsync();

                // ==========================================
                // 9. Get ONLY active documents for this claim
                // ==========================================
                var documents = await _context.ClaimDocuments
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();

                // ==========================================
                // 10. Claim Entries
                // ==========================================
                var claimEntries = new List<object>();

                decimal grandTotalAmount = 0m;

                foreach (var travel in travels)
                {
                    // ======================================
                    // FOOD
                    // ======================================
                    var food = await _context.ClaimFoods
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId &&
                            x.IsActive);

                    // ======================================
                    // EXPENSES
                    // ======================================
                    var expenses = await _context.ClaimExpenses
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.TravelId == travel.TravelId &&
                            x.IsActive)
                        .ToListAsync();

                    // ======================================
                    // VEHICLE NAME
                    // ======================================
                    var vehicleName = await _context.VehicleMaster
                        .Where(v =>
                            v.VehicleId == travel.VehicleTypeId &&
                            v.IsActive)
                        .Select(v => v.VehicleName)
                        .FirstOrDefaultAsync();

                    // ======================================
                    // EXPENSE LIST
                    // ======================================
                    var expenseList = expenses
                        .Select(e => new
                        {
                            expenseId = e.ExpenseId,
                            expenseType = e.ExpenseType,
                            invoiceNo = e.InvoiceNo,
                            description = e.Description,
                            expensePurpose = e.Purpose,
                            expenseAmount = e.Amount ?? 0m
                        })
                        .ToList();

                    // ======================================
                    // OTHER EXPENSE
                    // ======================================
                    decimal otherAmount = expenses
                        .Where(e =>
                            !string.Equals(
                                e.ExpenseType,
                                "Toll",
                                StringComparison.OrdinalIgnoreCase))
                        .Sum(e => e.Amount ?? 0m);

                    // ======================================
                    // TOLL
                    // ======================================
                    decimal tollAmount = expenses
                        .Where(e =>
                            string.Equals(
                                e.ExpenseType,
                                "Toll",
                                StringComparison.OrdinalIgnoreCase))
                        .Sum(e => e.Amount ?? 0m);

                    // ======================================
                    // TRAVEL AMOUNT
                    // ======================================
                    decimal travelAmount = travel.Amount ?? 0m;

                    // ======================================
                    // FOOD AMOUNT
                    // ======================================
                    decimal foodAmount = food?.Amount ?? 0m;

                    // ======================================
                    // ENTRY TOTAL
                    // ======================================
                    decimal entryTotalAmount =
                        travelAmount +
                        foodAmount +
                        tollAmount +
                        otherAmount;

                    grandTotalAmount += entryTotalAmount;

                    // ======================================
                    // DOCUMENTS FOR THIS TRAVEL
                    // ======================================
                    var travelDocuments = documents
                        .Where(d =>
                            d.ReferenceId == travel.TravelId)
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

                    // ======================================
                    // CLAIM ENTRY
                    // ======================================
                    claimEntries.Add(new
                    {
                        // ==============================
                        // TRAVEL
                        // ==============================
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

                        // ==============================
                        // FOOD
                        // ==============================
                        foodTypeId = food?.FoodTypeId,

                        foodAmount = foodAmount,

                        // ==============================
                        // EXPENSE TOTALS
                        // ==============================
                        tollAmount = tollAmount,

                        otherAmount = otherAmount,

                        // ==============================
                        // ENTRY TOTAL
                        // ==============================
                        totalAmount = entryTotalAmount,

                        // ==============================
                        // ALL EXPENSES
                        // ==============================
                        expenses = expenseList,

                        // ==============================
                        // DOCUMENTS
                        // ==============================
                        documents = travelDocuments
                    });
                }

                // ==========================================
                // 11. FINAL RESPONSE
                // ==========================================
                return Ok(new
                {
                    success = true,

                    data = new
                    {
                        claimId = claim.ClaimId,

                        claimNo = claim.ClaimNo,

                        employeeId = claim.EmployeeId,

                        organizationId = claim.OrganizationId,

                        claimMonth = claim.ClaimMonth,

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

        [Authorize]
        [HttpPut("update-draft/{claimId}")]
        public async Task<IActionResult> UpdateDraft(
            int claimId,
            [FromBody] CreateClaimDto dto)
        {
            try
            {
                // ============================================================
                // 1. GET ORGANIZATION ID FROM JWT
                // ============================================================

                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ============================================================
                // 2. BASIC VALIDATION
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
                // 3. GET LOGIN EMPLOYEE FROM JWT
                // ============================================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrWhiteSpace(empIdClaim) ||
                    !int.TryParse(empIdClaim, out int loginEmpId) ||
                    loginEmpId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmployeeId not found in token."
                    });
                }

                // ============================================================
                // 4. GET LOCAL EXPENSE ROLE FROM JWT
                // ============================================================

                var role = GetLocalExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // ============================================================
                // 5. VALIDATE EMPLOYEE IN COMMON DB
                //    company_users ONLY
                // ============================================================

                var companyUserExists = await _commonContext.CompanyUsers
                    .AnyAsync(x =>
                        x.EmpId == loginEmpId &&
                        x.OrgId == organizationId &&
                        (
                            x.Status == null ||
                            x.Status == "Active"
                        ));

                if (!companyUserExists)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found in company_users."
                    });
                }

                // ============================================================
                // 6. START TRANSACTION
                // ============================================================

                await using var transaction =
                    await _context.Database.BeginTransactionAsync();

                // ============================================================
                // 7. GET OWN CLAIM
                //    Employee + Organization based
                // ============================================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.EmployeeId == loginEmpId &&
                        x.OrganizationId == organizationId &&
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
                // 8. CLAIM MONTH VALIDATION
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
                // 9. TRACK INCOMING TRAVEL IDs
                //
                // Existing TravelId -> update
                // TravelId = 0 -> new travel
                // ============================================================

                var incomingTravelIds = new HashSet<int>();

                // ============================================================
                // 10. CALCULATED TOTAL
                // ============================================================

                decimal calculatedTotalAmount = 0m;

                // ============================================================
                // 11. PROCESS EACH TRAVEL ENTRY
                // ============================================================

                foreach (var entry in dto.ClaimEntries)
                {
                    ClaimTravel travel;

                    // ========================================================
                    // 11.1 TRAVEL VALIDATION
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
                    // 11.2 VEHICLE RATE + TRAVEL AMOUNT
                    // ========================================================

                    decimal? appliedRatePerKm = null;
                    decimal travelAmount = 0m;

                    // ========================================================
                    // BIKE / CAR
                    // VehicleTypeId 2 = Bike
                    // VehicleTypeId 3 = Car
                    // ========================================================

                    if (entry.VehicleTypeId == 2 ||
                        entry.VehicleTypeId == 3)
                    {
                        // ----------------------------------------------------
                        // Distance is optional
                        // ----------------------------------------------------

                        if (entry.KmRun > 0)
                        {
                            // ------------------------------------------------
                            // Fuel Type required when distance is entered
                            // ------------------------------------------------

                            if (!entry.FuelTypeId.HasValue ||
                                entry.FuelTypeId.Value <= 0)
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message =
                                        "Fuel Type is required when distance is entered."
                                });
                            }

                            // ------------------------------------------------
                            // GET VEHICLE RATE
                            // ------------------------------------------------

                            var currentRate =
                                await _context.VehicleRateMasters
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
                                        "Vehicle rate is not configured."
                                });
                            }

                            // ------------------------------------------------
                            // RATE SNAPSHOT
                            // ------------------------------------------------

                            appliedRatePerKm =
                                Convert.ToDecimal(
                                    currentRate.AmountPerKm);

                            // ------------------------------------------------
                            // KM × RATE
                            // ------------------------------------------------

                            travelAmount =
                                Convert.ToDecimal(entry.KmRun) *
                                Convert.ToDecimal(currentRate.AmountPerKm);
                        }
                        else
                        {
                            appliedRatePerKm = null;
                            travelAmount = 0m;
                        }
                    }

                    // ========================================================
                    // BUS / TAXI / OTHER
                    // User entered amount
                    // ========================================================

                    else
                    {
                        appliedRatePerKm = null;

                        travelAmount =
                            Convert.ToDecimal(entry.TravelAmount);
                    }

                    // ========================================================
                    // 11.3 EXISTING TRAVEL -> UPDATE
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
                    // 11.4 NEW TRAVEL -> INSERT
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
                    // 11.5 TRACK TRAVEL ID
                    // ========================================================

                    incomingTravelIds.Add(travel.TravelId);

                    // ========================================================
                    // 11.6 FOOD
                    // One Food per Travel
                    // ========================================================

                    var existingFood =
                        await _context.ClaimFoods
                            .FirstOrDefaultAsync(x =>
                                x.ClaimId == claimId &&
                                x.TravelId == travel.TravelId &&
                                x.IsActive);

                    // --------------------------------------------
                    // Food exists / add food
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
                    // 11.7 MULTIPLE EXPENSES
                    // ========================================================

                    var incomingExpenseIds =
                        new HashSet<int>();

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
                    // 11.8 DEACTIVATE REMOVED EXPENSES
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
                    // 11.9 CALCULATE TOTAL
                    // ========================================================

                    decimal expenseTotal =
                        entry.Expenses?
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(
                                    x.ExpenseType) &&
                                x.ExpenseAmount > 0)
                            .Sum(x =>
                                x.ExpenseAmount)
                        ?? 0m;

                    calculatedTotalAmount +=
                        travelAmount +
                        entry.FoodAmount +
                        expenseTotal;
                }

                // ============================================================
                // 12. DEACTIVATE REMOVED TRAVELS
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
                // 13. UPDATE CLAIM MASTER
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
                // 14. FINAL SAVE
                // ============================================================

                await _context.SaveChangesAsync();

                // ============================================================
                // 15. COMMIT
                // ============================================================

                await transaction.CommitAsync();

                // ============================================================
                // 16. SUCCESS RESPONSE
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
                        travelCount = incomingTravelIds.Count,
                        employeeId = loginEmpId,
                        organizationId = organizationId
                    }
                });
            }
            catch (Exception ex)
            {
                // ============================================================
                // ROLLBACK
                // ============================================================

                try
                {
                    // Transaction rollback is handled only when
                    // transaction has already been created.
                    // Since transaction is inside try, this is safe
                    // for normal database errors after transaction start.
                }
                catch
                {
                    // Ignore rollback errors
                }

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
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    await transaction.RollbackAsync();

                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // =====================================
                // 1. GET DRAFT / REJECTED CLAIM
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.OrganizationId == organizationId &&
                        (x.Status == "Draft" || x.Status == "Rejected") &&
                        x.IsActive);

                if (claim == null)
                {
                    await transaction.RollbackAsync();

                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                // =====================================
                // 2. VALIDATE CLAIM OWNERSHIP
                // =====================================

                var role =
                    User.FindFirst(
                        System.Security.Claims.ClaimTypes.Role)?.Value
                    ?? User.FindFirst("role")?.Value;

                bool isAdmin =
                    string.Equals(
                        role,
                        "Admin",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    string.Equals(
                        role,
                        "SuperAdmin",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    string.Equals(
                        role,
                        "Super Admin",
                        StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    var employeeIdClaim =
                        User.FindFirst("EmpId")?.Value;

                    if (!int.TryParse(
                            employeeIdClaim,
                            out int loggedInEmployeeId) ||
                        loggedInEmployeeId <= 0)
                    {
                        await transaction.RollbackAsync();

                        return Unauthorized(new
                        {
                            success = false,
                            message =
                                "Logged-in employee ID not found in token."
                        });
                    }

                    if (claim.EmployeeId != loggedInEmployeeId)
                    {
                        await transaction.RollbackAsync();

                        return StatusCode(403, new
                        {
                            success = false,
                            message =
                                "You are not authorized to submit this claim."
                        });
                    }
                }

                // =====================================
                // 3. CHECK RESUBMISSION
                // =====================================

                bool isResubmit =
                    string.Equals(
                        claim.Status,
                        "Rejected",
                        StringComparison.OrdinalIgnoreCase);

                // =====================================
                // 4. VALIDATE SELECTED DEPARTMENT
                // =====================================

                if (!claim.DepartmentId.HasValue ||
                    claim.DepartmentId.Value <= 0)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Department is not selected for this claim. " +
                            "Please select a department and update the claim."
                    });
                }

                int selectedDepartmentId = claim.DepartmentId.Value;

                // =====================================
                // 5. GET CLAIM EMPLOYEE FROM COMMON DB
                // =====================================

                var employee = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId &&
                        x.OrgId == organizationId &&
                        (x.Status == null ||
                         x.Status.Trim().ToUpper() == "ACTIVE"));

                if (employee == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Claim employee not found or inactive in Common Login database."
                    });
                }

                // =====================================
                // 6. VALIDATE EMPLOYEE BRANCH
                // =====================================

                if (!employee.BranchId.HasValue)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Employee branch is missing."
                    });
                }

                int employeeBranchId = employee.BranchId.Value;

                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == employeeBranchId &&
                        x.OrgId == organizationId);

                if (branch == null ||
                    string.IsNullOrWhiteSpace(branch.BranchName))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Employee branch was not found for this organization."
                    });
                }

                string branchName = branch.BranchName.Trim();

                // =====================================
                // 7. VALIDATE SELECTED DEPARTMENT
                //    AGAINST EMPLOYEE DEPARTMENT IDs
                // =====================================

                // CompanyUsers.DepartmentId may contain values such as:
                // "21,15"

                var employeeDepartmentIds =
                    (employee.DepartmentId ?? string.Empty)
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Select(value =>
                        {
                            return int.TryParse(value, out int departmentId)
                                ? departmentId
                                : 0;
                        })
                        .Where(departmentId => departmentId > 0)
                        .Distinct()
                        .ToList();

                if (!employeeDepartmentIds.Contains(selectedDepartmentId))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "The selected department is not assigned to this employee."
                    });
                }

                // =====================================
                // 8. GET APPROVAL CONFIGURATION
                //    BY SELECTED DEPARTMENT ID
                // =====================================

                const string moduleCode = "travel_portal";

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.ModuleCode == moduleCode &&
                            x.DepartmentId == selectedDepartmentId &&
                            x.Branch != null &&
                            x.Branch.Trim().ToLower() ==
                                branchName.ToLower() &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Approval configuration not found for the selected department and branch."
                    });
                }

                if (departmentConfig.TotalApprovalLevels <= 0)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Department approval levels are not configured correctly."
                    });
                }

                // =====================================
                // 9. GET EMPLOYEE APPROVAL CONFIGURATION
                //    FOR THIS DEPARTMENT ONLY
                // =====================================

                var approvalConfigs =
                    await _context.EmployeeApprovalConfigs
                        .AsNoTracking()
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.DepartmentApprovalConfigId ==
                                departmentConfig.Id &&
                            x.EmployeeId == claim.EmployeeId &&
                            x.IsActive)
                        .OrderBy(x => x.ApprovalLevel)
                        .ToListAsync();

                // =====================================
                // 10. VALIDATE APPROVAL LEVEL COUNT
                // =====================================

                if (approvalConfigs.Count !=
                    departmentConfig.TotalApprovalLevels)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Employee approval configuration is incomplete. " +
                            $"Expected {departmentConfig.TotalApprovalLevels} levels, " +
                            $"but found {approvalConfigs.Count}."
                    });
                }

                for (int i = 0; i < approvalConfigs.Count; i++)
                {
                    if (approvalConfigs[i].ApprovalLevel != i + 1)
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                "Approval levels must start from Level 1 and continue sequentially."
                        });
                    }
                }

                // =====================================
                // 11. VALIDATE APPROVERS AND BUILD RECORDS
                // =====================================

                var claimApprovalRecords = new List<ClaimApproval>();

                foreach (var config in approvalConfigs)
                {
                    // Current implementation supports AnyOne only.

                    if (!string.Equals(
                            config.ApprovalRule,
                            "AnyOne",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"Approval Level {config.ApprovalLevel} must use the AnyOne approval rule."
                        });
                    }

                    var assignments =
                        await _context.EmployeeApprovalConfigApprovers
                            .AsNoTracking()
                            .Where(x =>
                                x.EmployeeApprovalConfigId == config.Id &&
                                x.IsActive)
                            .OrderBy(x => x.ApprovalOrder)
                            .ToListAsync();

                    if (!assignments.Any())
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"No approvers configured for Approval Level {config.ApprovalLevel}."
                        });
                    }

                    var approverIds = assignments
                        .Select(x => x.ApproverEmployeeId)
                        .ToList();

                    // Prevent duplicate approvers.

                    if (approverIds.Distinct().Count() != approverIds.Count)
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"Duplicate approvers found at Approval Level {config.ApprovalLevel}."
                        });
                    }

                    // Employee cannot approve their own claim.

                    if (approverIds.Contains(claim.EmployeeId))
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"The claim employee cannot be their own approver at Level {config.ApprovalLevel}."
                        });
                    }

                    // Validate approvers in the Common Login DB.

                    var uniqueApproverIds = approverIds
                        .Distinct()
                        .ToList();

                    var validApproverIds =
                        await _commonContext.CompanyUsers
                            .AsNoTracking()
                            .Where(x =>
                                uniqueApproverIds.Contains(x.EmpId) &&
                                x.OrgId == organizationId &&
                                (x.Status == null ||
                                 x.Status.Trim().ToUpper() == "ACTIVE"))
                            .Select(x => x.EmpId)
                            .Distinct()
                            .ToListAsync();

                    if (validApproverIds.Count != uniqueApproverIds.Count)
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"One or more approvers at Level {config.ApprovalLevel} are invalid or inactive."
                        });
                    }

                    // Create approval records for this selected department.

                    foreach (var approverId in approverIds)
                    {
                        claimApprovalRecords.Add(
                            new ClaimApproval
                            {
                                ClaimId = claim.ClaimId,

                                // IMPORTANT:
                                // Save the department configuration ID.
                                DepartmentApprovalConfigId =
                                    departmentConfig.Id,

                                ApprovalLevel = config.ApprovalLevel,
                                ApproverEmpId = approverId,
                                Status = "Pending",
                                CreatedAt = DateTime.Now,
                                ApprovalToken = Guid.NewGuid()
                            });
                    }
                }

                if (!claimApprovalRecords.Any())
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval flow could not be created."
                    });
                }

                // =====================================
                // 12. REMOVE OLD APPROVAL RECORDS
                //    ONLY AFTER VALIDATING NEW FLOW
                // =====================================

                var oldApprovals = await _context.ClaimApprovals
                    .Where(x => x.ClaimId == claim.ClaimId)
                    .ToListAsync();

                if (oldApprovals.Any())
                {
                    _context.ClaimApprovals.RemoveRange(oldApprovals);
                }

                // =====================================
                // 13. ADD NEW APPROVAL RECORDS
                // =====================================

                await _context.ClaimApprovals.AddRangeAsync(
                    claimApprovalRecords);

                // =====================================
                // 14. UPDATE CLAIM STATUS
                // =====================================

                int firstApprovalLevel =
                    claimApprovalRecords.Min(x => x.ApprovalLevel);

                claim.Status = "Pending";
                claim.CurrentApprovalLevel = firstApprovalLevel;
                claim.SubmittedAt = DateTime.Now;
                claim.RejectedAt = null;
                claim.ApprovedAt = null;
                claim.LastModifiedAt = DateTime.Now;

                // Keep claim.DepartmentId unchanged:
                // it contains the department selected when creating the claim.

                await _context.SaveChangesAsync();

                // =====================================
                // 15. GET FIRST-LEVEL APPROVAL RECORDS
                // =====================================

                var firstLevelApprovals = claimApprovalRecords
                    .Where(x => x.ApprovalLevel == firstApprovalLevel)
                    .ToList();

                // =====================================
                // 16. GET CLAIM PURPOSE
                // =====================================

                var firstTravel = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claim.ClaimId);

                string purpose = firstTravel?.Purpose ?? "-";

                // =====================================
                // 17. SEND EMAIL TO FIRST-LEVEL APPROVERS
                // =====================================

                int emailSentCount = 0;
                int emailFailedCount = 0;

                var baseUrl = _configuration["AppSettings:BaseUrl"];

                foreach (var firstApproval in firstLevelApprovals)
                {
                    if (!firstApproval.ApproverEmpId.HasValue)
                    {
                        continue;
                    }

                    var approver =
                        await _commonContext.CompanyUsers
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x =>
                                x.EmpId == firstApproval.ApproverEmpId.Value &&
                                x.OrgId == organizationId &&
                                (x.Status == null ||
                                 x.Status.Trim().ToUpper() == "ACTIVE"));

                    if (approver == null ||
                        string.IsNullOrWhiteSpace(approver.Email))
                    {
                        emailFailedCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        emailFailedCount++;
                        continue;
                    }

                    var approveUrl =
                        $"{baseUrl}/api/TravelMaster/mail-approve" +
                        $"?token={firstApproval.ApprovalToken}";

                    var rejectUrl =
                        $"{baseUrl}/api/TravelMaster/reject-form" +
                        $"?token={firstApproval.ApprovalToken}";

                    string body = $@"
Dear {approver.FullName},

<br/><br/>

A new Local Expense Claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {employee.FullName}</li>
    <li>Employee ID : {employee.EmpCode}</li>
    <li>Department ID : {selectedDepartmentId}</li>
    <li>Branch : {branchName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
    <li>Approval Level : {firstApproval.ApprovalLevel}</li>
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
                            approver.Email,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);

                        emailSentCount++;
                    }
                    catch (Exception)
                    {
                        // Email failure should not undo a valid submission.
                        emailFailedCount++;
                    }
                }

                // =====================================
                // 18. COMMIT TRANSACTION
                // =====================================

                await transaction.CommitAsync();

                // =====================================
                // 19. SUCCESS RESPONSE
                // =====================================

                return Ok(new
                {
                    success = true,

                    message = isResubmit
                        ? "Claim resubmitted successfully."
                        : "Claim submitted successfully.",

                    data = new
                    {
                        claimId = claim.ClaimId,
                        claimNo = claim.ClaimNo,
                        departmentId = claim.DepartmentId,
                        departmentApprovalConfigId = departmentConfig.Id,
                        status = claim.Status,
                        currentApprovalLevel = claim.CurrentApprovalLevel,
                        submittedAt = claim.SubmittedAt,
                        firstApprovalLevel = firstApprovalLevel,
                        firstLevelApproverCount = firstLevelApprovals.Count,
                        emailSentCount = emailSentCount,
                        emailFailedCount = emailFailedCount
                    }
                });
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (Exception)
                {
                    // Preserve the original exception.
                }

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

        [Authorize]
        [HttpPost("approve-claim")]
        public async Task<IActionResult> ApproveClaim(
          [FromBody] ClaimApprovalActionDto dto)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // ==========================================
                // 1. VALIDATE REQUEST
                // ==========================================

                if (dto == null || dto.ClaimId <= 0)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Valid approval request and ClaimId are required."
                    });
                }

                // ==========================================
                // 2. GET EMPLOYEE ID FROM JWT
                // ==========================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int empId) || empId <= 0)
                {
                    await transaction.RollbackAsync();

                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid or missing EmpId in token."
                    });
                }

                // ==========================================
                // 3. GET ORGANIZATION ID FROM JWT
                // ==========================================

                var orgIdClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    await transaction.RollbackAsync();

                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid or missing organization ID in token."
                    });
                }

                // ==========================================
                // 4. GET CLAIM
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == dto.ClaimId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (claim == null)
                {
                    await transaction.RollbackAsync();

                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                // ==========================================
                // 5. CLAIM MUST BE PENDING
                // ==========================================

                if (!string.Equals(
                        claim.Status,
                        "Pending",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Claim cannot be approved because its current status is '{claim.Status}'."
                    });
                }

                // ==========================================
                // 6. VALIDATE APPROVER EMPLOYEE
                // ==========================================

                var approverEmployee = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == empId &&
                        x.OrgId == organizationId &&
                        (x.Status == null ||
                         x.Status.Trim().ToUpper() == "ACTIVE"));

                if (approverEmployee == null)
                {
                    await transaction.RollbackAsync();

                    return Unauthorized(new
                    {
                        success = false,
                        message =
                            "Approver employee not found in the current organization."
                    });
                }

                // ==========================================
                // 7. GET APPROVER'S PENDING RECORD
                // ==========================================

                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == dto.ClaimId &&
                        x.ApproverEmpId == empId &&
                        x.Status == "Pending");

                if (approval == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Approval record not found or already processed."
                    });
                }

                // ==========================================
                // 8. VERIFY CURRENT ACTIVE LEVEL
                // ==========================================

                if (!claim.CurrentApprovalLevel.HasValue ||
                    claim.CurrentApprovalLevel.Value != approval.ApprovalLevel)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "This approval level is not currently active."
                    });
                }

                // ==========================================
                // 9. GET CLAIM EMPLOYEE
                // ==========================================

                var claimEmployee = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId &&
                        x.OrgId == organizationId &&
                        (x.Status == null ||
                         x.Status.Trim().ToUpper() == "ACTIVE"));

                if (claimEmployee == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim employee not found."
                    });
                }

                // ==========================================
                // 9A. VALIDATE EMPLOYEE BRANCH AND DEPARTMENT
                // CompanyUsers.DepartmentId is a CSV string (example: "21,15").
                // ClaimMaster.DepartmentId is the single department selected for this claim (int?).
                // ==========================================

                if (!claimEmployee.BranchId.HasValue ||
                    claimEmployee.BranchId.Value <= 0 ||
                    string.IsNullOrWhiteSpace(claimEmployee.DepartmentId))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee BranchId or DepartmentId is missing."
                    });
                }

                int employeeBranchId = claimEmployee.BranchId.Value;

                var employeeDepartmentIds = claimEmployee.DepartmentId
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => int.TryParse(value.Trim(), out int id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (employeeDepartmentIds.Count == 0)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee DepartmentId is invalid."
                    });
                }

                if (!claim.DepartmentId.HasValue ||
                    claim.DepartmentId.Value <= 0 ||
                    !employeeDepartmentIds.Contains(claim.DepartmentId.Value))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Selected claim department does not belong to this employee.",
                        employeeDepartmentIds,
                        claimDepartmentId = claim.DepartmentId
                    });
                }

                int employeeDepartmentId = claim.DepartmentId.Value;

                // ==========================================
                // 9B. GET BRANCH
                // ==========================================

                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == employeeBranchId &&
                        x.OrgId == organizationId);

                if (branch == null ||
                    string.IsNullOrWhiteSpace(branch.BranchName))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Employee branch was not found for this organization."
                    });
                }

                string branchName = branch.BranchName.Trim();

                // ==========================================
                // 9C. GET DEPARTMENT FROM DEPARTMENT MASTER
                // DepartmentId is the identifier.
                // ==========================================

                var department = await _commonContext.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == employeeDepartmentId &&
                        x.OrgId == organizationId &&
                        x.BranchId == employeeBranchId);

                if (department == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Employee DepartmentId does not belong to this organization and branch."
                    });
                }

                int departmentId = department.Id;
                string departmentName = department.DepartmentName;

                // ==========================================
                // 9D. GET DEPARTMENT APPROVAL CONFIG
                // Organization + Module + Branch + DepartmentId
                // ==========================================

                const string moduleCode = "travel_portal";

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.ModuleCode == moduleCode &&
                            x.Branch == branchName &&
                            x.DepartmentId == departmentId &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Department approval configuration not found. Configure approval levels for this DepartmentId first.",
                        departmentId
                    });
                }

                // Ensure this pending approval belongs to this department's configuration.
                if (approval.DepartmentApprovalConfigId.HasValue &&
                    approval.DepartmentApprovalConfigId.Value != departmentConfig.Id)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval record does not belong to this department configuration."
                    });
                }

                // ==========================================
                // 9E. GET EMPLOYEE APPROVAL CONFIGURATION
                // ==========================================

                var approvalConfigs =
                    await _context.EmployeeApprovalConfigs
                        .AsNoTracking()
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.EmployeeId == claim.EmployeeId &&
                            x.IsActive)
                        .OrderBy(x => x.ApprovalLevel)
                        .ToListAsync();

                // ==========================================
                // 9F. VALIDATE CONFIGURED APPROVAL LEVELS
                // ==========================================

                if (approvalConfigs.Count != departmentConfig.TotalApprovalLevels)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Employee approval configuration is incomplete.",
                        departmentId,
                        expectedApprovalLevels = departmentConfig.TotalApprovalLevels,
                        actualApprovalLevels = approvalConfigs.Count
                    });
                }

                for (int i = 0; i < approvalConfigs.Count; i++)
                {
                    if (approvalConfigs[i].ApprovalLevel != i + 1)
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                "Approval levels must start from Level 1 and continue sequentially."
                        });
                    }
                }

                // ==========================================
                // 10. GET CURRENT LEVEL CONFIGURATION
                // ==========================================

                var currentConfig = approvalConfigs
                    .FirstOrDefault(x =>
                        x.ApprovalLevel == approval.ApprovalLevel);

                if (currentConfig == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Employee approval configuration not found for Level {approval.ApprovalLevel}."
                    });
                }

                string approvalMode = currentConfig.ApprovalRule?.Trim() ?? "";

                if (!approvalMode.Equals(
                        "AnyOne",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Only AnyOne approval rule is currently supported."
                    });
                }

                // ==========================================
                // 11. APPROVE CURRENT RECORD
                // ==========================================

                approval.Status = "Approved";

                approval.Remarks =
                    string.IsNullOrWhiteSpace(dto.Remarks)
                        ? "Approved"
                        : dto.Remarks.Trim();

                approval.ActionDate = DateTime.Now;

                await _context.SaveChangesAsync();

                // ==========================================
                // 12. ANYONE MODE
                // ==========================================

                var sameLevelPending = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == claim.ClaimId &&
                        x.ApprovalLevel == approval.ApprovalLevel &&
                        x.Status == "Pending")
                    .ToListAsync();

                foreach (var item in sameLevelPending)
                {
                    item.Status = "Skipped";
                    item.Remarks =
                        "Skipped because another approver approved this level.";
                    item.ActionDate = DateTime.Now;
                }

                if (sameLevelPending.Count > 0)
                {
                    await _context.SaveChangesAsync();
                }

                // ==========================================
                // 13. FIND NEXT APPROVAL LEVEL
                // ==========================================

                var nextLevel = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == claim.ClaimId &&
                        x.Status == "Pending" &&
                        x.ApprovalLevel > approval.ApprovalLevel)
                    .Select(x => (int?)x.ApprovalLevel)
                    .MinAsync();

                // ==========================================
                // 14. NO NEXT LEVEL - FINAL APPROVAL
                // ==========================================

                if (!nextLevel.HasValue)
                {
                    var remainingApprovals = await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claim.ClaimId &&
                            x.Status == "Pending")
                        .ToListAsync();

                    foreach (var item in remainingApprovals)
                    {
                        item.Status = "Skipped";
                        item.Remarks = "Skipped after final approval.";
                        item.ActionDate = DateTime.Now;
                    }

                    claim.Status = "Approved";
                    claim.ApprovedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "Claim approved successfully.",
                        data = new
                        {
                            claimId = claim.ClaimId,
                            claimNo = claim.ClaimNo,
                            departmentId,
                            status = claim.Status,
                            approvedAt = claim.ApprovedAt
                        }
                    });
                }

                // ==========================================
                // 15. GET NEXT LEVEL APPROVERS
                // ==========================================

                var nextLevelApprovals = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == claim.ClaimId &&
                        x.ApprovalLevel == nextLevel.Value &&
                        x.Status == "Pending" &&
                        x.ApproverEmpId.HasValue)
                    .OrderBy(x => x.ApproverEmpId)
                    .ToListAsync();

                if (nextLevelApprovals.Count == 0)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Approval Level {nextLevel.Value} has no valid pending approvers."
                    });
                }

                // ==========================================
                // 16. MOVE CLAIM TO NEXT LEVEL
                // ==========================================

                claim.CurrentApprovalLevel = nextLevel.Value;
                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // ==========================================
                // 17. GET CLAIM PURPOSE
                // ==========================================

                var firstTravel = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claim.ClaimId);

                string purpose = firstTravel?.Purpose ?? "-";

                // ==========================================
                // 18. EMAIL CONFIGURATION
                // ==========================================

                int emailSentCount = 0;
                int emailFailedCount = 0;

                var baseUrl = _configuration["AppSettings:BaseUrl"];

                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "AppSettings:BaseUrl is not configured."
                    });
                }

                // ==========================================
                // 19. SEND EMAIL TO NEXT LEVEL APPROVERS
                // ==========================================

                foreach (var nextApproval in nextLevelApprovals)
                {
                    if (!nextApproval.ApproverEmpId.HasValue)
                    {
                        continue;
                    }

                    var nextApprover = await _commonContext.CompanyUsers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == nextApproval.ApproverEmpId.Value &&
                            x.OrgId == organizationId &&
                            (x.Status == null ||
                             x.Status.Trim().ToUpper() == "ACTIVE"));

                    if (nextApprover == null ||
                        string.IsNullOrWhiteSpace(nextApprover.Email))
                    {
                        emailFailedCount++;
                        continue;
                    }

                    var approveUrl =
                        $"{baseUrl}/api/TravelMaster/mail-approve?token={nextApproval.ApprovalToken}";

                    var rejectUrl =
                        $"{baseUrl}/api/TravelMaster/reject-form?token={nextApproval.ApprovalToken}";

                    string body = $@"
Dear {nextApprover.FullName},

<br/><br/>

A Local Expense Claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>
    <li>Employee Name : {claimEmployee.FullName}</li>
    <li>Employee ID : {claimEmployee.EmpCode}</li>
    <li>Department : {departmentName}</li>
    <li>Department ID : {departmentId}</li>
    <li>Branch : {branchName}</li>
</ul>

<b>Claim Details</b>

<ul>
    <li>Claim Number : {claim.ClaimNo}</li>
    <li>Claim Type : Local Expense Claim</li>
    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
    <li>Amount : ₹{claim.TotalAmount:N2}</li>
    <li>Status : Pending Approval</li>
    <li>Approval Level : {nextLevel.Value}</li>
</ul>

<b>Purpose</b>

<p>{purpose}</p>

<br/>

<a href='{approveUrl}'
style='background:green;color:white;padding:10px 20px;
text-decoration:none;border-radius:5px;'>
Approve
</a>

&nbsp;&nbsp;

<a href='{rejectUrl}'
style='background:red;color:white;padding:10px 20px;
text-decoration:none;border-radius:5px;'>
Reject
</a>

<br/><br/>

Regards,<br/>
Local Expense Claim Portal
";

                    try
                    {
                        await _emailService.SendEmailAsync(
                            nextApprover.Email,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);

                        emailSentCount++;
                    }
                    catch (Exception)
                    {
                        emailFailedCount++;
                    }
                }

                // ==========================================
                // 20. COMMIT TRANSACTION
                // ==========================================

                await transaction.CommitAsync();

                // ==========================================
                // 21. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message =
                        "Claim approved and moved to the next approval level.",
                    data = new
                    {
                        claimId = claim.ClaimId,
                        claimNo = claim.ClaimNo,
                        departmentId,
                        approvedLevel = approval.ApprovalLevel,
                        nextApprovalLevel = nextLevel.Value,
                        status = claim.Status,
                        currentApprovalLevel = claim.CurrentApprovalLevel,
                        nextLevelApproverCount = nextLevelApprovals.Count,
                        emailSentCount,
                        emailFailedCount
                    }
                });
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (Exception)
                {
                    // Preserve the original exception.
                }

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while approving claim.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        [HttpPost("mail-reject")]
        public async Task<IActionResult> MailReject(
            [FromForm] Guid token,
            [FromForm] string? reason)
        {
            try
            {
                if (token == Guid.Empty)
                {
                    return BadRequest("Invalid rejection token.");
                }

                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ApprovalToken == token &&
                        x.Status == "Pending");

                if (approval == null)
                {
                    return Content(
                        "<h2>This approval link is invalid or has already been used.</h2>",
                        "text/html");
                }

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == approval.ClaimId &&
                        x.IsActive);

                if (claim == null)
                {
                    return Content(
                        "<h2>Claim not found.</h2>",
                        "text/html");
                }

                if (claim.Status != "Pending")
                {
                    return Content(
                        "<h2>This claim is no longer pending.</h2>",
                        "text/html");
                }

                if (claim.CurrentApprovalLevel != approval.ApprovalLevel)
                {
                    return Content(
                        "<h2>This approval level is no longer active.</h2>",
                        "text/html");
                }

                // Validate selected department and its approval configuration.
                if (!claim.DepartmentId.HasValue ||
                    !approval.DepartmentApprovalConfigId.HasValue)
                {
                    return Content(
                        "<h2>Department approval configuration is missing.</h2>",
                        "text/html");
                }

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.Id == approval.DepartmentApprovalConfigId.Value &&
                            x.OrganizationId == claim.OrganizationId &&
                            x.DepartmentId == claim.DepartmentId.Value &&
                            x.ModuleCode == "travel_portal" &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    return Content(
                        "<h2>Invalid department approval configuration.</h2>",
                        "text/html");
                }

                // Ensure this approval belongs to the selected configuration.
                var approvalBelongsToClaim =
                    await _context.ClaimApprovals.AnyAsync(x =>
                        x.ApprovalId == approval.ApprovalId &&
                        x.ClaimId == claim.ClaimId &&
                        x.DepartmentApprovalConfigId ==
                            departmentConfig.Id);

                if (!approvalBelongsToClaim)
                {
                    return Content(
                        "<h2>Approval configuration does not match this claim.</h2>",
                        "text/html");
                }

                await using var transaction =
                    await _context.Database.BeginTransactionAsync();

                try
                {
                    approval.Status = "Rejected";
                    approval.Remarks = string.IsNullOrWhiteSpace(reason)
                        ? "Rejected From Email"
                        : reason.Trim();

                    approval.ActionDate = DateTime.Now;

                    claim.Status = "Rejected";
                    claim.RejectedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;

                    // Cancel only future approvals belonging to this
                    // claim's selected department configuration.
                    var futureApprovals =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == claim.ClaimId &&
                                x.DepartmentApprovalConfigId ==
                                    departmentConfig.Id &&
                                x.ApprovalLevel > approval.ApprovalLevel &&
                                x.Status == "Pending")
                            .ToListAsync();

                    foreach (var futureApproval in futureApprovals)
                    {
                        futureApproval.Status = "Cancelled";
                        futureApproval.ActionDate = DateTime.Now;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var safeReason = System.Net.WebUtility.HtmlEncode(
                        string.IsNullOrWhiteSpace(reason)
                            ? "Rejected From Email"
                            : reason.Trim());

                    return Content($@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>Claim Rejected</title>
                </head>
                <body style='font-family:Arial,sans-serif;
                             text-align:center;padding:40px;'>
                    <h2 style='color:#c62828;'>Claim Rejected</h2>
                    <p>The claim has been rejected successfully.</p>
                    <p><b>Claim No:</b>
                        {System.Net.WebUtility.HtmlEncode(claim.ClaimNo ?? "")}
                    </p>
                    <p><b>Reason:</b> {safeReason}</p>
                </body>
                </html>",
                        "text/html");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Log ex using your existing logger.
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to reject the claim."
                });
            }
        }
        [Authorize]
        [HttpPost("reject-claim")]
        public async Task<IActionResult> RejectClaim(
       [FromBody] ClaimApprovalActionDto dto)
        {
            try
            {
                if (dto == null || dto.ClaimId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid claim details."
                    });
                }

                var empIdValue = User.FindFirst("EmpId")?.Value;
                var orgIdValue = User.FindFirst("orgId")?.Value
                                 ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(empIdValue, out int empId) ||
                    !int.TryParse(orgIdValue, out int orgId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid employee or organization token."
                    });
                }

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == dto.ClaimId &&
                        x.OrganizationId == orgId &&
                        x.IsActive);

                if (claim == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Claim not found."
                    });
                }

                if (claim.Status != "Pending")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Only pending claims can be rejected."
                    });
                }

                if (!claim.DepartmentId.HasValue)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim department is missing."
                    });
                }

                // Use the Common DB for employee validation.
                var approverEmployee = await _commonContext.CompanyUsers
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == empId &&
                        x.OrgId == orgId &&
                        x.Status == "Active");

                if (approverEmployee == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Active approver not found."
                    });
                }

                // Get the pending approval assigned to this employee
                // for this claim's selected department configuration.
                var approval = await _context.ClaimApprovals
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claim.ClaimId &&
                        x.ApproverEmpId == empId &&
                        x.Status == "Pending" &&
                        x.DepartmentApprovalConfigId != null);

                if (approval == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No pending approval found for this employee."
                    });
                }

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.Id == approval.DepartmentApprovalConfigId &&
                            x.OrganizationId == claim.OrganizationId &&
                            x.DepartmentId == claim.DepartmentId.Value &&
                            x.ModuleCode == "travel_portal" &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval does not match the claim's selected department."
                    });
                }

                if (claim.CurrentApprovalLevel != approval.ApprovalLevel)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This approval level is no longer active."
                    });
                }

                await using var transaction =
                    await _context.Database.BeginTransactionAsync();

                try
                {
                    approval.Status = "Rejected";
                    approval.Remarks = string.IsNullOrWhiteSpace(dto.Remarks)
                        ? "NA"
                        : dto.Remarks.Trim();

                    approval.ActionDate = DateTime.Now;

                    claim.Status = "Rejected";
                    claim.RejectedAt = DateTime.Now;
                    claim.CurrentApprovalLevel = null;
                    claim.LastModifiedAt = DateTime.Now;

                    // Cancel future approvals only for this department config.
                    var futureApprovals =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == claim.ClaimId &&
                                x.DepartmentApprovalConfigId ==
                                    departmentConfig.Id &&
                                x.ApprovalLevel > approval.ApprovalLevel &&
                                x.Status == "Pending")
                            .ToListAsync();

                    foreach (var futureApproval in futureApprovals)
                    {
                        futureApproval.Status = "Cancelled";
                        futureApproval.ActionDate = DateTime.Now;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "Claim rejected successfully.",
                        claimId = claim.ClaimId,
                        claimNo = claim.ClaimNo,
                        departmentId = claim.DepartmentId,
                        approvalLevel = approval.ApprovalLevel
                    });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Log ex using your existing logger.
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to reject the claim."
                });
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
                var orgIdClaim = User.FindFirst("orgId")?.Value;
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
                var orgIdClaim = User.FindFirst("orgId")?.Value;
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
                // ==========================================
                // 1. GET ORGANIZATION FROM TOKEN
                // ==========================================

                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrWhiteSpace(orgIdClaim) ||
                    !int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }


                // ==========================================
                // 2. GET ROLE FROM TOKEN
                // ==========================================

                var role = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Role not found in token."
                    });
                }


                // ==========================================
                // 3. GET EMPID FROM TOKEN
                // ==========================================

                int employeeId = 0;

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!string.IsNullOrWhiteSpace(empIdClaim))
                {
                    int.TryParse(empIdClaim, out employeeId);
                }


                // ==========================================
                // 4. IF EMPID NOT IN TOKEN
                //    GET USING EMAIL
                // ==========================================

                if (employeeId <= 0)
                {
                    var email =
                        User.FindFirst(ClaimTypes.Email)?.Value;

                    if (string.IsNullOrWhiteSpace(email))
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "User identity not found in token."
                        });
                    }

                    // --------------------------------------
                    // FIRST: COMMON DB company_users
                    // --------------------------------------

                    var commonUser =
                        await _commonContext.CompanyUsers
                            .FirstOrDefaultAsync(x =>
                                x.Email != null &&
                                x.Email.ToLower() == email.ToLower() &&
                                x.OrgId == organizationId &&
                                (string.IsNullOrWhiteSpace(x.Status) ||
                                 x.Status == "Active"));

                    if (commonUser != null &&
                        commonUser.EmpId > 0)
                    {
                        employeeId = commonUser.EmpId;
                    }


                    // --------------------------------------
                    // SECOND: APPLICATION DB employee_master
                    // --------------------------------------

                    if (employeeId <= 0)
                    {
                        var employee =
                            await _context.EmployeeMasters
                                .FirstOrDefaultAsync(x =>
                                    x.EmailId != null &&
                                    x.EmailId.ToLower() == email.ToLower() &&
                                    x.OrganizationId == organizationId &&
                                    x.IsActive);

                        if (employee != null)
                        {
                            employeeId = employee.EmpId;
                        }
                    }
                }


                // ==========================================
                // 5. STILL NO EMPLOYEE ID
                // ==========================================

                if (employeeId <= 0)
                {
                    return Ok(new
                    {
                        success = true,
                        count = 0,
                        data = new List<MyClaimDto>(),
                        message =
                            "No employee mapping found for this user."
                    });
                }


                // ==========================================
                // 6. GET ONLY THIS EMPLOYEE'S CLAIMS
                // ==========================================

                var claims =
                    await _context.ClaimMasters
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.EmployeeId == employeeId &&
                            x.Status != "Draft")
                        .OrderByDescending(x => x.ClaimId)
                        .ToListAsync();


                // ==========================================
                // 7. RESULT
                // ==========================================

                var result = new List<MyClaimDto>();

                foreach (var claim in claims)
                {
                    var approvals =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == claim.ClaimId)
                            .OrderBy(x =>
                                x.ApprovalLevel)
                            .ToListAsync();


                    // ==========================================
                    // LAST ACTION
                    // ==========================================

                    string lastActionBy = "-";
                    string lastAction = "-";

                    var lastActionRecord =
                        approvals
                            .Where(x =>
                                x.Status == "Approved" ||
                                x.Status == "Rejected")
                            .OrderByDescending(x =>
                                x.ApprovalLevel)
                            .FirstOrDefault();

                    if (lastActionRecord != null)
                    {
                        lastAction =
                            lastActionRecord.Status;

                        var approver =
                            await _commonContext.CompanyUsers
                                .FirstOrDefaultAsync(x =>
                                    x.EmpId ==
                                        lastActionRecord.ApproverEmpId &&
                                    x.OrgId == organizationId &&
                                    (string.IsNullOrWhiteSpace(x.Status) ||
                                     x.Status == "Active"));

                        if (approver != null)
                        {
                            lastActionBy =
                                approver.FullName ?? "-";
                        }
                        else
                        {
                            // fallback to application employee master
                            var appApprover =
                                await _context.EmployeeMasters
                                    .FirstOrDefaultAsync(x =>
                                        x.EmpId ==
                                            lastActionRecord.ApproverEmpId &&
                                        x.OrganizationId ==
                                            organizationId &&
                                        x.IsActive);

                            if (appApprover != null)
                            {
                                lastActionBy =
                                    appApprover.FullName;
                            }
                        }
                    }


                    // ==========================================
                    // APPROVAL STATUS
                    // ==========================================

                    string GetStatus(int level)
                    {
                        var approval =
                            approvals.FirstOrDefault(x =>
                                x.ApprovalLevel == level);

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


                    // ==========================================
                    // ADD RESULT
                    // ==========================================

                    result.Add(new MyClaimDto
                    {
                        ClaimId =
                            claim.ClaimId,

                        ClaimNo =
                            claim.ClaimNo ?? "",

                        ClaimMonth =
                            claim.ClaimMonth,

                        TotalAmount =
                            claim.TotalAmount ?? 0,

                        Status =
                            claim.Status,

                        CurrentApprovalLevel =
                            claim.CurrentApprovalLevel,

                        LastActionBy =
                            lastActionBy,

                        LastAction =
                            lastAction,

                        Approver1 =
                            GetStatus(1),

                        Approver2 =
                            GetStatus(2),

                        HrLevel1 =
                            GetStatus(3),

                        HrLevel2 =
                            GetStatus(4),

                        Accounts =
                            GetStatus(5)
                    });
                }


                // ==========================================
                // RESPONSE
                // ==========================================

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
                    message = "Error while fetching claims.",
                    error =
                        ex.InnerException?.Message ??
                        ex.Message
                });
            }
        }


       

        [Authorize]
        [HttpGet("claim-details/{claimId}")]
        public async Task<IActionResult> GetClaimDetails(int claimId)
        {
            try
            {
                // ==========================================
                // 1. VALIDATION
                // ==========================================

                if (claimId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid claim ID."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION FROM JWT
                // ==========================================

                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }


                // ==========================================
                // 3. CLAIM MASTER
                // Organization-wise filter
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.OrganizationId == organizationId &&
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
                // 4. EMPLOYEE DETAILS
                // COMMON DB - company_users
                // ==========================================

                var employee = await _commonContext.CompanyUsers
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId &&
                        x.OrgId == organizationId &&
                        (x.Status == null ||
                         x.Status == "Active"));


                string? designationName = null;

                if (employee != null)
                {
                    // company_users has PrimaryTitle
                    designationName =
                        employee.PrimaryTitle;
                }


                // ==========================================
                // 5. TRAVEL DETAILS
                // Only IsActive = true
                // ==========================================

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


                // ==========================================
                // 6. FOOD DETAILS
                // Only IsActive = true
                // ==========================================

                var foods = await _context.ClaimFoods
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();


                // ==========================================
                // 7. EXPENSE DETAILS
                // Only IsActive = true
                // ==========================================

                var expenses = await _context.ClaimExpenses
                    .Where(x =>
                        x.ClaimId == claimId &&
                        x.IsActive)
                    .ToListAsync();


                // ==========================================
                // 8. DOCUMENTS
                // Only IsActive = true
                // ==========================================

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


                // ==========================================
                // 9. APPROVAL HISTORY
                // COMMON DB company_users
                // ==========================================

                var approvals = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == claimId)
                    .OrderBy(x => x.ApprovalLevel)
                    .ToListAsync();


                // ==========================================
                // 10. GET APPROVER DETAILS
                // COMMON DB
                // ==========================================

                var approverIds = approvals
                    .Select(x => x.ApproverEmpId)
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                var approverUsers = await _commonContext.CompanyUsers
                    .Where(x =>
                        x.OrgId == organizationId &&
                        approverIds.Contains(x.EmpId))
                    .ToListAsync();


                var approverLookup = approverUsers
                    .ToDictionary(
                        x => x.EmpId,
                        x => x);


                // ==========================================
                // 11. APPROVAL RESPONSE
                // ==========================================

                var approvalResult = approvals
                    .Select(x =>
                    {
                        approverLookup.TryGetValue(
                            x.ApproverEmpId ?? 0,
                            out var approver);

                        return new
                        {
                            x.ApprovalLevel,

                            x.ApproverEmpId,

                            ApproverName =
                                approver?.FullName,

                            ApproverCode =
                                approver?.EmpCode,

                            ApproverEmail =
                                approver?.Email,

                            x.Status,

                            x.Remarks,

                            x.ActionDate
                        };
                    })
                    .ToList();


                // ==========================================
                // 12. FINAL RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,

                    claim = new
                    {
                        claim.ClaimId,

                        claim.ClaimNo,

                        claim.EmployeeId,

                        EmployeeName =
                            employee?.FullName,

                        Designation =
                            designationName,

                        EmployeeCode =
                            employee?.EmpCode,

                        Email =
                            employee?.Email,

                        claim.OrganizationId,

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

                    approvals = approvalResult
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,

                    message =
                        "Error while fetching claim details.",

                    error =
                        ex.InnerException?.Message
                        ?? ex.Message
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
        [Authorize]
        [HttpGet("claim-report/{claimId}")]
        public async Task<IActionResult> GetClaimReport(int claimId)
        {
            try
            {
                // ==========================================
                // 1. VALIDATION
                // ==========================================

                if (claimId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid claim ID."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION FROM JWT
                // ==========================================

                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }


                // ==========================================
                // 3. CLAIM MASTER
                // TRAVEL_FORM DB
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
                        x.OrganizationId == organizationId &&
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
                // 4. EMPLOYEE DETAILS
                // COMMON DB - company_users
                // ==========================================

                var employee =
                    await _commonContext.CompanyUsers
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == claim.EmployeeId &&
                            x.OrgId == organizationId);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message =
                            "Claim employee not found in common database."
                    });
                }


                // ==========================================
                // 5. DESIGNATION
                // COMMON DB - company_users.PrimaryTitle
                // ==========================================

                string? designationName =
                    employee.PrimaryTitle;


                // ==========================================
                // 6. TRAVEL DETAILS
                // ONLY ACTIVE RECORDS
                // ==========================================

                var travelDetails = await
                    (
                        from t in _context.ClaimTravels

                        // --------------------------------------
                        // VEHICLE TYPE
                        // --------------------------------------

                        join v in _context.VehicleMaster
                            on t.VehicleTypeId equals v.VehicleId

                        // --------------------------------------
                        // FUEL TYPE - LEFT JOIN
                        // --------------------------------------

                        join f in _context.FuelTypeMasters
                            on t.FuelTypeId equals f.FuelTypeId
                            into fuelGroup

                        from f in fuelGroup.DefaultIfEmpty()

                        where
                            t.ClaimId == claimId &&
                            t.IsActive

                        select new TravelReportDto
                        {
                            // =================================
                            // TRAVEL
                            // =================================

                            TravelId =
                                t.TravelId,

                            TravelDate =
                                t.TravelDate,

                            FromLocation =
                                t.FromLocation,

                            ToLocation =
                                t.ToLocation,

                            Purpose =
                                t.Purpose,


                            // =================================
                            // VEHICLE TYPE
                            // =================================

                            VehicleType =
                                v.VehicleName,


                            // =================================
                            // FUEL TYPE
                            // Bike = 2
                            // Car  = 3
                            //
                            // Taxi / Bus / Train = "-"
                            // =================================

                            FuelType =
                                (t.VehicleTypeId == 2 ||
                                 t.VehicleTypeId == 3)
                                    ? (f != null
                                        ? f.FuelTypeName
                                        : "-")
                                    : "-",


                            // =================================
                            // VEHICLE NUMBER
                            // =================================

                            VehicleNumber =
                                string.IsNullOrWhiteSpace(
                                    t.VehicleNumber)
                                ||
                                t.VehicleNumber.Trim().Equals(
                                    v.VehicleName,
                                    StringComparison.OrdinalIgnoreCase)
                                    ? null
                                    : t.VehicleNumber,


                            // =================================
                            // OTHER DETAILS
                            // =================================

                            KmRun =
                                t.KmRun,

                            Amount =
                                t.Amount,

                            AppliedRatePerKm =
                                t.AppliedRatePerKm
                        }
                    )
                    .ToListAsync();


                // ==========================================
                // 7. VEHICLE TYPE + VEHICLE NUMBER
                // ==========================================

                var vehicleDetails =
                    travelDetails
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(
                                x.VehicleType) &&
                            !string.IsNullOrWhiteSpace(
                                x.VehicleNumber))
                        .Select(x =>
                            $"{x.VehicleType} - {x.VehicleNumber}")
                        .Distinct()
                        .ToList();


                string vehicleNo =
                    vehicleDetails.Any()
                        ? string.Join(
                            " / ",
                            vehicleDetails)
                        : "-";


                // ==========================================
                // 8. FOOD DETAILS
                // ONLY ACTIVE RECORDS
                // ==========================================

                var foodDetails =
                    await _context.ClaimFoods
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.IsActive)
                        .Select(x => new FoodReportDto
                        {
                            FoodDate =
                                x.FoodDate,

                            FoodTypeId =
                                x.FoodTypeId,

                            Amount =
                                x.Amount
                        })
                        .ToListAsync();


                // ==========================================
                // 9. EXPENSE DETAILS
                // ONLY ACTIVE RECORDS
                // ==========================================

                var expenseDetails =
                    await _context.ClaimExpenses
                        .Where(x =>
                            x.ClaimId == claimId &&
                            x.IsActive)
                        .Select(x => new ExpenseReportDto
                        {
                            ExpenseId =
                                x.ExpenseId,

                            ExpenseDate =
                                x.ExpenseDate,

                            ExpenseType =
                                x.ExpenseType,

                            InvoiceNo =
                                x.InvoiceNo,

                            Description =
                                x.Description,

                            Purpose =
                                x.Purpose,

                            Amount =
                                x.Amount ?? 0
                        })
                        .ToListAsync();


                // ==========================================
                // 10. APPROVALS
                // TRAVEL_FORM DB
                // ==========================================

                var approvals =
                    await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claimId)
                        .OrderBy(x =>
                            x.ApprovalLevel)
                        .ToListAsync();


                // ==========================================
                // 11. GET APPROVER IDS
                // ==========================================

                var approverIds =
                    approvals
                        .Where(x =>
                            x.ApproverEmpId.HasValue &&
                            x.ApproverEmpId.Value > 0)
                        .Select(x =>
                            x.ApproverEmpId.Value)
                        .Distinct()
                        .ToList();


                // ==========================================
                // 12. APPROVER DETAILS
                // COMMON DB - company_users
                // ==========================================

                var approverUsers =
                    await _commonContext.CompanyUsers
                        .Where(x =>
                            x.OrgId == organizationId &&
                            approverIds.Contains(x.EmpId))
                        .ToListAsync();


                // ==========================================
                // 13. CREATE APPROVER LOOKUP
                // ==========================================

                var approverLookup =
                    approverUsers.ToDictionary(
                        x => x.EmpId,
                        x => x);


                // ==========================================
                // 14. APPROVAL RESPONSE
                // ==========================================

                var approvalStatus =
                    approvals
                        .Select(a =>
                        {
                            approverLookup.TryGetValue(
                                a.ApproverEmpId ?? 0,
                                out var approver);

                            return new ApprovalReportDto
                            {
                                ApprovalLevel =
                                    a.ApprovalLevel,

                                ApproverName =
                                    approver?.FullName ?? "-",

                                Status =
                                    a.Status,

                                Remarks =
                                    a.Remarks,

                                ActionDate =
                                    a.ActionDate
                            };
                        })
                        .ToList();


                // ==========================================
                // 15. MONTH FORMAT
                // Example: Jul-26
                // ==========================================

                string monthText =
                    claim.ClaimMonth.ToString("MMM-yy");


                // ==========================================
                // 16. BUILD REPORT
                // ==========================================

                var report =
                    new ClaimReportDto
                    {
                        // =================================
                        // EMPLOYEE
                        // =================================

                        Employee =
                            new EmployeeReportDto
                            {
                                Name =
                                    employee.FullName,

                                EmployeeCode =
                                    employee.EmpCode,

                                Designation =
                                    designationName,

                                VehicleNo =
                                    vehicleNo
                            },


                        // =================================
                        // CLAIM
                        // =================================

                        Claim =
                            new ClaimReportHeaderDto
                            {
                                ClaimId =
                                    claim.ClaimId,

                                ClaimNo =
                                    claim.ClaimNo,

                                ClaimMonth =
                                    claim.ClaimMonth,

                                Month =
                                    monthText,

                                TotalAmount =
                                    claim.TotalAmount ?? 0
                            },


                        // =================================
                        // APPROVAL
                        // =================================

                        ApprovalStatus =
                            approvalStatus,


                        // =================================
                        // TRAVEL
                        // =================================

                        TravelDetails =
                            travelDetails,


                        // =================================
                        // FOOD
                        // =================================

                        FoodDetails =
                            foodDetails,


                        // =================================
                        // EXPENSE
                        // =================================

                        ExpenseSummary =
                            expenseDetails
                    };


                // ==========================================
                // 17. RESPONSE
                // ==========================================

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

        //----------  Appproval Flow For local Expense claim ------------------------\\

        [Authorize]
        [HttpGet("approval-setup/branches")]
        public async Task<IActionResult> GetApprovalSetupBranches()
        {
            try
            {
                // 1. Get OrganizationId from JWT
                var orgIdClaim =
                    User.FindFirst("orgId")?.Value
                    ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // 2. Get all branches belonging to this organization
                var branches = await _commonContext.Branches
                    .AsNoTracking()
                    .Where(x => x.OrgId == organizationId)
                    .OrderBy(x => x.BranchName)
                    .Select(x => new
                    {
                        branchId = x.BranchId,
                        branchName = x.BranchName
                    })
                    .ToListAsync();

                // 3. Return response
                return Ok(new
                {
                    success = true,
                    organizationId,
                    data = branches
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch branches.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("approval-setup/departments")]
        public async Task<IActionResult> GetApprovalSetupDepartments(
       [FromQuery] int branchId)
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
                // ==========================================
                var orgIdClaim =
                    User.FindFirst("orgId")?.Value
                    ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // ==========================================
                // 2. VALIDATE BRANCH ID
                // ==========================================
                if (branchId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Valid BranchId is required."
                    });
                }

                // ==========================================
                // 3. VERIFY BRANCH BELONGS TO ORGANIZATION
                // ==========================================
                bool branchExists = await _commonContext.Branches
                    .AsNoTracking()
                    .AnyAsync(b =>
                        b.BranchId == branchId &&
                        b.OrgId == organizationId);

                if (!branchExists)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Branch not found for this organization."
                    });
                }

                // ==========================================
                // 4. GET DEPARTMENTS FOR SELECTED BRANCH
                //    AND ORGANIZATION
                // ==========================================
                var departments = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(d =>
                        d.OrgId == organizationId &&
                        d.BranchId == branchId)
                    .Select(d => new
                    {
                        departmentId = d.Id,
                        departmentName = d.DepartmentName
                    })
                    .Distinct()
                    .OrderBy(d => d.departmentName)
                    .ToListAsync();

                // ==========================================
                // 5. RETURN RESPONSE
                // ==========================================
                return Ok(new
                {
                    success = true,
                    organizationId,
                    branchId,
                    totalDepartments = departments.Count,
                    data = departments
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch departments.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpGet("approval-setup/employees")]
        public async Task<IActionResult> GetApprovalSetupEmployees(
      [FromQuery] int branchId,
      [FromQuery] string department)
        {
            try
            {
                // 1. Get OrganizationId from JWT
                var orgIdClaim = User.FindFirst("orgId")?.Value
                    ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId)
                    || organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // 2. Validate BranchId
                if (branchId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Valid BranchId is required."
                    });
                }

                // 3. Validate Department IDs
                if (string.IsNullOrWhiteSpace(department))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "At least one DepartmentId is required."
                    });
                }

                var selectedDepartmentIds = new List<int>();

                foreach (var value in department.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
                {
                    if (!int.TryParse(value, out int id) || id <= 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Invalid DepartmentId: {value}"
                        });
                    }

                    selectedDepartmentIds.Add(id);
                }

                selectedDepartmentIds = selectedDepartmentIds
                    .Distinct()
                    .ToList();

                if (selectedDepartmentIds.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "At least one valid DepartmentId is required."
                    });
                }

                // 4. Validate branch belongs to organization
                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .Where(b =>
                        b.BranchId == branchId &&
                        b.OrgId == organizationId)
                    .Select(b => new
                    {
                        branchId = b.BranchId,
                        branchName = b.BranchName
                    })
                    .FirstOrDefaultAsync();

                if (branch == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Branch not found for this organization."
                    });
                }

                // 5. Get departments belonging to this branch and organization
                var departments = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(d =>
                        d.OrgId == organizationId &&
                        d.BranchId == branchId &&
                        selectedDepartmentIds.Contains(d.Id))
                    .Select(d => new
                    {
                        departmentId = d.Id,
                        departmentName = d.DepartmentName
                    })
                    .ToListAsync();

                if (departments.Count != selectedDepartmentIds.Count)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "One or more departments do not belong to this branch or organization."
                    });
                }

                // 6. Get employees for this branch and organization
                var employeeRows = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(e =>
                        e.OrgId == organizationId &&
                        e.BranchId == branchId &&
                        (e.Status == null ||
                         e.Status.Trim().ToUpper() == "ACTIVE"))
                    .Select(e => new
                    {
                        employeeId = e.EmpId,
                        employeeCode = e.EmpCode,
                        employeeName = e.FullName,
                        departmentId = e.DepartmentId
                    })
                    .ToListAsync();

                // 7. Match employee department IDs and return all matching names
                // Assumes CompanyUsers.DepartmentId stores values such as "13" or "13,14".
                var employees = employeeRows
                    .Select(e =>
                    {
                        var employeeDepartmentIds = (e.departmentId ?? string.Empty)
                            .Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries)
                            .Where(x => int.TryParse(x, out _))
                            .Select(int.Parse)
                            .Distinct()
                            .ToList();

                        var matchedDepartments = departments
                            .Where(d =>
                                employeeDepartmentIds.Contains(d.departmentId))
                            .Select(d => new
                            {
                                departmentId = d.departmentId,
                                departmentName = d.departmentName
                            })
                            .ToList();

                        return new
                        {
                            e.employeeId,
                            e.employeeCode,
                            e.employeeName,
                            departments = matchedDepartments
                        };
                    })
                    .Where(e => e.departments.Count > 0)
                    .OrderBy(e => e.employeeName)
                    .Select(e => new
                    {
                        e.employeeId,
                        e.employeeCode,
                        e.employeeName,
                        branchId = branch.branchId,
                        branchName = branch.branchName,
                        departments = e.departments
                    })
                    .ToList();

                // 8. Return response
                return Ok(new
                {
                    success = true,
                    organizationId,
                    branchId = branch.branchId,
                    branchName = branch.branchName,
                    departmentIds = selectedDepartmentIds,
                    totalEmployees = employees.Count,
                    data = employees
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch employees.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpPost("approval-setup/department-config")]
        public async Task<IActionResult> SaveDepartmentApprovalConfig(
            [FromBody] SaveDepartmentApprovalConfigRequest request)
        {
            try
            {
                var orgClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgClaim, out int organizationId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in token."
                    });
                }

                var role =
                    User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ??
                    User.FindFirst("role")?.Value;

                if (role is null ||
                    !(role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                      role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                      role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase)))
                {
                    return Forbid();
                }

                if (request == null ||
                    string.IsNullOrWhiteSpace(request.ModuleCode) ||
                    request.BranchId <= 0 ||
                    request.DepartmentId <= 0 ||
                    request.TotalApprovalLevels < 1 ||
                    request.TotalApprovalLevels > 10)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid request. Check module, branch, department and approval levels."
                    });
                }

                var department = await _commonContext.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.Id == request.DepartmentId &&
                        d.OrgId == organizationId &&
                        d.BranchId == request.BranchId);

                if (department == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Department does not belong to this organization and branch."
                    });
                }

                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b =>
                        b.BranchId == request.BranchId &&
                        b.OrgId == organizationId);

                if (branch == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid branch."
                    });
                }

                var moduleCode = request.ModuleCode.Trim();

                var moduleEnabled = await (
     from om in _commonContext.OrganizationModules
     join mm in _commonContext.ModuleMasters
         on om.ModuleCode equals mm.ModuleCode  
     where om.OrgId == organizationId
           && om.IsActive
           && mm.IsActive
           && om.ModuleCode == moduleCode
     select om
 ).AnyAsync();

                if (!moduleEnabled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This module is not enabled for the organization."
                    });
                }

                var branchName = branch.BranchName.Trim();

                var config = await _context.DepartmentApprovalConfigs
                    .FirstOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.ModuleCode == moduleCode &&
                        x.DepartmentId == request.DepartmentId &&
                        x.IsActive);

                var now = DateTime.UtcNow;

                if (config != null)
                {
                    config.TotalApprovalLevels = request.TotalApprovalLevels;
                    config.Branch = branchName;
                    config.UpdatedDate = now;
                }
                else
                {
                    config = new DepartmentApprovalConfig
                    {
                        OrganizationId = organizationId,
                        ModuleCode = moduleCode,
                        Branch = branchName,
                        DepartmentId = request.DepartmentId,
                        TotalApprovalLevels = request.TotalApprovalLevels,
                        IsActive = true,
                        CreatedDate = now
                    };

                    _context.DepartmentApprovalConfigs.Add(config);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Department approval configuration saved successfully.",
                    data = new
                    {
                        config.Id,
                        config.OrganizationId,
                        config.ModuleCode,
                        config.DepartmentId,
                        config.TotalApprovalLevels,
                        config.IsActive
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to save department approval configuration.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("approval-setup/department-config")]
        public async Task<IActionResult> GetDepartmentApprovalConfig()
        {
            try
            {
                // Step 1: Get OrganizationId from JWT
                var orgClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgClaim, out int organizationId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in token."
                    });
                }

                // Step 2: Validate Admin role
                var role =
                    User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ??
                    User.FindFirst("role")?.Value;

                if (role == null ||
                    !(role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                      role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                      role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase)))
                {
                    return Forbid();
                }

                // Step 3: Get approval configurations from Travel DB
                var configs = await _context.DepartmentApprovalConfigs
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive)
                    .ToListAsync();

                // Step 4: Get department details from Common DB
                var departmentIds = configs
                    .Select(x => x.DepartmentId)
                    .Distinct()
                    .ToList();

                var departments = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(d => departmentIds.Contains(d.Id))
                    .ToListAsync();

                // Step 5: Join both results in memory
                var result = configs
                    .Select(config =>
                    {
                        var department = departments
                            .FirstOrDefault(d => d.Id == config.DepartmentId);

                        return new
                        {
                            departmentApprovalConfigId = config.Id,
                            departmentId = config.DepartmentId,
                            departmentName = department?.DepartmentName,
                            moduleCode = config.ModuleCode,
                            branch = config.Branch,
                            totalApprovalLevels = config.TotalApprovalLevels,
                            isActive = config.IsActive,
                            createdDate = config.CreatedDate,
                            updatedDate = config.UpdatedDate
                        };
                    })
                    .OrderBy(x => x.branch)
                    .ThenBy(x => x.departmentName)
                    .ToList();

                // Step 6: Return response
                return Ok(new
                {
                    success = true,
                    organizationId,
                    totalDepartments = result.Count,
                    departments = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch department approval configuration.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpDelete("approval-setup/department-config/{configId}")]
        public async Task<IActionResult> DeleteDepartmentApprovalConfig(int configId)
        {
            try
            {
                // Get OrganizationId from JWT
                var orgClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgClaim, out int organizationId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in token."
                    });
                }

                // Validate Admin role
                var role =
                    User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ??
                    User.FindFirst("role")?.Value;

                if (role == null ||
                    !(role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                      role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                      role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase)))
                {
                    return Forbid();
                }

                if (configId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid configuration ID."
                    });
                }

                // Find active configuration for this organization
                var config = await _context.DepartmentApprovalConfigs
                    .FirstOrDefaultAsync(x =>
                        x.Id == configId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (config == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Active department approval configuration not found."
                    });
                }

                // Soft delete
                config.IsActive = false;
                config.UpdatedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Department approval configuration deactivated successfully.",
                    data = new
                    {
                        config.Id,
                        config.OrganizationId,
                        config.ModuleCode,
                        config.DepartmentId,
                        config.TotalApprovalLevels,
                        config.IsActive
                    }
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to delete department approval configuration.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpPost("approval-setup/employee-config")]
        public async Task<IActionResult> SaveEmployeeApprovalConfig(
    [FromBody] SaveApprovalSetupRequest request)
        {
            try
            {
                // 1. Organization ID
                var orgIdClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // 2. Role validation
                var role =
                    User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ??
                    User.FindFirst("role")?.Value;

                bool isAdmin =
                    string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "Super Admin", StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message = "Only Admin or SuperAdmin can configure approvals."
                    });
                }

                // 3. Request validation
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.ModuleCode) ||
                    request.BranchId <= 0 ||
                    request.DepartmentId <= 0 ||
                    request.EmployeeId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "ModuleCode, BranchId, DepartmentId and EmployeeId are required."
                    });
                }

                string moduleCode = request.ModuleCode.Trim();

                // 4. Validate module access
                bool moduleEnabled = await (
                    from om in _commonContext.OrganizationModules.AsNoTracking()
                    join mm in _commonContext.ModuleMasters.AsNoTracking()
                        on om.ModuleCode equals mm.ModuleCode
                    where om.OrgId == organizationId
                          && om.IsActive
                          && mm.IsActive
                          && om.ModuleCode == moduleCode
                    select om
                ).AnyAsync();

                if (!moduleEnabled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Selected module is not enabled for this organization."
                    });
                }

                // 5. Validate branch
                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == request.BranchId &&
                        x.OrgId == organizationId);

                if (branch == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Branch does not belong to this organization."
                    });
                }

                string branchName = branch.BranchName?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(branchName))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Branch name is missing."
                    });
                }

                // 6. Validate department against department master
                var department = await _commonContext.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.Id == request.DepartmentId &&
                        d.OrgId == organizationId &&
                        d.BranchId == request.BranchId);

                if (department == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Department does not belong to the selected organization and branch."
                    });
                }

                // 7. Get configured approval levels using DepartmentId
                var departmentConfig = await _context.DepartmentApprovalConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.ModuleCode == moduleCode &&
                        x.DepartmentId == request.DepartmentId &&
                        x.Branch == branchName &&
                        x.IsActive);

                if (departmentConfig == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message =
                            "Department approval configuration not found. Configure department approval levels first."
                    });
                }

                if (request.TotalApprovalLevels != departmentConfig.TotalApprovalLevels)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "TotalApprovalLevels must match the department configuration.",
                        configuredApprovalLevels = departmentConfig.TotalApprovalLevels
                    });
                }

                // 8. Validate employee
                // ==========================================
                // 8. VALIDATE EMPLOYEE
                // CompanyUsers.DepartmentId is a string (CSV)
                // Example: "21,15,30"
                // ==========================================

                var employee = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == request.EmployeeId &&
                        x.OrgId == organizationId &&
                        x.BranchId == request.BranchId &&
                        (x.Status == null ||
                         x.Status.Trim().ToUpper() == "ACTIVE"));

                if (employee == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee does not belong to the selected organization and branch, or is inactive."
                    });
                }

                // Convert CSV DepartmentId string into integer list
                var employeeDepartmentIds = (employee.DepartmentId ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id =>
                    {
                        return int.TryParse(id.Trim(), out int departmentId)
                            ? departmentId
                            : 0;
                    })
                    .Where(id => id > 0)
                    .ToList();

                // Validate selected department
                if (!employeeDepartmentIds.Contains(request.DepartmentId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee does not belong to the selected department.",
                        employeeId = request.EmployeeId,
                        selectedDepartmentId = request.DepartmentId,
                        employeeDepartmentIds
                    });
                }

                // 9. Validate approval levels
                if (request.ApprovalLevels == null ||
                    request.ApprovalLevels.Count != departmentConfig.TotalApprovalLevels)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "ApprovalLevels count must match the configured department approval levels."
                    });
                }

                var orderedLevels = request.ApprovalLevels
                    .OrderBy(x => x.ApprovalLevel)
                    .ToList();

                for (int i = 0; i < orderedLevels.Count; i++)
                {
                    var level = orderedLevels[i];

                    if (level.ApprovalLevel != i + 1)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Approval levels must be sequential, starting from 1."
                        });
                    }

                    if (!string.Equals(
                            level.ApprovalRule,
                            "AnyOne",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Currently, only the AnyOne approval rule is supported."
                        });
                    }

                    if (level.ApproverEmployeeIds == null ||
                        level.ApproverEmployeeIds.Count == 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"At least one approver is required for level {level.ApprovalLevel}."
                        });
                    }

                    if (level.ApproverEmployeeIds.Any(id => id <= 0))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"Invalid approver ID in level {level.ApprovalLevel}."
                        });
                    }

                    if (level.ApproverEmployeeIds.Distinct().Count() !=
                        level.ApproverEmployeeIds.Count)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"Duplicate approvers are not allowed within level {level.ApprovalLevel}."
                        });
                    }
                }

                // 10. Validate approvers belong to this organization and are active
                var approverIds = orderedLevels
                    .SelectMany(x => x.ApproverEmployeeIds)
                    .Distinct()
                    .ToList();

                var validApproverIds = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        approverIds.Contains(x.EmpId) &&
                        (x.Status == null ||
                         x.Status.Trim().ToUpper() == "ACTIVE"))
                    .Select(x => x.EmpId)
                    .Distinct()
                    .ToListAsync();

                var invalidApproverIds = approverIds
                    .Except(validApproverIds)
                    .ToList();

                if (invalidApproverIds.Count > 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Some approvers do not belong to this organization or are inactive.",
                        invalidApproverIds
                    });
                }

                // 11. Save inside transaction
                await using var transaction =
                    await _context.Database.BeginTransactionAsync();

                try
                {
                    var now = DateTime.UtcNow;

                    var previousConfigs = await _context.EmployeeApprovalConfigs
                        .Include(x => x.Approvers)
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.EmployeeId == request.EmployeeId &&
                            x.IsActive)
                        .ToListAsync();

                    // Preserve history; deactivate previous configuration
                    foreach (var previousConfig in previousConfigs)
                    {
                        previousConfig.IsActive = false;
                        previousConfig.UpdatedDate = now;

                        foreach (var previousApprover in previousConfig.Approvers)
                        {
                            previousApprover.IsActive = false;
                            previousApprover.UpdatedDate = now;
                        }
                    }

                    var newConfigs = new List<EmployeeApprovalConfig>();

                    foreach (var level in orderedLevels)
                    {
                        var employeeApproval = new EmployeeApprovalConfig
                        {
                            OrganizationId = organizationId,
                            DepartmentApprovalConfigId = departmentConfig.Id,
                            EmployeeId = request.EmployeeId,
                            ApprovalLevel = level.ApprovalLevel,
                            ApprovalRule = "AnyOne",
                            IsActive = true,
                            CreatedDate = now
                        };

                        for (int i = 0; i < level.ApproverEmployeeIds.Count; i++)
                        {
                            employeeApproval.Approvers.Add(
                                new EmployeeApprovalConfigApprover
                                {
                                    ApproverEmployeeId = level.ApproverEmployeeIds[i],
                                    ApprovalOrder = i + 1,
                                    IsActive = true,
                                    CreatedDate = now
                                });
                        }

                        newConfigs.Add(employeeApproval);
                    }

                    _context.EmployeeApprovalConfigs.AddRange(newConfigs);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "Employee approval configuration saved successfully.",
                        organizationId,
                        departmentApprovalConfigId = departmentConfig.Id,
                        moduleCode,
                        branchId = request.BranchId,
                        departmentId = request.DepartmentId,
                        employeeId = request.EmployeeId,
                        totalApprovalLevels = request.TotalApprovalLevels,
                        savedLevels = newConfigs.Select(x => new
                        {
                            x.Id,
                            x.ApprovalLevel,
                            x.ApprovalRule,
                            approvers = x.Approvers.Select(a => new
                            {
                                a.ApproverEmployeeId,
                                a.ApprovalOrder
                            })
                        })
                    });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to save employee approval configuration.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("approval-setup/employee-config")]
        public async Task<IActionResult> GetEmployeeApprovalConfig()
        {
            try
            {
                // 1. Get OrganizationId from JWT
                var orgIdClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // 2. Role validation
                var role =
                    User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ??
                    User.FindFirst("role")?.Value;

                bool isAdmin =
                    string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "Super Admin", StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message = "Only Admin or SuperAdmin can view approval configurations."
                    });
                }

                // 3. Get active department approval configurations
                // from Travel DB
                var departmentConfigs = await _context.DepartmentApprovalConfigs
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive)
                    .ToListAsync();

                if (!departmentConfigs.Any())
                {
                    return Ok(new
                    {
                        success = true,
                        organizationId,
                        totalDepartments = 0,
                        departments = new List<object>()
                    });
                }

                var departmentConfigIds = departmentConfigs
                    .Select(x => x.Id)
                    .ToList();

                // 4. Get active employee approval configurations
                // from Travel DB
                var employeeConfigs = await _context.EmployeeApprovalConfigs
                    .AsNoTracking()
                    .Include(x => x.Approvers)
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive &&
                        departmentConfigIds.Contains(x.DepartmentApprovalConfigId))
                    .ToListAsync();

                // 5. Get department names from Common DB
                var departmentIds = departmentConfigs
                    .Select(x => x.DepartmentId)
                    .Distinct()
                    .ToList();

                var departments = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(d =>
                        d.OrgId == organizationId &&
                        departmentIds.Contains(d.Id))
                    .ToListAsync();

                // 6. Get employee IDs from configurations
                var employeeIds = employeeConfigs
                    .Select(x => x.EmployeeId)
                    .Concat(
                        employeeConfigs
                            .SelectMany(x => x.Approvers)
                            .Select(a => a.ApproverEmployeeId))
                    .Distinct()
                    .ToList();

                // 7. Get employee names from Common DB
                var employees = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        employeeIds.Contains(x.EmpId))
                    .ToListAsync();

                // 8. Build department-wise response
                var result = departmentConfigs
                    .Select(departmentConfig =>
                    {
                        var department = departments
                            .FirstOrDefault(d =>
                                d.Id == departmentConfig.DepartmentId);

                        var configuredEmployees = employeeConfigs
                            .Where(x =>
                                x.DepartmentApprovalConfigId == departmentConfig.Id)
                            .GroupBy(x => x.EmployeeId)
                            .Select(employeeGroup =>
                            {
                                var employee = employees
                                    .FirstOrDefault(e =>
                                        e.EmpId == employeeGroup.Key);

                                var levels = employeeGroup
                                    .OrderBy(x => x.ApprovalLevel)
                                    .Select(levelConfig => new
                                    {
                                        approvalConfigId = levelConfig.Id,
                                        approvalLevel = levelConfig.ApprovalLevel,
                                        approvalRule = levelConfig.ApprovalRule,

                                        approvers = levelConfig.Approvers
                                            .Where(a => a.IsActive)
                                            .OrderBy(a => a.ApprovalOrder)
                                            .Select(a =>
                                            {
                                                var approver = employees
                                                    .FirstOrDefault(e =>
                                                        e.EmpId == a.ApproverEmployeeId);

                                                return new
                                                {
                                                    approverEmployeeId =
                                                        a.ApproverEmployeeId,

                                                    approverName =
                                                        approver?.FullName,

                                                    approverEmail =
                                                        approver?.Email,

                                                    approvalOrder =
                                                        a.ApprovalOrder
                                                };
                                            })
                                            .ToList()
                                    })
                                    .ToList();

                                return new
                                {
                                    employeeId = employeeGroup.Key,
                                    employeeName = employee?.FullName,
                                    employeeEmail = employee?.Email,
                                    approvalLevels = levels
                                };
                            })
                            .ToList();

                        return new
                        {
                            departmentApprovalConfigId = departmentConfig.Id,
                            departmentId = departmentConfig.DepartmentId,
                            departmentName = department?.DepartmentName,
                            moduleCode = departmentConfig.ModuleCode,
                            branch = departmentConfig.Branch,
                            totalApprovalLevels =
                                departmentConfig.TotalApprovalLevels,

                            totalConfiguredEmployees =
                                configuredEmployees.Count,

                            employees = configuredEmployees
                        };
                    })
                    .ToList();

                // 9. Return response
                return Ok(new
                {
                    success = true,
                    organizationId,
                    totalDepartments = result.Count,
                    departments = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch employee approval configuration.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("eligible-employees")]
        public async Task<IActionResult> GetEligibleEmployees()
        {
            try
            {
                // 1. Get logged-in Employee ID from JWT
                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (!int.TryParse(empIdClaim, out int loggedInEmployeeId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid employee token."
                    });
                }

                // 2. Get Organization ID from JWT
                var orgIdClaim = User.FindFirst("orgId")?.Value
                                 ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Organization ID not found in token."
                    });
                }

                // 3. Validate logged-in employee
                var loggedInEmployee = await _commonContext.CompanyUsers
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == loggedInEmployeeId &&
                        x.OrgId == organizationId &&
                        x.Status == "ACTIVE");

                if (loggedInEmployee == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Employee not found or inactive."
                    });
                }

                // 4. Get all active Task Manager department configurations
                var departmentConfigs = await _context.DepartmentApprovalConfigs
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.ModuleCode == "task_management" &&
                        x.IsActive)
                    .ToListAsync();

                if (!departmentConfigs.Any())
                {
                    return Ok(new
                    {
                        success = true,
                        canCreateTask = false,
                        message = "No active Task Manager department configurations found.",
                        departments = new List<object>()
                    });
                }

                var departmentConfigIds = departmentConfigs
                    .Select(x => x.Id)
                    .ToList();

                // 5. Get employee approval configurations
                // Logged-in employee must be configured as an approver
                var approvalConfigs = await _context.EmployeeApprovalConfigs
                    .Include(x => x.Approvers)
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        departmentConfigIds.Contains(x.DepartmentApprovalConfigId) &&
                        x.IsActive &&
                        x.Approvers.Any(a =>
                            a.ApproverEmployeeId == loggedInEmployeeId &&
                            a.IsActive))
                    .ToListAsync();

                if (!approvalConfigs.Any())
                {
                    return Ok(new
                    {
                        success = true,
                        canCreateTask = false,
                        message = "You are not configured as an approver for any Task Manager department.",
                        departments = new List<object>()
                    });
                }

                // 6. Get eligible employee IDs grouped by department configuration
                var eligibleGroups = approvalConfigs
                    .GroupBy(x => x.DepartmentApprovalConfigId)
                    .ToList();

                // 7. Get branch details
                var branches = await _commonContext.Branches
                    .Where(x => x.OrgId == organizationId)
                    .ToListAsync();

                var result = new List<object>();

                // 8. Process each eligible department
                foreach (var group in eligibleGroups)
                {
                    var departmentConfig = departmentConfigs
                        .FirstOrDefault(x => x.Id == group.Key);

                    if (departmentConfig == null)
                        continue;

                    // Find branch using configured branch name
                    var branch = branches.FirstOrDefault(x =>
                        x.BranchName.Trim().ToLower() ==
                        departmentConfig.Branch.Trim().ToLower());

                    if (branch == null)
                        continue;

                    var eligibleEmployeeIds = group
                        .Select(x => x.EmployeeId)
                        .Distinct()
                        .ToList();

                    // 9. Get employees from common database
                    var commonEmployees = await _commonContext.CompanyUsers
                        .Where(x =>
                            eligibleEmployeeIds.Contains(x.EmpId) &&
                            x.OrgId == organizationId &&
                            x.BranchId == branch.BranchId &&
                            x.Status == "ACTIVE")
                        .ToListAsync();

                    // 10. Filter employees belonging to configured department
                    var employees = commonEmployees
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.DepartmentId) &&
                            x.DepartmentId
                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(id => id.Trim())
                                .Contains(departmentConfig.DepartmentId.ToString()))
                        .Select(x => new
                        {
                            employeeId = x.EmpId,
                            empCode = x.EmpCode,
                            employeeName = x.FullName
                        })
                        .ToList();

                    if (!employees.Any())
                        continue;

                    result.Add(new
                    {
                        departmentConfigId = departmentConfig.Id,
                        departmentId = departmentConfig.DepartmentId,
                        branch = departmentConfig.Branch,
                        totalApprovalLevels = departmentConfig.TotalApprovalLevels,
                        employees
                    });
                }

                return Ok(new
                {
                    success = true,
                    canCreateTask = result.Any(),
                    message = result.Any()
                        ? "Eligible departments and employees retrieved successfully."
                        : "No eligible employees found.",
                    departments = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while retrieving eligible employees.",
                    error = ex.Message
                });
            }
        }



        //-------------Task Manager Configurater _-----------//


        //  [Authorize]
        //  [HttpGet("task-manager/approval-levels")]
        //  public async Task<IActionResult> GetTaskManagerApprovalLevels(
        //[FromQuery] int departmentId)
        //  {
        //      try
        //      {
        //          int organizationId = GetOrganizationId();

        //          if (departmentId <= 0)
        //          {
        //              return BadRequest(new
        //              {
        //                  message = "Valid DepartmentId is required."
        //              });
        //          }

        //          var config = await _context.DepartmentApprovalConfigs
        //              .Where(x =>
        //                  x.OrganizationId == organizationId &&
        //                  x.DepartmentId == departmentId &&
        //                  x.ModuleCode == "task_management" &&
        //                  x.IsActive)
        //              .Select(x => new TaskManagerApprovalLevelsResponsedto
        //              {
        //                  ModuleCode = x.ModuleCode,
        //                  DepartmentId = departmentId,
        //                  TotalApprovalLevels = x.TotalApprovalLevels,
        //                  Levels = Enumerable.Range(1, x.TotalApprovalLevels).ToList()
        //              })
        //              .FirstOrDefaultAsync();

        //          if (config == null)
        //          {
        //              return NotFound(new
        //              {
        //                  message = "Approval configuration not found for this department."
        //              });
        //          }

        //          return Ok(config);
        //      }
        //      catch (UnauthorizedAccessException ex)
        //      {
        //          return Unauthorized(new { message = ex.Message });
        //      }
        //      catch (Exception ex)
        //      {
        //          return StatusCode(500, new
        //          {
        //              message = "Unable to fetch approval levels.",
        //              error = ex.Message
        //          });
        //      }
        //  }

        [Authorize]
        [HttpGet("approval-setup/approval-levels")]
        public async Task<IActionResult> GetApprovalLevels(
    [FromQuery] string moduleCode,
    [FromQuery] int branchId,
    [FromQuery] int departmentId)
        {
            try
            {
                // 1. Get OrganizationId from JWT
                int organizationId = GetOrganizationId();

                // 2. Validate request
                if (string.IsNullOrWhiteSpace(moduleCode) ||
                    branchId <= 0 ||
                    departmentId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "ModuleCode, BranchId and DepartmentId are required."
                    });
                }

                moduleCode = moduleCode.Trim();

                // 3. Validate branch belongs to organization
                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == branchId &&
                        x.OrgId == organizationId);

                if (branch == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Branch does not belong to this organization."
                    });
                }

                string branchName = branch.BranchName.Trim();

                // 4. Validate department belongs to organization and branch
                var department = await _commonContext.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == departmentId &&
                        x.OrgId == organizationId &&
                        x.BranchId == branchId);

                if (department == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Department does not belong to this organization and branch."
                    });
                }

                // 5. Check module is enabled for organization
                var moduleEnabled = await (
                    from om in _commonContext.OrganizationModules
                    join mm in _commonContext.ModuleMasters
                        on om.ModuleCode equals mm.ModuleCode
                    where om.OrgId == organizationId
                          && om.IsActive
                          && mm.IsActive
                          && om.ModuleCode == moduleCode
                    select om
                ).AnyAsync();

                if (!moduleEnabled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This module is not enabled for the organization."
                    });
                }

                // 6. Fetch approval configuration
                var config = await _context.DepartmentApprovalConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.ModuleCode == moduleCode &&
                        x.DepartmentId == departmentId &&
                        x.Branch == branchName &&
                        x.IsActive);

                if (config == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Approval configuration not found for this department."
                    });
                }

                // 7. Return approval levels
                return Ok(new
                {
                    success = true,
                    organizationId,
                    moduleCode = config.ModuleCode,
                    branchId,
                    branchName,
                    departmentId = config.DepartmentId,
                    departmentName = department.DepartmentName,
                    totalApprovalLevels = config.TotalApprovalLevels,

                    levels = Enumerable
                        .Range(1, config.TotalApprovalLevels)
                        .ToList()
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch approval levels.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("task-manager/department-employees")]
        public async Task<IActionResult> GetTaskManagerDepartmentEmployees(
      [FromQuery] int departmentId,
      [FromQuery] int branchId)
        {
            try
            {
                int organizationId = GetOrganizationId();

                if (departmentId <= 0 || branchId <= 0)
                {
                    return BadRequest(new
                    {
                        message = "Valid DepartmentId and BranchId are required."
                    });
                }

                // Check Task Management module
                bool moduleEnabled = await _commonContext.OrganizationModules
                    .AnyAsync(x =>
                        x.OrgId == organizationId &&
                        x.ModuleCode == "task_management" &&
                        x.IsActive);

                if (!moduleEnabled)
                {
                    return BadRequest(new
                    {
                        message = "Task Management is not enabled for this organization."
                    });
                }

                // Validate department
                var department = await _commonContext.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == departmentId &&
                        x.OrgId == organizationId);

                if (department == null)
                {
                    return NotFound(new
                    {
                        message = "Department not found."
                    });
                }

                // Validate branch
                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == branchId &&
                        x.OrgId == organizationId);

                if (branch == null)
                {
                    return NotFound(new
                    {
                        message = "Branch not found."
                    });
                }

                // Get employees for the selected organization and branch
                var employees = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        x.BranchId == branchId)
                    .Select(x => new
                    {
                        x.EmpId,
                        x.EmpCode,
                        x.FullName,
                        x.Email,
                        x.DepartmentId,
                        x.Status
                    })
                    .ToListAsync();

                // DepartmentId supports CSV values such as "21,15,30"
                var result = employees
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.DepartmentId) &&
                        x.DepartmentId
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(id => id.Trim())
                            .Any(id =>
                                int.TryParse(id, out int parsedId) &&
                                parsedId == departmentId))
                    .Where(x =>
                        string.IsNullOrWhiteSpace(x.Status) ||
                        x.Status.Equals(
                            "Active",
                            StringComparison.OrdinalIgnoreCase) ||
                        x.Status.Equals(
                            "1",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(x => new
                    {
                        employeeId = x.EmpId,
                        employeeCode = x.EmpCode,
                        employeeName = x.FullName,
                        email = x.Email,
                        departmentId = departmentId,
                        branchId = branchId
                    })
                    .OrderBy(x => x.employeeName)
                    .ToList();

                return Ok(new
                {
                    organizationId,
                    departmentId,
                    departmentName = department.DepartmentName,
                    branchId,
                    branchName = branch.BranchName,
                    totalEmployees = result.Count,
                    employees = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new
                {
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Unable to fetch department employees.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("task-manager/permissions")]
        public async Task<IActionResult> GetTaskManagerPermissions(
    [FromQuery] int departmentId,
    [FromQuery] int branchId)
        {
            try
            {
                int organizationId = GetOrganizationId();

                if (departmentId <= 0 || branchId <= 0)
                {
                    return BadRequest(new
                    {
                        message = "Valid DepartmentId and BranchId are required."
                    });
                }

                // Check Task Management module
                bool moduleEnabled = await _commonContext.OrganizationModules
                    .AnyAsync(x =>
                        x.OrgId == organizationId &&
                        x.ModuleCode == "task_management" &&
                        x.IsActive);

                if (!moduleEnabled)
                {
                    return BadRequest(new
                    {
                        message = "Task Management is not enabled for this organization."
                    });
                }

                // Validate department
                var department = await _commonContext.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == departmentId &&
                        x.OrgId == organizationId);

                if (department == null)
                {
                    return NotFound(new
                    {
                        message = "Department not found."
                    });
                }

                // Validate branch
                var branch = await _commonContext.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == branchId &&
                        x.OrgId == organizationId);

                if (branch == null)
                {
                    return NotFound(new
                    {
                        message = "Branch not found."
                    });
                }

                // Get active permissions for this organization,
                // department and branch
                var permissions = await _taskManagerDbContext.TaskCreatorPermissions
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.DepartmentId == departmentId &&
                        x.BranchId == branchId &&
                        x.IsActive)
                    .Select(x => new
                    {
                        x.EmployeeId,
                        x.PermissionLevel
                    })
                    .ToListAsync();

                // Get employee details from the common database
                var employeeIds = permissions
                    .Select(x => x.EmployeeId)
                    .Distinct()
                    .ToList();

                var employees = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        employeeIds.Contains(x.EmpId))
                    .Select(x => new
                    {
                        employeeId = x.EmpId,
                        employeeCode = x.EmpCode,
                        employeeName = x.FullName,
                        email = x.Email
                    })
                    .ToListAsync();

                var result = permissions
                    .Join(
                        employees,
                        permission => permission.EmployeeId,
                        employee => employee.employeeId,
                        (permission, employee) => new
                        {
                            employeeId = employee.employeeId,
                            employeeCode = employee.employeeCode,
                            employeeName = employee.employeeName,
                            email = employee.email,
                            permissionLevel = permission.PermissionLevel
                        })
                    .OrderBy(x => x.permissionLevel)
                    .ThenBy(x => x.employeeName)
                    .ToList();

                return Ok(new
                {
                    organizationId,
                    departmentId,
                    departmentName = department.DepartmentName,
                    branchId,
                    branchName = branch.BranchName,
                    totalEmployees = result.Count,
                    employees = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new
                {
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Unable to fetch Task Manager permissions.",
                    error = ex.Message
                });
            }
        }

        // [Authorize]
        // [HttpPost("task-manager/permissions/save")]
        // public async Task<IActionResult> SaveTaskManagerPermissions(
        //[FromBody] TaskManagerPermissionSaveRequestDto request)
        // {
        //     using var transaction = await _taskManagerDbContext.Database
        //         .BeginTransactionAsync();

        //     try
        //     {
        //         int organizationId = GetOrganizationId();

        //         // 1. Validate request
        //         if (request == null ||
        //             request.DepartmentId <= 0 ||
        //             request.BranchId <= 0)
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Valid DepartmentId and BranchId are required."
        //             });
        //         }

        //         if (request.Employees == null)
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Employees list is required. Send an empty list to remove all permissions in this department and branch."
        //             });
        //         }

        //         // 2. Validate employee IDs and permission levels
        //         if (request.Employees.Any(x =>
        //             x.EmployeeId <= 0 || x.PermissionLevel <= 0))
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Invalid EmployeeId or PermissionLevel."
        //             });
        //         }

        //         // Same employee can have different levels,
        //         // but duplicate employee + level combinations are not allowed.
        //         if (request.Employees
        //             .GroupBy(x => new
        //             {
        //                 x.EmployeeId,
        //                 x.PermissionLevel
        //             })
        //             .Any(g => g.Count() > 1))
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Duplicate employee and permission level combinations are not allowed."
        //             });
        //         }

        //         // 3. Check Task Management module
        //         bool moduleEnabled = await _commonContext.OrganizationModules
        //             .AnyAsync(x =>
        //                 x.OrgId == organizationId &&
        //                 x.ModuleCode == "task_management" &&
        //                 x.IsActive);

        //         if (!moduleEnabled)
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Task Management is not enabled for this organization."
        //             });
        //         }

        //         // 4. Validate department
        //         var department = await _commonContext.Departments
        //             .AsNoTracking()
        //             .FirstOrDefaultAsync(x =>
        //                 x.Id == request.DepartmentId &&
        //                 x.OrgId == organizationId);

        //         if (department == null)
        //         {
        //             return NotFound(new
        //             {
        //                 message = "Department not found."
        //             });
        //         }

        //         // 5. Validate branch
        //         var branch = await _commonContext.Branches
        //             .AsNoTracking()
        //             .FirstOrDefaultAsync(x =>
        //                 x.BranchId == request.BranchId &&
        //                 x.OrgId == organizationId);

        //         if (branch == null)
        //         {
        //             return NotFound(new
        //             {
        //                 message = "Branch not found."
        //             });
        //         }

        //         // 6. Get configured approval levels
        //         var config = await _context.DepartmentApprovalConfigs
        //             .AsNoTracking()
        //             .Where(x =>
        //                 x.OrganizationId == organizationId &&
        //                 x.DepartmentId == request.DepartmentId &&
        //                 x.ModuleCode == "task_management" &&
        //                 x.IsActive)
        //             .Select(x => new
        //             {
        //                 x.TotalApprovalLevels
        //             })
        //             .FirstOrDefaultAsync();

        //         if (config == null || config.TotalApprovalLevels <= 0)
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Configure approval levels for this department first."
        //             });
        //         }

        //         // 7. Validate selected approval levels
        //         if (request.Employees.Any(x =>
        //             x.PermissionLevel > config.TotalApprovalLevels))
        //         {
        //             return BadRequest(new
        //             {
        //                 message = $"PermissionLevel must be between 1 and {config.TotalApprovalLevels}."
        //             });
        //         }

        //         // 8. Get selected employee IDs
        //         var employeeIds = request.Employees
        //             .Select(x => x.EmployeeId)
        //             .Distinct()
        //             .ToList();

        //         // 9. Validate selected employees
        //         var employees = await _commonContext.CompanyUsers
        //             .AsNoTracking()
        //             .Where(x =>
        //                 x.OrgId == organizationId &&
        //                 employeeIds.Contains(x.EmpId) &&
        //                 x.BranchId == request.BranchId)
        //             .Select(x => new
        //             {
        //                 x.EmpId,
        //                 x.DepartmentId,
        //                 x.Status
        //             })
        //             .ToListAsync();

        //         if (employees.Count != employeeIds.Count)
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "One or more employees do not belong to the selected organization or branch."
        //             });
        //         }

        //         var invalidEmployeeIds = employees
        //             .Where(x =>
        //                 string.IsNullOrWhiteSpace(x.DepartmentId) ||
        //                 !x.DepartmentId
        //                     .Split(',', StringSplitOptions.RemoveEmptyEntries)0000000000000
        //                     .Select(id => id.Trim())
        //                     .Any(id =>
        //                         int.TryParse(id, out int parsedId) &&
        //                         parsedId == request.DepartmentId) ||
        //                 (!string.IsNullOrWhiteSpace(x.Status) &&
        //                  !x.Status.Equals(
        //                      "Active",
        //                      StringComparison.OrdinalIgnoreCase) &&
        //                  !x.Status.Equals(
        //                      "1",
        //                      StringComparison.OrdinalIgnoreCase)))
        //             .Select(x => x.EmpId)
        //             .ToList();

        //         if (invalidEmployeeIds.Any())
        //         {
        //             return BadRequest(new
        //             {
        //                 message = "Some employees are inactive or do not belong to the selected department.",
        //                 employeeIds = invalidEmployeeIds
        //             });
        //         }

        //         // 10. Load existing active permissions for this exact scope.
        //         // Other organizations, departments and branches are not affected.
        //         var existingPermissions =
        //             await _taskManagerDbContext.TaskCreatorPermissions
        //                 .Where(x =>
        //                     x.OrganizationId == organizationId &&
        //                     x.DepartmentId == request.DepartmentId &&
        //                     x.BranchId == request.BranchId &&
        //                     x.IsActive)
        //                 .ToListAsync();

        //         var now = DateTime.UtcNow;

        //         // 11. Add or update selected employee + level combinations
        //         foreach (var selected in request.Employees)
        //         {
        //             var existing = existingPermissions.FirstOrDefault(x =>
        //                 x.EmployeeId == selected.EmployeeId &&
        //                 x.PermissionLevel == selected.PermissionLevel);

        //             if (existing != null)
        //             {
        //                 existing.IsActive = true;
        //                 existing.UpdatedAt = now;
        //             }
        //             else
        //             {
        //                 // If an inactive matching record exists, reactivate it.
        //                 var inactiveExisting =
        //                     await _taskManagerDbContext.TaskCreatorPermissions
        //                         .FirstOrDefaultAsync(x =>
        //                             x.OrganizationId == organizationId &&
        //                             x.DepartmentId == request.DepartmentId &&
        //                             x.BranchId == request.BranchId &&
        //                             x.EmployeeId == selected.EmployeeId &&
        //                             x.PermissionLevel == selected.PermissionLevel &&
        //                             !x.IsActive);

        //                 if (inactiveExisting != null)
        //                 {
        //                     inactiveExisting.IsActive = true;
        //                     inactiveExisting.UpdatedAt = now;
        //                 }
        //                 else
        //                 {
        //                     _taskManagerDbContext.TaskCreatorPermissions.Add(
        //                         new TaskCreatorPermission
        //                         {
        //                             OrganizationId = organizationId,
        //                             DepartmentId = request.DepartmentId,
        //                             BranchId = request.BranchId,
        //                             EmployeeId = selected.EmployeeId,
        //                             PermissionLevel = selected.PermissionLevel,
        //                             IsActive = true,
        //                             CreatedAt = now
        //                         });
        //                 }
        //             }
        //         }

        //         // 12. Deactivate permissions removed from the submitted selection.
        //         // Compare EmployeeId + PermissionLevel so the same employee
        //         // can retain another approval level.
        //         var selectedPairs = request.Employees
        //             .Select(x => new
        //             {
        //                 x.EmployeeId,
        //                 x.PermissionLevel
        //             })
        //             .ToList();

        //         foreach (var existing in existingPermissions)
        //         {
        //             bool stillSelected = selectedPairs.Any(x =>
        //                 x.EmployeeId == existing.EmployeeId &&
        //                 x.PermissionLevel == existing.PermissionLevel);

        //             if (!stillSelected)
        //             {
        //                 existing.IsActive = false;
        //                 existing.UpdatedAt = now;
        //             }
        //         }

        //         // 13. Save all changes
        //         await _taskManagerDbContext.SaveChangesAsync();
        //         await transaction.CommitAsync();

        //         return Ok(new
        //         {
        //             message = "Task Manager permissions saved successfully.",
        //             organizationId,
        //             departmentId = request.DepartmentId,
        //             branchId = request.BranchId,
        //             totalApprovalLevels = config.TotalApprovalLevels,
        //             savedPermissions = request.Employees.Count,
        //             activePermissions = request.Employees.Count,
        //             removedPermissions = existingPermissions.Count(x =>
        //                 !x.IsActive)
        //         });
        //     }
        //     catch (UnauthorizedAccessException ex)
        //     {
        //         await transaction.RollbackAsync();

        //         return Unauthorized(new
        //         {
        //             message = ex.Message
        //         });
        //     }
        //     catch (Exception ex)
        //     {
        //         await transaction.RollbackAsync();

        //         return StatusCode(500, new
        //         {
        //             message = "Unable to save Task Manager permissions.",
        //             error = ex.Message
        //         });
        //     }
        // }
        [Authorize]
        [HttpGet("approval-setup/download-excel")]
        public async Task<IActionResult> DownloadApprovalExcel(
            [FromQuery] string moduleCode,
            [FromQuery] int branchId,
            [FromQuery] int departmentId)
        {
            try
            {
                var orgClaim = User.FindFirst("orgId")?.Value
                            ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgClaim, out int organizationId))
                    return Unauthorized("OrganizationId missing in token.");

                if (string.IsNullOrWhiteSpace(moduleCode))
                    return BadRequest("ModuleCode is required.");

                var branch = await _commonContext.Branches
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == branchId &&
                        x.OrgId == organizationId);

                if (branch == null)
                    return BadRequest("Branch not found.");

                var department = await _commonContext.Departments
                    .FirstOrDefaultAsync(x =>
                        x.Id == departmentId &&
                        x.OrgId == organizationId);

                if (department == null)
                    return BadRequest("Department not found.");

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.ModuleCode == moduleCode &&
                            x.Branch == branch.BranchName &&
                            x.DepartmentId == departmentId &&
                            x.IsActive);

                if (departmentConfig == null)
                    return BadRequest("Department approval configuration not found.");

                int totalLevels = departmentConfig.TotalApprovalLevels;

                if (totalLevels <= 0)
                    return BadRequest("Approval levels are not configured.");

                // Load active employees for organization + branch
                var allEmployees = await _commonContext.CompanyUsers
                    .Where(x =>
                        x.OrgId == organizationId &&
                        x.BranchId == branchId &&
                        x.Status.ToUpper() == "ACTIVE")
                    .ToListAsync();

                // DepartmentId is CSV format: "11,12,13"
                var employees = allEmployees
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.DepartmentId) &&
                        x.DepartmentId
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(d => d.Trim())
                            .Contains(departmentId.ToString()))
                    .OrderBy(x => x.FullName)
                    .ToList();

                if (!employees.Any())
                    return BadRequest("No employees found in selected department.");

                var existingConfigs =
                    await _context.EmployeeApprovalConfigs
                        .Include(x => x.Approvers)
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.IsActive)
                        .ToListAsync();

                // All valid approvers from same organization and branch
                var approverEmployees = allEmployees
                    .Where(x => !string.IsNullOrWhiteSpace(x.EmpCode))
                    .ToList();

                var approverNames = approverEmployees
                    .Select(x => $"{x.FullName} ({x.EmpCode})")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                // Calculate number of existing approvers per level
                var columnsPerLevel = new Dictionary<int, int>();

                for (int level = 1; level <= totalLevels; level++)
                {
                    int maxApprovers = employees
                        .Select(employee =>
                            existingConfigs
                                .Where(x =>
                                    x.EmployeeId == employee.EmpId &&
                                    x.ApprovalLevel == level)
                                .SelectMany(x => x.Approvers)
                                .Count(a => a.IsActive))
                        .DefaultIfEmpty(0)
                        .Max();

                    columnsPerLevel[level] = Math.Max(1, maxApprovers);
                }

                using var workbook = new XLWorkbook();

                var ws = workbook.Worksheets.Add("Approval Configuration");

                int totalColumns = 1 + columnsPerLevel.Values.Sum();

                // Title
                ws.Range(1, 1, 1, totalColumns).Merge();

                ws.Cell(1, 1).Value = "EMPLOYEE APPROVAL CONFIGURATION";

                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 16;
                ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(1, 1).Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#17365D");

                ws.Row(1).Height = 30;

                // Metadata
                ws.Cell(3, 1).Value = "Module";
                ws.Cell(3, 2).Value = moduleCode;

                ws.Cell(4, 1).Value = "Branch";
                ws.Cell(4, 2).Value = branch.BranchName;

                ws.Cell(5, 1).Value = "Department";
                ws.Cell(5, 2).Value = department.DepartmentName;

                ws.Cell(6, 1).Value = "Approval Levels";
                ws.Cell(6, 2).Value = totalLevels;

                ws.Range("A3:A6").Style.Font.Bold = true;
                ws.Range("A3:A6").Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#D9EAF7");

                // Instruction
                ws.Range(7, 1, 7, totalColumns).Merge();

                ws.Cell(7, 1).Value =
                    "NOTE: Select approver from dropdown or manually enter " +
                    "Full Name (EmployeeCode), e.g. Rajesh (EMP-004). " +
                    "For multiple approvers, add another column and use " +
                    "the same Level header, e.g. Level 1.";

                ws.Cell(7, 1).Style.Font.Italic = true;
                ws.Cell(7, 1).Style.Font.FontColor = XLColor.Blue;
                ws.Cell(7, 1).Style.Alignment.WrapText = true;

                // Headers
                int column = 1;

                ws.Cell(8, column++).Value = "Employee";

                var levelColumns = new Dictionary<int, List<int>>();

                for (int level = 1; level <= totalLevels; level++)
                {
                    levelColumns[level] = new List<int>();

                    for (int i = 0; i < columnsPerLevel[level]; i++)
                    {
                        ws.Cell(8, column).Value = $"Level {level}";

                        levelColumns[level].Add(column);

                        column++;
                    }
                }

                // Header styling
                ws.Range(8, 1, 8, totalColumns).Style.Font.Bold = true;
                ws.Range(8, 1, 8, totalColumns).Style.Font.FontColor =
                    XLColor.White;

                ws.Range(8, 1, 8, totalColumns).Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#17365D");

                // Employees and existing approvers
                int row = 9;

                foreach (var employee in employees)
                {
                    ws.Cell(row, 1).Value =
                        $"{employee.FullName} ({employee.EmpCode})";

                    for (int level = 1; level <= totalLevels; level++)
                    {
                        var config = existingConfigs.FirstOrDefault(x =>
                            x.EmployeeId == employee.EmpId &&
                            x.ApprovalLevel == level &&
                            x.IsActive);

                        if (config == null)
                            continue;

                        var approvers = config.Approvers
                            .Where(x => x.IsActive)
                            .OrderBy(x => x.ApprovalOrder)
                            .ToList();

                        for (int i = 0; i < approvers.Count; i++)
                        {
                            var approver = approverEmployees.FirstOrDefault(x =>
                                x.EmpId == approvers[i].ApproverEmployeeId);

                            if (approver == null)
                                continue;

                            if (i < levelColumns[level].Count)
                            {
                                int approverColumn = levelColumns[level][i];

                                ws.Cell(row, approverColumn).Value =
                                    $"{approver.FullName} ({approver.EmpCode})";
                            }
                        }
                    }

                    row++;
                }

                int lastEmployeeRow = row - 1;

                // Dropdown worksheet
                var listSheet = workbook.Worksheets.Add("ApproverList");

                for (int i = 0; i < approverNames.Count; i++)
                {
                    listSheet.Cell(i + 1, 1).Value = approverNames[i];
                }

                if (approverNames.Count > 0)
                {
                    workbook.NamedRanges.Add(
                        "ApproverList",
                        listSheet.Range(1, 1, approverNames.Count, 1));
                }

                listSheet.Hide();

                // --------------------------------------------------
                // DROPDOWN VALIDATION FOR EXISTING + FUTURE COLUMNS
                // B:AZ columns, rows 9:1000
                // --------------------------------------------------

                int firstApproverColumn = 2;
                int lastApproverColumn = 52; // AZ
                int firstDataRow = 9;
                int lastValidationRow = 1000;

                if (approverNames.Count > 0)
                {
                    for (int col = firstApproverColumn;
                         col <= lastApproverColumn;
                         col++)
                    {
                        var validationRange = ws.Range(
                            firstDataRow,
                            col,
                            lastValidationRow,
                            col);

                        var validation =
                            validationRange.CreateDataValidation();

                        validation.List("=ApproverList");

                        validation.IgnoreBlanks = true;
                        validation.InCellDropdown = true;

                        // Manual typing is permitted
                        validation.ShowErrorMessage = false;
                    }
                }

                // Formatting
                ws.Column(1).Width = 35;

                for (int col = 2; col <= lastApproverColumn; col++)
                {
                    ws.Column(col).Width = 30;
                }

                ws.Range(9, 1, lastEmployeeRow, totalColumns)
                    .Style.Alignment.Vertical =
                    XLAlignmentVerticalValues.Center;

                ws.Range(9, 1, lastEmployeeRow, totalColumns)
                    .Style.Alignment.WrapText = true;

                ws.Range(8, 1, lastEmployeeRow, totalColumns)
                    .SetAutoFilter();

                ws.SheetView.FreezeRows(8);

                // No worksheet protection
                // Extra columns, rows, header editing and manual input allowed.

                using var stream = new MemoryStream();

                workbook.SaveAs(stream);

                stream.Position = 0;

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Approval_Config_{moduleCode}_{departmentId}.xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error generating approval Excel.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpPost("approval-setup/upload-excel")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadApprovalExcel(
     [FromQuery] string moduleCode,
     [FromQuery] int branchId,
     [FromQuery] int departmentId,
     IFormFile file)
        {
            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // ==========================================
                // 1. ORGANIZATION ID FROM JWT
                // ==========================================

                var organizationClaim =
                    User.FindFirst("orgId")?.Value ??
                    User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(organizationClaim, out int organizationId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 2. VALIDATE EXCEL FILE
                // ==========================================

                if (file == null || file.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Please upload Excel file."
                    });
                }

                if (!Path.GetExtension(file.FileName)
                    .Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Only .xlsx files are allowed."
                    });
                }

                // ==========================================
                // 3. GET DEPARTMENT APPROVAL CONFIGURATION
                // ==========================================

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.ModuleCode == moduleCode &&
                            x.DepartmentId == departmentId &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Department approval configuration not found."
                    });
                }

                if (departmentConfig.TotalApprovalLevels <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval levels are not configured."
                    });
                }

                // ==========================================
                // 4. VALIDATE BRANCH
                // ==========================================

                var branch = await _commonContext.Branches
                    .FirstOrDefaultAsync(x =>
                        x.BranchId == branchId &&
                        x.OrgId == organizationId);

                if (branch == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid branch."
                    });
                }

                if (!string.Equals(
                        departmentConfig.Branch?.Trim(),
                        branch.BranchName?.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Branch does not match department approval configuration."
                    });
                }

                // ==========================================
                // 5. GET ACTIVE EMPLOYEES FROM COMMON DB
                // ==========================================

                var allEmployees = await _commonContext.CompanyUsers
                    .Where(x =>
                        x.OrgId == organizationId &&
                        x.BranchId == branchId &&
                        x.Status == "ACTIVE")
                    .ToListAsync();

                // DepartmentId is CSV string:
                // Example: "11,12,15"

                var departmentEmployees = allEmployees
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.DepartmentId) &&
                        x.DepartmentId
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(d => d.Trim())
                            .Contains(departmentId.ToString()))
                    .ToList();

                if (!departmentEmployees.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No active employees found in selected department."
                    });
                }

                // ==========================================
                // 6. EMPLOYEE DICTIONARY
                // EmpCode is STRING
                // ==========================================

                var employeeDictionary = departmentEmployees
                    .Where(x => !string.IsNullOrWhiteSpace(x.EmpCode))
                    .GroupBy(x => x.EmpCode.Trim().ToUpper())
                    .ToDictionary(
                        group => group.Key,
                        group => group.First()
                    );

                // ==========================================
                // 7. READ EXCEL
                // ==========================================

                using var memoryStream = new MemoryStream();

                await file.CopyToAsync(memoryStream);

                memoryStream.Position = 0;

                using var workbook = new XLWorkbook(memoryStream);

                var worksheet = workbook.Worksheets.FirstOrDefault();

                if (worksheet == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Excel worksheet not found."
                    });
                }

                // Excel format:
                // Header row = 8
                // Employee data starts = 9

                const int headerRow = 8;
                const int dataStartRow = 9;

                int lastColumn =
                    worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

                int lastRow =
                    worksheet.LastRowUsed()?.RowNumber() ?? 0;

                if (lastColumn < 2 || lastRow < dataStartRow)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Excel data is empty or invalid."
                    });
                }

                // ==========================================
                // 8. VALIDATE EMPLOYEE COLUMN
                // ==========================================

                string employeeHeader =
                    worksheet.Cell(headerRow, 1).GetString().Trim();

                if (!employeeHeader.Equals(
                        "Employee",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Excel column A header must be Employee."
                    });
                }

                // ==========================================
                // 9. FIND LEVEL COLUMNS
                // Supports:
                // Level 1 | Level 1 | Level 2 | Level 3
                // ==========================================

                var levelColumns = new List<(int Column, int Level)>();

                for (int col = 2; col <= lastColumn; col++)
                {
                    string header =
                        worksheet.Cell(headerRow, col).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(header))
                        continue;

                    // Manual parsing instead of Regex
                    string lowerHeader = header.ToLower();

                    int levelPosition = lowerHeader.IndexOf("level");

                    if (levelPosition < 0)
                        continue;

                    string remaining =
                        lowerHeader.Substring(levelPosition + 5).Trim();

                    string levelNumber = "";

                    foreach (char character in remaining)
                    {
                        if (char.IsDigit(character))
                        {
                            levelNumber += character;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (!int.TryParse(levelNumber, out int level))
                        continue;

                    if (level < 1 ||
                        level > departmentConfig.TotalApprovalLevels)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Invalid approval level '{header}'."
                        });
                    }

                    levelColumns.Add((col, level));
                }

                if (!levelColumns.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No approval level columns found."
                    });
                }

                // ==========================================
                // 10. PREPARE APPROVAL DATA
                // EmployeeId -> Level -> Approver EmployeeIds
                // ==========================================

                var approvalData =
                    new Dictionary<int, Dictionary<int, HashSet<int>>>();

                var validationErrors = new List<string>();

                var processedEmployees = new HashSet<int>();

                // ==========================================
                // 11. READ EMPLOYEE ROWS
                // ==========================================

                for (int row = dataStartRow; row <= lastRow; row++)
                {
                    string employeeCell =
                        worksheet.Cell(row, 1).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(employeeCell))
                        continue;

                    // Extract EmpCode from:
                    // Employee Name (EMP001)

                    string employeeCode = string.Empty;

                    int employeeOpenBracket = employeeCell.LastIndexOf('(');
                    int employeeCloseBracket = employeeCell.LastIndexOf(')');

                    if (employeeOpenBracket >= 0 &&
                        employeeCloseBracket > employeeOpenBracket)
                    {
                        employeeCode = employeeCell
                            .Substring(
                                employeeOpenBracket + 1,
                                employeeCloseBracket - employeeOpenBracket - 1)
                            .Trim();
                    }

                    if (string.IsNullOrWhiteSpace(employeeCode))
                    {
                        validationErrors.Add(
                            $"Row {row}: Invalid employee format '{employeeCell}'. Expected Name (EmpCode).");

                        continue;
                    }

                    string employeeKey =
                        employeeCode.Trim().ToUpper();

                    // ======================================
                    // 12. VALIDATE EMPLOYEE
                    // ======================================

                    if (!employeeDictionary.TryGetValue(
                            employeeKey,
                            out var employee))
                    {
                        validationErrors.Add(
                            $"Row {row}: Employee '{employeeCell}' not found in selected Organization / Branch / Department.");

                        continue;
                    }

                    if (!processedEmployees.Add(employee.EmpId))
                    {
                        validationErrors.Add(
                            $"Row {row}: Duplicate employee '{employeeCell}'.");

                        continue;
                    }

                    if (!approvalData.ContainsKey(employee.EmpId))
                    {
                        approvalData[employee.EmpId] =
                            new Dictionary<int, HashSet<int>>();
                    }

                    // ======================================
                    // 13. VALIDATE APPROVERS
                    // ======================================

                    foreach (var levelColumn in levelColumns)
                    {
                        int level = levelColumn.Level;

                        string approverCell =
                            worksheet.Cell(row, levelColumn.Column)
                                .GetString()
                                .Trim();

                        // Blank approver allowed
                        if (string.IsNullOrWhiteSpace(approverCell))
                            continue;

                        string approverCode = string.Empty;

                        int approverOpenBracket = approverCell.LastIndexOf('(');
                        int approverCloseBracket = approverCell.LastIndexOf(')');

                        if (approverOpenBracket >= 0 &&
                            approverCloseBracket > approverOpenBracket)
                        {
                            approverCode = approverCell
                                .Substring(
                                    approverOpenBracket + 1,
                                    approverCloseBracket - approverOpenBracket - 1)
                                .Trim();
                        }

                        if (string.IsNullOrWhiteSpace(approverCode))
                        {
                            validationErrors.Add(
                                $"Row {row}, Level {level}: Invalid approver format '{approverCell}'.");

                            continue;
                        }

                        string approverKey =
                            approverCode.Trim().ToUpper();

                        // ==================================
                        // 14. APPROVER MUST EXIST IN DEPARTMENT
                        // ==================================

                        if (!employeeDictionary.TryGetValue(
                                approverKey,
                                out var approver))
                        {
                            validationErrors.Add(
                                $"Row {row}, Level {level}: Approver '{approverCell}' is not an active employee in the selected department.");

                            continue;
                        }

                        // ==================================
                        // 15. VALIDATE FULL NAME + CODE
                        // ==================================

                        string expectedApproverName =
                            $"{approver.FullName.Trim()} ({approver.EmpCode.Trim()})";

                        if (!string.Equals(
                                approverCell.Trim(),
                                expectedApproverName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            validationErrors.Add(
                                $"Row {row}, Level {level}: Name mismatch. Expected '{expectedApproverName}'.");

                            continue;
                        }

                        // ==================================
                        // 16. SELF APPROVAL NOT ALLOWED
                        // ==================================

                        if (employee.EmpId == approver.EmpId)
                        {
                            validationErrors.Add(
                                $"Row {row}, Level {level}: Employee cannot be his own approver.");

                            continue;
                        }

                        // ==================================
                        // 17. MULTIPLE APPROVERS PER LEVEL
                        // ==================================

                        if (!approvalData[employee.EmpId]
                            .ContainsKey(level))
                        {
                            approvalData[employee.EmpId][level] =
                                new HashSet<int>();
                        }

                        approvalData[employee.EmpId][level]
                            .Add(approver.EmpId);
                    }
                }

                // ==========================================
                // 18. VALIDATION FAILED - DO NOT SAVE
                // ==========================================

                if (validationErrors.Any())
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Excel validation failed. No data was saved.",
                        errors = validationErrors
                    });
                }

                if (!approvalData.Any())
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "No valid approval configuration found."
                    });
                }

                // ==========================================
                // 19. DEACTIVATE OLD APPROVAL CONFIGURATION
                // ==========================================

                var existingConfigs =
                    await _context.EmployeeApprovalConfigs
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.IsActive)
                        .ToListAsync();

                foreach (var existingConfig in existingConfigs)
                {
                    existingConfig.IsActive = false;
                    existingConfig.UpdatedDate = DateTime.UtcNow;

                    var existingApprovers =
                        await _context.EmployeeApprovalConfigApprovers
                            .Where(x =>
                                x.EmployeeApprovalConfigId == existingConfig.Id &&
                                x.IsActive)
                            .ToListAsync();

                    foreach (var existingApprover in existingApprovers)
                    {
                        existingApprover.IsActive = false;
                        existingApprover.UpdatedDate = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();

                // ==========================================
                // 20. INSERT NEW APPROVAL CONFIGURATION
                // ==========================================

                foreach (var employeeData in approvalData)
                {
                    int employeeId = employeeData.Key;

                    foreach (var levelData in employeeData.Value)
                    {
                        int approvalLevel = levelData.Key;

                        var approverIds =
                            levelData.Value.ToList();

                        if (!approverIds.Any())
                            continue;

                        var employeeApprovalConfig =
                            new EmployeeApprovalConfig
                            {
                                OrganizationId = organizationId,

                                DepartmentApprovalConfigId =
                                    departmentConfig.Id,

                                EmployeeId = employeeId,

                                ApprovalLevel = approvalLevel,

                                ApprovalRule = "AnyOne",

                                IsActive = true,

                                CreatedDate = DateTime.UtcNow
                            };

                        _context.EmployeeApprovalConfigs.Add(
                            employeeApprovalConfig);

                        await _context.SaveChangesAsync();

                        int approvalOrder = 1;

                        foreach (int approverId in approverIds)
                        {
                            var approverConfig =
                                new EmployeeApprovalConfigApprover
                                {
                                    EmployeeApprovalConfigId =
                                        employeeApprovalConfig.Id,

                                    ApproverEmployeeId = approverId,

                                    ApprovalOrder = approvalOrder++,

                                    IsActive = true,

                                    CreatedDate = DateTime.UtcNow
                                };

                            _context.EmployeeApprovalConfigApprovers.Add(
                                approverConfig);
                        }

                        await _context.SaveChangesAsync();
                    }
                }

                // ==========================================
                // 21. COMMIT
                // ==========================================

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Approval configuration uploaded successfully.",

                    organizationId,
                    moduleCode,
                    branchId,                                                 
                    departmentId,

                    configuredEmployees = approvalData.Count,

                    configuredLevels =
                        departmentConfig.TotalApprovalLevels
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error uploading approval Excel.",
                    error = ex.Message
                });
            }
        }
        //---------------Expense Tracker  Module..------------------------------//



        //--- This is add Amount popup Api ------

        [HttpGet("GetEmployees")]
        public async Task<IActionResult> GetEmployees()
        {
            try
            {
                var organizationId = GetOrganizationId();

                var employees = await _commonContext.CompanyUsers
                    .Where(emp =>
                        emp.OrgId == organizationId)
                    .Select(emp => new EmployeemasterDto
                    {
                        EmpId = emp.EmpId,
                        EmpCode = emp.EmpCode,
                        FullName = emp.FullName
                    })
                    .ToListAsync();

                return Ok(employees);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new
                {
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error while fetching employees.",
                    error = ex.Message
                });
            }
        }
        private bool IsAdmin()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value == "Admin";
        }

        //------For Department base Filters -----------------



        [Authorize]
        [HttpGet("advance-summary/departments")]
        public async Task<IActionResult> GetAdvanceSummaryDepartments()
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
                // ==========================================
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in login token."
                    });
                }

                // ==========================================
                // 2. GET ACTIVE EMPLOYEES
                //    DepartmentId is stored as STRING
                // ==========================================
                var employeeRows = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(e =>
                        e.OrgId == organizationId &&
                        (e.Status == null ||
                         e.Status.Trim().ToUpper() == "ACTIVE"))
                    .Select(e => new
                    {
                        employeeId = e.EmpId,
                        employeeCode = e.EmpCode,
                        employeeName = e.FullName,
                        departmentId = e.DepartmentId
                    })
                    .ToListAsync();

                // ==========================================
                // 3. GET ALL DEPARTMENTS FOR THIS ORGANIZATION
                //    Employee assignment is NOT required
                // ==========================================
                var departmentRows = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(d => d.OrgId == organizationId)
                    .Select(d => new
                    {
                        d.Id,
                        d.DepartmentName
                    })
                    .ToListAsync();

                // ==========================================
                // 4. MAP EMPLOYEES TO DEPARTMENT NAMES
                //    Supports "9" and "9,11"
                // ==========================================
                var employees = employeeRows
                    .Select(e =>
                    {
                        var employeeDepartmentIds =
                            (e.departmentId ?? string.Empty)
                            .Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries)
                            .Where(id => int.TryParse(id, out _))
                            .Select(int.Parse)
                            .Distinct()
                            .ToList();

                        var matchedDepartments = departmentRows
                            .Where(d => employeeDepartmentIds.Contains(d.Id))
                            .ToList();

                        return new
                        {
                            e.employeeId,
                            e.employeeCode,
                            e.employeeName,

                            // Original DepartmentId string
                            departmentId = e.departmentId,

                            // Department names assigned to this employee
                            departmentName = matchedDepartments.Count > 0
                                ? string.Join(
                                    ", ",
                                    matchedDepartments.Select(d => d.DepartmentName))
                                : (string?)null,

                            // Internal employee department details
                            DepartmentNames = matchedDepartments
                                .Select(d => new
                                {
                                    departmentId = d.Id,
                                    departmentName = d.DepartmentName
                                })
                                .ToList()
                        };
                    })
                    .OrderBy(e => e.employeeName)
                    .ToList();

                // ==========================================
                // 5. RETURN ALL DEPARTMENTS FOR THIS ORG
                //    Do NOT filter using employee assignments
                // ==========================================
                var departments = departmentRows
                    .Select(d => new
                    {
                        departmentId = d.Id,
                        departmentName = d.DepartmentName
                    })
                    .OrderBy(d => d.departmentName)
                    .ToList();

                // ==========================================
                // 6. EMPLOYEE RESPONSE
                // ==========================================
                var employeeResponse = employees
                    .Select(e => new
                    {
                        e.employeeId,
                        e.employeeCode,
                        e.employeeName,
                        e.departmentId,
                        e.departmentName
                    })
                    .ToList();

                // ==========================================
                // 7. RETURN RESPONSE
                // ==========================================
                return Ok(new
                {
                    success = true,
                    organizationId,
                    departments,
                    employees = employeeResponse
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to load departments and employees.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpGet("advance-summary/employees")]
        public async Task<IActionResult> GetAdvanceSummaryEmployees(
    [FromQuery] string? departmentId)
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
                // ==========================================
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in login token."
                    });
                }

                // ==========================================
                // 2. COMPANY EXPENSE ROLE VALIDATION
                // ==========================================
                string role = GetCompanyExpenseRole();

                if (!new[] { "Admin", "SuperAdmin", "Super Admin" }
                    .Contains(role, StringComparer.OrdinalIgnoreCase))
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message = "You do not have permission to access employee details."
                    });
                }

                // ==========================================
                // 3. VALIDATE DEPARTMENT ID
                // departmentId is string
                // ==========================================
                int parsedDepartmentId = 0;

                if (!string.IsNullOrWhiteSpace(departmentId))
                {
                    if (!int.TryParse(departmentId, out parsedDepartmentId) ||
                        parsedDepartmentId <= 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Invalid departmentId."
                        });
                    }
                }

                // ==========================================
                // 4. VALIDATE DEPARTMENT BELONGS TO ORG
                // ==========================================
                if (!string.IsNullOrWhiteSpace(departmentId))
                {
                    bool departmentExists = await _commonContext.Departments
                        .AsNoTracking()
                        .AnyAsync(d =>
                            d.Id == parsedDepartmentId &&
                            d.OrgId == organizationId);

                    if (!departmentExists)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Department not found in this organization."
                        });
                    }
                }

                // ==========================================
                // 5. GET ACTIVE EMPLOYEES FOR ORGANIZATION
                // DepartmentId is stored as STRING
                // Supports "9" and "9,11"
                // ==========================================
                var employeeRows = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(e =>
                        e.OrgId == organizationId &&
                        (e.Status == null ||
                         e.Status.Trim().ToUpper() == "ACTIVE"))
                    .Select(e => new
                    {
                        employeeId = e.EmpId,
                        employeeCode = e.EmpCode,
                        employeeName = e.FullName,
                        departmentId = e.DepartmentId
                    })
                    .ToListAsync();

                // ==========================================
                // 6. GET DEPARTMENTS FOR THIS ORGANIZATION
                // ==========================================
                var departmentRows = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(d => d.OrgId == organizationId)
                    .Select(d => new
                    {
                        departmentId = d.Id,
                        departmentName = d.DepartmentName
                    })
                    .ToListAsync();

                // ==========================================
                // 7. MAP AND FILTER EMPLOYEES
                // ==========================================
                var employees = employeeRows
                    .Select(e =>
                    {
                        // Convert employee DepartmentId string into integer IDs.
                        // Example: "9" => [9]
                        // Example: "9,11" => [9, 11]
                        var employeeDepartmentIds =
                            (e.departmentId ?? string.Empty)
                            .Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries)
                            .Where(id => int.TryParse(id, out _))
                            .Select(int.Parse)
                            .Distinct()
                            .ToList();

                        // Match department names within the same organization.
                        var matchedDepartments = departmentRows
                            .Where(d =>
                                employeeDepartmentIds.Contains(d.departmentId))
                            .ToList();

                        return new
                        {
                            e.employeeId,
                            e.employeeCode,
                            e.employeeName,

                            // Original DepartmentId string
                            departmentId = e.departmentId,

                            // Matched department names
                            departmentName = matchedDepartments.Count > 0
                                ? string.Join(
                                    ", ",
                                    matchedDepartments.Select(
                                        d => d.departmentName))
                                : (string?)null,

                            // Internal use for filtering
                            DepartmentIds = employeeDepartmentIds
                        };
                    })
                    .Where(e =>
                        string.IsNullOrWhiteSpace(departmentId) ||
                        e.DepartmentIds.Contains(parsedDepartmentId))
                    .OrderBy(e => e.employeeName)
                    .Select(e => new
                    {
                        e.employeeId,
                        e.employeeCode,
                        e.employeeName,
                        e.departmentId,
                        e.departmentName
                    })
                    .ToList();

                // ==========================================
                // 8. RETURN RESPONSE
                // ==========================================
                return Ok(new
                {
                    success = true,
                    organizationId,
                    departmentId,
                    totalEmployees = employees.Count,
                    data = employees
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch employees.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }



  

        [Authorize]
        [HttpGet("summary-filtered")]
        public async Task<IActionResult> GetFilteredExpenseSummary(
            [FromQuery] int? departmentId = null,
            [FromQuery] int? employeeId = null,
            [FromQuery] int? month = null,
            [FromQuery] int? year = null,
            [FromQuery] int? week = null)
        {
            try
            {
                // 1. Get OrganizationId from JWT
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization."
                    });
                }

                // 2. Role validation
                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
                    !role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) &&
                    !role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }

                // 3. Validate filters
                if (departmentId.HasValue && departmentId.Value <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid departmentId."
                    });
                }

                if (employeeId.HasValue && employeeId.Value <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid employeeId."
                    });
                }

                if (month.HasValue && (month.Value < 1 || month.Value > 12))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Month must be between 1 and 12."
                    });
                }

                if (year.HasValue && (year.Value < 1 || year.Value > 9999))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid year."
                    });
                }

                if (week.HasValue && (week.Value < 1 || week.Value > 5))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Week must be between 1 and 5."
                    });
                }

                if (week.HasValue && !month.HasValue)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Month is required when filtering by week."
                    });
                }

                // 4. Selected month/year
                var now = DateTime.Now;

                int selectedMonth = month ?? now.Month;
                int selectedYear = year ?? now.Year;

                var monthStart = new DateTime(selectedYear, selectedMonth, 1);
                var nextMonthStart = monthStart.AddMonths(1);

                var yearStart = new DateTime(selectedYear, 1, 1);
                var nextYearStart = yearStart.AddYears(1);

                // 5. Get employees using department ID / employee ID
                List<int>? employeeIds = null;

                if (departmentId.HasValue || employeeId.HasValue)
                {
                    var employeeQuery = _commonContext.CompanyUsers
                        .AsNoTracking()
                        .Where(x =>
                            x.OrgId == organizationId &&
                            (x.Status == null ||
                             x.Status.Trim().ToUpper() == "ACTIVE"));

                    if (employeeId.HasValue)
                    {
                        employeeQuery = employeeQuery
                            .Where(x => x.EmpId == employeeId.Value);
                    }

                    var employeeRows = await employeeQuery
                        .Select(x => new
                        {
                            x.EmpId,
                            x.DepartmentId
                        })
                        .ToListAsync();

                    // DepartmentId may contain "9" or "9,11".
                    employeeIds = employeeRows
                        .Where(x =>
                            !departmentId.HasValue ||
                            (x.DepartmentId ?? string.Empty)
                                .Split(
                                    ',',
                                    StringSplitOptions.RemoveEmptyEntries |
                                    StringSplitOptions.TrimEntries)
                                .Any(id =>
                                    int.TryParse(id, out int parsedId) &&
                                    parsedId == departmentId.Value))
                        .Select(x => x.EmpId)
                        .Distinct()
                        .ToList();

                    if (employeeIds.Count == 0)
                    {
                        return NotFound(new
                        {
                            success = false,
                            message = "No employees found for the selected filters."
                        });
                    }
                }

                // 6. Advance query
                var advanceQuery = _context.ExpenseMaster
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.Status == "Active" &&
                        x.AssignedTo.HasValue);

                if (employeeIds != null)
                {
                    advanceQuery = advanceQuery
                        .Where(x => employeeIds.Contains(x.AssignedTo!.Value));
                }

                // 7. Spent query
                var spentQuery = _context.SpentAmounts
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive &&
                        x.employee_id.HasValue);

                if (employeeIds != null)
                {
                    spentQuery = spentQuery
                        .Where(x => employeeIds.Contains(x.employee_id!.Value));
                }

                // 8. Filtered advance/spent totals
                // These totals follow all selected filters.
                var filteredAdvanceQuery = advanceQuery;
                var filteredSpentQuery = spentQuery;

                if (month.HasValue)
                {
                    filteredAdvanceQuery = filteredAdvanceQuery.Where(x =>
                        x.CreatedDate >= monthStart &&
                        x.CreatedDate < nextMonthStart);

                    filteredSpentQuery = filteredSpentQuery.Where(x =>
                        x.Date >= monthStart &&
                        x.Date < nextMonthStart);
                }

                if (year.HasValue)
                {
                    filteredAdvanceQuery = filteredAdvanceQuery.Where(x =>
                        x.CreatedDate >= yearStart &&
                        x.CreatedDate < nextYearStart);

                    filteredSpentQuery = filteredSpentQuery.Where(x =>
                        x.Date >= yearStart &&
                        x.Date < nextYearStart);
                }

                // 9. Week range
                DateTime? weekStart = null;
                DateTime? weekEndExclusive = null;

                if (week.HasValue)
                {
                    weekStart = monthStart.AddDays((week.Value - 1) * 7);

                    if (weekStart.Value >= nextMonthStart)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "The selected week does not exist in this month."
                        });
                    }

                    DateTime calculatedWeekEnd = weekStart.Value.AddDays(7);

                    weekEndExclusive = calculatedWeekEnd < nextMonthStart
                        ? calculatedWeekEnd
                        : nextMonthStart;
                }

                // 10. Filtered totals
                // When week is supplied, totalAdvance/totalSpent also reflect that week.
                if (weekStart.HasValue && weekEndExclusive.HasValue)
                {
                    filteredAdvanceQuery = filteredAdvanceQuery.Where(x =>
                        x.CreatedDate >= weekStart.Value &&
                        x.CreatedDate < weekEndExclusive.Value);

                    filteredSpentQuery = filteredSpentQuery.Where(x =>
                        x.Date >= weekStart.Value &&
                        x.Date < weekEndExclusive.Value);
                }

                decimal totalAdvance = await filteredAdvanceQuery
                    .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                decimal totalSpent = await filteredSpentQuery
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                // 11. Monthly totals
                var monthAdvanceQuery = advanceQuery.Where(x =>
                    x.CreatedDate >= monthStart &&
                    x.CreatedDate < nextMonthStart);

                var monthSpentQuery = spentQuery.Where(x =>
                    x.Date >= monthStart &&
                    x.Date < nextMonthStart);

                decimal monthAdvance = await monthAdvanceQuery
                    .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                decimal monthSpent = await monthSpentQuery
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                // 12. Yearly totals
                var yearAdvanceQuery = advanceQuery.Where(x =>
                    x.CreatedDate >= yearStart &&
                    x.CreatedDate < nextYearStart);

                var yearSpentQuery = spentQuery.Where(x =>
                    x.Date >= yearStart &&
                    x.Date < nextYearStart);

                decimal yearAdvance = await yearAdvanceQuery
                    .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                decimal yearSpent = await yearSpentQuery
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                // 13. Weekly totals
                decimal weekAdvance = 0m;
                decimal weekSpent = 0m;

                if (weekStart.HasValue && weekEndExclusive.HasValue)
                {
                    weekAdvance = await advanceQuery
                        .Where(x =>
                            x.CreatedDate >= weekStart.Value &&
                            x.CreatedDate < weekEndExclusive.Value)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                    weekSpent = await spentQuery
                        .Where(x =>
                            x.Date >= weekStart.Value &&
                            x.Date < weekEndExclusive.Value)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                }

                // 14. Advance details
                var advanceRecords = await advanceQuery
                    .OrderByDescending(x => x.CreatedDate)
                    .Select(x => new
                    {
                        x.ExpenseId,
                        x.AssignedBy,
                        x.AssignedTo,
                        x.ExpenseAmount,
                        x.Purpose,
                        x.Remarks,
                        x.CreatedDate
                    })
                    .ToListAsync();

                // 15. Spent details
                var spentRecords = await spentQuery
                    .OrderByDescending(x => x.Date)
                    .ThenByDescending(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.employee_id,
                        x.PurchaseItem,
                        x.Date,
                        x.Amount,
                        x.Remarks,
                        x.BillFile
                    })
                    .ToListAsync();

                // 16. Related employee IDs
                var relatedEmployeeIds = advanceRecords
                    .SelectMany(x => new int?[]
                    {
                x.AssignedTo,
                x.AssignedBy
                    })
                    .Concat(spentRecords.Select(x => x.employee_id))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .ToList();

                // 17. Employee lookup
                var employeeLookup = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        relatedEmployeeIds.Contains(x.EmpId))
                    .ToDictionaryAsync(x => x.EmpId, x => x);

                // 18. Department name lookup
                var departmentIds = employeeLookup.Values
                    .SelectMany(x =>
                        (x.DepartmentId ?? string.Empty)
                            .Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries))
                    .Select(id =>
                        int.TryParse(id, out int parsedId) ? parsedId : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                var departmentLookup = await _commonContext.Departments
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        departmentIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.DepartmentName);

                string? GetDepartmentNames(string? departmentIdValue)
                {
                    if (string.IsNullOrWhiteSpace(departmentIdValue))
                        return null;

                    var names = departmentIdValue
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Select(id =>
                            int.TryParse(id, out int parsedId) &&
                            departmentLookup.TryGetValue(parsedId, out var name)
                                ? name
                                : null)
                        .Where(name => !string.IsNullOrWhiteSpace(name));

                    return string.Join(", ", names);
                }

                // 19. Purchase type lookup used only for spent detail names.
                // No purchaseTypes array is returned.
                var purchaseTypes = await _context.PurchaseTypes
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId)
                    .ToDictionaryAsync(x => x.Id, x => x.Name);

                // 20. Advance detail projection
                var advanceDetails = advanceRecords.Select(x =>
                {
                    employeeLookup.TryGetValue(
                        x.AssignedTo ?? 0,
                        out var assignedEmployee);

                    employeeLookup.TryGetValue(
                        x.AssignedBy ?? 0,
                        out var assignedByEmployee);

                    return new
                    {
                        expenseId = x.ExpenseId,
                        date = x.CreatedDate,

                        employeeId = x.AssignedTo,
                        employeeCode = assignedEmployee?.EmpCode,
                        employeeName = assignedEmployee?.FullName,
                        departmentId = assignedEmployee?.DepartmentId,
                        department = GetDepartmentNames(
                            assignedEmployee?.DepartmentId),

                        assignedBy = assignedByEmployee?.FullName,

                        advanceAmount = x.ExpenseAmount,
                        purpose = x.Purpose,
                        remarks = x.Remarks
                    };
                }).ToList();

                // 21. Spent detail projection
                var spentDetails = spentRecords.Select(x =>
                {
                    employeeLookup.TryGetValue(
                        x.employee_id ?? 0,
                        out var employee);

                    purchaseTypes.TryGetValue(
                        x.PurchaseItem,
                        out var purchaseType);

                    return new
                    {
                        spentId = x.Id,
                        date = x.Date,

                        employeeId = x.employee_id,
                        employeeCode = employee?.EmpCode,
                        employeeName = employee?.FullName,
                        departmentId = employee?.DepartmentId,
                        department = GetDepartmentNames(employee?.DepartmentId),

                        purchaseItemId = x.PurchaseItem,
                        purchaseType,

                        spentAmount = x.Amount,
                        remarks = x.Remarks,
                        billFile = x.BillFile
                    };
                }).ToList();

                // 22. Final response
                return Ok(new
                {
                    success = true,
                    organizationId,

                    filters = new
                    {
                        departmentId,
                        employeeId,
                        month,
                        year,
                        week,

                        weekStart,
                        weekEnd = weekEndExclusive?.AddDays(-1)
                    },

                    summary = new
                    {
                        MonthAdvance = monthAdvance,
                        YearAdvance = yearAdvance,

                        MonthSpent = monthSpent,
                        YearSpent = yearSpent,

                        MonthBalance = monthAdvance - monthSpent,
                        YearBalance = yearAdvance - yearSpent,

                        WeekAdvance = weekAdvance,
                        WeekSpent = weekSpent,
                        WeekBalance = weekAdvance - weekSpent,

                        TotalAdvance = totalAdvance,
                        TotalSpent = totalSpent,
                        TotalBalance = totalAdvance - totalSpent
                    },

                    advanceDetails,
                    spentDetails
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching filtered Company Expense summary.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }





        [Authorize]
        [HttpPost("expense-master-Add_amount")]
        public async Task<IActionResult> ExpenseMasterApi(
            [FromBody] ExpenseMaster model)
        {
            try
            {
                // ==========================================
                // 1. VALIDATE REQUEST
                // ==========================================

                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid request data."
                    });
                }


                // ==========================================
                // 2. GET ORGANIZATION FROM JWT
                // ==========================================

                var organizationId = GetOrganizationId();


                // ==========================================
                // 3. GET ROLE FROM JWT
                // ==========================================

                var role = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Role not found in token."
                    });
                }


                // ==========================================
                // 4. GET EMPLOYEE ID FROM JWT
                // ==========================================

                var employeeIdClaim =
                    User.FindFirst("EmpId")?.Value;


                // ==========================================
                // 5. GET EMAIL FROM JWT
                // ==========================================

                var email =
                    User.FindFirst(ClaimTypes.Email)?.Value;


                // ==========================================
                // 6. ASSIGNED BY
                // COMMON DB
                // ==========================================

                int? assignedBy = null;


                // ==========================================
                // EMPLOYEE / HR / MANAGER
                // ==========================================

                if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
                    !role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(
                            employeeIdClaim,
                            out int employeeId) ||
                        employeeId <= 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "Logged-in employee ID not found in token."
                        });
                    }


                    // --------------------------------------
                    // CHECK ASSIGNED BY IN COMMON DB
                    // --------------------------------------

                    var assignedByUser =
                        await _commonContext.CompanyUsers
                            .FirstOrDefaultAsync(x =>
                                x.EmpId == employeeId &&
                                x.OrgId == organizationId &&
                                (string.IsNullOrWhiteSpace(x.Status) ||
                                 x.Status == "Active"));

                    if (assignedByUser == null)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message =
                                "AssignedBy employee does not belong to this organization."
                        });
                    }

                    assignedBy = assignedByUser.EmpId;
                }


                // ==========================================
                // 7. ADMIN
                // ==========================================

                else
                {
                    /*
                     * New Admin may NOT have employee_master record.
                     *
                     * Therefore:
                     * assignedBy = NULL
                     *
                     * If Admin also has employee record in common
                     * company_users, we can use that EmpId.
                     */

                    if (int.TryParse(
                            employeeIdClaim,
                            out int adminEmployeeId) &&
                        adminEmployeeId > 0)
                    {
                        // --------------------------------------
                        // CHECK ADMIN EMPLOYEE IN COMMON DB
                        // --------------------------------------

                        var adminEmployee =
                            await _commonContext.CompanyUsers
                                .FirstOrDefaultAsync(x =>
                                    x.EmpId == adminEmployeeId &&
                                    x.OrgId == organizationId &&
                                    (string.IsNullOrWhiteSpace(x.Status) ||
                                     x.Status == "Active"));

                        if (adminEmployee != null)
                        {
                            assignedBy = adminEmployee.EmpId;
                        }
                    }
                }


                // ==========================================
                // 8. GET
                // EXISTING LOGIC - UNCHANGED
                // ==========================================

                if (model.Action.Equals(
                        "GET",
                        StringComparison.OrdinalIgnoreCase))
                {
                    int employeeId = 0;


                    // --------------------------------------
                    // Admin
                    // --------------------------------------

                    if (role.Equals(
                            "Admin",
                            StringComparison.OrdinalIgnoreCase) ||
                        role.Equals(
                            "SuperAdmin",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        employeeId = 0;
                    }


                    // --------------------------------------
                    // Employee / HR / Manager
                    // --------------------------------------

                    else
                    {
                        if (!int.TryParse(
                                employeeIdClaim,
                                out employeeId) ||
                            employeeId <= 0)
                        {
                            return Unauthorized(new
                            {
                                success = false,
                                message =
                                    "EmployeeId not found in token."
                            });
                        }
                    }


                    // --------------------------------------
                    // CALL SP
                    // --------------------------------------

                    var data =
                        await _context.ExpenseMasterResponse
                            .FromSqlRaw(
                                @"EXEC dbo.SP_ExpenseMaster
                          @Action = 'GET',
                          @EmployeeId = {0},
                          @Role = {1},
                          @OrganizationId = {2}",
                                employeeId,
                                role,
                                organizationId)
                            .ToListAsync();


                    // --------------------------------------
                    // ONLY ACTIVE RECORDS
                    // --------------------------------------

                    data = data
                        .Where(x => x.Status == "Active")
                        .ToList();


                    // ======================================
                    // ADMIN NAME FALLBACK
                    // COMMON DB
                    // ======================================

                    if ((role.Equals(
                            "Admin",
                            StringComparison.OrdinalIgnoreCase) ||
                         role.Equals(
                            "SuperAdmin",
                            StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(email))
                    {
                        var adminName =
                            await _commonContext.UsersMaster
                                .Where(x =>
                                    x.EMail == email &&
                                    x.OrgId == organizationId)
                                .Select(x => x.UserName)
                                .FirstOrDefaultAsync();

                        foreach (var item in data)
                        {
                            if (item.AssignedBy == null)
                            {
                                item.credited_by_empname =
                                    adminName;
                            }
                        }
                    }


                    return Ok(new
                    {
                        success = true,
                        message = "Data fetched successfully",
                        data = data
                    });
                }


                // ==========================================
                // 9. INSERT
                // ==========================================

                if (model.Action.Equals(
                        "INSERT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // ======================================
                    // ASSIGNED TO REQUIRED
                    // ======================================

                    if (model.AssignedTo <= 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message =
                                "AssignedTo employee is required."
                        });
                    }


                    // ======================================
                    // CHECK ASSIGNED TO
                    // COMMON DB
                    // ======================================

                    var assignedToUser =
                        await _commonContext.CompanyUsers
                            .FirstOrDefaultAsync(x =>
                                x.EmpId == model.AssignedTo &&
                                x.OrgId == organizationId &&
                                (string.IsNullOrWhiteSpace(x.Status) ||
                                 x.Status == "Active"));

                    if (assignedToUser == null)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message =
                                "AssignedTo employee does not belong to this organization."
                        });
                    }


                    // ======================================
                    // ADDED LOGIC ONLY:
                    // GET LOGGED-IN USER ID FROM JWT
                    // ======================================

                    var userIdClaim =
                        User.FindFirst("UserId")?.Value
                        ?? User.FindFirst("userId")?.Value
                        ?? User.FindFirst("user_id")?.Value;

                    if (!int.TryParse(
                            userIdClaim,
                            out int assignedByUserId) ||
                        assignedByUserId <= 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message =
                                "Logged-in UserId not found in token."
                        });
                    }


                    // ======================================
                    // INSERT
                    // EXISTING LOGIC + AssignedByUserId
                    // ======================================

                    await _context.Database.ExecuteSqlRawAsync(
                        @"EXEC dbo.SP_ExpenseMaster
                  @Action = 'INSERT',
                  @AssignedBy = @AssignedBy,
                  @AssignedByUserId = @AssignedByUserId,
                  @AssignedTo = @AssignedTo,
                  @Amount = @Amount,
                  @Purpose = @Purpose,
                  @Remarks = @Remarks,
                  @CreatedDate = @CreatedDate,
                  @OrganizationId = @OrganizationId",

                        new SqlParameter(
                            "@AssignedBy",
                            SqlDbType.Int)
                        {
                            Value =
                                (object?)assignedBy ??
                                DBNull.Value
                        },

                        // ADDED PARAMETER ONLY
                        new SqlParameter(
                            "@AssignedByUserId",
                            SqlDbType.Int)
                        {
                            Value = assignedByUserId
                        },

                        new SqlParameter(
                            "@AssignedTo",
                            SqlDbType.Int)
                        {
                            Value = assignedToUser.EmpId
                        },

                        new SqlParameter(
                            "@Amount",
                            SqlDbType.Decimal)
                        {
                            Precision = 18,
                            Scale = 2,
                            Value = model.ExpenseAmount
                        },

                        new SqlParameter(
                            "@Purpose",
                            SqlDbType.NVarChar,
                            200)
                        {
                            Value =
                                (object?)model.Purpose ??
                                DBNull.Value
                        },

                        new SqlParameter(
                            "@Remarks",
                            SqlDbType.NVarChar,
                            300)
                        {
                            Value =
                                (object?)model.Remarks ??
                                DBNull.Value
                        },

                        new SqlParameter(
                            "@CreatedDate",
                            SqlDbType.DateTime)
                        {
                            Value =
                                (object?)model.CreatedDate ??
                                DBNull.Value
                        },

                        new SqlParameter(
                            "@OrganizationId",
                            SqlDbType.Int)
                        {
                            Value = organizationId
                        }
                    );


                    return Ok(new
                    {
                        success = true,
                        message = "Inserted Successfully",
                        assignedBy = assignedBy,
                        assignedTo = assignedToUser.EmpId,
                        organizationId = organizationId,
                        role = role
                    });
                }


                // ==========================================
                // INVALID ACTION
                // ==========================================

                return BadRequest(new
                {
                    success = false,
                    message =
                        "Invalid Action. Use GET or INSERT."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Error while processing expense.",
                    error =
                        ex.InnerException?.InnerException?.Message
                        ?? ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }


        [Authorize]
        [HttpGet("received")]
        public async Task<IActionResult> GetReceived()
        {
            try
            {
                var organizationId = GetOrganizationId();

                var data = await _context.ReceivedAmounts
                    .Where(x => x.IsActive == true &&
                                x.OrganizationId == organizationId)
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

        private string? GetCompanyExpenseRole()
        {
            try
            {
                // JWT la portal_roles claim irukanum
                var portalRolesClaim = User.FindFirst("portal_roles")?.Value;

                if (string.IsNullOrWhiteSpace(portalRolesClaim))
                    return null;

                using var document = JsonDocument.Parse(portalRolesClaim);

                if (document.RootElement.TryGetProperty("company_expenses", out var companyExpenseRole))
                {
                    return companyExpenseRole.GetString();
                }

                // Optional fallback if JSON uses company_expense
                if (document.RootElement.TryGetProperty("company_expense", out var companyExpenseRole2))
                {
                    return companyExpenseRole2.GetString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            try
            {
                var organizationId = GetOrganizationId();

                var data = await _context.ExpenseMaster
                    .FirstOrDefaultAsync(x =>
                        x.ExpenseId == id &&
                        x.OrganizationId == organizationId);

                if (data == null)
                    return NotFound("Expense not found");

                data.Status = "Inactive";

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

        //[Authorize]
        //[HttpGet("summary")]
        //public async Task<IActionResult> GetSummary()
        //{
        //    try
        //    {
        //        // ==========================================
        //        // 1. OrganizationId from JWT
        //        // ==========================================
        //        var organizationId = GetOrganizationId();

        //        // ==========================================
        //        // 2. Company Expense Role
        //        //    FROM portal_roles
        //        // ==========================================
        //        var role = GetCompanyExpenseRole();

        //        if (string.IsNullOrWhiteSpace(role))
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "Company Expense role not found in portal_roles."
        //            });
        //        }

        //        // ==========================================
        //        // 3. Check Admin / SuperAdmin
        //        // ==========================================
        //        bool isAdmin =
        //            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
        //            role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
        //            role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);

        //        // ==========================================
        //        // 4. EmployeeId
        //        //    Only required for Employee
        //        // ==========================================
        //        int employeeId = 0;

        //        if (!isAdmin)
        //        {
        //            var employeeIdClaim = User.FindFirst("EmpId")?.Value;

        //            if (!int.TryParse(employeeIdClaim, out employeeId) ||
        //                employeeId <= 0)
        //            {
        //                return Unauthorized(new
        //                {
        //                    success = false,
        //                    message = "EmployeeId not found in token."
        //                });
        //            }
        //        }

        //        // ==========================================
        //        // 5. Current Month / Year
        //        // ==========================================
        //        var currentMonth = DateTime.Now.Month;
        //        var currentYear = DateTime.Now.Year;

        //        decimal monthReceived = 0;
        //        decimal yearReceived = 0;

        //        decimal monthExpense = 0;
        //        decimal yearExpense = 0;

        //        // ==========================================
        //        // 6. ACCESS
        //        // ==========================================
        //        bool fullAccess = false;

        //        if (isAdmin)
        //        {
        //            // Admin has full organization access
        //            fullAccess = true;
        //        }
        //        else
        //        {


        //            if (role != "Employee") 
        //            {
        //                fullAccess = true;
        //            }
        //            else 
        //            {

        //                fullAccess = false;
        //            }
        //        }

        //        // ==========================================
        //        // 7. RECEIVED / ADVANCE
        //        // ==========================================

        //        if (fullAccess)
        //        {
        //            // Admin / Full Access
        //            monthReceived = await _context.ExpenseMaster
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.Status == "Active" &&
        //                    x.CreatedDate.Month == currentMonth &&
        //                    x.CreatedDate.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

        //            yearReceived = await _context.ExpenseMaster
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.Status == "Active" &&
        //                    x.CreatedDate.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
        //        }
        //        else
        //        {
        //            // Employee / Limited Access
        //            monthReceived = await _context.ExpenseMaster
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.AssignedTo == employeeId &&
        //                    x.Status == "Active" &&
        //                    x.CreatedDate.Month == currentMonth &&
        //                    x.CreatedDate.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

        //            yearReceived = await _context.ExpenseMaster
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.AssignedTo == employeeId &&
        //                    x.Status == "Active" &&
        //                    x.CreatedDate.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
        //        }

        //        // ==========================================
        //        // 8. SPENT / EXPENSE
        //        // ==========================================

        //        if (fullAccess)
        //        {
        //            // Admin / Full Access
        //            monthExpense = await _context.SpentAmounts
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.IsActive &&
        //                    x.Date.Month == currentMonth &&
        //                    x.Date.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.Amount) ?? 0;

        //            yearExpense = await _context.SpentAmounts
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.IsActive &&
        //                    x.Date.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.Amount) ?? 0;
        //        }
        //        else
        //        {
        //            // Employee / Limited Access
        //            monthExpense = await _context.SpentAmounts
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.IsActive &&
        //                    x.employee_id == employeeId &&
        //                    x.Date.Month == currentMonth &&
        //                    x.Date.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.Amount) ?? 0;

        //            yearExpense = await _context.SpentAmounts
        //                .Where(x =>
        //                    x.OrganizationId == organizationId &&
        //                    x.IsActive &&
        //                    x.employee_id == employeeId &&
        //                    x.Date.Year == currentYear)
        //                .SumAsync(x => (decimal?)x.Amount) ?? 0;
        //        }

        //        // ==========================================
        //        // 9. BALANCE
        //        // ==========================================
        //        var monthBalance = monthReceived - monthExpense;
        //        var yearBalance = yearReceived - yearExpense;

        //        // ==========================================
        //        // 10. RESPONSE
        //        // ==========================================
        //        return Ok(new
        //        {
        //            success = true,

        //            // Company Expense role from portal_roles
        //            role = role,

        //            organizationId = organizationId,

        //            employeeId = isAdmin
        //                ? (int?)null
        //                : employeeId,

        //            fullAccess = fullAccess,

        //            MonthAdvance = monthReceived,
        //            YearAdvance = yearReceived,

        //            MonthSpent = monthExpense,
        //            YearSpent = yearExpense,

        //            MonthBalance = monthBalance,
        //            YearBalance = yearBalance
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "Error while fetching summary.",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}



        [Authorize]
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                // ==========================================
                // 1. OrganizationId from JWT
                // ==========================================
                var organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization."
                    });
                }

                // ==========================================
                // 2. Company Expense Role
                // ==========================================
                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Company Expense role not found in portal_roles."
                    });
                }

                // ==========================================
                // 3. Check Admin / SuperAdmin
                // ==========================================
                bool isAdmin =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);

                // ==========================================
                // 4. EmployeeId
                //    Only required for Employee
                // ==========================================
                int employeeId = 0;

                if (!isAdmin)
                {
                    var employeeIdClaim = User.FindFirst("EmpId")?.Value;

                    if (!int.TryParse(employeeIdClaim, out employeeId) ||
                        employeeId <= 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "EmployeeId not found in token."
                        });
                    }
                }

                // ==========================================
                // 5. Current Month / Year
                //    EXISTING LOGIC - UNCHANGED
                // ==========================================
                var now = DateTime.Now;

                var currentMonth = now.Month;
                var currentYear = now.Year;

                decimal monthReceived = 0;
                decimal yearReceived = 0;

                decimal monthExpense = 0;
                decimal yearExpense = 0;

                // ==========================================
                // 6. ACCESS
                //    EXISTING LOGIC - UNCHANGED
                // ==========================================
                bool fullAccess = false;

                if (isAdmin)
                {
                    fullAccess = true;
                }
                else
                {
                    if (!role.Equals(
                        "Employee",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        fullAccess = true;
                    }
                    else
                    {
                        fullAccess = false;
                    }
                }

                // ==========================================
                // 7. RECEIVED / ADVANCE
                //    EXISTING MONTH / YEAR LOGIC
                // ==========================================
                if (fullAccess)
                {
                    // Month Advance
                    monthReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Month == currentMonth &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                    // Year Advance
                    yearReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;
                }
                else
                {
                    // Employee Month Advance
                    monthReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.AssignedTo == employeeId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Month == currentMonth &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                    // Employee Year Advance
                    yearReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.AssignedTo == employeeId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;
                }

                // ==========================================
                // 8. SPENT / EXPENSE
                //    EXISTING MONTH / YEAR LOGIC
                // ==========================================
                if (fullAccess)
                {
                    // Month Expense
                    monthExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.Date.Month == currentMonth &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                    // Year Expense
                    yearExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                }
                else
                {
                    // Employee Month Expense
                    monthExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.employee_id == employeeId &&
                            x.Date.Month == currentMonth &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                    // Employee Year Expense
                    yearExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.employee_id == employeeId &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                }

                // ==========================================
                // 9. NEW: CURRENT WEEK DATE RANGE
                //    Monday 00:00 to next Monday 00:00
                // ==========================================
                int daysSinceMonday =
                    ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

                DateTime weekStart = now.Date.AddDays(-daysSinceMonday);
                DateTime weekEndExclusive = weekStart.AddDays(7);

                // ==========================================
                // 10. NEW: WEEKLY ADVANCE
                // ==========================================
                decimal weekReceived = 0m;

                if (fullAccess)
                {
                    weekReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.Status == "Active" &&
                            x.CreatedDate >= weekStart &&
                            x.CreatedDate < weekEndExclusive)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;
                }
                else
                {
                    weekReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.AssignedTo == employeeId &&
                            x.Status == "Active" &&
                            x.CreatedDate >= weekStart &&
                            x.CreatedDate < weekEndExclusive)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;
                }

                // ==========================================
                // 11. NEW: WEEKLY EXPENSE
                // ==========================================
                decimal weekExpense = 0m;

                if (fullAccess)
                {
                    weekExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.Date >= weekStart &&
                            x.Date < weekEndExclusive)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                }
                else
                {
                    weekExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.employee_id == employeeId &&
                            x.Date >= weekStart &&
                            x.Date < weekEndExclusive)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                }

                // ==========================================
                // 12. BALANCE
                //    EXISTING + NEW WEEK BALANCE
                // ==========================================
                var monthBalance = monthReceived - monthExpense;
                var yearBalance = yearReceived - yearExpense;

                var weekBalance = weekReceived - weekExpense;

                // ==========================================
                // 13. RESPONSE
                //    EXISTING FIELDS PRESERVED
                // ==========================================
                return Ok(new
                {
                    success = true,

                    role = role,
                    organizationId = organizationId,

                    employeeId = isAdmin
                        ? (int?)null
                        : employeeId,

                    fullAccess = fullAccess,

                    // Existing Month / Year summary
                    MonthAdvance = monthReceived,
                    YearAdvance = yearReceived,

                    MonthSpent = monthExpense,
                    YearSpent = yearExpense,

                    MonthBalance = monthBalance,
                    YearBalance = yearBalance,

                    // New Current Week summary
                    WeekAdvance = weekReceived,
                    WeekSpent = weekExpense,
                    WeekBalance = weekBalance,

                    // Current week dates
                    WeekStart = weekStart,
                    WeekEnd = weekEndExclusive.AddDays(-1)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching summary.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }




        [Authorize]
        [HttpGet("spent")]
        public async Task<IActionResult> GetSpents()
        {
            try
            {
                var organizationId = GetOrganizationId();

                var data = await _context.SpentAmounts  
                    .Where(x =>
                        x.IsActive &&
                        x.OrganizationId == organizationId)
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




 

        [Authorize]
        [HttpPost("spent")]
        public async Task<IActionResult> AddSpent([FromForm] SpentCreateDto dto)
        {
            try
            {
                var organizationId = GetOrganizationId();

                if (dto == null)
                    return BadRequest("DTO is null");

                var model = new SpentAmount
                {
                    Date = dto.Date,
                    PurchaseItem = dto.PurchaseItem,
                    Amount = dto.Amount,
                    Remarks = dto.Remarks,
                    employee_id = dto.employee_id,
                    IsActive = true,
                    OrganizationId = organizationId
                };

                // FILE UPLOAD
                if (dto.billFile != null && dto.billFile.Length > 0)
                {
                    var webRoot = _environment.WebRootPath;

                    if (string.IsNullOrEmpty(webRoot))
                    {
                        webRoot = Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot");
                    }

                    var folderPath = Path.Combine(
                        webRoot,
                        "Uploads",
                        "Bills");

                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                    var fileName =
                        Guid.NewGuid().ToString()
                        + Path.GetExtension(dto.billFile.FileName);

                    var filePath = Path.Combine(
                        folderPath,
                        fileName);

                    using (var stream = new FileStream(
                        filePath,
                        FileMode.Create))
                    {
                        await dto.billFile.CopyToAsync(stream);
                    }

                    model.BillFile =
                        $"Uploads/Bills/{fileName}";
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
                return StatusCode(500, new
                {
                    message = "Error while adding spent.",
                    error = ex.Message
                });
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
                    x.Purchase_item,
                    x.EmpCode,
                    x.EmployeeName,
                    x.Employee_Id,
                    x.PurchaseTypeName,

                    // ✅ Correct mapping
                    BillUrl = string.IsNullOrEmpty(x.Bill_File)
                        ? null
                        : $"/api/bill/{x.Bill_File}"
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

        [Authorize]
        [HttpDelete("spent/{id}")]
        public async Task<IActionResult> DeleteSpent(int id)
        {
            try
            {
                // ==========================================
                // 1. Validate ID
                // ==========================================

                if (id <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid spent ID."
                    });
                }

                // ==========================================
                // 2. OrganizationId from JWT
                // ==========================================

                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token."
                    });
                }

                // ==========================================
                // 3. Company Expense role
                //    portal_roles.company_expenses
                // ==========================================

                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // ==========================================
                // 4. DELETE ACCESS
                //
                // Employee  -> ❌
                // Manager   -> ❌
                // Admin     -> ✅
                // Full Access -> ✅
                // ==========================================

                bool canDelete =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Full Access", StringComparison.OrdinalIgnoreCase);

                if (!canDelete)
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "You do not have permission to delete spent records."
                        });
                }

                // ==========================================
                // 5. Get active spent record
                //    Organization based
                // ==========================================

                var spent = await _context.SpentAmounts
                    .FirstOrDefaultAsync(x =>
                        x.Id == id &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (spent == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Spent record not found."
                    });
                }

                // ==========================================
                // 6. SOFT DELETE
                // ==========================================

                spent.IsActive = false;

                await _context.SaveChangesAsync();

                // ==========================================
                // 7. Response
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message = "Spent record deleted successfully.",
                    id = spent.Id,
                    organizationId = spent.OrganizationId,
                    role = role,
                    isActive = spent.IsActive
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deleting spent record.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


     

        ///-------Accees Controll Api -----///

        //[Authorize]
        ///dei idhu irukatum apdye...enaku spent_access mtum thiruba podu podre   ro
        ///

        [Authorize]
        [HttpGet("employee/access-control")]
        public async Task<IActionResult> GetAccessControl()
        {
            try
            {
                // OrganizationId from JWT
                var organizationId = GetOrganizationId();

                // Role from JWT
                var role = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Role not found in token."
                    });
                }

                // Admin / SuperAdmin
                bool isAdmin =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase);

                int employeeId = 0;

                // EmployeeId is required ONLY for non-admin users
                if (!isAdmin)
                {
                    var employeeIdClaim = User.FindFirst("EmpId")?.Value;

                    if (!int.TryParse(employeeIdClaim, out employeeId) ||
                        employeeId <= 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "EmployeeId not found in token."
                        });
                    }
                }

                // ============================
                // ADMIN
                // ============================
                if (isAdmin)
                {
                    return Ok(new
                    {
                        success = true,
                        status = "full_access",
                        totalAmount = 0,
                        role = role,
                        organizationId = organizationId
                    });
                }

                // ============================
                // EMPLOYEE
                // ============================
                var result = await _context.Set<EmployeeAccessResultsdto>()
                    .FromSqlRaw(
                        @"EXEC dbo.GetEmployeeAccessAndAmount
                    @empid,
                    @OrganizationId",
                        new SqlParameter("@empid", employeeId),
                        new SqlParameter("@OrganizationId", organizationId)
                    )
                    .ToListAsync();

                var data = result.FirstOrDefault();

                return Ok(new
                {
                    success = true,
                    status = data?.status ?? "no_access",
                    totalAmount = data?.TotalAmount ?? 0,
                    role = role,
                    employeeId = employeeId,
                    organizationId = organizationId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error occurred while checking employee access.",
                    error = ex.Message
                });
            }
        }

  

        [Authorize]
        [HttpPut("roles/{roleId}/permission")]
        public async Task<IActionResult> UpdateRolePermission(
    int roleId,
    [FromBody] CreatePermissionDto model)
        {
            try
            {
                
                // ==========================================
                // 1. Validate request
                // ==========================================

                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Permission data is required."
                    });
                }

                // ==========================================
                // 2. Get OrganizationId from JWT
                // ==========================================

                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrWhiteSpace(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Org_Id not found in token."
                    });
                }

                if (!int.TryParse(orgIdClaim, out int organizationId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid Org_Id in token."
                    });
                }

                // ==========================================
                // 3. Check Role exists in same Organization
                // ==========================================

                var role = await _context.RoleMasters
                    .FirstOrDefaultAsync(x =>
                        x.RoleId == roleId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (role == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Role not found for this organization."
                    });
                }

                // ==========================================
                // 4. Find Company Expense Access row
                // ==========================================

                var access = await _context.CompanyexpenseAccesses
                    .FirstOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.RoleId == roleId);

                // ==========================================
                // 5. Create if row doesn't exist
                // ==========================================

                if (access == null)
                {
                    access = new CompanyexpenseAccesses
                    {
                        OrganizationId = organizationId,
                        RoleId = roleId,
                        Action = model.Action,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    };

                    _context.CompanyexpenseAccesses.Add(access);
                }
                else
                {
                    // ==========================================
                    // 6. Update existing row
                    // ==========================================

                    access.Action = model.Action;
                    access.IsActive = true;

                    _context.Entry(access)
                        .Property(x => x.Action)
                        .IsModified = true;

                    _context.Entry(access)
                        .Property(x => x.IsActive)
                        .IsModified = true;
                }

                // ==========================================
                // 7. Save changes
                // ==========================================

                await _context.SaveChangesAsync();

                // ==========================================
                // 8. Verify saved value from DB
                // ==========================================

                var updatedAccess = await _context.CompanyexpenseAccesses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.RoleId == roleId);

                if (updatedAccess == null)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Permission was not found after save."
                    });
                }

                // ==========================================
                // 9. Return response
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message = "Company Expense permission updated successfully.",
                    data = new
                    {
                        roleId = role.RoleId,
                        roleName = role.RoleName,
                        organizationId = organizationId,

                        action = updatedAccess.Action,

                        accessType = updatedAccess.Action
                            ? "Limited Access"
                            : "Full Access"
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while updating role permission.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("Total_Access")]
        public async Task<IActionResult> GetTotalAccess()
        {
            try
            {
                // =========================================
                // 1. OrganizationId from JWT
                // =========================================
                var organizationId = GetOrganizationId();

                // =========================================
                // 2. Role from JWT
                // =========================================
                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Forbid();
                }

                // =========================================
                // 3. Check Admin
                // =========================================
                bool isAdmin =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase);

                int loggedInEmployeeId = 0;

                // =========================================
                // 4. EmpId required ONLY for non-admin
                // =========================================
                if (!isAdmin)
                {
                    var employeeIdClaim = User.FindFirst("EmpId")?.Value;

                    if (!int.TryParse(employeeIdClaim, out loggedInEmployeeId) ||
                        loggedInEmployeeId <= 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "EmployeeId not found in token."
                        });
                    }
                }

                // =========================================
                // 5. SP 1 - Spent Details
                // =========================================

                var spent = await _context.SpentDetails
                    .FromSqlRaw(
                        @"EXEC dbo.get_spent_details_with_access
                    @EmployeeId,
                    @OrganizationId,
                    @Role",
                        new SqlParameter("@EmployeeId", loggedInEmployeeId),
                        new SqlParameter("@OrganizationId", organizationId),
                        new SqlParameter("@Role", role)
                    )
                    .ToListAsync();

                // =========================================
                // 6. SP 2 - Advance Details
                // =========================================

                var advance = await _context.ExpenseMaster
                    .FromSqlRaw(
                        @"EXEC dbo.get_advance_details_with_access
                    @EmployeeId,
                    @OrganizationId,
                    @Role",
                        new SqlParameter("@EmployeeId", loggedInEmployeeId),
                        new SqlParameter("@OrganizationId", organizationId),
                        new SqlParameter("@Role", role)
                    )
                    .ToListAsync();

                // =========================================
                // 7. Response
                // =========================================

                return Ok(new
                {
                    success = true,

                    fullAccess = isAdmin,

                    role = role,

                    organizationId = organizationId,

                    employeeId = isAdmin
                        ? (int?)null
                        : loggedInEmployeeId,

                    spentDetails = spent,

                    advanceDetails = advance
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching total access details.",
                    error = ex.Message
                });
            }
        }

      

        [Authorize]
        [HttpGet("spent_Access")]
        public async Task<IActionResult> SpentAccess()
        {
            try
            {
                // =========================================
                // 1. OrganizationId from JWT
                // =========================================
                var organizationId = GetOrganizationId();

                // =========================================
                // 2. Get Company Expense Role
                //    from portal_roles
                // =========================================
                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Company Expense role not found in portal_roles."
                    });
                }

                // =========================================
                // 3. Check Company Expense Admin
                // =========================================
                bool isAdmin =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);

                int loggedInEmployeeId = 0;

                // =========================================
                // 4. EmpId ONLY for non-admin
                // =========================================
                if (!isAdmin)
                {
                    var employeeIdClaim = User.FindFirst("EmpId")?.Value;

                    if (!int.TryParse(employeeIdClaim, out loggedInEmployeeId) ||
                        loggedInEmployeeId <= 0)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = "EmployeeId not found in token."
                        });
                    }
                }

                // =========================================
                // 5. Call SP
                // =========================================
                var spent = await _context.SpentDetails
                    .FromSqlRaw(
                        @"EXEC dbo.get_spent_details_with_access
                  @EmployeeId,
                  @OrganizationId,
                  @Role",
                        new SqlParameter("@EmployeeId", loggedInEmployeeId),
                        new SqlParameter("@OrganizationId", organizationId),
                        new SqlParameter("@Role", role)
                    )
                    .ToListAsync();

                // =========================================
                // 6. Response
                // =========================================
                return Ok(new
                {
                    success = true,
                    fullAccess = isAdmin,
                    role = role,
                    organizationId = organizationId,
                    employeeId = isAdmin
                        ? (int?)null
                        : loggedInEmployeeId,
                    spentDetails = spent
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching spent access details.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        //-------------api for purchaase type--------

        // GET: api/purchasetype
        [Authorize]
        [HttpGet("purchase-types")]
        public async Task<IActionResult> Get()
        {
            try
            {
                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                var data = await _context.PurchaseTypes
                    .Where(x => x.OrganizationId == organizationId)
                    .OrderBy(x => x.Name)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    organizationId = organizationId,
                    data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching purchase types.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpPost("purchase-type")]
        public async Task<IActionResult> Post(
            [FromBody] PurchaseType model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Purchase type data is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(model.Name))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Name is required."
                    });
                }

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                string name = model.Name.Trim();

                // Duplicate check within organization
                bool exists = await _context.PurchaseTypes
                    .AnyAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.Name.ToLower() == name.ToLower());
                        

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "This purchase type already exists for this organization."
                    });
                }

                var purchaseType = new PurchaseType
                {
                    Name = name,
                    OrganizationId = organizationId,
                    Created_At = DateTime.UtcNow
                };

                _context.PurchaseTypes.Add(purchaseType);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Purchase type created successfully.",
                    data = purchaseType
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while creating purchase type.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpPut("purchase-type/{id}")]
        public async Task<IActionResult> Put(
    int id,
    [FromBody] PurchaseType model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Purchase type data is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(model.Name))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Name is required."
                    });
                }

                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }   

                var purchaseType =
                    await _context.PurchaseTypes
                        .FirstOrDefaultAsync(x =>
                            x.Id == id &&
                            x.OrganizationId == organizationId);

                if (purchaseType == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Purchase type not found."
                    });
                }

                string name = model.Name.Trim();

                // Duplicate check excluding current record
                bool duplicateExists =
                    await _context.PurchaseTypes
                        .AnyAsync(x =>
                            x.Id != id &&
                            x.OrganizationId == organizationId &&
                            x.Name.ToLower() == name.ToLower());

                if (duplicateExists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "This purchase type already exists for this organization."
                    });
                }

                // Only name can be changed
                // OrganizationId always comes from JWT
                purchaseType.Name = name;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Purchase type updated successfully.",
                    data = purchaseType
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while updating purchase type.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpDelete("purchase-type/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                int organizationId;

                try
                {
                    organizationId = GetOrganizationId();
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }

                var purchaseType =
                    await _context.PurchaseTypes
                        .FirstOrDefaultAsync(x =>
                            x.Id == id &&
                            x.OrganizationId == organizationId);

                if (purchaseType == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Purchase type not found."
                    });
                }

                _context.PurchaseTypes.Remove(purchaseType);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Purchase type deleted successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deleting purchase type.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }



        [Authorize]
        [HttpGet("voucher")]
        public async Task<IActionResult> GetVoucher(
         int employeeId,
         int month,
         int year)
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
                // ==========================================

                var organizationId = GetOrganizationId();


                // ==========================================
                // 2. EMPLOYEE DETAILS
                // COMMON DB - company_users
                // ==========================================

                var employee =
                    await _commonContext.CompanyUsers
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == employeeId &&
                            x.OrgId == organizationId &&
                            (x.Status == null ||
                             x.Status == "Active"));


                if (employee == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message =
                            "Employee Not Found in this Organization"
                    });
                }


                // ==========================================
                // 3. PERSONAL DETAILS
                // TRAVEL_FORM DB
                // ==========================================

                var personalDetails =
                    await _context.EmployeePersonalDetails
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == employeeId);


                // ==========================================
                // 4. GENERATE TITLE
                // ==========================================

                string title = "Mr";

                if (!string.IsNullOrEmpty(
                        personalDetails?.Gender))
                {
                    var gender =
                        personalDetails.Gender.ToLower();

                    var maritalStatus =
                        personalDetails.MaritalStatus?.ToLower();

                    if (gender == "female" ||
                        gender == "f")
                    {
                        title =
                            maritalStatus == "married"
                                ? "Mrs"
                                : "Ms";
                    }
                    else
                    {
                        title = "Mr";
                    }
                }


                // ==========================================
                // 5. GET SPENT DATA
                // ==========================================

                var spentData =
                    await _context.SpentAmounts
                        .Where(x =>
                            x.employee_id == employeeId &&
                            x.OrganizationId == organizationId &&
                            x.IsActive == true &&
                            x.Date.Month == month &&
                            x.Date.Year == year)
                        .ToListAsync();


                if (!spentData.Any())
                {
                    return Ok(new
                    {
                        success = false,
                        message =
                            "No Voucher Found For Selected Month",
                        data = (object)null
                    });
                }


                // ==========================================
                // 6. TOTAL AMOUNT
                // ==========================================

                var totalAmount =
                    spentData.Sum(x => x.Amount);


                // ==========================================
                // 7. PURCHASE TYPES
                // ==========================================

                var purchaseTypes =
                    await (
                        from s in _context.SpentAmounts

                        join p in _context.PurchaseTypes
                            on s.PurchaseItem equals p.Id

                        where
                            s.employee_id == employeeId &&
                            s.OrganizationId == organizationId &&
                            s.IsActive == true &&
                            s.Date.Month == month &&
                            s.Date.Year == year

                        select p.Name
                    )
                    .Distinct()
                    .ToListAsync();


                // ==========================================
                // 8. CONVERT TO STRING
                // ==========================================

                var towardsText =
                    string.Join(
                        ", ",
                        purchaseTypes);


                // ==========================================
                // 9. FINAL RESPONSE
                // ==========================================

                var result =
                    new VoucherDto
                    {
                        PaidTo =
                            $"{title} {employee.FullName}",

                        Amount =
                            totalAmount,

                        RupeesInWords =
                            NumberToWords(
                                (long)totalAmount)
                            + " Rupees Only",

                        Towards =
                            towardsText,

                        Date =
                            DateTime.Now
                    };


                return Ok(new
                {
                    success = true,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Error while generating voucher.",
                    error =
                        ex.InnerException?.Message
                        ?? ex.Message
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


