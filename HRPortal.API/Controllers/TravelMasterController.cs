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
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
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
        private readonly CommonDbContext _commonContext;



        public TravelMasterController(ApplicationDbContext context, IWebHostEnvironment environment, IConfiguration configuration, HttpClient httpClient, EmailService emailService, CommonDbContext commonContext)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
            _httpClient = httpClient;
            _emailService = emailService;
            _commonContext = commonContext;
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
                var organizationId = GetOrganizationId();
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
           x.OrganizationId == organizationId&&
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
                        OrganizationId = organizationId,
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
                            x.EmpId == dto.EmployeeId &&
                            x.OrganizationId == organizationId);

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
                // =====================================
                // 1. GET DRAFT / REJECTED CLAIM
                // =====================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claimId &&
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
                // 2. CHECK RESUBMIT;
                // =====================================

                bool isResubmit =
                    claim.Status.Equals(
                        "Rejected",
                        StringComparison.OrdinalIgnoreCase);

                // =====================================
                // 3. REMOVE OLD APPROVAL RECORDS
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
                // 4. RESET CLAIM STATUS
                // =====================================

                claim.Status = "Pending";

                // Don't set this to 1 yet.
                // We will set it after creating approvals.

                claim.CurrentApprovalLevel = null;

                claim.SubmittedAt = DateTime.Now;

                claim.RejectedAt = null;

                claim.ApprovedAt = null;

                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // =====================================
                // 5. GET CLAIM EMPLOYEE FROM COMMON DB
                // =====================================

                var employee = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId &&
                        x.OrgId == claim.OrganizationId &&
                        x.Status != null &&
                        x.Status.ToLower() == "active");

                if (employee == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim employee not found in Common Login database."
                    });
                }

                //if (string.IsNullOrWhiteSpace(employee.Branch) ||
                    //string.IsNullOrWhiteSpace(employee.Department))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee branch or department is missing."
                    });
                }


                // =====================================
                // 6. GET DEPARTMENT APPROVAL CONFIG
                // =====================================

                // Local Expense uses travel_portal module code.

                const string moduleCode = "travel_portal";

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == claim.OrganizationId &&
                            x.ModuleCode == moduleCode &&
                            //x.Branch == employee.Branch!.Trim() &&
                            x.Department == employee.Department!.Trim() &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Approval configuration not found for this module, branch and department."
                    });
                }


                // =====================================
                // 7. GET EMPLOYEE APPROVAL CONFIGURATION
                // =====================================

                var approvalConfigs =
                    await _context.EmployeeApprovalConfigs
                        .AsNoTracking()
                        .Where(x =>
                            x.OrganizationId == claim.OrganizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.EmployeeId == claim.EmployeeId &&
                            x.IsActive)
                        .OrderBy(x => x.ApprovalLevel)
                        .ToListAsync();


                // =====================================
                // 8. VALIDATE APPROVAL LEVELS
                // =====================================

                // The employee must have a configuration
                // for every level configured for the department.

                if (approvalConfigs.Count != departmentConfig.TotalApprovalLevels)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Employee approval configuration is incomplete. " +
                            $"Expected {departmentConfig.TotalApprovalLevels} levels, " +
                            $"but found {approvalConfigs.Count}."
                    });
                }

                // Levels must be continuous: 1, 2, 3...

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
                // 9. CREATE APPROVAL RECORDS
                // =====================================

                // Stores configured approvers for each level.
                // Used later to select the first-level approvers
                // who should receive the initial notification.

                var approverIdsByLevel = new Dictionary<int, List<int>>();

                foreach (var config in approvalConfigs)
                {
                    // Initially, we support AnyOne only.
                    // A level completes when any one approver approves.

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


                    // Get all approvers configured for this level.

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


                    // The claim employee cannot approve their own claim.

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


                    // Validate every approver against Common Login DB.
                    // Approvers must be active employees of the same organization.

                    var validApproverIds =
                        await _commonContext.CompanyUsers
                            .AsNoTracking()
                            .Where(x =>
                                approverIds.Contains(x.EmpId) &&
                                x.OrgId == claim.OrganizationId &&
                                x.Status != null &&
                                x.Status.ToLower() == "active")
                            .Select(x => x.EmpId)
                            .Distinct()
                            .ToListAsync();

                    if (validApproverIds.Count != approverIds.Count)
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                $"One or more approvers at Level {config.ApprovalLevel} are invalid or inactive."
                        });
                    }


                    // Keep approvers grouped by level.

                    approverIdsByLevel[config.ApprovalLevel] = approverIds;


                    // Create a ClaimApproval record for each approver.

                    foreach (var approverId in approverIds)
                    {
                        _context.ClaimApprovals.Add(
                            new ClaimApproval
                            {
                                ClaimId = claim.ClaimId,

                                ApprovalLevel = config.ApprovalLevel,

                                ApproverEmpId = approverId,

                                Status = "Pending",

                                CreatedAt = DateTime.Now,

                                ApprovalToken = Guid.NewGuid()
                            });
                    }
                }

                // =====================================
                // 10. SAVE ALL APPROVAL RECORDS
                // =====================================

                await _context.SaveChangesAsync();

                // =====================================
                // 11. GET PENDING APPROVAL RECORDS
                // =====================================

                var approvalRecords =
                    await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claim.ClaimId &&
                            x.Status == "Pending")
                        .OrderBy(x => x.ApprovalLevel)
                        .ThenBy(x => x.ApproverEmpId)
                        .ToListAsync();

                // =====================================
                // 12. SAFETY CHECK
                // =====================================

                if (!approvalRecords.Any())
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Approval flow could not be created."
                    });
                }

                // =====================================
                // 13. FIRST APPROVAL LEVEL
                // =====================================

                int firstApprovalLevel =
                    approvalRecords.Min(
                        x => x.ApprovalLevel);

                claim.CurrentApprovalLevel =
                    firstApprovalLevel;

                claim.Status = "Pending";

                claim.LastModifiedAt =
                    DateTime.Now;

                await _context.SaveChangesAsync();

                // =====================================
                // 14. DEPARTMENT NAME
                // =====================================

                
string departmentName = employee.Department ?? "-";

              

                // =====================================
                // 15. PURPOSE
                // =====================================

                var firstTravel =
                    await _context.ClaimTravels
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claim.ClaimId);

                string purpose =
                    firstTravel?.Purpose ?? "-";

                // =====================================
                // 16. GET ALL FIRST LEVEL APPROVERS
                // =====================================

                var firstLevelApprovals =
                    approvalRecords
                        .Where(x =>
                            x.ApprovalLevel ==
                            firstApprovalLevel)
                        .ToList();

                int emailSentCount = 0;

                int emailFailedCount = 0;

                // =====================================
                // 17. SEND EMAIL TO ALL FIRST LEVEL
                // APPROVERS
                // =====================================

                foreach (var firstApproval
                         in firstLevelApprovals)
                {
                    if (!firstApproval.ApproverEmpId.HasValue)
                    {
                        continue;
                    }

                    var approver =
                        await _context.EmployeeMasters
                            .FirstOrDefaultAsync(x =>
                                x.EmpId ==
                                    firstApproval.ApproverEmpId.Value &&
                                x.OrganizationId ==
                                    claim.OrganizationId &&
                                x.IsActive);

                    if (approver == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(
                            approver.EmailId))
                    {
                        continue;
                    }

                    var baseUrl =
                        _configuration[
                            "AppSettings:BaseUrl"];

                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        emailFailedCount++;
                        continue;
                    }

                    // =====================================
                    // APPROVE URL
                    // =====================================

                    var approveUrl =
                        $"{baseUrl}/api/TravelMaster/mail-approve?token={firstApproval.ApprovalToken}";

                    // =====================================
                    // REJECT URL
                    // =====================================

                    var rejectUrl =
                        $"{baseUrl}/api/TravelMaster/reject-form?token={firstApproval.ApprovalToken}";

                    // =====================================
                    // EMAIL BODY
                    // =====================================

                    string body = $@"

Dear {approver.FullName},

<br/><br/>

A new Local Expense Claim requires your approval.

<br/><br/>

<b>Employee Details</b>

<ul>

    <li>
        Employee Name : {employee.FullName}
    </li>

    <li>
        Employee ID : {employee.EmpCode}
    </li>

    <li>
        Department : {departmentName}
    </li>

</ul>

<b>Claim Details</b>

