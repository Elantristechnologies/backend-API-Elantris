using Azure.Core;
using CsvHelper;
using CsvHelper.Configuration;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Spreadsheet;
using global::HRPortal.API.Data;
using HRPortal.API.Data;
using HRPortal.API.DTOs;
using HRPortal.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.Formats.Asn1;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;


namespace HRPortal.API.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeMasterController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly CommonDbContext _commonContext;

        public EmployeeMasterController(ApplicationDbContext context, CommonDbContext commonContext, IConfiguration configuration)
        {
            _context = context;
            _commonContext = commonContext;
            _configuration = configuration;

        }





        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromQuery] string userName,
            [FromQuery] string password)
        {
            try
            {
                // ==========================================
                // 1. VALIDATION
                // ==========================================

                if (string.IsNullOrWhiteSpace(userName))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Username is required."
                    });
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Password is required."
                    });
                }

                string loginValue = userName.Trim();


                // ==========================================
                // 2. CHECK company_users - COMMON DB
                // ==========================================

                var companyUser =
                    await _commonContext.CompanyUsers
                        .FirstOrDefaultAsync(x =>
                            (x.Email != null &&
                             x.Email.ToLower() ==
                                 loginValue.ToLower())
                            ||
                            (x.EmpCode != null &&
                             x.EmpCode.ToLower() ==
                                 loginValue.ToLower())
                        );


                // ==========================================
                // 3. CHECK user_admin - COMMON DB
                // ==========================================

                var adminUser =
                    await _commonContext.UsersMaster
                        .FirstOrDefaultAsync(x =>
                            (x.EMail != null &&
                             x.EMail.ToLower() ==
                                 loginValue.ToLower())
                            ||
                            (x.UserName != null &&
                             x.UserName.ToLower() ==
                                 loginValue.ToLower())
                        );


                // ==========================================
                // 4. USER NOT FOUND
                // ==========================================

                if (companyUser == null &&
                    adminUser == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid username or password."
                    });
                }


                // ==========================================
                // 5. PASSWORD VALIDATION
                // ==========================================

                bool companyPasswordValid = false;
                bool adminPasswordValid = false;


                // ------------------------------------------
                // COMPANY USER PASSWORD
                // ------------------------------------------

                if (companyUser != null)
                {
                    companyPasswordValid =
                        VerifyPasswordFlexible(
                            password,
                            companyUser.Password);
                }


                // ------------------------------------------
                // ADMIN USER PASSWORD
                // ------------------------------------------

                if (adminUser != null)
                {
                    adminPasswordValid =
                        VerifyPasswordFlexible(
                            password,
                            adminUser.Password);
                }


                // ==========================================
                // 6. INVALID PASSWORD
                // ==========================================

                if (!companyPasswordValid &&
                    !adminPasswordValid)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Invalid username or password."
                    });
                }


                // ==========================================
                // 7. COMMON VARIABLES
                // ==========================================

                int userId = 0;
                int empId = 0;
                int organizationId = 0;

                string empCode = string.Empty;
                string finalUserName = string.Empty;
                string email = string.Empty;
                string role = "Employee";
                string companyName = string.Empty;


                // ==========================================
                // 8. MODULE VARIABLES
                // ==========================================

                var allowedModules =
                    new List<string>();

                var allowedPortals =
                    new List<string>();

                var portalRoles =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);


                // ==========================================
                // 9. FUTURE REPORTING MANAGER VARIABLES
                // ==========================================
                //
                // Ippo DB-la reporting manager column illa.
                // Future-la company_users-la add pannumbothu
                // inga values assign pannalam.
                //

                int? reportingManagerId = null;

                string? reportingManagerCode = null;

                string? reportingManagerName = null;


                // ==========================================
                // 10. COMPANY USER
                // COMMON DB ONLY
                // ==========================================

                if (companyPasswordValid &&
                    companyUser != null)
                {
                    // --------------------------------------
                    // Basic details from company_users
                    // --------------------------------------

                    userId =
                        companyUser.EmpId;

                    empId =
                        companyUser.EmpId;

                    empCode =
                        companyUser.EmpCode ??
                        string.Empty;

                    finalUserName =
                        companyUser.FullName ??
                        companyUser.Email ??
                        string.Empty;

                    email =
                        companyUser.Email ??
                        string.Empty;

                    organizationId =
                        companyUser.OrgId ?? 0;


                    // --------------------------------------
                    // GLOBAL ROLE
                    // company_users.PrimaryTitle
                    // --------------------------------------

                    role =
                        !string.IsNullOrWhiteSpace(
                            companyUser.PrimaryTitle)
                        ? companyUser.PrimaryTitle
                        : "Employee";


                    // --------------------------------------
                    // Parse portal_roles
                    // --------------------------------------

                    if (!string.IsNullOrWhiteSpace(
                            companyUser.PortalRoles))
                    {
                        try
                        {
                            using var document =
                                JsonDocument.Parse(
                                    companyUser.PortalRoles);

                            if (document.RootElement.ValueKind ==
                                JsonValueKind.Object)
                            {
                                foreach (var property in
                                         document.RootElement
                                             .EnumerateObject())
                                {
                                    if (property.Value.ValueKind ==
                                        JsonValueKind.String)
                                    {
                                        string portal =
                                            property.Name;

                                        string? portalRole =
                                            property.Value
                                                .GetString();

                                        if (!string.IsNullOrWhiteSpace(
                                                portalRole))
                                        {
                                            portalRoles[portal] =
                                                portalRole;

                                            if (!allowedPortals
                                                    .Any(x =>
                                                        x.Equals(
                                                            portal,
                                                            StringComparison
                                                                .OrdinalIgnoreCase)))
                                            {
                                                allowedPortals.Add(
                                                    portal);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Invalid portal_roles JSON
                        }
                    }
                }


                // ==========================================
                // 11. ADMIN USER
                // COMMON DB ONLY
                // ==========================================

                if (!companyPasswordValid &&
                    adminPasswordValid &&
                    adminUser != null)
                {
                    userId =
                        adminUser.UserId;

                    empId = 0;

                    empCode =
                        string.Empty;

                    finalUserName =
                        adminUser.UserName ??
                        string.Empty;

                    email =
                        adminUser.EMail ??
                        string.Empty;

                    organizationId =
                        adminUser.OrgId ?? 0;

                    companyName =
                        adminUser.CompName ??
                        string.Empty;

                    role = "Admin";
                }


                // ==========================================
                // 12. ORGANIZATION VALIDATION
                // ==========================================

                if (organizationId <= 0)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message =
                            "Organization is not assigned to this user."
                    });
                }


                // ==========================================
                // 13. GET ORGANIZATION MODULES
                // COMMON DB
                // ==========================================

                var modules =
                    await _commonContext.OrganizationModules
                        .Where(x =>
                            x.OrgId == organizationId &&
                            x.IsActive)
                        .Join(
                            _commonContext.ModuleMasters
                                .Where(x => x.IsActive),

                            om => om.ModuleCode,

                            mm => mm.ModuleCode,

                            (om, mm) => new
                            {
                                mm.ModuleId,
                                mm.ModuleCode,
                                mm.ModuleName
                            })
                        .OrderBy(x => x.ModuleId)
                        .ToListAsync();


                // ==========================================
                // 14. ALLOWED MODULES
                // ==========================================

                allowedModules =
                    modules
                        .Select(x => x.ModuleCode)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();


                // ==========================================
                // 15. ADMIN PORTALS
                // ==========================================

                if (!companyPasswordValid &&
                    adminPasswordValid &&
                    adminUser != null)
                {
                    allowedPortals =
                        allowedModules.ToList();

                    portalRoles =
                        allowedPortals
                            .ToDictionary(
                                x => x,
                                x => "Admin",
                                StringComparer.OrdinalIgnoreCase);
                }


                // ==========================================
                // 16. JWT CLAIMS
                // ==========================================

                var claims =
                    new List<Claim>
                    {
                // ------------------------------
                // Subject / Email
                // ------------------------------

                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    email),

                // ------------------------------
                // User ID
                // ------------------------------

                new Claim(
                    "userId",
                    userId.ToString()),

                // ------------------------------
                // Organization
                // ------------------------------

                new Claim(
                    "orgId",
                    organizationId.ToString()),

                // ------------------------------
                // GLOBAL ROLE
                // ------------------------------

                new Claim(
                    "role",
                    role),

                new Claim(
                    ClaimTypes.Role,
                    role),

                // ------------------------------
                // Name
                // ------------------------------

                new Claim(
                    ClaimTypes.Name,
                    finalUserName),

                new Claim(
                    "name",
                    finalUserName),

                // ------------------------------
                // Email
                // ------------------------------

                new Claim(
                    ClaimTypes.Email,
                    email),

                // ------------------------------
                // DB TYPE
                // ------------------------------

                new Claim(
                    "dbType",
                    "SQL Server"),

                // ------------------------------
                // APPROVAL STATUS
                // ------------------------------

                new Claim(
                    "isApproved",
                    "true")
                    };


                // ==========================================
                // 16A. EMPLOYEE ID
                // ==========================================

                if (empId > 0)
                {
                    // Common token style
                    claims.Add(
                        new Claim(
                            "emp_id",
                            empId.ToString()));

                    claims.Add(
                        new Claim(
                            "EmpId",
                            empId.ToString()));
                }


                // ==========================================
                // 16B. EMPLOYEE CODE
                // ==========================================

                if (!string.IsNullOrWhiteSpace(empCode))
                {
                    claims.Add(
                        new Claim(
                            "emp_code",
                            empCode));

                    claims.Add(
                        new Claim(
                            "EmpCode",
                            empCode));
                }


                // ==========================================
                // 16C. MODULES
                // ==========================================

                if (allowedModules.Count > 0)
                {
                    claims.Add(
                        new Claim(
                            "modules",
                            string.Join(
                                ",",
                                allowedModules)));
                }


                // ==========================================
                // 16D. ALLOWED PORTALS
                // ==========================================
                //
                // Same claim multiple times.
                // JWT decoder can show this as an array.
                //

                foreach (var portal in
                         allowedPortals.Distinct(
                             StringComparer.OrdinalIgnoreCase))
                {
                    claims.Add(
                        new Claim(
                            "allowed_portals",
                            portal));
                }


                // ==========================================
                // 16E. PORTAL ROLES
                // ==========================================

                if (portalRoles.Count > 0)
                {
                    claims.Add(
                        new Claim(
                            "portal_roles",
                            JsonSerializer.Serialize(
                                portalRoles)));
                }


                // ==========================================
                // 16F. COMPANY
                // ==========================================

                if (!string.IsNullOrWhiteSpace(companyName))
                {
                    claims.Add(
                        new Claim(
                            "company",
                            companyName));
                }


                // ==========================================
                // 16G. FUTURE REPORTING MANAGER
                // ==========================================
                //
                // Ippo reporting manager DB column illa.
                //
                // Future-la company_users-la add pannumbothu:
                //
                // claims.Add(new Claim(
                //     "ReportingManagerId",
                //     reportingManagerId.ToString()));
                //
                // etc.
                //
                // Ippo add panna vendam.
                // ==========================================


                // ==========================================
                // 17. JWT CONFIG
                // ==========================================

                var jwtKey =
                    _configuration["Jwt:Key"];

                if (string.IsNullOrWhiteSpace(jwtKey))
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message =
                            "JWT Key is not configured."
                    });
                }


                var key =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey));


                var credentials =
                    new SigningCredentials(
                        key,
                        SecurityAlgorithms.HmacSha256);


                // ==========================================
                // 18. CREATE TOKEN
                // ==========================================

                var token =
                    new JwtSecurityToken(
                        issuer:
                            _configuration["Jwt:Issuer"],

                        audience:
                            _configuration["Jwt:Audience"],

                        claims:
                            claims,

                        expires:
                            DateTime.Now.AddDays(1),

                        signingCredentials:
                            credentials);


                var jwtToken =
                    new JwtSecurityTokenHandler()
                        .WriteToken(token);


                // ==========================================
                // 19. FINAL RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,

                    token = jwtToken,

                    userId =
                        userId,

                    empId =
                        empId,

                    empCode =
                        empCode,

                    userName =
                        finalUserName,

                    email =
                        email,

                    role =
                        role,

                    companyName =
                        companyName,

                    orgId =
                        organizationId,

                    allowedModules =
                        allowedModules,

                    allowedPortals =
                        allowedPortals,

                    portalRoles =
                        portalRoles,


                    // --------------------------------------
                    // Employee details
                    // COMMON DB ONLY
                    // --------------------------------------

                    employee = new
                    {
                        EmpId = empId,

                        EmployeeCode = empCode,

                        FullName = finalUserName,

                        EmailId = email,

                        ReportingManagerId =
                            reportingManagerId,

                        ReportingManager_code =
                            reportingManagerCode,

                        ReportingManager_name =
                            reportingManagerName
                    },

                    message =
                        "Login successful"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,

                    message =
                        "An error occurred during login.",

                    error =
                        ex.InnerException?.InnerException?.Message
                        ?? ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }

        private async Task<IActionResult> CreateCommonLoginResponseAsync(
    CompanyUser? companyUser,
    UsersMaster? adminUser)
        {
            try
            {
                int userId;
                int empId;
                string empCode;
                string userName;
                string email;
                int orgId;
                string role;
                string companyName;

                List<string> allowedModules;
                List<string> allowedPortals;

                Dictionary<string, string> portalRoles;

                // ==========================================
                // COMPANY USER
                // ==========================================

                if (companyUser != null)
                {
                    userId = companyUser.EmpId;
                    empId = companyUser.EmpId;

                    empCode = companyUser.EmpCode ?? string.Empty;

                    userName =
                        companyUser.FullName ??
                        companyUser.Email ??
                        string.Empty;

                    email = companyUser.Email ?? string.Empty;

                    orgId = companyUser.OrgId ?? 0;

                    companyName = string.Empty;

                    // Dynamic portal roles
                    portalRoles =
                        ParsePortalRoles(companyUser.PortalRoles);

                    // Dynamic portals
                    allowedPortals =
                        portalRoles.Keys
                            .OrderBy(x => x)
                            .ToList();

                    // Dynamic modules
                    allowedModules =
                        ParseModuleList(
                            companyUser.ErpCustomModules);

                    // If no module list is available,
                    // use portal names
                    if (allowedModules.Count == 0)
                    {
                        allowedModules =
                            allowedPortals.ToList();
                    }

                    // No hardcoded portal name
                    role =
                        portalRoles.Values.FirstOrDefault()
                        ?? "Employee";
                }
                else
                {
                    // ==========================================
                    // ADMIN USER
                    // ==========================================

                    userId = adminUser!.UserId;

                    empId = 0;

                    empCode = string.Empty;

                    userName =
                        adminUser.UserName ?? string.Empty;

                    email =
                        adminUser.EMail ?? string.Empty;

                    orgId =
                        adminUser.OrgId ?? 0;

                    companyName =
                        adminUser.CompName ?? string.Empty;

                    role = "Admin";

                    allowedModules =
                        new List<string>();

                    allowedPortals =
                        new List<string>();

                    portalRoles =
                        new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                }

                // ==========================================
                // ORGANIZATION CHECK
                // ==========================================

                if (orgId <= 0)
                {
                    return new UnauthorizedObjectResult(new
                    {
                        success = false,
                        message =
                            "Organization is not assigned to this user."
                    });
                }

                // ==========================================
                // JWT CLAIMS
                // ==========================================

                var claims = new List<Claim>
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                email),

            new Claim(
                "userId",
                userId.ToString()),

            new Claim(
                "orgId",
                orgId.ToString()),

            new Claim(
                ClaimTypes.Email,
                email),

            new Claim(
                ClaimTypes.Role,
                role),

            new Claim(
                "role",
                role)
        };

                if (empId > 0)
                {
                    claims.Add(
                        new Claim(
                            "EmpId",
                            empId.ToString()));
                }

                if (!string.IsNullOrWhiteSpace(companyName))
                {
                    claims.Add(
                        new Claim(
                            "company",
                            companyName));
                }

                // ==========================================
                // JWT CONFIG
                // ==========================================

                var jwtKey =
                    _configuration["Jwt:Key"];

                if (string.IsNullOrWhiteSpace(jwtKey))
                {
                    return new ObjectResult(new
                    {
                        success = false,
                        message = "JWT Key is not configured."
                    })
                    {
                        StatusCode = 500
                    };
                }

                var key =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey));

                var credentials =
                    new SigningCredentials(
                        key,
                        SecurityAlgorithms.HmacSha256);

                // ==========================================
                // CREATE TOKEN
                // ==========================================

                var token =
                    new JwtSecurityToken(
                        issuer:
                            _configuration["Jwt:Issuer"],

                        audience:
                            _configuration["Jwt:Audience"],

                        claims: claims,

                        expires:
                            DateTime.Now.AddDays(1),

                        signingCredentials:
                            credentials);

                var jwtToken =
                    new JwtSecurityTokenHandler()
                        .WriteToken(token);

                // ==========================================
                // FINAL RESPONSE
                // ==========================================

                return new OkObjectResult(new
                {
                    success = true,

                    token = jwtToken,

                    userId = userId,

                    empId = empId,

                    empCode = empCode,

                    userName = userName,

                    email = email,

                    role = role,

                    companyName = companyName,

                    orgId = orgId,

                    allowedModules = allowedModules,

                    allowedPortals = allowedPortals,

                    portalRoles = portalRoles,

                    message = "Login successful"
                });
            }
            catch (Exception ex)
            {
                return new ObjectResult(new
                {
                    success = false,
                    message =
                        "Error while creating login response.",

                    error =
                        ex.InnerException?.InnerException?.Message
                        ?? ex.InnerException?.Message
                        ?? ex.Message
                })
                {
                    StatusCode = 500
                };
            }
        }

        private Dictionary<string, string> ParsePortalRoles(
    string? portalRoles)
        {
            try
            {
                var result =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase
                    );

                if (string.IsNullOrWhiteSpace(portalRoles))
                {
                    return result;
                }

                using var document =
                    JsonDocument.Parse(portalRoles);

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var property in
                         document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind ==
                        JsonValueKind.String)
                    {
                        var value =
                            property.Value.GetString();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            result[property.Name] = value;
                        }
                    }
                }

                return result;
            }
            catch
            {
                return new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }

        private List<string> ParseModuleList(
           string? modules)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modules))
                {
                    return new List<string>();
                }

                // ==========================================
                // JSON ARRAY
                // ==========================================

                if (modules.TrimStart().StartsWith("["))
                {
                    using var document =
                        JsonDocument.Parse(modules);

                    if (document.RootElement.ValueKind ==
                        JsonValueKind.Array)
                    {
                        var result =
                            new List<string>();

                        foreach (var item in
                                 document.RootElement
                                     .EnumerateArray())
                        {
                            if (item.ValueKind ==
                                JsonValueKind.String)
                            {
                                var value =
                                    item.GetString();

                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    result.Add(value);
                                }
                            }
                        }

                        return result
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                }

                // ==========================================
                // COMMA SEPARATED
                // ==========================================

                return modules
                    .Split(
                        ',',
                        StringSplitOptions
                            .RemoveEmptyEntries
                    )
                    .Select(x => x.Trim())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private bool VerifyPasswordFlexible(
     string enteredPassword,
     string? storedPassword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(storedPassword))
                {
                    return false;
                }

                // ==========================================
                // BCrypt password
                // ==========================================

                if (storedPassword.StartsWith("$2a$") ||
                    storedPassword.StartsWith("$2b$") ||
                    storedPassword.StartsWith("$2y$"))
                {
                    return BCrypt.Net.BCrypt.Verify(
                        enteredPassword,
                        storedPassword);
                }

                // ==========================================
                // Normal / plain password
                // ==========================================

                return storedPassword == enteredPassword;
            }
            catch
            {
                return false;
            }
        }


        //[AllowAnonymous]
        //[HttpPost("login")]
        //public async Task<IActionResult> Login(
        //    string userName,
        //    string password,
        //    int checkType)
        //{
        //    try
        //    {
        //        // ==========================================
        //        // 1. LOGIN
        //        // EXISTING DATABASE / EXISTING SP
        //        // ==========================================

        //        var employees = await _context.LoginResponseDtos
        //            .FromSqlRaw(
        //                "EXEC dbo.sp_Login @UserName, @Password",
        //                new SqlParameter("@UserName", userName),
        //                new SqlParameter("@Password", password)
        //            )
        //            .ToListAsync();

        //        var employee = employees.FirstOrDefault();

        //        // ==========================================
        //        // INVALID LOGIN
        //        // ==========================================

        //        if (employee == null || employee.IsSuccess == 0)
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "Invalid Username or Password"
        //            });
        //        }

        //        // ==========================================
        //        // 2. ROLE
        //        // ==========================================

        //        string role = employee.Role ?? "Employee";

        //        // ==========================================
        //        // 3. GET MODULES
        //        // ORGANIZATION BASED ONLY
        //        // ==========================================

        //        var modules = new List<ModuleResponseDto>();

        //        if (employee.orgId.HasValue &&
        //            employee.orgId.Value > 0)
        //        {
        //            modules = await _context.OrganizationModules
        //                .Where(x =>
        //                    x.OrganizationId == employee.orgId.Value &&
        //                    x.IsActive)
        //                .Join(
        //                    _context.ModuleMasters
        //                        .Where(x => x.IsActive),
        //                    om => om.ModuleId,
        //                    mm => mm.ModuleId,
        //                    (om, mm) => new ModuleResponseDto
        //                    {
        //                        ModuleId = mm.ModuleId,
        //                        ModuleCode = mm.ModuleCode,
        //                        ModuleName = mm.ModuleName
        //                    })
        //                .OrderBy(x => x.ModuleId)
        //                .ToListAsync();
        //        }

        //        // ==========================================
        //        // 4. JWT TOKEN
        //        // ==========================================

        //        var token = GenerateJwtToken(
        //            employee.EmailId,
        //            role,
        //            employee.orgId,
        //            employee.EmpId
        //        );

        //        // ==========================================
        //        // 5. ORGANIZATION SETUP
        //        // ==========================================

        //        bool isOrganizationSetupRequired =
        //            employee.orgId == null;

        //        // ==========================================
        //        // 6. PROFILE SETUP
        //        // ==========================================

        //        bool isProfileSetupRequired =
        //            role != "Admin" &&
        //            !role.Equals(
        //                "SuperAdmin",
        //                StringComparison.OrdinalIgnoreCase) &&
        //            (
        //                string.IsNullOrEmpty(employee.Phone) ||
        //                employee.DepartmentId == null ||
        //                employee.DesignationId == null ||
        //                employee.DateOfJoining == null
        //            );

        //        // ==========================================
        //        // 7. LOGIN RESPONSE
        //        // ==========================================

        //        return Ok(new
        //        {
        //            success = true,

        //            message = "Login successful",

        //            role = role,

        //            token = token,

        //            IsOrganizationSetupRequired =
        //                isOrganizationSetupRequired,

        //            IsProfileSetupRequired =
        //                isProfileSetupRequired,

        //            // Organization based modules
        //            modules = modules,

        //            employee = new
        //            {
        //                employee.UserId,
        //                employee.UserName,

        //                employee.EmpId,
        //                employee.EmployeeCode,

        //                employee.FullName,
        //                employee.EmailId,
        //                employee.Phone,

        //                employee.RoleId,
        //                employee.Role,

        //                employee.DepartmentId,
        //                employee.DepartmentName,

        //                employee.DesignationId,
        //                employee.DesignationName,

        //                employee.ReportingManagerId,
        //                employee.ReportingManager_code,
        //                employee.ReportingManager_name,

        //                employee.DateOfJoining,
        //                employee.CategoryId,
        //                employee.ConfirmationDate,
        //                employee.GradeId,

        //                employee.Gender,
        //                employee.MaritalStatus,

        //                employee.orgId
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "An error occurred during login.",
        //            error = ex.InnerException?.Message ?? ex.Message
        //        });
        //    }
        //}



        private string GenerateJwtToken(
        string email,
        string role,
        int? orgId,
        int? EmpId)
        {
            var claimsList = new List<Claim>
    {
        new Claim(ClaimTypes.Email, email),
        new Claim(ClaimTypes.Role, role)
    };

            // EmpId இருந்தால் மட்டும் JWT-ல் add செய்யும்
            if (EmpId.HasValue && EmpId.Value > 0)
            {
                claimsList.Add(
                    new Claim("EmpId", EmpId.Value.ToString())
                );
            }

            // OrganizationId இருந்தால் JWT-ல் add செய்யும்
            if (orgId.HasValue)
            {
                claimsList.Add(
                    new Claim(
                        "orgId",
                        orgId.Value.ToString()
                    )
                );
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"])
            );

            var creds = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claimsList,
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        private int GetOrganizationId()
        {
            var organizationIdClaim =
                User.FindFirst("orgId");

            if (organizationIdClaim == null)
                throw new UnauthorizedAccessException(
                    "OrganizationId not found in token.");

            return int.Parse(organizationIdClaim.Value);
        }

        //[Authorize]
        //[HttpPost("create-organization")]
        //public async Task<IActionResult> CreateOrganization([FromBody] OrganizationCreateDto request)
        //{
        //    try
        //    {
        //        var email = User.FindFirst(ClaimTypes.Email)?.Value;

        //        if (string.IsNullOrEmpty(email))
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "Invalid token"
        //            });
        //        }

        //        var user = await _context.UsersMaster
        //            .FirstOrDefaultAsync(x => x.Email == email);

        //        if (user == null)
        //        {
        //            return NotFound(new
        //            {
        //                success = false,
        //                message = "User not found"
        //            });
        //        }

        //        if (user.org_id != null)
        //        {
        //            return BadRequest(new
        //            {
        //                success = false,
        //                message = "Organization already assigned"
        //            });
        //        }

        //        var organization = new OrganizationMaster
        //        {
        //            OrganizationName = request.OrganizationName,
        //            OrganizationCode = request.OrganizationCode,
        //            Email = request.Email,
        //            Phone = request.Phone,
        //            Address = request.Address,
        //            IsActive = true,
        //            CreatedDate = DateTime.Now
        //        };

        //        _context.OrganizationMasters.Add(organization);
        //        await _context.SaveChangesAsync();

        //        user.org_id = organization.OrganizationId;

        //        var employee = await _context.EmployeeMasters
        //            .FirstOrDefaultAsync(x => x.EmailId == user.Email);

        //        if (employee != null)
        //        {
        //            employee.OrganizationId = organization.OrganizationId;
        //        }

        //        await _context.SaveChangesAsync();

        //        return Ok(new
        //        {
        //            success = true,
        //            message = "Organization created successfully",
        //            organizationId = organization.OrganizationId
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = ex.Message,
        //            innerError = ex.InnerException?.Message
        //        });
        //    }
        //}

        //[HttpPost("create-organization")]
        //public async Task<IActionResult> CreateOrganization(OrganizationCreateDto dto)
        //{
        //    try
        //    {
        //        var userIdClaim = User.FindFirst("userId")?.Value;
        //        int userId = 0;
        //        if (!string.IsNullOrEmpty(userIdClaim))
        //        {
        //            int.TryParse(userIdClaim, out userId);
        //        }
        //        else if (dto != null && !string.IsNullOrEmpty(dto.UserId))
        //        {
        //            int.TryParse(dto.UserId, out userId);
        //        }

        //        var connStr = _configuration.GetConnectionString("ElantrisConnection")
        //            ?? _configuration.GetConnectionString("DefaultConnection")
        //            ?? "Server=100.124.4.114,62534;Database=Elantris_Technologies;User Id=sa;Password=ET#2026;TrustServerCertificate=True;";

        //        using var conn = new SqlConnection(connStr);
        //        await conn.OpenAsync();

        //        using var cmd = conn.CreateCommand();
        //        cmd.CommandText = @"EXEC dbo.sp_CreateOrganization 
        //     @OrgName, @AddressStreet1, @AddressStreet2, @City, @PostalCode,
        //     @StateId, @CountryId, @IndustryId, @UserId, @GSTNo,
        //     @TimezoneId, @CurrencyId, @DBName, @StateCode";

        //        cmd.Parameters.Add(new SqlParameter("@OrgName", (object?)dto?.OrgName ?? DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@AddressStreet1", (object?)dto?.AddressStreet1 ?? DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@AddressStreet2", (object?)dto?.AddressStreet2 ?? DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@City", (object?)dto?.City ?? DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@PostalCode", (object?)dto?.PostalCode ?? DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@StateId", !string.IsNullOrEmpty(dto?.StateId) ? Convert.ToInt32(dto.StateId) : DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@CountryId", !string.IsNullOrEmpty(dto?.CountryId) ? Convert.ToInt32(dto.CountryId) : DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@IndustryId", !string.IsNullOrEmpty(dto?.IndustryId) ? Convert.ToInt32(dto.IndustryId) : DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@UserId", userId > 0 ? (object)userId : DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@GSTNo", (object?)dto?.GSTNo ?? DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@TimezoneId", !string.IsNullOrEmpty(dto?.TimezoneId) ? Convert.ToInt32(dto.TimezoneId) : DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@CurrencyId", !string.IsNullOrEmpty(dto?.CurrencyId) ? Convert.ToInt32(dto.CurrencyId) : DBNull.Value));
        //        cmd.Parameters.Add(new SqlParameter("@DBName", string.IsNullOrWhiteSpace(dto?.DBName) || dto?.DBName == "ERP" ? "Elantris_Technologies" : dto.DBName));
        //        cmd.Parameters.Add(new SqlParameter("@StateCode", DBNull.Value));

        //        var sb = new StringBuilder();
        //        using var reader = await cmd.ExecuteReaderAsync();
        //        while (await reader.ReadAsync())
        //        {
        //            if (!reader.IsDBNull(0))
        //            {
        //                sb.Append(reader.GetString(0));
        //            }
        //        }

        //        string jsonResult = sb.ToString();
        //        if (string.IsNullOrWhiteSpace(jsonResult))
        //        {
        //            return BadRequest(new { status = false, message = "Failed to create organization." });
        //        }

        //        // Ensure selected_modules is saved as 'erp' (or requested modules) in Organization table
        //        try
        //        {
        //            string modulesToSave = !string.IsNullOrWhiteSpace(dto?.SelectedModules)
        //                ? dto.SelectedModules
        //                : (!string.IsNullOrWhiteSpace(dto?.RequestedModules) ? dto.RequestedModules : "erp");

        //            using var updateCmd = conn.CreateCommand();
        //            updateCmd.CommandText = @"UPDATE dbo.Organization 
        //         SET selected_modules = @SelectedModules 
        //         WHERE (user_id = CAST(@UserId AS VARCHAR(100)) AND @UserId > 0)
        //            OR (org_name = @OrgName AND (selected_modules IS NULL OR selected_modules = ''));";
        //            updateCmd.Parameters.Add(new SqlParameter("@SelectedModules", modulesToSave));
        //            updateCmd.Parameters.Add(new SqlParameter("@UserId", userId));
        //            updateCmd.Parameters.Add(new SqlParameter("@OrgName", (object?)dto?.OrgName ?? DBNull.Value));
        //            await updateCmd.ExecuteNonQueryAsync();
        //        }
        //        catch { }

        //        return Content(jsonResult, "application/json");
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        //    }
        //}

        //// GET ALL ORGANIZATIONS (via Stored Procedure sp_GetAllOrganizations)
        ///


        [HttpGet("get-all-organization")]
        public async Task<IActionResult> GetAllOrganization()
        {
            try
            {
                // ==========================================
                // COMMON DB CONNECTION
                // ==========================================

                var connStr = _configuration.GetConnectionString("CommonDb");

                if (string.IsNullOrWhiteSpace(connStr))
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "CommonDb connection string not configured."
                    });
                }

                // ==========================================
                // OPEN DATABASE CONNECTION
                // ==========================================

                await using var conn = new SqlConnection(connStr);

                await conn.OpenAsync();

                // ==========================================
                // STORED PROCEDURE
                // ==========================================

                await using var cmd = conn.CreateCommand();

                cmd.CommandText = "dbo.sp_GetAllOrganizations";
                cmd.CommandType = CommandType.StoredProcedure;

                // ==========================================
                // EXECUTE STORED PROCEDURE
                // ==========================================

                var sb = new StringBuilder();

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        sb.Append(reader.GetString(0));
                    }
                }

                // ==========================================
                // GET JSON RESULT
                // ==========================================

                var jsonResult = sb.ToString();

                if (string.IsNullOrWhiteSpace(jsonResult))
                {
                    return Content("[]", "application/json");
                }

                // ==========================================
                // RETURN JSON DIRECTLY
                // ==========================================

                return Content(
                    jsonResult,
                    "application/json"
                );
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error fetching organizations.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // GET SINGLE ORGANIZATION (via Stored Procedure sp_GetOrganizationDetails)
        [Authorize]
        [HttpGet("get-organization")]
        public async Task<IActionResult> GetOrganization()
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
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
                // 2. GET USER ID FROM JWT
                // ==========================================

                var userIdClaim = User.FindFirst("userId")?.Value;

                int? userId = null;

                if (!string.IsNullOrWhiteSpace(userIdClaim) &&
                    int.TryParse(userIdClaim, out int parsedUserId) &&
                    parsedUserId > 0)
                {
                    userId = parsedUserId;
                }

                // ==========================================
                // 3. USE COMMON DB CONNECTION
                // NO HARDCODED CONNECTION STRING
                // ==========================================

                var conn = _commonContext.Database.GetDbConnection();

                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                // ==========================================
                // 4. CALL COMMON DB SP
                // ==========================================

                using var cmd = conn.CreateCommand();

                cmd.CommandText = "dbo.sp_GetOrganizationDetails";
                cmd.CommandType = CommandType.StoredProcedure;

                var orgParameter = cmd.CreateParameter();
                orgParameter.ParameterName = "@OrgId";
                orgParameter.Value = organizationId;
                cmd.Parameters.Add(orgParameter);

                var userParameter = cmd.CreateParameter();
                userParameter.ParameterName = "@UserId";
                userParameter.Value =
                    userId.HasValue
                        ? userId.Value
                        : DBNull.Value;

                cmd.Parameters.Add(userParameter);

                // ==========================================
                // 5. READ JSON RESPONSE
                // ==========================================

                var sb = new StringBuilder();

                using var reader =
                    await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        sb.Append(reader.GetString(0));
                    }
                }

                var jsonResult = sb.ToString();

                // ==========================================
                // 6. NOT FOUND
                // ==========================================

                if (string.IsNullOrWhiteSpace(jsonResult))
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Organization not found."
                    });
                }

                // ==========================================
                // 7. RETURN JSON
                // ==========================================

                return Content(
                    jsonResult,
                    "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error fetching organization.",
                    error =
                        ex.InnerException?.InnerException?.Message
                        ?? ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("modules")]
        public async Task<IActionResult> GetOrganizationModules()
        {
            try
            {
                // ==========================================
                // GET ORGANIZATION ID FROM JWT
                // ==========================================

                var organizationId = GetOrganizationId();

                // ==========================================
                // GET MODULES
                // Organization_Module + Module_Master
                // ==========================================

                var modules = await _context.OrganizationModules
                    .Where(x =>
                        x.OrgId == organizationId &&
                        x.IsActive)
                    .Join(
                        _context.ModuleMasters,
                        om => om.Id,
                        mm => mm.ModuleId,
                        (om, mm) => new
                        {
                            moduleId = mm.ModuleId,
                            moduleCode = mm.ModuleCode,
                            moduleName = mm.ModuleName,
                            isActive = om.IsActive,
                            createdDate = om.AssignedDate
                        })
                    .Where(x => x.isActive)
                    .OrderBy(x => x.moduleId)
                    .ToListAsync();

                // ==========================================
                // RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,
                    organizationId = organizationId,
                    modules = modules
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
                    message = "An error occurred while fetching organization modules.",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // HR-ADMIN profile update
        [Authorize]
        [HttpPut("update-profile/{id}")]
        public async Task<IActionResult> UpdateProfile(int id, HrAdmin updated)
        {
            var hr = await _context.HrAdmins.FindAsync(id);

            if (hr == null)
                return NotFound();

            hr.empcode = updated.empcode;
            hr.PhoneNumber = updated.PhoneNumber;

            await _context.SaveChangesAsync();

            return Ok("Profile Updated");

        }

        // all employee dashboard la display pannum Muthu
        //[Authorize]
        //[HttpGet]
        //public async Task<IActionResult> GetEmployees()
        //{
        //    try
        //    {
        //        var orgIdClaim = User.FindFirst("OrganizationId")?.Value;

        //        int organizationId = Convert.ToInt32(orgIdClaim);

        //        var employees = await (
        //            from e in _context.EmployeeMasters

        //            join d in _context.Designations
        //                on e.DesignationId equals d.Id into desigJoin
        //            from d in desigJoin.DefaultIfEmpty()

        //            join dept in _context.Department_Master
        //                on e.DepartmentId equals dept.DepartmentId into deptJoin
        //            from dept in deptJoin.DefaultIfEmpty()

        //            where e.OrganizationId == organizationId

        //            select new
        //            {
        //                empId = e.EmpId,

        //                organizationId = e.OrganizationId,

        //                employeeCode = e.EmployeeCode,

        //                fullName = e.FullName,

        //                emailId = e.EmailId,

        //                phone = e.Phone,

        //                departmentId = e.DepartmentId,

        //                departmentName = dept != null
        //                    ? dept.DepartmentName
        //                    : null,

        //                designationId = e.DesignationId,

        //                designationName = d != null
        //                    ? d.Name
        //                    : null,

        //                dateOfJoining = e.DateOfJoining
        //            }

        //        ).ToListAsync();

        //        return Ok(employees);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "An error occurred while fetching employees",
        //            error = ex.Message
        //        });
        //    }
        //}

        //[HttpGet]
        //public async Task<IActionResult> GetEmployees() //Dhanush
        //{
        //    try
        //    {
        //        var employees = await (
        //    from e in _context.EmployeeMasters

        //    join d in _context.Designations
        //        on e.DesignationId equals d.Id into desigJoin
        //    from d in desigJoin.DefaultIfEmpty()

        //    join dept in _context.Department_Master
        //        on e.DepartmentId equals dept.DepartmentId into deptJoin
        //    from dept in deptJoin.DefaultIfEmpty()

        //    select new
        //    {
        //        empId = e.EmpId,
        //        employeeCode = e.EmployeeCode,
        //        fullName = e.FullName,
        //        emailId = e.EmailId,

        //        departmentId = e.DepartmentId,

        //        department = dept != null ? dept.DepartmentName : null,   // ✅ department name

        //        designationId = e.DesignationId,
        //        designation = d != null ? d.Name : null,        // ✅ designation name

        //        dateOfJoining = e.DateOfJoining
        //    }
        //).ToListAsync();

        //        return Ok(employees);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "An error occurred while fetching employees",
        //            error = ex.Message
        //        });
        //    }
        //}

        //[Authorize]
        //[HttpGet("employees")]
        //public async Task<IActionResult> GetEmployees()
        //{
        //    try
        //    {
        //        var orgIdClaim = User.FindFirst("orgId")?.Value;

        //        if (string.IsNullOrEmpty(orgIdClaim))
        //        {
        //            return Unauthorized(new
        //            {
        //                success = false,
        //                message = "orgId not found in token."
        //            });
        //        }

        //        int organizationId = int.Parse(orgIdClaim);

        //        var employees = await _coo.EmployeeMasters
        //            .Where(x =>
        //                x.OrganizationId == organizationId &&
        //                x.IsActive)
        //            .Select(x => new
        //            {
        //                empId = x.EmpId,
        //                employeeCode = x.EmployeeCode,
        //                fullName = x.FullName,
        //                emailId = x.EmailId,
        //                phone = x.Phone,
        //                departmentId = x.DepartmentId,
        //                designationId = x.DesignationId,
        //                roleId = x.RoleId,
        //                reportingManagerId = x.ReportingManagerId,
        //                dateOfJoining = x.DateOfJoining,
        //                isActive = x.IsActive
        //            })
        //            .OrderBy(x => x.fullName)
        //            .ToListAsync();

        //        return Ok(new
        //        {
        //            success = true,
        //            data = employees
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = "Error while fetching employees.",
        //            error = ex.Message
        //        });
        //    }
        //}


        [Authorize]
        [HttpGet("employees")]
        public async Task<IActionResult> GetEmployees()
        {
            try
            {
                // ==========================================
                // 1. GET ORGANIZATION ID FROM JWT
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
                        message = "Invalid orgId in token."
                    });
                }

                // ==========================================
                // 2. GET EMPLOYEES FROM COMMON DB
                // company_users
                // ==========================================

                var employees =
                    await _commonContext.CompanyUsers
                        .Where(x =>
                            x.OrgId == organizationId &&
                            (
                                string.IsNullOrWhiteSpace(x.Status) ||
                                x.Status == "Active"
                            )
                        )
                        .Select(x => new
                        {
                            empId = x.EmpId,

                            employeeCode = x.EmpCode,

                            fullName = x.FullName,

                            emailId = x.Email,

                            phone = x.MobileNumber,

                            department = x.Department,

                            designation = x.PrimaryTitle,

                            status = x.Status,

                            organizationId = x.OrgId,

                            isActive =
                                string.IsNullOrWhiteSpace(x.Status) ||
                                x.Status == "Active"
                        })
                        .OrderBy(x => x.fullName)
                        .ToListAsync();

                // ==========================================
                // 3. RESPONSE
                // ==========================================

                return Ok(new
                {
                    success = true,

                    organizationId = organizationId,

                    data = employees
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching employees.",
                    error =
                        ex.InnerException?.InnerException?.Message
                        ?? ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }
        [Authorize]
        [HttpGet("designation")]
        public async Task<IActionResult> GetDesignation()
        {
            try
            {
                var organizationId = GetOrganizationId();

                var data = await _context.Designation
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = x.Name,
                        isReportingManager = x.IsReportingManager,
                        isActive = x.IsActive,
                        createdDate = x.CreatedDate
                    })
                    .OrderBy(x => x.name)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching designations.",
                    error = ex.Message
                });
            }
        }




        // DESIGNATION MASTER ---RAJESH
        [HttpPost("designation")]
        public async Task<IActionResult> DesignationMasterApi(
     int? id,
     string? name,
     bool isReportingManager,
     bool isActive,
     string actionType)
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_DesignationMaster @Id={0}, @Name={1}, @IsReportingManager={2}, @ActionType={3}, @IsActive={4}",
                    id,
                    name,
                    isReportingManager,
                    actionType.ToUpper(),
                    isActive
                );

                return Ok(new
                {
                    success = true,
                    message = $"{actionType} completed successfully"
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

        // HR- profile display pannum
        [Authorize]
        [HttpGet("profile/{id}")]
        public async Task<IActionResult> GetHr(int id)
        {
            try
            {
                var hr = await _context.HrAdmins.FindAsync(id);

                if (hr == null)
                    return NotFound(new
                    {
                        message = "HR record not found"
                    });

                return Ok(hr);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching HR profile",
                    error = ex.Message
                });
            }
        }




        //[Authorize]
        [HttpGet("reporting-managers")]
        public async Task<IActionResult> GetReportingManagers()
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "OrganizationId not found in token"
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                var managers = await (
                    from e in _context.EmployeeMasters
                    join d in _context.Designations
                        on e.DesignationId equals d.Id
                    where e.OrganizationId == organizationId
                          && d.Name != "Employee"
                    select new
                    {
                        e.EmpId,
                        e.EmployeeCode,
                        e.FullName,
                        Designation = d.Name
                    }
                ).ToListAsync();

                return Ok(new
                {
                    success = true,
                    count = managers.Count,
                    data = managers
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }
        }

        // employee register 
        // [Authorize]    Muthu
        //[Authorize]
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegisterEmployee(
     [FromBody] EmployeeCreateDto model)
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "orgId not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                var exists = await _context.EmployeeMasters
                    .AnyAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.EmailId == model.EmailId);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Employee already exists."
                    });
                }

                var employee = new EmployeeMaster
                {
                    EmployeeCode = model.EmployeeCode,
                    FullName = model.FullName,
                    EmailId = model.EmailId,
                    Phone = model.Phone,
                    DepartmentId = model.DepartmentID,
                    DesignationId = model.DesignationID,
                    RoleId = model.RoleId,
                    ReportingManagerId = model.ReportingManagerId,
                    DateOfJoining = model.DateOfJoining,

                    // JWT-la irundhu
                    OrganizationId = organizationId,

                    // New employee active
                    IsActive = true
                };

                _context.EmployeeMasters.Add(employee);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Employee registered successfully.",
                    empId = employee.EmpId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while registering employee.",
                    error = ex.InnerException?.InnerException?.Message
                             ?? ex.InnerException?.Message
                             ?? ex.Message
                });
            }
        }


        [Authorize]
        [HttpPut("employee/{id}")]
        public async Task<IActionResult> UpdateEmployee(
       int id,
       [FromBody] EmployeeCreateDto model)
        {
            try
            {
                // Get OrganizationId from JWT
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "orgId not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                // Find employee only inside current organization
                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == id &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found."
                    });
                }

                // Update employee details
                employee.EmployeeCode = model.EmployeeCode;
                employee.FullName = model.FullName;
                employee.EmailId = model.EmailId;
                employee.Phone = model.Phone;

                employee.RoleId = model.RoleId;
                employee.ReportingManagerId = model.ReportingManagerId;
                employee.DateOfJoining = model.DateOfJoining;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Employee updated successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while updating employee.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpDelete("employee/{id}")]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "orgId not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == id &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found."
                    });
                }

                employee.IsActive = false;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Employee deactivated successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deactivating employee.",
                    error = ex.Message
                });
            }
        }
        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "orgId not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                var roles = await _context.RoleMasters
                    .Where(x =>
                        x.OrganizationId == organizationId &&
                        x.IsActive)
                    .OrderBy(x => x.RoleName)
                    .Select(x => new
                    {
                        roleId = x.RoleId,
                        roleName = x.RoleName
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = roles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while fetching roles.",
                    error = ex.Message
                });
            }
        }


        // =====================================================
        // POST - ADD ROLE
        // =====================================================

        [HttpPost("roles")]
        public async Task<IActionResult> CreateRole(
            [FromBody] CreateRoleDto model)
        {
            try
            {
                if (model == null ||
                    string.IsNullOrWhiteSpace(model.RoleName))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "RoleName is required."
                    });
                }


                // Get OrganizationId from JWT
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Org_Id not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);


                // Check duplicate role
                var exists = await _context.RoleMasters
                    .AnyAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.RoleName == model.RoleName.Trim() &&
                        x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Role already exists."
                    });
                }


                // Create role
                var role = new RoleMaster
                {
                    RoleName = model.RoleName.Trim(),
                    OrganizationId = organizationId,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };

                _context.RoleMasters.Add(role);

                await _context.SaveChangesAsync();

                var access = new CompanyexpenseAccesses
                {
                    OrganizationId = organizationId,
                    RoleId = role.RoleId,
                    Action = true,       // 1 = Limited Access
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };

                _context.CompanyexpenseAccesses.Add(access);

                await _context.SaveChangesAsync();


                return Ok(new
                {
                    success = true,
                    message = "Role created successfully.",
                    data = new
                    {
                        roleId = role.RoleId,
                        roleName = role.RoleName
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while creating role.",
                    error = ex.Message
                });
            }
        }

        [HttpPut("roles/{id}")]
        public async Task<IActionResult> UpdateRole(
    int id,
    [FromBody] CreateRoleDto model)
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Org_Id not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                var role = await _context.RoleMasters
                    .FirstOrDefaultAsync(x =>
                        x.RoleId == id &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (role == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Role not found."
                    });
                }

                var exists = await _context.RoleMasters
                    .AnyAsync(x =>
                        x.RoleId != id &&
                        x.OrganizationId == organizationId &&
                        x.RoleName == model.RoleName.Trim() &&
                        x.IsActive);

                if (exists)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Role already exists."
                    });
                }

                role.RoleName = model.RoleName.Trim();

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Role updated successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while updating role.",
                    error = ex.Message
                });
            }
        }

        [HttpDelete("roles/{id}")]
        public async Task<IActionResult> DeleteRole(int id)
        {
            try
            {
                var orgIdClaim = User.FindFirst("orgId")?.Value;

                if (string.IsNullOrEmpty(orgIdClaim))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "orgId not found in token."
                    });
                }

                int organizationId = int.Parse(orgIdClaim);

                var role = await _context.RoleMasters
                    .FirstOrDefaultAsync(x =>
                        x.RoleId == id &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (role == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Role not found."
                    });
                }

                role.IsActive = false;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Role deactivated successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error while deleting role.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpPost("{id}/employment-information")]
        public async Task<IActionResult> SaveEmploymentInformation(
    int id,
    [FromBody] EmployeeEmploymentInformationDto dto)
        {
            try
            {
                var employeeExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmpId == id);

                if (!employeeExists)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found"
                    });
                }

                var sql = @"
            UPDATE employee_master
            SET
                category_id = {0},
                department_id = {1},
                Designationid = {2},
                ReportingManager_Id = {3},
                date_of_joining = {4},
                ProbationPeriod = {5},
                ConfirmationDate = {6},
                GradeID = {7}
            WHERE emp_id = {8}";

                await _context.Database.ExecuteSqlRawAsync(
                    sql,
                    dto.CategoryId,
                    dto.DepartmentId,
                    dto.DesignationId,//HIII HELLO 
                    dto.ReportingManagerId,//updated one
                    dto.DateOfJoining,
                    dto.ProbationPeriod,
                    dto.ConfirmationDate,
                    dto.GradeID,
                    id
                );

                return Ok(new
                {
                    success = true,
                    message = "Employment information updated successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        //    [HttpPost("{id}/employment-information")]
        //    public async Task<IActionResult> SaveEmploymentInformation(
        //int id,
        //[FromBody] EmployeeEmploymentInformationDto dto)
        //    {
        //        try
        //        {
        //            var employee = await _context.EmployeeMasters
        //                .FirstOrDefaultAsync(e => e.EmpId == id);

        //            if (employee == null)
        //            {
        //                return NotFound(new { message = "Employee not found" });
        //            }

        //            var data = await _context.EmployeeEmploymentInformations
        //                .FirstOrDefaultAsync(x => x.EmpId == id);

        //            if (data == null)
        //            {
        //                data = new EmployeeEmploymentInformation
        //                {
        //                    EmpId = id
        //                };

        //                _context.EmployeeEmploymentInformations.Add(data);
        //            }

        //            // ✅ ONLY IDS SAVE
        //            data.EmployeeType = dto.EmployeeType;
        //            data.Departmentid = dto.Departmentid;
        //            data.DesignationId = dto.DesignationId;
        //            data.ReportingManager = dto.ReportingManager;
        //            data.DateOfJoining = dto.DateOfJoining;
        //            data.ProbationPeriod = dto.ProbationPeriod;
        //            data.ConfirmationDate = dto.ConfirmationDate;
        //            data.EmployeeGradeLevel = dto.EmployeeGradeLevel;

        //            await _context.SaveChangesAsync();

        //            return Ok(new
        //            {
        //                success = true,
        //                message = "Saved successfully"
        //            });
        //        }
        //        catch (Exception ex)
        //        {
        //            return StatusCode(500, new
        //            {
        //                success = false,
        //                error = ex.Message
        //            });
        //        }
        //    }

        //Employment Information GET Muthu
        [Authorize]
        [HttpGet("{id}/employment-information")]
        public async Task<IActionResult> GetEmploymentInformation(int id)
        {
            try
            {
                var employmentInfo = await _context.EmployeeMasters
                    .Where(e => e.EmpId == id)
                    .Select(e => new
                    {
                        e.EmpId,
                        e.CategoryId,
                        e.DepartmentId,
                        e.DesignationId,
                        e.ReportingManagerId,
                        e.DateOfJoining,
                        e.ProbationPeriod,
                        e.ConfirmationDate,
                        e.GradeId
                    })
                    .FirstOrDefaultAsync();

                if (employmentInfo == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Employment information fetched successfully",
                    data = employmentInfo
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // RAJESH
        //[HttpGet("{id}/employment-information")]
        //public async Task<IActionResult> GetEmploymentInformation(int id)
        //{
        //    try
        //    {
        //        var result = await (from e in _context.EmployeeEmploymentInformations
        //                            join d in _context.Designations
        //                            on e.DesignationId equals d.Id into desGroup
        //                            from d in desGroup.DefaultIfEmpty()

        //                            where e.EmpId == id
        //                            select new
        //                            {
        //                                e.Id,
        //                                e.EmpId,
        //                                e.EmployeeType,

        //                                DesignationId = e.DesignationId,
        //                                DesignationName = d != null ? d.Name : null,

        //                                e.ReportingManager,
        //                                e.DateOfJoining,
        //                                e.ProbationPeriod,
        //                                e.ConfirmationDate,
        //                                e.EmployeeGradeLevel
        //                            }).FirstOrDefaultAsync();

        //        if (result == null)
        //        {
        //            return NotFound(new
        //            {
        //                success = false,
        //                message = "Employment information not found"
        //            });
        //        }

        //        return Ok(new
        //        {
        //            success = true,
        //            message = "Employment information fetched successfully",
        //            data = result
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            success = false,
        //            message = ex.Message
        //        });
        //    }
        //}

        // dROPDOWN API FOR REGISTER 
        [Authorize]
        [HttpGet("departments")]
        public async Task<IActionResult> GetDepartments()
        {
            try
            {
                var data = await _context.Department_Master
                    .Select(x => new DropdownDTO
                    {
                        Id = x.DepartmentId,
                        Name = x.DepartmentName
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching departments",
                    error = ex.Message
                });
            }
        }
        [Authorize]
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var data = await _context.Category_Master
                    .Select(x => new DropdownDTO
                    {
                        Id = x.CategoryTypeId,
                        Name = x.CategoryName
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching categories",
                    error = ex.Message
                });
            }
        }


        // employee address add only added 
        //   muthu
        [Authorize]
        [HttpPost("{id}/address")]
        public async Task<IActionResult> SaveAddress(int id, [FromBody] EmployeeAddressDto dto)
        {
            try
            {
                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(e => e.EmpId == id);

                if (employee == null)
                    return NotFound("Employee not found");

                var address = await _context.EmployeeAddresses
                    .FirstOrDefaultAsync(a => a.EmpId == id);

                if (address == null)
                {
                    address = new EmployeeAddress
                    {
                        EmpId = id
                    };

                    _context.EmployeeAddresses.Add(address);
                }

                address.CurrentDoorNo = dto.CurrentDoorNo;
                address.CurrentStreet = dto.CurrentStreet;
                address.CurrentArea = dto.CurrentArea;
                address.CurrentCity = dto.CurrentCity;
                address.CurrentState = dto.CurrentState;
                address.CurrentPincode = dto.CurrentPincode;
                address.CurrentCountry = dto.CurrentCountry;

                address.PermanentDoorNo = dto.PermanentDoorNo;
                address.PermanentStreet = dto.PermanentStreet;
                address.PermanentArea = dto.PermanentArea;
                address.PermanentCity = dto.PermanentCity;
                address.PermanentState = dto.PermanentState;
                address.PermanentPincode = dto.PermanentPincode;
                address.PermanentCountry = dto.PermanentCountry;

                await _context.SaveChangesAsync();

                return Ok("Address saved successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Something went wrong",
                    error = ex.Message
                });
            }
        }
        // set address Muthu
        [Authorize]
        [HttpGet("{id}/address")]
        public async Task<IActionResult> GetAddress(int id)
        {
            try
            {
                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(e => e.EmpId == id);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found"
                    });
                }

                var address = await _context.EmployeeAddresses
                    .FirstOrDefaultAsync(a => a.EmpId == id);

                if (address == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Address not found"
                    });
                }

                var response = new
                {
                    currentDoorNo = address.CurrentDoorNo,
                    currentStreet = address.CurrentStreet,
                    currentArea = address.CurrentArea,
                    currentCity = address.CurrentCity,
                    currentState = address.CurrentState,
                    currentPincode = address.CurrentPincode,
                    currentCountry = address.CurrentCountry,

                    permanentDoorNo = address.PermanentDoorNo,
                    permanentStreet = address.PermanentStreet,
                    permanentArea = address.PermanentArea,
                    permanentCity = address.PermanentCity,
                    permanentState = address.PermanentState,
                    permanentPincode = address.PermanentPincode,
                    permanentCountry = address.PermanentCountry
                };

                return Ok(new
                {
                    success = true,
                    message = "Address fetched successfully",
                    data = response
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Something went wrong",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
        // employee personal details only added 
        //  muthu  full code

        [Authorize]
        [HttpPost("{id}/personal-details")]
        public async Task<IActionResult> SavePersonalDetails(int id, [FromBody] EmployeePersonalDetailsDto dto)
        {
            try
            {
                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(e => e.EmpId == id);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        message = "Employee not found"
                    });
                }

                var personal = await _context.EmployeePersonalDetails
                    .FirstOrDefaultAsync(p => p.EmpId == id);

                if (personal == null)
                {
                    personal = new EmployeePersonalDetails
                    {
                        EmpId = id
                    };

                    await _context.EmployeePersonalDetails.AddAsync(personal);
                }

                personal.FirstName = dto.FirstName;
                personal.LastName = dto.LastName;
                personal.FullName = dto.FullName;
                personal.FatherName = dto.FatherName;
                personal.Religion = dto.Religion;
                personal.Mobile = dto.Mobile;
                personal.AlternateContact = dto.AlternateContact;


                if (dto.DateOfBirth.HasValue)
                {
                    personal.DateOfBirth = DateTime.SpecifyKind(
                        dto.DateOfBirth.Value,
                        DateTimeKind.Utc
                    );
                }

                personal.MaritalStatus = dto.MaritalStatus;
                personal.BloodGroup = dto.BloodGroup;
                personal.Nationality = dto.Nationality;
                personal.Gender = dto.Gender;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Personal details saved successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Something went wrong",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        //EDIT EDUCSATION   MUTHU  full code  "AddMultipleEducation alternative "
        [Authorize]
        [HttpPut("education/{educationId}")]
        public async Task<IActionResult> UpdateEducation(
    int educationId,
    [FromBody] EmployeeEducationDto dto)
        {
            try
            {
                var education = await _context.EmployeeEducations
                    .FirstOrDefaultAsync(e => e.Id == educationId);

                if (education == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Education record not found"
                    });
                }

                // UPDATE SAME ROW
                education.Qualification = dto.Qualification;
                education.DegreeName = dto.DegreeName;
                education.University = dto.University;
                education.YearOfPassing = dto.YearOfPassing;
                education.Percentage = dto.Percentage;
                education.Certifications = dto.Certifications;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Education updated successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Something went wrong",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        // employee education only added database 
        //  [Authorize]  muthu  full code
        [Authorize]
        [HttpPost("{id}/education/bulk")]
        public async Task<IActionResult> SaveEducationBulk(
     int id,
     [FromBody] List<EmployeeEducationDto> dtos)
        {
            try
            {
                var employeeExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmpId == id);

                if (!employeeExists)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Employee not found"
                    });
                }

                foreach (var dto in dtos)
                {

                    if (dto.Id > 0)
                    {
                        var existingEducation = await _context.EmployeeEducations
                            .FirstOrDefaultAsync(e =>
                                e.Id == dto.Id &&
                                e.EmpId == id);

                        if (existingEducation != null)
                        {
                            existingEducation.Qualification = dto.Qualification;
                            existingEducation.DegreeName = dto.DegreeName;
                            existingEducation.University = dto.University;
                            existingEducation.YearOfPassing = dto.YearOfPassing;
                            existingEducation.Percentage = dto.Percentage;
                            existingEducation.Certifications = dto.Certifications;

                            continue;
                        }
                    }

                    var newEducation = new EmployeeEducation
                    {
                        EmpId = id,
                        Qualification = dto.Qualification,
                        DegreeName = dto.DegreeName,
                        University = dto.University,
                        YearOfPassing = dto.YearOfPassing,
                        Percentage = dto.Percentage,
                        Certifications = dto.Certifications
                    };

                    await _context.EmployeeEducations.AddAsync(newEducation);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Education details saved successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Something went wrong",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        // employee compensation only added database 
        [Authorize]
        [HttpPost("{id}/compensation")]
        public async Task<IActionResult> SaveCompensation(
    int id,
    [FromBody] EmployeeCompensationDto dto)
        {
            try
            {
                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(e => e.EmpId == id);

                if (employee == null)
                    return NotFound(new
                    {
                        message = "Employee not found"
                    });

                var compensation = await _context.EmployeeCompensations
                    .FirstOrDefaultAsync(c => c.EmpId == id);

                if (compensation == null)
                {
                    compensation = new EmployeeCompensation
                    {
                        EmpId = id
                    };

                    _context.EmployeeCompensations.Add(compensation);
                }

                compensation.AccountHolderName = dto.AccountHolderName;
                compensation.BankName = dto.BankName;
                compensation.BranchName = dto.BranchName;
                compensation.AccountNumber = dto.AccountNumber;
                compensation.IFSCCode = dto.IFSCCode;
                compensation.AccountType = dto.AccountType;
                compensation.TaxInfo = dto.TaxInfo;
                compensation.Benefits = dto.Benefits;
                compensation.PANNo = dto.PANNo;
                compensation.PFNo = dto.PFNo;
                compensation.UANNo = dto.UANNo;
                compensation.ESINo = dto.ESINo;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Compensation details saved successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while saving compensation details",
                    error = ex.Message
                });
            }
        }


        // employee previous-employment only added database 
        [Authorize]
        [HttpPost("{id}/previous-employment")]
        public async Task<IActionResult> SavePreviousEmployment(
    int id,
    [FromBody] List<EmployeePreviousEmploymentDto> dtos)
        {
            try
            {
                if (dtos == null || !dtos.Any())
                    return BadRequest("No employment data provided");

                var employeeExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmpId == id);

                if (!employeeExists)
                    return NotFound("Employee not found");

                int inserted = 0;
                int updated = 0;

                foreach (var dto in dtos)
                {
                    var companyNormalized = (dto.CompanyName ?? string.Empty).Trim().ToLowerInvariant();

                    var existing = await _context.EmployeePreviousEmployments
                        .Where(e => e.EmpId == id)
                        .FirstOrDefaultAsync(e =>
                            ((e.CompanyName ?? string.Empty).Trim().ToLower() == companyNormalized)
                            && e.DateOfJoining == dto.DateOfJoining);

                    if (existing != null)
                    {
                        existing.CompanyName = dto.CompanyName;
                        existing.Designation = dto.Designation;
                        existing.Experience = dto.Experience;
                        existing.DateOfJoining = dto.DateOfJoining;
                        existing.DateOfRelieving = dto.DateOfRelieving;
                        existing.ReasonForLeaving = dto.ReasonForLeaving;
                        existing.LastDrawnSalary = dto.LastDrawnSalary;

                        updated++;
                    }
                    else
                    {
                        var employment = new EmployeePreviousEmployment
                        {
                            EmpId = id,
                            CompanyName = dto.CompanyName,
                            Designation = dto.Designation,
                            Experience = dto.Experience,
                            DateOfJoining = dto.DateOfJoining,
                            DateOfRelieving = dto.DateOfRelieving,
                            ReasonForLeaving = dto.ReasonForLeaving,
                            LastDrawnSalary = dto.LastDrawnSalary
                        };

                        _context.EmployeePreviousEmployments.Add(employment);
                        inserted++;
                    }
                }

                await _context.SaveChangesAsync();

                return Ok($"{inserted} record(s) created, {updated} record(s) updated");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while saving previous employment details",
                    error = ex.Message
                });
            }
        }

        // GET Employee GetPersonalDetails
        [Authorize]
        [HttpGet("{id}/personal-details")]
        public async Task<IActionResult> GetPersonalDetails(int id)
        {
            try
            {
                var personal = await _context.EmployeePersonalDetails
                    .FirstOrDefaultAsync(p => p.EmpId == id);

                if (personal == null)
                    return NotFound("Personal details not found");

                return Ok(personal);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching personal details",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpDelete("{id}/documents/{documentType}")] // muthu  full code 
        public async Task<IActionResult> DeleteEmployeeDocument(
    int id,
    string documentType)
        {
            try
            {
                var documents = await _context.EmployeeDocuments
                    .FirstOrDefaultAsync(d => d.EmployeeId == id);

                if (documents == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Documents not found"
                    });
                }

                switch (documentType.ToLower())
                {
                    case "resume":
                        documents.Resume = null;
                        break;

                    case "offerletter":
                        documents.OfferLetter = null;
                        break;

                    case "appointmentletter":
                        documents.AppointmentLetter = null;
                        break;

                    case "idproof":
                        documents.IdProof = null;
                        break;

                    case "addressproof":
                        documents.AddressProof = null;
                        break;

                    case "educationalcertificates":
                        documents.EducationalCertificates = null;
                        break;

                    case "experienceletters":
                        documents.ExperienceLetters = null;
                        break;

                    case "passportphotos":
                        documents.PassportPhotos = null;
                        break;

                    case "bankaccountdetails":
                        documents.BankAccountDetails = null;
                        break;

                    case "signednda":
                        documents.SignedNda = null;
                        break;

                    default:
                        return BadRequest(new
                        {
                            success = false,
                            message = "Invalid document type"
                        });
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"{documentType} deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while deleting document",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // GET Employee Documents

        [Authorize]
        [HttpGet("{id}/documents")] // muthu  full code
        public async Task<IActionResult> GetEmployeeDocuments(int id)
        {
            try
            {
                var documents = await _context.EmployeeDocuments
                    .FirstOrDefaultAsync(d => d.EmployeeId == id);

                if (documents == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Documents not found"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Documents fetched successfully",
                    data = documents
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while fetching employee documents",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // employee UploadDocuments only added database 
        [Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocuments([FromForm] EmployeeDocumentsDto dto)
        {
            try
            {
                var employeeExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmpId == dto.EmployeeId);

                if (!employeeExists)
                    return BadRequest("Invalid Employee ID");

                var existing = await _context.EmployeeDocuments
                    .FirstOrDefaultAsync(d => d.EmployeeId == dto.EmployeeId);

                if (existing == null)
                {
                    existing = new EmployeeDocuments
                    {
                        EmployeeId = dto.EmployeeId
                    };

                    _context.EmployeeDocuments.Add(existing);
                }

                if (dto.Resume != null)
                    existing.Resume = await ConvertToBytes(dto.Resume);

                if (dto.OfferLetter != null)
                    existing.OfferLetter = await ConvertToBytes(dto.OfferLetter);

                if (dto.AppointmentLetter != null)
                    existing.AppointmentLetter = await ConvertToBytes(dto.AppointmentLetter);

                if (dto.IdProof != null)
                    existing.IdProof = await ConvertToBytes(dto.IdProof);

                if (dto.AddressProof != null)
                    existing.AddressProof = await ConvertToBytes(dto.AddressProof);

                if (dto.EducationalCertificates != null)
                    existing.EducationalCertificates = await ConvertToBytes(dto.EducationalCertificates);

                if (dto.ExperienceLetters != null)
                    existing.ExperienceLetters = await ConvertToBytes(dto.ExperienceLetters);

                if (dto.PassportPhotos != null)
                    existing.PassportPhotos = await ConvertToBytes(dto.PassportPhotos);

                if (dto.BankAccountDetails != null)
                    existing.BankAccountDetails = await ConvertToBytes(dto.BankAccountDetails);

                if (dto.SignedNda != null)
                    existing.SignedNda = await ConvertToBytes(dto.SignedNda);

                await _context.SaveChangesAsync();

                return Ok("Documents Stored Successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while uploading documents",
                    error = ex.Message
                });
            }
        }

        private async Task<byte[]> ConvertToBytes(IFormFile file)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            return ms.ToArray();
        }


        [HttpGet("{id}")]
        public async Task<ActionResult<EmployeeMaster>> GetEmployee(int id)
        {
            var employee = await _context.EmployeeMasters
                .FirstOrDefaultAsync(e => e.EmpId == id);

            if (employee == null)
                return NotFound();

            return employee;
        }


        // Get Employee Education    // muthu   full code

        [Authorize]
        [HttpGet("{id}/education")]
        public async Task<IActionResult> GetEducation(int id)
        {
            try
            {
                var educations = await _context.EmployeeEducations
                    .Where(e => e.EmpId == id)
                    .ToListAsync();

                if (educations == null || !educations.Any())
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "No education records found"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Education details fetched successfully",
                    data = educations
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while fetching education details",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        // Get Employee Compensation 

        [Authorize]
        [HttpGet("{id}/compensation")]
        public async Task<ActionResult<EmployeeCompensation>> GetCompensation(int id)
        {
            try
            {
                var compensation = await _context.EmployeeCompensations
                    .FirstOrDefaultAsync(c => c.EmpId == id);

                if (compensation == null)
                    return NotFound();

                return Ok(compensation);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching compensation details",
                    error = ex.Message
                });
            }
        }


        // Get Previous Employment

        [Authorize]
        [HttpGet("{id}/previous-employment")]
        public async Task<ActionResult<IEnumerable<EmployeePreviousEmployment>>> GetPreviousEmployment(int id)
        {
            try
            {
                var previous = await _context.EmployeePreviousEmployments
                    .Where(p => p.EmpId == id)
                    .ToListAsync();

                return Ok(previous);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching previous employment details",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpPost("import")]
        public async Task<IActionResult> ImportAttendance(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("File empty");

                using var reader = new StreamReader(file.OpenReadStream());

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    MissingFieldFound = null,
                    HeaderValidated = null
                };

                using var csv = new CsvReader(reader, config);

                csv.Read();
                csv.ReadHeader();

                var dateFormats = new[] { "dd-MMM-yy", "dd-MMM-yyyy", "dd-MM-yyyy", "dd/MM/yyyy", "M/d/yyyy", "yyyy-MM-dd" };

                var cache = new Dictionary<string, AttendanceMaster>();

                while (csv.Read())
                {
                    try
                    {
                        string empCode = csv.GetField(3)?.Trim();
                        string dateValue = csv.GetField(1)?.Trim();
                        string activity = csv.GetField(14)?.Trim();
                        string timeValue = csv.GetField(15)?.Trim();

                        if (string.IsNullOrEmpty(empCode))
                            continue;

                        var employee = await _context.EmployeeMasters
                            .FirstOrDefaultAsync(e => e.EmployeeCode == empCode);

                        if (employee == null)
                            continue;

                        if (!DateTime.TryParseExact(dateValue, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                        {
                            if (!DateTime.TryParse(dateValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                                continue;
                        }

                        if (!DateTime.TryParse(timeValue, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out DateTime parsedTime))
                            continue;

                        var attendanceDateOnly = parsedDate.Date;
                        var checkTime = parsedTime.TimeOfDay;

                        bool isCheckIn = !string.IsNullOrEmpty(activity) &&
                                         activity.Contains("CheckedIn", StringComparison.OrdinalIgnoreCase);

                        bool isCheckOut = !string.IsNullOrEmpty(activity) &&
                                          activity.Contains("CheckedOut", StringComparison.OrdinalIgnoreCase);

                        var key = $"{employee.EmpId}_{attendanceDateOnly:yyyy-MM-dd}";

                        if (!cache.TryGetValue(key, out var attendance))
                        {
                            var nextDay = attendanceDateOnly.AddDays(1);

                            attendance = await _context.AttendanceMasters
                                .Where(a => a.EmpId == employee.EmpId
                                            && a.AttendanceDate >= attendanceDateOnly
                                            && a.AttendanceDate < nextDay)
                                .FirstOrDefaultAsync();

                            if (attendance == null)
                            {
                                attendance = new AttendanceMaster
                                {
                                    EmpId = employee.EmpId,
                                    ShiftId = 1,
                                    WeekOffId = 1,
                                    AttendanceDate = attendanceDateOnly,
                                    CreatedAt = DateTime.UtcNow
                                };

                                _context.AttendanceMasters.Add(attendance);
                            }

                            cache[key] = attendance;
                        }

                        if (isCheckIn)
                        {
                            if (!attendance.CheckIn.HasValue ||
                                checkTime < attendance.CheckIn.Value)
                            {
                                attendance.CheckIn = checkTime;
                            }
                        }

                        if (isCheckOut)
                        {
                            if (!attendance.CheckOut.HasValue ||
                                checkTime > attendance.CheckOut.Value)
                            {
                                attendance.CheckOut = checkTime;
                            }
                        }

                        attendance.ModifiedAt = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

                await _context.SaveChangesAsync();

                return Ok("Attendance Imported Successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while importing attendance",
                    error = ex.Message
                });
            }
        }


        // GET ALL ATTENDANCE
        [Authorize]
        [HttpGet("attendance")]
        public async Task<IActionResult> GetAttendance()
        {
            try
            {
                var data = await _context.AttendanceMasters
                    .Include(a => a.Employee)
                    .Include(a => a.Shift)
                    .Include(a => a.WeekOff)
                    .Select(a => new AttendanceDto
                    {
                        AttendanceId = a.AttendanceId,
                        EmpId = a.EmpId,
                        EmployeeCode = a.Employee.EmployeeCode,
                        FullName = a.Employee.FullName,
                        AttendanceDate = a.AttendanceDate,
                        CheckIn = a.CheckIn,
                        CheckOut = a.CheckOut,
                        Shift = a.Shift == null ? null : new ShiftDto
                        {
                            ShiftId = a.Shift.ShiftId,
                            ShiftName = a.Shift.ShiftName
                        },
                        WeekOff = a.WeekOff == null ? null : new WeekOffDto
                        {
                            WeekOffId = a.WeekOff.WeekOffId,
                            Name = a.WeekOff.Name
                        }
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching attendance data",
                    error = ex.Message
                });
            }
        }
        // GET ATTENDANCE BY EMPLOYEE
        [Authorize]
        [HttpGet("employee/{empId}/attendance")]
        public async Task<IActionResult> GetEmployeeAttendance(int empId)
        {
            try
            {
                var data = await _context.AttendanceMasters
                    .Where(a => a.EmpId == empId)
                    .Include(a => a.Shift)
                    .Include(a => a.WeekOff)
                    .Select(a => new AttendanceDto
                    {
                        AttendanceId = a.AttendanceId,
                        EmpId = a.EmpId,
                        EmployeeCode = null,
                        FullName = null,
                        AttendanceDate = a.AttendanceDate,
                        CheckIn = a.CheckIn,
                        CheckOut = a.CheckOut,
                        Shift = a.Shift == null ? null : new ShiftDto
                        {
                            ShiftId = a.Shift.ShiftId,
                            ShiftName = a.Shift.ShiftName
                        },
                        WeekOff = a.WeekOff == null ? null : new WeekOffDto
                        {
                            WeekOffId = a.WeekOff.WeekOffId,
                            Name = a.WeekOff.Name
                        }
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching employee attendance",
                    error = ex.Message
                });
            }
        }

        //CREATE PAYROLL HEAD
        [Authorize]
        [HttpPost("add-head")]
        public async Task<IActionResult> AddPayrollHead([FromBody] PayrollHead head)
        {
            try
            {
                _context.PayrollHeads.Add(head);
                await _context.SaveChangesAsync();

                return Ok(head);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while adding payroll head",
                    error = ex.Message
                });
            }
        }


        //  GET ALL PAYROLL HEADS      
        [Authorize]
        [HttpGet("heads")]
        public async Task<IActionResult> GetPayrollHeads()
        {
            try
            {
                var heads = await _context.PayrollHeads.ToListAsync();

                return Ok(heads);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching payroll heads",
                    error = ex.Message
                });
            }
        }


        // 3️⃣ CREATE PAY ELEMENT
        [Authorize]
        [HttpPost("add-element")]
        public async Task<IActionResult> AddPayElement([FromBody] PayElement element)
        {
            try
            {
                var headExists = await _context.PayrollHeads
                    .AnyAsync(x => x.HeadId == element.HeadId);

                if (!headExists)
                    return BadRequest("Invalid HeadId");

                if (element.ElementId != 0)
                {
                    var existing = await _context.PayElements.FindAsync(element.ElementId);

                    if (existing != null)
                    {
                        existing.ElementValue = element.ElementValue;
                        existing.HeadId = element.HeadId;
                        existing.ValueCalculating = element.ValueCalculating;
                        existing.ValueType = element.ValueType;

                        _context.PayElements.Update(existing);
                        await _context.SaveChangesAsync();

                        return Ok(existing);
                    }
                }

                element.ElementId = 0;
                element.PayrollHead = null;

                _context.PayElements.Add(element);
                await _context.SaveChangesAsync();

                return Ok(element);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while adding or updating pay element",
                    error = ex.Message
                });
            }
        }
        [Authorize]
        //  GET PAY ELEMENTS
        [HttpGet("elements")]
        public async Task<IActionResult> GetPayElements()
        {
            try
            {
                var elements = await _context.PayElements
                    .Include(e => e.PayrollHead)
                    .ToListAsync();

                return Ok(elements);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching pay elements",
                    error = ex.Message
                });
            }
        }


        // ADD PAYROLL ENTRY + UPDATE SUMMARY  muthu   full code30
        [Authorize]
        [HttpPost("add-payroll")]
        public async Task<IActionResult> AddPayroll([FromBody] Payroll payroll)
        {
            try
            {
                // 🔹 Check pay element exists
                var elementExists = await _context.PayElements
                    .Include(e => e.PayrollHead)
                    .FirstOrDefaultAsync(e => e.ElementId == payroll.ElementId);

                if (elementExists == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid ElementId"
                    });
                }

                // 🔹 Check existing payroll
                var existingPayroll = await _context.Payrolls
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == payroll.EmpId &&
                        x.ElementId == payroll.ElementId &&
                        x.PayrollMonth == payroll.PayrollMonth &&
                        x.FinancialYear == payroll.FinancialYear);

                if (existingPayroll != null)
                {
                    // UPDATE existing payroll
                    existingPayroll.Amount = payroll.Amount;
                    existingPayroll.CreatedDate = DateTime.UtcNow;

                    _context.Payrolls.Update(existingPayroll);
                }
                else
                {
                    // INSERT new payroll
                    payroll.CreatedDate = DateTime.UtcNow;

                    await _context.Payrolls.AddAsync(payroll);
                }

                // 🔹 Save payroll
                await _context.SaveChangesAsync();

                // 🔹 Get all payroll rows for employee/month/year
                var payrollData = await _context.Payrolls
                    .Include(p => p.PayElement)
                    .ThenInclude(e => e.PayrollHead)
                    .Where(p =>
                        p.EmpId == payroll.EmpId &&
                        p.PayrollMonth == payroll.PayrollMonth &&
                        p.FinancialYear == payroll.FinancialYear)
                    .ToListAsync();

                // 🔹 Calculate totals
                decimal gross = payrollData
                    .Where(x => x.PayElement.PayrollHead.HeadType == 1)
                    .Sum(x => x.Amount);

                decimal deductions = payrollData
                    .Where(x => x.PayElement.PayrollHead.HeadType == 2)
                    .Sum(x => x.Amount);

                decimal net = gross - deductions;

                decimal annualCtc = gross * 12;

                // 🔹 Check existing summary
                var summary = await _context.EmployeePayrollSummary
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == payroll.EmpId &&
                        x.PayrollMonth == payroll.PayrollMonth &&
                        x.FinancialYear == payroll.FinancialYear);

                if (summary != null)
                {
                    // UPDATE summary
                    summary.MonthlyGross = gross;
                    summary.TotalDeductions = deductions;
                    summary.NetTakeHome = net;
                    summary.AnnualCtc = annualCtc;
                    summary.CreatedAt = DateTime.UtcNow;

                    _context.EmployeePayrollSummary.Update(summary);
                }
                else
                {
                    // INSERT summary
                    var newSummary = new EmployeePayrollSummary
                    {
                        EmpId = payroll.EmpId,
                        PayrollMonth = payroll.PayrollMonth,
                        FinancialYear = payroll.FinancialYear,
                        MonthlyGross = gross,
                        TotalDeductions = deductions,
                        NetTakeHome = net,
                        AnnualCtc = annualCtc,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _context.EmployeePayrollSummary.AddAsync(newSummary);
                }

                // 🔹 Save summary
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Payroll Saved Successfully",
                    gross,
                    deductions,
                    net,
                    annualCtc
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Something went wrong",
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }
        }


        // GET EMPLOYEE PAYROLL
        [Authorize]
        [HttpGet("employee/{empId}")]
        public async Task<IActionResult> GetEmployeePayroll(int empId)
        {
            try
            {
                var payroll = await _context.Payrolls
                    .Where(p => p.EmpId == empId)
                    .Include(p => p.PayElement)
                    .ThenInclude(e => e.PayrollHead)
                    .ToListAsync();

                return Ok(payroll);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching employee payroll",
                    error = ex.Message
                });
            }
        }
        [Authorize]
        [HttpPost("generate-summary")]
        public async Task<IActionResult> GeneratePayrollSummary([FromBody] EmployeePayrollSummary model)
        {
            try
            {
                if (model == null)
                    return BadRequest("Invalid data");

                var summary = await _context.EmployeePayrollSummary
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == model.EmpId &&
                        x.PayrollMonth == model.PayrollMonth &&
                        x.FinancialYear == model.FinancialYear);

                if (summary != null)
                {
                    summary.MonthlyGross = model.MonthlyGross;
                    summary.TotalDeductions = model.TotalDeductions;
                    summary.NetTakeHome = model.NetTakeHome;
                    summary.AnnualCtc = model.AnnualCtc;
                    summary.CreatedAt = DateTime.UtcNow;
                }
                else
                {
                    summary = new EmployeePayrollSummary
                    {
                        EmpId = model.EmpId,
                        PayrollMonth = model.PayrollMonth,
                        FinancialYear = model.FinancialYear,
                        MonthlyGross = model.MonthlyGross,
                        TotalDeductions = model.TotalDeductions,
                        NetTakeHome = model.NetTakeHome,
                        AnnualCtc = model.AnnualCtc,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _context.EmployeePayrollSummary.AddAsync(summary);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Payroll saved successfully",
                    summary
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while generating payroll summary",
                    error = ex.Message
                });
            }
        }


        // Rajesh payslip
        [Authorize]
        [HttpGet("payslip/{employeeId}")]
        public async Task<IActionResult> GetPayslip(int employeeId)
        {
            try
            {
                var result = await _context.PayslipDTO
                    .FromSqlInterpolated(
                        $"EXEC sp_GetEmployeePayslip @EmployeeId = {employeeId}")
                    .ToListAsync();

                if (result == null || result.Count == 0)
                {
                    return NotFound(new
                    {
                        Message = "Payslip Not Found"
                    });
                }

                var first = result.First();

                var response = new PayslipDTO
                {
                    EmployeeName = first.EmployeeName,
                    EmployeeCode = first.EmployeeCode,

                    Department = first.Department,
                    Designation = first.Designation,

                    FatherName = first.FatherName,
                    DateOfBirth = first.DateOfBirth,

                    BankName = first.BankName,
                    AccountNumber = first.AccountNumber,

                    PFNo = first.PFNo,
                    UANNo = first.UANNo,
                    ESINo = first.ESINo,

                    PayrollMonth = first.PayrollMonth,
                    FinancialYear = first.FinancialYear,

                    MonthlyGross = first.MonthlyGross,
                    TotalDeductions = first.TotalDeductions,
                    NetTakeHome = first.NetTakeHome,
                    AnnualCtc = first.AnnualCtc,

                    Earnings = result
                        .Where(x => x.head_type == 1)
                        .Select(x => new SalaryComponentDTO
                        {
                            HeadName = x.head_name,
                            Amount = x.Amount
                        }).ToList(),

                    Deductions = result
                        .Where(x => x.head_type == 2)
                        .Select(x => new SalaryComponentDTO
                        {
                            HeadName = x.head_name,
                            Amount = x.Amount
                        }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "Internal Server Error",
                    Error = ex.Message
                });
            }
        }


        [Authorize]
        [HttpGet("payroll-overview")]
        public async Task<IActionResult> GetPayrollOverview(int empId, int month, string financialYear)
        {
            try
            {
                var earnings = await _context.Payrolls
                    .Include(p => p.PayElement)
                    .ThenInclude(e => e.PayrollHead)
                    .Where(p => p.EmpId == empId &&
                                p.PayrollMonth == month &&
                                p.FinancialYear == financialYear &&
                                p.PayElement.PayrollHead.HeadType == 1)
                    .Select(p => new
                    {
                        HeadName = p.PayElement.PayrollHead.HeadName,
                        Amount = p.Amount
                    })
                    .ToListAsync();

                var deductions = await _context.Payrolls
                    .Include(p => p.PayElement)
                    .ThenInclude(e => e.PayrollHead)
                    .Where(p => p.EmpId == empId &&
                                p.PayrollMonth == month &&
                                p.FinancialYear == financialYear &&
                                p.PayElement.PayrollHead.HeadType == 2)
                    .Select(p => new
                    {
                        HeadName = p.PayElement.PayrollHead.HeadName,
                        Amount = p.Amount
                    })
                    .ToListAsync();

                var summary = await _context.EmployeePayrollSummary
                    .FirstOrDefaultAsync(x =>
                        x.EmpId == empId &&
                        x.PayrollMonth == month &&
                        x.FinancialYear == financialYear);

                return Ok(new
                {
                    Earnings = earnings,
                    Deductions = deductions,
                    MonthlyGross = summary?.MonthlyGross ?? 0,
                    TotalDeductions = summary?.TotalDeductions ?? 0,
                    NetTakeHome = summary?.NetTakeHome ?? 0,
                    AnnualCtc = summary?.AnnualCtc ?? 0
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching payroll overview",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("employee-payroll-history/{empId}")]
        public async Task<IActionResult> GetPayrollSummary(int empId, int month, string financialYear)
        {
            try
            {
                var summary = await _context.EmployeePayrollSummary
                    .Where(x => x.EmpId == empId &&
                                x.PayrollMonth == month &&
                                x.FinancialYear == financialYear)
                    .FirstOrDefaultAsync();

                if (summary == null)
                    return NotFound("Payroll summary not found");

                return Ok(summary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching payroll summary",
                    error = ex.Message
                });
            }
        }


        [Authorize]
        [HttpPost("generate-company-overview")]
        public async Task<IActionResult> GenerateCompanyOverview(int month, string year)
        {
            try
            {
                var summaries = await _context.EmployeePayrollSummary
                    .Where(x => x.PayrollMonth == month && x.FinancialYear == year)
                    .ToListAsync();

                if (!summaries.Any())
                    return BadRequest("No employee payroll summaries found");

                decimal totalGross = summaries.Sum(x => x.MonthlyGross);
                decimal totalDeduction = summaries.Sum(x => x.TotalDeductions);
                decimal totalNet = summaries.Sum(x => x.NetTakeHome);
                decimal totalCtc = summaries.Sum(x => x.AnnualCtc);

                var existing = await _context.CompanyPayrollOverviews
                    .FirstOrDefaultAsync(x => x.PayrollMonth == month && x.FinancialYear == year);

                if (existing != null)
                {
                    existing.MonthlyGrossSalary = totalGross;
                    existing.TotalDeductions = totalDeduction;
                    existing.NetTakeHome = totalNet;
                    existing.TotalAnnualCtc = totalCtc;
                }
                else
                {
                    var overview = new CompanyPayrollOverview
                    {
                        PayrollMonth = month,
                        FinancialYear = year,
                        MonthlyGrossSalary = totalGross,
                        TotalDeductions = totalDeduction,
                        NetTakeHome = totalNet,
                        TotalAnnualCtc = totalCtc
                    };

                    _context.CompanyPayrollOverviews.Add(overview);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    MonthlyGrossSalary = totalGross,
                    TotalDeductions = totalDeduction,
                    NetTakeHome = totalNet,
                    TotalAnnualCtc = totalCtc
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while generating company overview",
                    error = ex.Message
                });
            }
        }



        //[HttpGet("payroll-overview")]
        //public async Task<IActionResult> GetPayrollOverview(int empId, int month, string financialYear)
        //{
        //    var payrollData = await _context.Payrolls
        //        .Include(p => p.PayElement)
        //        .ThenInclude(e => e.PayrollHead)
        //        .Where(p => p.EmpId == empId &&
        //                    p.PayrollMonth == month &&
        //                    p.FinancialYear == financialYear)
        //        .ToListAsync();

        //    if (!payrollData.Any())
        //        return NotFound("Payroll data not found");

        //    var earnings = payrollData
        //        .Where(x => x.PayElement.PayrollHead.HeadType == 1)
        //        .Select(x => new
        //        {
        //            HeadName = x.PayElement.PayrollHead.HeadName,
        //            Amount = x.Amount
        //        });

        //    var deductions = payrollData
        //        .Where(x => x.PayElement.PayrollHead.HeadType == 2)
        //        .Select(x => new
        //        {
        //            HeadName = x.PayElement.PayrollHead.HeadName,
        //            Amount = x.Amount
        //        });

        //    var summary = await _context.EmployeePayrollSummaries
        //        .FirstOrDefaultAsync(x =>
        //            x.EmpId == empId &&
        //            x.PayrollMonth == month &&
        //            x.FinancialYear == financialYear);

        //    return Ok(new
        //    {
        //        Earnings = earnings,
        //        Deductions = deductions,
        //        NetTakeHome = summary?.NetTakeHome,
        //        AnnualCTC = summary?.AnnualCtc
        //    });
        //}
        [Authorize]
        [HttpGet("company-payroll-overview")]
        public async Task<IActionResult> GetCompanyPayrollOverview(int month, string financialYear)
        {
            try
            {
                var summaries = await _context.EmployeePayrollSummary
                    .Where(x => x.PayrollMonth == month &&
                                x.FinancialYear == financialYear)
                    .ToListAsync();

                if (!summaries.Any())
                {
                    return Ok(new
                    {
                        MonthlyGross = 0,
                        TotalDeductions = 0,
                        NetTakeHome = 0,
                        TotalAnnualCtc = 0
                    });
                }

                return Ok(new
                {
                    MonthlyGross = summaries.Sum(x => x.MonthlyGross),
                    TotalDeductions = summaries.Sum(x => x.TotalDeductions),
                    NetTakeHome = summaries.Sum(x => x.NetTakeHome),
                    TotalAnnualCtc = summaries.Sum(x => x.AnnualCtc)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching company payroll overview",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("payroll-summary/{empId}")]
        public async Task<IActionResult> GetPayrollSummary(int empId)
        {
            try
            {
                var data = await _context.EmployeePayrollSummary
                    .Where(x => x.EmpId == empId)
                    .OrderByDescending(x => x.PayrollMonth)
                    .Select(x => new
                    {
                        x.EmpId,
                        x.PayrollMonth,
                        x.FinancialYear,
                        x.MonthlyGross,
                        x.TotalDeductions,
                        x.NetTakeHome,
                        x.AnnualCtc
                    })
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching payroll summary",
                    error = ex.Message
                });
            }
        }


        [Authorize]

        [HttpPost("save-payroll-summary")]
        public async Task<IActionResult> SavePayrollSummary([FromBody] PayrollSummaryDto dto)
        {
            try
            {
                var summary = await _context.EmployeePayrollSummary
                    .SingleOrDefaultAsync(x =>
                        x.EmpId == dto.EmpId &&
                        x.PayrollMonth == dto.PayrollMonth &&
                        x.FinancialYear == dto.FinancialYear);

                if (summary == null)
                {
                    summary = new EmployeePayrollSummary
                    {
                        EmpId = dto.EmpId,
                        PayrollMonth = dto.PayrollMonth,
                        FinancialYear = dto.FinancialYear,
                        MonthlyGross = dto.MonthlyGross,
                        TotalDeductions = dto.TotalDeductions,
                        NetTakeHome = dto.NetTakeHome,
                        AnnualCtc = dto.AnnualCtc,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.EmployeePayrollSummary.Add(summary);
                }
                else
                {
                    summary.MonthlyGross = dto.MonthlyGross;
                    summary.TotalDeductions = dto.TotalDeductions;
                    summary.NetTakeHome = dto.NetTakeHome;
                    summary.AnnualCtc = dto.AnnualCtc;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Payroll Summary Saved Successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while saving payroll summary",
                    error = ex.Message
                });
            }
        }

        // CSV Mapping Model
        public class AttendanceCsvRow
        {
            public string EmployeeId { get; set; }

            public DateTime Date { get; set; }

            public DateTime? CheckIn { get; set; }

            public DateTime? CheckOut { get; set; }
        }


    }
}