<ul>

    <li>
        Claim Number : {claim.ClaimNo}
    </li>

    <li>
        Claim Type : Local Expense Claim
    </li>

    <li>
        Claim Month : {claim.ClaimMonth:MMM-yyyy}
    </li>

    <li>
        Amount : ₹{claim.TotalAmount:N2}
    </li>

    <li>
        Status : Pending Approval
    </li>

    <li>
        Approval Level : {firstApproval.ApprovalLevel}
    </li>

</ul>

<b>Purpose</b>

<p>
    {purpose}
</p>

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

                    // =====================================
                    // SEND EMAIL
                    // =====================================

                    try
                    {
                        await _emailService.SendEmailAsync(
                            approver.EmailId,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);

                        emailSentCount++;
                    }
                    catch
                    {
                        emailFailedCount++;
                    }
                }

                // =====================================
                // 18. COMMIT
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
                        claimId =
                            claim.ClaimId,

                        claimNo =
                            claim.ClaimNo,

                        status =
                            claim.Status,

                        currentApprovalLevel =
                            claim.CurrentApprovalLevel,

                        submittedAt =
                            claim.SubmittedAt,

                        firstApprovalLevel =
                            firstApprovalLevel,

                        firstLevelApproverCount =
                            firstLevelApprovals.Count,

                        emailSentCount =
                            emailSentCount,

                        emailFailedCount =
                            emailFailedCount
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,

                    message =
                        "Error while submitting claim.",

                    error =
                        ex.InnerException?.Message
                        ?? ex.Message
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
        //        [HttpPost("approve-claim")]
        //        public async Task<IActionResult> ApproveClaim(
        //      [FromBody] ClaimApprovalActionDto dto)
        //        {
        //            try
        //            {
        //                var empIdClaim = User.FindFirst("EmpId")?.Value;

        //                if (string.IsNullOrEmpty(empIdClaim))
        //                {
        //                    return Unauthorized(new
        //                    {
        //                        success = false,
        //                        message = "EmployeeId not found in token."
        //                    });
        //                }

        //                int empId = int.Parse(empIdClaim);

        //                // =====================================
        //                // Current Pending Approval
        //                // =====================================

        //                var approval = await _context.ClaimApprovals
        //                    .FirstOrDefaultAsync(x =>
        //                        x.ClaimId == dto.ClaimId &&
        //                        x.ApproverEmpId == empId &&
        //                        x.Status == "Pending");

        //                if (approval == null)
        //                {
        //                    return BadRequest(new
        //                    {
        //                        success = false,
        //                        message = "Approval record not found."
        //                    });
        //                }

        //                // =====================================
        //                // Update Current Approval
        //                // =====================================

        //                approval.Status = "Approved";
        //                approval.Remarks = dto.Remarks;
        //                approval.ActionDate = DateTime.Now;

        //                await _context.SaveChangesAsync();

        //                // =====================================
        //                // Get Claim
        //                // =====================================

        //                var claim = await _context.ClaimMasters
        //                    .FirstOrDefaultAsync(x => x.ClaimId == dto.ClaimId);

        //                if (claim == null)
        //                {
        //                    return BadRequest(new
        //                    {
        //                        success = false,
        //                        message = "Claim not found."
        //                    });
        //                }

        //                ClaimApproval? nextPending = null;

        //                // =====================================
        //                // COMPANY FLOW
        //                // Level 2 = Final Manual Approval
        //                // =====================================

        //                if (approval.ApprovalLevel == 2)
        //                {
        //                    claim.Status = "Approved";
        //                    claim.ApprovedAt = DateTime.Now;
        //                    claim.CurrentApprovalLevel = null;
        //                    claim.LastModifiedAt = DateTime.Now;

        //                    // Auto Approve Remaining Levels

        //                    var remainingApprovals = await _context.ClaimApprovals
        //                        .Where(x =>
        //                            x.ClaimId == dto.ClaimId &&
        //                            x.Status == "Pending")
        //                        .ToListAsync();

        //                    foreach (var item in remainingApprovals)
        //                    {
        //                        item.Status = "Approved";
        //                        item.Remarks = "Auto Approved";
        //                        item.ActionDate = DateTime.Now;
        //                    }
        //                }
        //                else
        //                {
        //                    // Move To Next Approval

        //                    nextPending = await _context.ClaimApprovals
        //                        .Where(x =>
        //                            x.ClaimId == dto.ClaimId &&
        //                            x.Status == "Pending")
        //                        .OrderBy(x => x.ApprovalLevel)
        //                        .FirstOrDefaultAsync();

        //                    if (nextPending != null)
        //                    {
        //                        claim.CurrentApprovalLevel = nextPending.ApprovalLevel;
        //                        claim.LastModifiedAt = DateTime.Now;
        //                    }
        //                    else
        //                    {
        //                        claim.Status = "Approved";
        //                        claim.ApprovedAt = DateTime.Now;
        //                        claim.CurrentApprovalLevel = null;
        //                        claim.LastModifiedAt = DateTime.Now;
        //                    }
        //                }

        //                await _context.SaveChangesAsync();
        //                // =====================================
        //                // SEND MAIL TO NEXT APPROVER
        //                // =====================================

        //                if (nextPending != null)
        //                {
        //                    var nextApprover = await _context.EmployeeMasters
        //                        .FirstOrDefaultAsync(x => x.EmpId == nextPending.ApproverEmpId);

        //                    var employee = await _context.EmployeeMasters
        //                        .FirstOrDefaultAsync(x => x.EmpId == claim.EmployeeId);

        //                    string departmentName = "-";

        //                    if (employee?.DepartmentId != null)
        //                    {
        //                        var department = await _context.Department_Master
        //                            .FirstOrDefaultAsync(x => x.DepartmentId == employee.DepartmentId.Value);

        //                        if (department != null)
        //                        {
        //                            departmentName = department.DepartmentName;
        //                        }
        //                    }

        //                    var firstTravel = await _context.ClaimTravels
        //                        .FirstOrDefaultAsync(x => x.ClaimId == claim.ClaimId);

        //                    string purpose = firstTravel?.Purpose ?? "-";

        //                    if (nextApprover != null &&
        //                        !string.IsNullOrWhiteSpace(nextApprover.EmailId))
        //                    {
        //                        var baseUrl = _configuration["AppSettings:BaseUrl"];

        //                        var approveUrl =
        //                            $"{baseUrl}/api/TravelMaster/mail-approve?token={nextPending.ApprovalToken}";

        //                        var rejectUrl =
        //                            $"{baseUrl}/api/TravelMaster/reject-form?token={nextPending.ApprovalToken}";
        //                        string body = $@"

        //Dear {nextApprover.FullName},

        //<br/><br/>

        //A new Local expense claim requires your approval.

        //<br/><br/>

        //<b>Employee Details</b>

        //<ul>
        //    <li>Employee Name : {employee.FullName}</li>
        //    <li>Employee ID : {employee.EmployeeCode}</li>
        //    <li>Department : {departmentName}</li>
        //</ul>

        //<b>Claim Details</b>

        //<ul>
        //    <li>Claim Number : {claim.ClaimNo}</li>
        //    <li>Claim Type : Local Expense Claim</li>
        //    <li>Claim Month : {claim.ClaimMonth:MMM-yyyy}</li>
        //    <li>Amount : ₹{claim.TotalAmount:N2}</li>
        //    <li>Status : Pending Approval</li>
        //</ul>

        //<b>Purpose</b>

        //<p>{purpose}</p>

        //<br/>

        //<a href='{approveUrl}'
        //style='background:green;
        //color:white;
        //padding:10px 20px;
        //text-decoration:none;
        //border-radius:5px;'>

        //Approve

        //</a>

        //&nbsp;&nbsp;

        //<a href='{rejectUrl}'
        //style='background:red;
        //color:white;
        //padding:10px 20px;
        //text-decoration:none;
        //border-radius:5px;'>

        //Reject

        //</a>

        //<br/><br/>

        //Regards,<br/>
        //Local Expense Claim Portal
        //";

        //                        await _emailService.SendEmailAsync(
        //                            nextApprover.EmailId,
        //                            $"Claim Approval Required - {claim.ClaimNo}",
        //                            body);
        //                    }
        //                }

        //                    return Ok(new
        //                    {
        //                        success = true,
        //                        message = "Claim approved successfully."
        //                    });
        //                }

        //            catch (Exception ex)
        //            {
        //                return StatusCode(500, new
        //                {
        //                    success = false,
        //                    message = "Error while approving claim.",
        //                    error = ex.InnerException?.Message ?? ex.Message
        //                });
        //            }
        //        }


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

                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Approval request is required."
                    });
                }

                // ==========================================
                // 2. GET EMPLOYEE ID FROM JWT
                // ==========================================

                var empIdClaim =
                    User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrWhiteSpace(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmpId not found in token."
                    });
                }

                if (!int.TryParse(empIdClaim, out int empId) ||
                    empId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid EmpId in token."
                    });
                }

                // ==========================================
                // 3. GET ORGANIZATION ID FROM JWT
                // IMPORTANT: Org_Id
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

                if (!int.TryParse(
                        orgIdClaim,
                        out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid Org_Id in token."
                    });
                }

                // ==========================================
                // 4. GET CLAIM
                // ==========================================

                var claim =
                    await _context.ClaimMasters
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

                if (!claim.Status.Equals(
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

                var approverEmployee =
                    await _commonContext.CompanyUsers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == empId &&
                            x.OrgId == organizationId &&
                            x.Status != null &&
                            x.Status.ToLower() == "active");

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
                // 7. GET THIS APPROVER'S PENDING RECORD
                // ==========================================

                var approval =
                    await _context.ClaimApprovals
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

                if (claim.CurrentApprovalLevel.HasValue &&
                    claim.CurrentApprovalLevel.Value !=
                    approval.ApprovalLevel)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "This approval level is not currently active."
                    });
                }

                // ==========================================
                // 9. GET CLAIM EMPLOYEE AND APPROVAL CONFIG
                // ==========================================

                // Get the employee who submitted the claim
                // from the Common Login database.

                var claimEmployee =
                    await _commonContext.CompanyUsers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.EmpId == claim.EmployeeId &&
                            x.OrgId == organizationId &&
                            x.Status != null &&
                            x.Status.ToLower() == "active");

                if (claimEmployee == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Claim employee not found."
                    });
                }

                //if (string.IsNullOrWhiteSpace(claimEmployee.Branch) ||
                    //string.IsNullOrWhiteSpace(claimEmployee.Department))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee branch or department is missing."
                    });
                }


                // Get department approval configuration.

                const string moduleCode = "travel_portal";

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.ModuleCode == moduleCode &&
                            //x.Branch == claimEmployee.Branch!.Trim() &&
                            x.Department == claimEmployee.Department!.Trim() &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Department approval configuration not found."
                    });
                }


                // Get the configuration for the current approval level.

                var currentConfig =
                    await _context.EmployeeApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == organizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.EmployeeId == claim.EmployeeId &&
                            x.ApprovalLevel == approval.ApprovalLevel &&
                            x.IsActive);

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

                // ==========================================
                // 10. APPROVAL RULE - ANYONE ONLY
                // ==========================================

                string approvalMode =
                    currentConfig.ApprovalRule?.Trim() ?? "";

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
                // 12. CHECK CURRENT LEVEL
                // ==========================================

                bool currentLevelCompleted = false;

                // ==========================================
                // 13. ANYONE MODE
                // ==========================================

                if (approvalMode.Equals(
                        "AnyOne",
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentLevelCompleted = true;

                    var sameLevelPending =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == claim.ClaimId &&
                                x.ApprovalLevel ==
                                    approval.ApprovalLevel &&
                                x.Status == "Pending")
                            .ToListAsync();

                    foreach (var item in sameLevelPending)
                    {
                        item.Status = "Skipped";

                        item.Remarks =
                            "Skipped because another approver approved this level.";

                        item.ActionDate = DateTime.Now;
                    }

                    if (sameLevelPending.Any())
                    {
                        await _context.SaveChangesAsync();
                    }
                }

                // ==========================================
                // 14. ALL MODE
                // ==========================================

                else if (approvalMode.Equals(
                             "All",
                             StringComparison.OrdinalIgnoreCase))
                {
                    var sameLevelPending =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == claim.ClaimId &&
                                x.ApprovalLevel ==
                                    approval.ApprovalLevel &&
                                x.Status == "Pending")
                            .ToListAsync();

                    if (!sameLevelPending.Any())
                    {
                        currentLevelCompleted = true;
                    }
                    else
                    {
                        currentLevelCompleted = false;
                    }
                }

                // ==========================================
                // 15. SEQUENTIAL MODE
                // ==========================================

                else
                {
                    currentLevelCompleted = true;
                }

                // ==========================================
                // 16. CURRENT LEVEL NOT COMPLETE
                // MAINLY FOR ALL MODE
                // ==========================================

                if (!currentLevelCompleted)
                {
                    claim.CurrentApprovalLevel =
                        approval.ApprovalLevel;

                    claim.LastModifiedAt =
                        DateTime.Now;

                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        message =
                            "Approval recorded. Waiting for remaining approvers.",
                        data = new
                        {
                            claimId = claim.ClaimId,
                            claimNo = claim.ClaimNo,
                            approvalLevel =
                                approval.ApprovalLevel,
                            claimStatus =
                                claim.Status,
                            currentApprovalLevel =
                                claim.CurrentApprovalLevel
                        }
                    });
                }

                // ==========================================
                // 17. FIND NEXT APPROVAL LEVEL
                // ==========================================

                var nextLevel =
                    await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claim.ClaimId &&
                            x.Status == "Pending" &&
                            x.ApprovalLevel >
                                approval.ApprovalLevel)
                        .Select(x => (int?)x.ApprovalLevel)
                        .MinAsync();

                // ==========================================
                // 18. NO NEXT LEVEL
                // FINAL APPROVAL
                // ==========================================

                if (!nextLevel.HasValue)
                {
                    // There should not normally be pending records
                    // after the last completed level.
                    // Mark any unexpected pending records as skipped.

                    var remainingApprovals =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == claim.ClaimId &&
                                x.Status == "Pending")
                            .ToListAsync();

                    foreach (var item in remainingApprovals)
                    {
                        item.Status = "Skipped";

                        item.Remarks =
                            "Skipped after final approval.";

                        item.ActionDate = DateTime.Now;
                    }

                    // ======================================
                    // FINAL CLAIM APPROVAL
                    // ======================================

                    claim.Status = "Approved";

                    claim.ApprovedAt =
                        DateTime.Now;

                    claim.CurrentApprovalLevel =
                        null;

                    claim.LastModifiedAt =
                        DateTime.Now;

                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        message =
                            "Claim approved successfully.",
                        data = new
                        {
                            claimId =
                                claim.ClaimId,

                            claimNo =
                                claim.ClaimNo,

                            status =
                                claim.Status,

                            approvedAt =
                                claim.ApprovedAt
                        }
                    });
                }

                // ==========================================
                // 19. GET ALL APPROVERS
                // FOR NEXT LEVEL
                // ==========================================

                var nextLevelApprovals =
                    await _context.ClaimApprovals
                        .Where(x =>
                            x.ClaimId == claim.ClaimId &&
                            x.ApprovalLevel ==
                                nextLevel.Value &&
                            x.Status == "Pending" &&
                            x.ApproverEmpId.HasValue)
                        .OrderBy(x => x.ApproverEmpId)
                        .ToListAsync();

                if (!nextLevelApprovals.Any())
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
                // 20. MOVE CLAIM TO NEXT LEVEL
                // ==========================================

                claim.CurrentApprovalLevel =
                    nextLevel.Value;

                claim.LastModifiedAt =
                    DateTime.Now;

                await _context.SaveChangesAsync();

                // ==========================================
                // 21. GET CLAIM EMPLOYEE
                // ==========================================
                // ==========================================
                // 2. DEPARTMENT NAME
                // ==========================================

                string departmentName = claimEmployee.Department ?? "-";

                // ==========================================
                // 23. PURPOSE
                // ==========================================

                var firstTravel =
                    await _context.ClaimTravels
                        .FirstOrDefaultAsync(x =>
                            x.ClaimId == claim.ClaimId);

                string purpose =
                    firstTravel?.Purpose ?? "-";

                // ==========================================
                // 24. SEND EMAIL TO ALL NEXT LEVEL APPROVERS
                // ==========================================

                int emailSentCount = 0;
                int emailFailedCount = 0;

                var baseUrl =
                    _configuration["AppSettings:BaseUrl"];

                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "AppSettings:BaseUrl is not configured."
                    });
                }

                foreach (var nextApproval
                         in nextLevelApprovals)
                {
                    var nextApprover =
     await _commonContext.CompanyUsers
         .AsNoTracking()
         .FirstOrDefaultAsync(x =>
             x.EmpId == nextApproval.ApproverEmpId.Value &&
             x.OrgId == organizationId &&
             x.Status != null &&
             x.Status.ToLower() == "active");

                    if (nextApprover == null)
                    {
                        emailFailedCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(nextApprover.Email))
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
    <li>
        Employee Name : {claimEmployee.FullName}
    </li>

    <li>
        Employee ID : {claimEmployee.EmpCode}
    </li>

    <li>
        Department : {departmentName}
    </li>
</ul>

<b>Claim Details</b>

<ul>
    <li>
        Claim Number : {claim.ClaimNo}
    </li>

    <li>
        Claim Type : Local Expense Claim
    </li>

    <li>
        Claim Month : {claim.ClaimMonth:MMM-yyyy}
    </li>

    <li>
        Amount : ₹{claim.TotalAmount:N2}
    </li>

    <li>
        Status : Pending Approval
    </li>

    <li>
        Approval Level : {nextLevel.Value}
    </li>
</ul>

<b>Purpose</b>

<p>
    {purpose}
</p>

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
                            nextApprover.Email,
                            $"Claim Approval Required - {claim.ClaimNo}",
                            body);

                        emailSentCount++;
                    }
                    catch
                    {
                        emailFailedCount++;
                    }
                }

                // ==========================================
                // 25. COMMIT
                // ==========================================

                await transaction.CommitAsync();

                // ==========================================
                // 26. SUCCESS RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,

                    message =
                        "Claim approved and moved to the next approval level.",

                    data = new
                    {
                        claimId =
                            claim.ClaimId,

                        claimNo =
                            claim.ClaimNo,

                        approvedLevel =
                            approval.ApprovalLevel,

                        nextApprovalLevel =
                            nextLevel.Value,

                        status =
                            claim.Status,

                        currentApprovalLevel =
                            claim.CurrentApprovalLevel,

                        nextLevelApproverCount =
                            nextLevelApprovals.Count,

                        emailSentCount =
                            emailSentCount,

                        emailFailedCount =
                            emailFailedCount
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Error while approving claim.",
                    error =
                        ex.InnerException?.Message
                        ?? ex.Message
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
                var orgIdClaim = User.FindFirst("orgId")?.Value;
                // ==========================================
                // FIND PENDING APPROVAL BY TOKEN
                // ==========================================

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

                // ==========================================
                // GET CLAIM
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == approval.ClaimId &&
                        x.IsActive);

                if (claim == null)
                {
                    return Content("Claim not found.");
                }

                // ==========================================
                // CHECK CLAIM STATUS
                // ==========================================

                if (claim.Status != "Pending")
                {
                    return Content("This claim is no longer pending.");
                }

                // ==========================================
                // REJECT CURRENT APPROVAL
                // ==========================================

                approval.Status = "Rejected";

                approval.Remarks =
                    string.IsNullOrWhiteSpace(reason)
                        ? "Rejected From Email"
                        : reason.Trim();

                approval.ActionDate = DateTime.Now;

                // ==========================================
                // UPDATE CLAIM
                // ==========================================

                claim.Status = "Rejected";
                claim.RejectedAt = DateTime.Now;
                claim.CurrentApprovalLevel = null;
                claim.LastModifiedAt = DateTime.Now;

                // ==========================================
                // CANCEL FUTURE APPROVALS
                // ==========================================

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

                // ==========================================
                // SAVE
                // ==========================================

                await _context.SaveChangesAsync();

                // ==========================================
                // SUCCESS PAGE
                // ==========================================

                var safeReason =
                    string.IsNullOrWhiteSpace(reason)
                        ? "Rejected From Email"
                        : reason.Trim();

                return Content($@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <meta charset='utf-8'>
    <title>Claim Rejected</title>
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

{System.Net.WebUtility.HtmlEncode(safeReason)}

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
<p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>
</body>
</html>",
                    "text/html");
            }
        }

        [HttpGet("mail-approve")]
        public async Task<IActionResult> MailApprove(Guid token)
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;
                // ==========================================
                // FIND CURRENT PENDING APPROVAL BY TOKEN
                // ==========================================

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
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta charset='utf-8'>
    <title>Invalid Approval Link</title>
</head>

<body style='font-family:Arial,sans-serif;
background:#f4f6f9;
display:flex;
justify-content:center;
align-items:center;
height:100vh;
margin:0;'>

<div style='background:white;
padding:30px;
border-radius:12px;
box-shadow:0 2px 10px rgba(0,0,0,.15);
text-align:center;
width:90%;
max-width:400px;'>

<div style='font-size:60px;color:red;'>✖</div>

<h2 style='color:red;'>
Invalid Approval Link
</h2>

<p style='color:#555;'>
This approval link has already been used or is no longer valid.
</p>

</div>

</body>
</html>",
                        "text/html");
                }

                // ==========================================
                // GET CLAIM
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == approval.ClaimId &&
                        x.IsActive);

                if (claim == null)
                {
                    return Content("Claim Not Found");
                }

                // ==========================================
                // CHECK CLAIM STATUS
                // ==========================================

                if (claim.Status != "Pending")
                {
                    return Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>

<body style='font-family:Arial;text-align:center;padding-top:80px;background:#f4f6f9;'>

<div style='background:white;
max-width:400px;
margin:auto;
padding:30px;
border-radius:12px;
box-shadow:0 2px 10px rgba(0,0,0,.15);'>

<h2 style='color:#d97706;'>
Claim Already Processed
</h2>

<p>
This claim is no longer pending approval.
</p>

</div>

</body>
</html>",
                        "text/html");
                }
                // ==========================================
                // 11. VALIDATE ACTIVE APPROVAL LEVEL
                // ==========================================

                if (!claim.CurrentApprovalLevel.HasValue ||
                    claim.CurrentApprovalLevel.Value != approval.ApprovalLevel)
                {
                    return Content(
                        "This approval link is not for the current active approval level.");
                }


                // ==========================================
                // 12. GET CLAIM EMPLOYEE FROM COMMON DB
                // ==========================================

                var claimEmployee = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId &&
                        x.OrgId == claim.OrganizationId &&
                        x.Status != null &&
                        x.Status.ToLower() == "active");

                if (claimEmployee == null)
                {
                    return Content("Claim employee not found.");
                }

                //if (string.IsNullOrWhiteSpace(claimEmployee.Branch) ||
                    //string.IsNullOrWhiteSpace(claimEmployee.Department))
                {
                    return Content("Employee branch or department is missing.");
                }


                // ==========================================
                // 13. GET DEPARTMENT APPROVAL CONFIGURATION
                // ==========================================

                const string moduleCode = "travel_portal";

                var departmentConfig =
                    await _context.DepartmentApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == claim.OrganizationId &&
                            x.ModuleCode == moduleCode &&
                            //x.Branch == claimEmployee.Branch.Trim() &&
                            x.Department == claimEmployee.Department.Trim() &&
                            x.IsActive);

                if (departmentConfig == null)
                {
                    return Content(
                        "Department approval configuration not found.");
                }


                // ==========================================
                // 14. GET EMPLOYEE APPROVAL CONFIGURATION
                // ==========================================

                var currentConfig =
                    await _context.EmployeeApprovalConfigs
                        .FirstOrDefaultAsync(x =>
                            x.OrganizationId == claim.OrganizationId &&
                            x.DepartmentApprovalConfigId == departmentConfig.Id &&
                            x.EmployeeId == claim.EmployeeId &&
                            x.ApprovalLevel == approval.ApprovalLevel &&
                            x.IsActive);

                if (currentConfig == null)
                {
                    return Content(
                        $"Employee approval configuration not found for Level {approval.ApprovalLevel}.");
                }


                // ==========================================
                // 15. ANYONE APPROVAL RULE
                // ==========================================

                string approvalMode =
                    currentConfig.ApprovalRule?.Trim() ?? "";

                if (!approvalMode.Equals(
                        "AnyOne",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Content(
                        "Only AnyOne approval rule is currently supported.");
                }

                // ==========================================
                // APPROVE CURRENT RECORD
                // ==========================================

                approval.Status = "Approved";
                approval.Remarks = "Approved From Email";
                approval.ActionDate = DateTime.Now;

                await _context.SaveChangesAsync();

                // ==========================================
                // CHECK CURRENT LEVEL
                // ==========================================

                bool currentLevelCompleted = false;

                // ==========================================
                // ANY ONE
                // ==========================================

                if (approvalMode.Equals(
                        "AnyOne",
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentLevelCompleted = true;

                    // Other same-level pending approvals
                    // are no longer required.

                    var sameLevelPending =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == approval.ClaimId &&
                                x.ApprovalLevel == approval.ApprovalLevel &&
                                x.Status == "Pending")
                            .ToListAsync();

                    foreach (var item in sameLevelPending)
                    {
                        item.Status = "Skipped";
                        item.Remarks = "Skipped because another approver approved this level.";
                        item.ActionDate = DateTime.Now;
                    }

                    if (sameLevelPending.Count > 0)
                    {
                        await _context.SaveChangesAsync();
                    }
                }

                // ==========================================
                // ALL MODE - WAIT FOR OTHER APPROVERS
                // ==========================================

                if (!currentLevelCompleted)
                {
                    claim.CurrentApprovalLevel = approval.ApprovalLevel;
                    claim.LastModifiedAt = DateTime.Now;

                    await _context.SaveChangesAsync();

                    return Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta charset='utf-8'>
    <title>Approval Recorded</title>
</head>

<body style='font-family:Arial,sans-serif;
background:#f4f6f9;
display:flex;
justify-content:center;
align-items:center;
height:100vh;
margin:0;'>

<div style='background:white;
padding:30px;
border-radius:12px;
box-shadow:0 2px 10px rgba(0,0,0,.15);
text-align:center;
width:90%;
max-width:400px;'>

<div style='font-size:60px;color:#f59e0b;'>✓</div>

<div style='font-size:24px;
font-weight:bold;
color:#f59e0b;
margin-top:10px;'>
Approval Recorded
</div>

<p style='color:#555;'>
Your approval has been recorded.
Waiting for the remaining approvers at this level.
</p>

</div>

</body>
</html>",
                        "text/html");
                }

                // ==========================================
                // FIND NEXT PENDING APPROVAL
                // ==========================================

                var nextPending = await _context.ClaimApprovals
                    .Where(x =>
                        x.ClaimId == approval.ClaimId &&
                        x.Status == "Pending" &&
                        x.ApproverEmpId.HasValue &&
                        x.ApprovalLevel > approval.ApprovalLevel)
                    .OrderBy(x => x.ApprovalLevel)
                    .FirstOrDefaultAsync();

                // ==========================================
                // NO NEXT LEVEL
                // FINAL APPROVAL
                // ==========================================

                if (nextPending == null)
                {
                    // Any leftover pending records should not block
                    // final completion.

                    var remainingApprovals =
                        await _context.ClaimApprovals
                            .Where(x =>
                                x.ClaimId == approval.ClaimId &&
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

                    return Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta charset='utf-8'>
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
    color:green;
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

    <div class='icon'>✔</div>

    <div class='title'>
        Claim Approved
    </div>

    <div class='message'>
        The claim has been approved successfully.
    </div>

</div>

</body>
</html>",
                        "text/html");
                }

                // ==========================================
                // MOVE CLAIM TO NEXT LEVEL
                // ==========================================

                claim.CurrentApprovalLevel = nextPending.ApprovalLevel;
                claim.LastModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();
                // ==========================================
                // GET NEXT APPROVER FROM COMMON LOGIN DB
                // ==========================================

                var nextApprover = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == nextPending.ApproverEmpId.Value &&
                        x.OrgId == claim.OrganizationId &&
                        x.Status != null &&
                        x.Status.ToLower() == "active");

                if (nextApprover == null)
                {
                    return Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta charset='utf-8'>
    <title>Next Approver Not Found</title>
</head>

<body style='font-family:Arial;text-align:center;padding-top:80px;background:#f4f6f9;'>

<div style='background:white;
max-width:450px;
margin:auto;
padding:30px;
border-radius:12px;
box-shadow:0 2px 10px rgba(0,0,0,.15);'>

<h2 style='color:#d97706;'>
Next Approver Not Found
</h2>

<p>
Approval moved to the next level, but the configured approver could not be found.
</p>

</div>

</body>
</html>",
                        "text/html");
                }
                // ==========================================
                // GET CLAIM EMPLOYEE
                // ==========================================

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == claim.EmployeeId &&
                        x.OrganizationId == claim.OrganizationId &&
                        x.IsActive);

                if (employee == null)
                {
                    return Content("Claim Employee Not Found");
                }

                // ==========================================
                // GET DEPARTMENT
                // ==========================================

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

                // ==========================================
                // GET PURPOSE
                // ==========================================

                string purpose = "-";

                var firstTravel = await _context.ClaimTravels
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == claim.ClaimId);

                if (firstTravel != null &&
                    !string.IsNullOrWhiteSpace(firstTravel.Purpose))
                {
                    purpose = firstTravel.Purpose;
                }

                // ==========================================
                // SEND MAIL TO NEXT APPROVER
                // ==========================================

                if (!string.IsNullOrWhiteSpace(nextApprover.Email))
                {
                    var baseUrl =
                        _configuration["AppSettings:BaseUrl"];

                    var approveUrl =
                        $"{baseUrl}/api/TravelMaster/mail-approve?token={nextPending.ApprovalToken}";

                    var rejectUrl =
                        $"{baseUrl}/api/TravelMaster/reject-form?token={nextPending.ApprovalToken}";

                    string body = $@"

Dear {nextApprover.FullName},

<br/><br/>

A Local Expense Claim requires your approval.

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
    <li>Approval Level : {nextPending.ApprovalLevel}</li>
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
                        nextApprover.Email,
                        $"Claim Approval Required - {claim.ClaimNo}",
                        body);
                }

                // ==========================================
                // SUCCESS PAGE
                // ==========================================

                return Content($@"
<!DOCTYPE html>
<html>

<head>

<meta name='viewport'
      content='width=device-width, initial-scale=1.0'>

<meta charset='utf-8'>

<title>Claim Approved</title>

<style>

body{{
    font-family:Arial,sans-serif;
    background:#f4f6f9;
    display:flex;
    justify-content:center;
    align-items:center;
    height:100vh;
    margin:0;
}}

.card{{
    background:white;
    padding:30px;
    border-radius:12px;
    box-shadow:0 2px 10px rgba(0,0,0,0.15);
    text-align:center;
    width:90%;
    max-width:400px;
}}

.icon{{
    font-size:60px;
    color:green;
}}

.title{{
    color:green;
    font-size:24px;
    font-weight:bold;
    margin-top:10px;
}}

.message{{
    margin-top:10px;
    color:#555;
}}

</style>

</head>

<body>

<div class='card'>

    <div class='icon'>✔</div>

    <div class='title'>
        Approval Completed
    </div>

    <div class='message'>
        Your approval has been recorded successfully.
        The claim has moved to the next approval level.
    </div>

</div>

</body>

</html>",
                    "text/html");
            }
            catch (Exception ex)
            {
                return Content($@"
<!DOCTYPE html>
<html>

<head>
    <meta name='viewport'
          content='width=device-width, initial-scale=1.0'>
</head>

<body style='font-family:Arial;
text-align:center;
padding-top:70px;
background:#f4f6f9;'>

<div style='max-width:450px;
margin:auto;
background:white;
padding:30px;
border-radius:12px;
box-shadow:0 2px 10px rgba(0,0,0,.15);'>

<h2 style='color:red;'>
Error
</h2>

<p>
{System.Net.WebUtility.HtmlEncode(
            ex.InnerException?.Message ?? ex.Message)}
</p>

</div>

</body>
</html>",
                    "text/html");
            }
        }
        [Authorize]
        [HttpPost("reject-claim")]
        public async Task<IActionResult> RejectClaim(
       [FromBody] ClaimApprovalActionDto dto)
        {
            try
            {
               
                // ==========================================
                // VALIDATE REQUEST
                // ==========================================

                if (dto == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Rejection request is required."
                    });
                }

                // ==========================================
                // GET EMPLOYEE ID FROM JWT
                // ==========================================

                var empIdClaim = User.FindFirst("EmpId")?.Value;

                if (string.IsNullOrWhiteSpace(empIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "EmpId not found in token."
                    });
                }

                if (!int.TryParse(empIdClaim, out int empId) || empId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid EmpId in token."
                    });
                }

                // ==========================================
                // GET ORGANIZATION ID FROM JWT
                // ==========================================

                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrWhiteSpace(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "orgId not found in token."
                    });
                }

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid Org_Id in token."
                    });
                }

                // ==========================================
                // GET CLAIM
                // ==========================================

                var claim = await _context.ClaimMasters
                    .FirstOrDefaultAsync(x =>
                        x.ClaimId == dto.ClaimId &&
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
                // CHECK CLAIM STATUS
                // ==========================================

                if (claim.Status != "Pending")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"Claim cannot be rejected because its current status is '{claim.Status}'."
                    });
                }

                // ==========================================
                // GET APPROVER EMPLOYEE
                // ==========================================

                var approverEmployee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == empId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (approverEmployee == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Approver employee not found in the current organization."
                    });
                }

                // ==========================================
                // GET CURRENT PENDING APPROVAL
                // ==========================================

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
                        message = "Approval record not found or already processed."
                    });
                }

                // ==========================================
                // VERIFY CURRENT LEVEL
                // ==========================================

                if (claim.CurrentApprovalLevel.HasValue &&
                    claim.CurrentApprovalLevel.Value != approval.ApprovalLevel)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This approval level is not currently active."
                    });
                }

                // ==========================================
                // REJECT CURRENT APPROVAL
                // ==========================================

                approval.Status = "Rejected";

                approval.Remarks =
                    string.IsNullOrWhiteSpace(dto.Remarks)
                        ? "NA"
                        : dto.Remarks.Trim();

                approval.ActionDate = DateTime.Now;

                // ==========================================
                // UPDATE CLAIM
                // ==========================================

                claim.Status = "Rejected";
                claim.RejectedAt = DateTime.Now;
                claim.CurrentApprovalLevel = null;
                claim.LastModifiedAt = DateTime.Now;

                // ==========================================
                // CANCEL FUTURE APPROVALS
                // ==========================================

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

                // ==========================================
                // SAVE
                // ==========================================

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Claim rejected successfully.",
                    data = new
                    {
                        claimId = claim.ClaimId,
                        claimNo = claim.ClaimNo,
                        status = claim.Status,
                        rejectedAt = claim.RejectedAt,
                        rejectedLevel = approval.ApprovalLevel
                    }
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

        [HttpGet("approval-setup/modules")]
        public async Task<IActionResult> GetApprovalSetupModules()
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value
                                 ?? User.FindFirst("OrganizationId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId)
                    || organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        message = "Invalid organization ID."
                    });
                }

                var modules = await
                    (from organizationModule in
                        _commonContext.OrganizationModules.AsNoTracking()

                     join module in
                        _commonContext.ModuleMasters.AsNoTracking()

                     on organizationModule.ModuleCode equals module.ModuleCode

                     where organizationModule.OrgId == organizationId
                           && organizationModule.IsActive
                           && module.IsActive

                     select new
                     {
                         ModuleCode = module.ModuleCode,
                         ModuleName = module.ModuleName
                     })
                    .Distinct()
                    .OrderBy(x => x.ModuleName)
                    .ToListAsync();

                return Ok(modules);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Unable to fetch modules.",
                    error = ex.Message
                });
            }
        }

        //[HttpGet("approval-setup/branches")]
        //public async Task<IActionResult> GetApprovalSetupBranches()
        //{
        //    try
        //    {
        //        var orgIdClaim = User.FindFirst("orgId")?.Value
        //                         ?? User.FindFirst("OrganizationId")?.Value;

        //        if (!int.TryParse(orgIdClaim, out int organizationId)
        //            || organizationId <= 0)
        //        {
        //            return Unauthorized(new
        //            {
        //                message = "Invalid organization ID."
        //            });
        //        }

        //        //var branches = await _commonContext.CompanyUsers
        //        //    .AsNoTracking()
        //        //    .Where(x =>
        //        //        x.OrgId == organizationId &&
        //        //        x.Status != null &&()
        //        //        //x.Status.ToLower() == "active" &&
        //        //        //x.Branch != null &&
        //        //        //x.Branch.Trim() != "")
        //        //    //.Select(x => x.Branch!.Trim())
        //        //    .Distinct()
        //        //    .OrderBy(x => x)
        //        //    .ToListAsync();

        //        //return Ok(branches);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "Unable to fetch branches.",
        //            error = ex.Message
        //        });
        //    }
        //}

        [Authorize]
        [HttpGet("approval-setup/departments")]
        public async Task<IActionResult> GetApprovalSetupDepartments(
            [FromQuery] string branch)
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (!int.TryParse(orgIdClaim, out int organizationId) ||
                    organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                if (string.IsNullOrWhiteSpace(branch))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Branch is required."
                    });
                }

                var departments = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        x.Status != null &&
                        x.Status.ToLower() == "active" &&
                        //x.Branch != null &&
                        //x.Branch.Trim() == branch.Trim() &&
                        x.Department != null &&
                        x.Department.Trim() != "")
                    .Select(x => x.Department!.Trim())
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = departments
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to fetch departments."
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

        private async Task<IActionResult?> ValidateCompanyExpenseAdminAsync(
      int organizationId)
        {
            try
            {
                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // Read Company Expense role from portal_roles.
                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Company Expense portal role not found."
                    });
                }

                bool isAdmin =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    return Forbid();
                }

                // Verify Company Expense is enabled for this organization.
                bool moduleEnabled =
                    await _commonContext.OrganizationModules
                        .AsNoTracking()
                        .AnyAsync(x =>
                            x.OrgId == organizationId &&
                            x.ModuleCode == "company_expenses" &&
                            x.IsActive);

                if (!moduleEnabled)
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        message =
                            "Company Expense module is not enabled for this organization."
                    });
                }

                return null;
            }
            catch
            {
                throw;
            }
        }


        [Authorize]
        [HttpGet("advance-summary/departments")]
        public async Task<IActionResult> GetAdvanceSummaryDepartments()
        {
            try
            {
                // Login token-la irukkura OrganizationId
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in login token."
                    });
                }

                // Andha organization-oda employees mattum fetch pannuvom.
                // Company Expense module validation inga repeat panna vendam.
                var employees = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(e =>
                        e.OrgId == organizationId &&
                        (e.Status == null ||
                         e.Status.ToLower() == "active"))
                    .OrderBy(e => e.Department)
                    .ThenBy(e => e.FullName)
                    .Select(e => new
                    {
                        employeeId = e.EmpId,
                        employeeCode = e.EmpCode,
                        employeeName = e.FullName,
                        department = e.Department == null
                            ? null
                            : e.Department.Trim()
                    })
                    .ToListAsync();

                // Employees data-lendhu unique department list
                var departments = employees
                    .Select(e => e.department)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => d!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(d => d)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    organizationId,
                    departments,
                    employees
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to load departments and employees."
                });
            }
        }

        [Authorize]
        [HttpGet("advance-summary/employees")]
        public async Task<IActionResult> GetAdvanceSummaryEmployees(
     [FromQuery] string? department)
        {
            try
            {
                // Login token-la irukkura OrganizationId
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization in login token."
                    });
                }

                // Company Expense portal role mattum validate pannuvom.
                // Module validation helper-ai inga call panna vendam.
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

                // Token-la irukkura organization-oda employees mattum
                var query = _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        (x.Status == null ||
                         x.Status.Trim().ToUpper() == "ACTIVE"));

                // Department filter optional
                if (!string.IsNullOrWhiteSpace(department))
                {
                    string selectedDepartment = department.Trim().ToUpper();

                    query = query.Where(x =>
                        x.Department != null &&
                        x.Department.Trim().ToUpper() == selectedDepartment);
                }

                var employees = await query
                    .OrderBy(x => x.FullName)
                    .Select(x => new
                    {
                        employeeId = x.EmpId,
                        employeeCode = x.EmpCode,
                        employeeName = x.FullName,
                        department = x.Department
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    organizationId,
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
       [FromQuery] string? department = null,
       [FromQuery] int? employeeId = null)
        {
            try
            {
                // 1. GET ORGANIZATION ID FROM JWT
                int organizationId = GetOrganizationId();

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid organization ID."
                    });
                }

                // 2. GET COMPANY EXPENSE PORTAL ROLE
                var role = GetCompanyExpenseRole();

                if (string.IsNullOrWhiteSpace(role))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Company Expense portal role not found."
                    });
                }

                bool isAdmin =
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    return Forbid();
                }

                // NOTE:
                // OrganizationModules table-la module access check
                // inga repeat panna vendam.
                // OrganizationId-based filtering keezha maintain pannirukkom.

                // 3. VALIDATE FILTERS
                department = string.IsNullOrWhiteSpace(department)
                    ? null
                    : department.Trim();

                if (employeeId.HasValue && employeeId.Value <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid EmployeeId."
                    });
                }

                // 4. GET EMPLOYEES FROM COMMON LOGIN DB
                List<int>? employeeIds = null;

                if (department != null || employeeId.HasValue)
                {
                    var employeeQuery = _commonContext.CompanyUsers
                        .AsNoTracking()
                        .Where(x =>
                            x.OrgId == organizationId &&
                            (
                                x.Status == null ||
                                x.Status.Trim().ToUpper() == "ACTIVE"
                            ));

                    if (department != null)
                    {
                        string selectedDepartment = department.ToLower();

                        employeeQuery = employeeQuery.Where(x =>
                            x.Department != null &&
                            x.Department.Trim().ToLower() == selectedDepartment);
                    }

                    if (employeeId.HasValue)
                    {
                        employeeQuery = employeeQuery.Where(x =>
                            x.EmpId == employeeId.Value);
                    }

                    employeeIds = await employeeQuery
                        .Select(x => x.EmpId)
                        .Distinct()
                        .ToListAsync();

                    if (employeeId.HasValue && employeeIds.Count == 0)
                    {
                        return NotFound(new
                        {
                            success = false,
                            message =
                                "Employee not found in the selected organization or department."
                        });
                    }
                }

                // 5. ADVANCE QUERY
                // Organization-specific active advance records
                var advanceQuery = _context.ExpenseMaster
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.Status == "Active" &&
                        x.AssignedTo.HasValue);

                if (employeeIds != null)
                {
                    advanceQuery = advanceQuery.Where(x =>
                        employeeIds.Contains(x.AssignedTo!.Value));
                }

                // 6. SPENT QUERY
                // Organization-specific active spent records
                var spentQuery = _context.SpentAmounts
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive &&
                        x.employee_id.HasValue);

                if (employeeIds != null)
                {
                    spentQuery = spentQuery.Where(x =>
                        employeeIds.Contains(x.employee_id!.Value));
                }

                // 7. CURRENT MONTH AND YEAR
                var now = DateTime.Now;

                var monthStart = new DateTime(
                    now.Year,
                    now.Month,
                    1);

                var nextMonthStart = monthStart.AddMonths(1);

                var yearStart = new DateTime(
                    now.Year,
                    1,
                    1);

                var nextYearStart = yearStart.AddYears(1);

                // 8. MONTHLY AND YEARLY ADVANCE SUMMARY
                decimal monthAdvance = await advanceQuery
                    .Where(x =>
                        x.CreatedDate >= monthStart &&
                        x.CreatedDate < nextMonthStart)
                    .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                decimal yearAdvance = await advanceQuery
                    .Where(x =>
                        x.CreatedDate >= yearStart &&
                        x.CreatedDate < nextYearStart)
                    .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0m;

                // 9. MONTHLY AND YEARLY SPENT SUMMARY
                decimal monthSpent = await spentQuery
                    .Where(x =>
                        x.Date >= monthStart &&
                        x.Date < nextMonthStart)
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                decimal yearSpent = await spentQuery
                    .Where(x =>
                        x.Date >= yearStart &&
                        x.Date < nextYearStart)
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                // 10. GET ADVANCE TABLE RECORDS
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

                // 11. GET SPENT TABLE RECORDS
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

                // 12. COLLECT EMPLOYEE IDS
                var relatedEmployeeIds = advanceRecords
                    .Where(x => x.AssignedTo.HasValue)
                    .Select(x => x.AssignedTo!.Value)
                    .Concat(
                        advanceRecords
                            .Where(x => x.AssignedBy.HasValue)
                            .Select(x => x.AssignedBy!.Value))
                    .Concat(
                        spentRecords
                            .Where(x => x.employee_id.HasValue)
                            .Select(x => x.employee_id!.Value))
                    .Distinct()
                    .ToList();

                // 13. GET EMPLOYEE DETAILS
                // Common DB-la same organization employees mattum
                var people = await _commonContext.CompanyUsers
                    .AsNoTracking()
                    .Where(x =>
                        x.OrgId == organizationId &&
                        relatedEmployeeIds.Contains(x.EmpId))
                    .Select(x => new
                    {
                        x.EmpId,
                        x.EmpCode,
                        x.FullName,
                        x.Department
                    })
                    .ToListAsync();

                var peopleById = people.ToDictionary(x => x.EmpId);

                // 14. GET PURCHASE TYPE NAMES
                var purchaseTypes = await _context.PurchaseTypes
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId)
                    .Select(x => new
                    {
                        x.Id,
                        x.Name
                    })
                    .ToListAsync();

                var purchaseTypeById = purchaseTypes
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First().Name);

                // 15. ADVANCE AMOUNT TABLE
                var advanceDetails = advanceRecords
                    .Select(x =>
                    {
                        peopleById.TryGetValue(
                            x.AssignedTo ?? 0,
                            out var employee);

                        peopleById.TryGetValue(
                            x.AssignedBy ?? 0,
                            out var assignedBy);

                        return new
                        {
                            expenseId = x.ExpenseId,
                            date = x.CreatedDate,
                            employeeId = x.AssignedTo,

                            employeeCode = employee?.EmpCode ?? "-",
                            employeeName = employee?.FullName ?? "-",
                            department = employee?.Department ?? "-",

                            assignedBy = assignedBy?.FullName ?? "Admin",

                            advanceAmount = x.ExpenseAmount,
                            purpose = x.Purpose,
                            remarks = x.Remarks
                        };
                    })
                    .ToList();

                // 16. SPENT AMOUNT TABLE
                var spentDetails = spentRecords
                    .Select(x =>
                    {
                        peopleById.TryGetValue(
                            x.employee_id ?? 0,
                            out var employee);

                        purchaseTypeById.TryGetValue(
                            x.PurchaseItem,
                            out var purchaseTypeName);

                        return new
                        {
                            spentId = x.Id,
                            date = x.Date,
                            employeeId = x.employee_id,

                            employeeCode = employee?.EmpCode ?? "-",
                            employeeName = employee?.FullName ?? "-",
                            department = employee?.Department ?? "-",

                            purchaseType = purchaseTypeName ?? "Others",
                            purchaseItemId = x.PurchaseItem,

                            spentAmount = x.Amount,
                            remarks = x.Remarks,
                            billFile = x.BillFile
                        };
                    })
                    .ToList();

                // 17. FINAL RESPONSE
                return Ok(new
                {
                    success = true,
                    organizationId,

                    filters = new
                    {
                        department,
                        employeeId
                    },

                    summary = new
                    {
                        MonthAdvance = monthAdvance,
                        YearAdvance = yearAdvance,

                        MonthSpent = monthSpent,
                        YearSpent = yearSpent,

                        MonthBalance = monthAdvance - monthSpent,
                        YearBalance = yearAdvance - yearSpent
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
                    message =
                        "Error while fetching filtered Company Expense summary.",
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

                // ==========================================
                // 2. Company Expense Role
                //    FROM portal_roles
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
                // ==========================================
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;

                decimal monthReceived = 0;
                decimal yearReceived = 0;

                decimal monthExpense = 0;
                decimal yearExpense = 0;

                // ==========================================
                // 6. ACCESS
                // ==========================================
                bool fullAccess = false;

                if (isAdmin)
                {
                    // Admin has full organization access
                    fullAccess = true;
                }
                else
                {
                   

                    if (role != "Employee") 
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
                // ==========================================

                if (fullAccess)
                {
                    // Admin / Full Access
                    monthReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Month == currentMonth &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

                    yearReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
                }
                else
                {
                    // Employee / Limited Access
                    monthReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.AssignedTo == employeeId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Month == currentMonth &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;

                    yearReceived = await _context.ExpenseMaster
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.AssignedTo == employeeId &&
                            x.Status == "Active" &&
                            x.CreatedDate.Year == currentYear)
                        .SumAsync(x => (decimal?)x.ExpenseAmount) ?? 0;
                }

                // ==========================================
                // 8. SPENT / EXPENSE
                // ==========================================

                if (fullAccess)
                {
                    // Admin / Full Access
                    monthExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.Date.Month == currentMonth &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;

                    yearExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;
                }
                else
                {
                    // Employee / Limited Access
                    monthExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.employee_id == employeeId &&
                            x.Date.Month == currentMonth &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;

                    yearExpense = await _context.SpentAmounts
                        .Where(x =>
                            x.OrganizationId == organizationId &&
                            x.IsActive &&
                            x.employee_id == employeeId &&
                            x.Date.Year == currentYear)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;
                }

                // ==========================================
                // 9. BALANCE
                // ==========================================
                var monthBalance = monthReceived - monthExpense;
                var yearBalance = yearReceived - yearExpense;

                // ==========================================
                // 10. RESPONSE
                // ==========================================
                return Ok(new
                {
                    success = true,

                    // Company Expense role from portal_roles
                    role = role,

                    organizationId = organizationId,

                    employeeId = isAdmin
                        ? (int?)null
                        : employeeId,

                    fullAccess = fullAccess,

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


        // employee and all are can delete spent recards api 

        //[Authorize]
        //[HttpDelete("spent/{id}")]
        //public async Task<IActionResult> DeleteSpent(int id)
        //{
        //    try
        //    {
        //        // ==========================================
        //        // 1. VALIDATE ID
        //        // ==========================================

        //        if (id <= 0)
        //        {
        //            return BadRequest(new
        //            {
        //                success = false,
        //                message = "Invalid spent ID."
        //            });
        //        }

        //        // ==========================================
        //        // 2. GET ORGANIZATION FROM JWT
        //        // ==========================================

        //        var organizationId = GetOrganizationId();

        //        if (organizationId <= 0)
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "OrganizationId not found in token."
        //            });
        //        }

        //        // ==========================================
        //        // 3. GET EMPLOYEE ID FROM JWT
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
        //        // 4. FIND OWN ACTIVE SPENT RECORD
        //        //    Organization + Employee based
        //        // ==========================================

        //        var spent = await _context.SpentAmounts
        //            .FirstOrDefaultAsync(x =>
        //                x.Id == id &&
        //                x.employee_id == employeeId &&
        //                x.OrganizationId == organizationId &&
        //                x.IsActive);

        //        if (spent == null)
        //        {
        //            return NotFound(new
        //            {
        //                success = false,
        //                message =
        //                    "Spent record not found or you are not authorized to delete it."
        //            });
        //        }

        //        // ==========================================
        //        // 5. SOFT DELETE
        //        // ==========================================

        //        spent.IsActive = false;

        //        await _context.SaveChangesAsync();

        //        // ==========================================
        //        // 6. RESPONSE
        //        // ==========================================

        //        return Ok(new
        //        {
        //            success = true,
        //            message = "Spent deleted successfully.",
        //            data = new
        //            {
        //                id = spent.Id,
        //                employeeId = spent.employee_id,
        //                organizationId = spent.OrganizationId,
        //                isActive = spent.IsActive
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "Error while deleting spent.",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}

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

    //    [Authorize]
    //    [HttpPut("roles/{roleId}/permission")]
    //    public async Task<IActionResult> UpdateRolePermission(
    //int roleId,
    //[FromBody] CreatePermissionDto model)
    //    {
    //        try
    //        {
    //            // ==========================================
    //            // 1. Validate request
    //            // ==========================================

    //            if (model == null)
    //            {
    //                return BadRequest(new
    //                {
    //                    success = false,
    //                    message = "Permission data is required."
    //                });
    //            }

    //            // ==========================================
    //            // 2. Get OrganizationId from JWT
    //            // ==========================================

    //            var orgIdClaim = User.FindFirst("Org_Id")?.Value;

    //            if (string.IsNullOrEmpty(orgIdClaim))
    //            {
    //                return Unauthorized(new
    //                {
    //                    success = false,
    //                    message = "Org_Id not found in token."
    //                });
    //            }

    //            if (!int.TryParse(orgIdClaim, out int organizationId))
    //            {
    //                return Unauthorized(new
    //                {
    //                    success = false,
    //                    message = "Invalid Org_Id in token."
    //                });
    //            }

    //            // ==========================================
    //            // 3. Check Role exists in this organization
    //            // ==========================================

    //            var role = await _context.RoleMasters
    //                .FirstOrDefaultAsync(x =>
    //                    x.RoleId == roleId &&
    //                    x.OrganizationId == organizationId &&
    //                    x.IsActive);

    //            if (role == null)
    //            {
    //                return NotFound(new
    //                {
    //                    success = false,
    //                    message = "Role not found."
    //                });
    //            }

    //            // ==========================================
    //            // 4. Find Company Expense Access
    //            // ==========================================

    //            var access = await _context.CompanyexpenseAccesses
    //                .FirstOrDefaultAsync(x =>
    //                    x.OrganizationId == organizationId &&
    //                    x.RoleId == roleId);

    //            // ==========================================
    //            // 5. If access row doesn't exist → create
    //            // ==========================================

    //            if (access == null)
    //            {
    //                access = new CompanyexpenseAccesses
    //                {
    //                    OrganizationId = organizationId,
    //                    RoleId = roleId,
    //                    Action = model.Action,
    //                    IsActive = true,
    //                    CreatedDate = DateTime.Now
    //                };

    //                _context.CompanyexpenseAccesses.Add(access);
    //            }
    //            else
    //            {
    //                // ==========================================
    //                // 6. Existing row → update permission
    //                // ==========================================

    //                access.Action = model.Action;
    //                access.IsActive = true;
    //            }

    //            // ==========================================
    //            // 7. Save
    //            // ==========================================

    //            await _context.SaveChangesAsync();

    //            // ==========================================
    //            // 8. Response
    //            // ==========================================

    //            return Ok(new
    //            {
    //                success = true,
    //                message = "Company Expense permission updated successfully.",
    //                data = new
    //                {
    //                    roleId = role.RoleId,
    //                    roleName = role.RoleName,
    //                    organizationId = organizationId,
    //                    action = model.Action,
    //                    accessType = model.Action
    //                        ? "Limited Access"
    //                        : "Full Access"
    //                }
    //            });
    //        }
    //        catch (Exception ex)
    //        {
    //            return StatusCode(500, new
    //            {
    //                success = false,
    //                message = "Error while updating role permission.",
    //                error = ex.Message
    //            });
    //        }
    //    }

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

        //[Authorize]
        //[HttpGet("spent_Access")]
        //public async Task<IActionResult> SpentAccess()
        //{
        //    try
        //    {
        //        // =========================================
        //        // 1. OrganizationId from JWT
        //        // =========================================
        //        var organizationId = GetOrganizationId();

        //        // =========================================
        //        // 2. Role from JWT
        //        // =========================================
        //        var role = User.FindFirst(ClaimTypes.Role)?.Value;

        //        if (string.IsNullOrWhiteSpace(role))
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "Role not found in token."
        //            });
        //        }

        //        // =========================================
        //        // 3. Check Admin
        //        // =========================================
        //        bool isAdmin =
        //            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
        //            role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase);

        //        int loggedInEmployeeId = 0;

        //        // =========================================
        //        // 4. EmpId ONLY for non-admin
        //        // =========================================
        //        if (!isAdmin)
        //        {
        //            var employeeIdClaim = User.FindFirst("EmpId")?.Value;

        //            if (!int.TryParse(employeeIdClaim, out loggedInEmployeeId) ||
        //                loggedInEmployeeId <= 0)
        //            {
        //                return Unauthorized(new
        //                {
        //                    success = false,
        //                    message = "EmployeeId not found in token."
        //                });
        //            }
        //        }

        //        // =========================================
        //        // 5. Call SP
        //        // =========================================

        //        var spent = await _context.SpentDetails
        //            .FromSqlRaw(
        //                @"EXEC dbo.get_spent_details_with_access
        //            @EmployeeId,
        //            @OrganizationId,
        //            @Role",
        //                new SqlParameter("@EmployeeId", loggedInEmployeeId),
        //                new SqlParameter("@OrganizationId", organizationId),
        //                new SqlParameter("@Role", role)
        //            )
        //            .ToListAsync();

        //        // =========================================
        //        // 6. Response
        //        // =========================================

        //        return Ok(new
        //        {
        //            success = true,

        //            fullAccess = isAdmin,

        //            role = role,

        //            organizationId = organizationId,

        //            employeeId = isAdmin
        //                ? (int?)null
        //                : loggedInEmployeeId,

        //            spentDetails = spent
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "Error while fetching spent access details.",
        //            error = ex.Message
        //        });
        //    }
        //}

        //[Authorize]
        //[HttpDelete("spent/{id}")]
        //public async Task<IActionResult> DeleteSpent(int id)
        //{
        //    try
        //    {
        //        var organizationId = GetOrganizationId();

        //        var data = await _context.SpentAmounts
        //            .FirstOrDefaultAsync(x =>
        //                x.Id == id &&
        //                x.OrganizationId == organizationId);

        //        if (data == null)
        //            return NotFound("Spent record not found");

        //        data.IsActive = false;

        //        await _context.SaveChangesAsync();

        //        return Ok(new
        //        {
        //            message = "Spent record deleted successfully"
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            Message = "An error occurred while deleting the spent record.",
        //            Error = ex.Message
        //        });
        //    }
        //}

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


