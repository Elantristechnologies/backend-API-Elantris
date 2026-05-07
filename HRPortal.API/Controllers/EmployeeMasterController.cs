using CsvHelper;
using CsvHelper.Configuration;
using global::HRPortal.API.Data;
using HRPortal.API.Data;
using HRPortal.API.DTOs;
using HRPortal.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
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


namespace HRPortal.API.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeMasterController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;


        public EmployeeMasterController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;

        }

        // HR Register
        [HttpPost("HRregister")]
        public async Task<IActionResult> Register(HrRegisterDto dto)
        {
            try
            {
                var exists = await _context.HrAdmins
                    .AnyAsync(x => x.Email == dto.Email);

                if (exists)
                    return BadRequest("Email already registered");

                var hr = new HrAdmin
                {
                    empcode = dto.empcode,
                    Email = dto.Email,
                    PhoneNumber = dto.PhoneNumber,
                    PasswordHash = dto.Password
                };

                _context.HrAdmins.Add(hr);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "HR account created successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while creating HR account",
                    error = ex.Message
                });
            }
        }




        //[HttpPost("loginss")]
        //public async Task<IActionResult> Login(string userName, string password, int checkType)
        //{
        //    HrAdmin? hrAdmin = null;
        //    EmployeeMaster? employee = null;

        //    if (checkType == 0)
        //    {
        //        // HR login
        //        hrAdmin = await _context.HrAdmins
        //            .FirstOrDefaultAsync(x => x.Email == userName
        //                                  && x.PasswordHash == password);

        //        if (hrAdmin == null)
        //            return Unauthorized("Invalid HR login");

        //        employee = await _context.EmployeeMasters
        //            .FirstOrDefaultAsync(x => x.EmployeeCode == hrAdmin.empcode);
        //    }
        //    else
        //    {
        //        // Employee login
        //        employee = await _context.EmployeeMasters
        //            .FirstOrDefaultAsync(x => x.EmailId == userName
        //                                  && x.Password == password);

        //        if (employee == null)
        //            return Unauthorized("Invalid Employee login");
        //    }

        //    return Ok(new
        //    {
        //        HrAdmin = hrAdmin,
        //        Employee = employee
        //    });
        //}
        /// <summary>
        /// //hiiiiiiiiii allll
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <param name="checkType"></param>
        //<returns></returns>
        //[HttpPost("login")]
        //public async Task<IActionResult> Login(string userName, string password, int checkType)
        //{
        //    HrAdmin? hrAdmin = null;
        //    EmployeeMaster? employee = null;

        //    // Get employee from SP
        //    var employees = await _context.EmployeeMasters
        //        .FromSqlRaw("EXEC sp_Login @UserName, @Password, @CheckType",
        //            new SqlParameter("@UserName", userName),
        //            new SqlParameter("@Password", password),
        //            new SqlParameter("@CheckType", checkType))
        //        .ToListAsync();

        //    employee = employees.FirstOrDefault();

        //    if (employee == null)
        //        return Unauthorized("Invalid login");

        //    // If HR login then fetch hr_admin
        //    if (checkType == 0)
        //    {
        //        hrAdmin = await _context.HrAdmins
        //            .FirstOrDefaultAsync(x => x.empcode == employee.EmployeeCode);
        //    }

        //    return Ok(new
        //    {
        //        HrAdmin = hrAdmin,
        //        Employee = employee
        //    });
        //}




        ///logimn apiu with jwt token

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login(string userName, string password, int checkType)
        {
            try
            {
                HrAdmin? hrAdmin = null;
                EmployeeMaster? employee = null;

                var employees = await _context.EmployeeMasters
                    .FromSqlRaw("EXEC sp_Login @UserName, @Password, @CheckType",
                        new SqlParameter("@UserName", userName),
                        new SqlParameter("@Password", password),
                        new SqlParameter("@CheckType", checkType))
                    .ToListAsync();

                employee = employees.FirstOrDefault();

                if (employee == null)
                {
                    return Unauthorized("Invalid login");
                }

                string role = "Employee";

                if (checkType == 0)
                {
                    hrAdmin = await _context.HrAdmins
                        .FirstOrDefaultAsync(x => x.empcode == employee.EmployeeCode);

                    role = "HR";
                }

                // use your existing JWT method
                var token = GenerateJwtToken(employee.EmailId, role);

                return Ok(new
                {
                    message = "Login successful",
                    token = token,
                    Employee = employee,
                    HrAdmin = hrAdmin,
                    departmentName = employee.DepartmentName

                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error occurred",
                    error = ex.Message
                });
            }
        }


        //[AllowAnonymous]
        //[HttpPost("login")]
        //public async Task<IActionResult> Login(string userName, string password, int checkType)
        //{
        //    try
        //    {
        //        HrAdmin? hrAdmin = null;
        //        EmployeeMaster? employee = null;

        //        var employees = await _context.EmployeeMasters
        //            .FromSqlRaw("EXEC sp_Login @UserName, @Password, @CheckType",
        //                new SqlParameter("@UserName", userName),
        //                new SqlParameter("@Password", password),
        //                new SqlParameter("@CheckType", checkType))
        //            .ToListAsync();

        //        employee = employees.FirstOrDefault();

        //        if (employee == null)
        //        {
        //            return Unauthorized("Invalid login");
        //        }

        //        string role = "Employee";

        //        if (checkType == 0)
        //        {
        //            hrAdmin = await _context.HrAdmins
        //                .FirstOrDefaultAsync(x => x.empcode == employee.EmployeeCode);

        //            role = "HR";
        //        }

        //        // ✅ Department fetch
        //        //var department = await _context.Department
        //        //    .FirstOrDefaultAsync(d => d.DepartmentId == employee.DepartmentId);

        //        // ✅ JWT token
        //        var token = GenerateJwtToken(employee.EmailId, role);

        //        return Ok(new
        //        {
        //            message = "Login successful",
        //            token = token,
        //            employee = new
        //            {
        //                employee.EmpId,
        //                employee.FullName,
        //                employee.EmailId,
        //                employee.EmployeeCode,
        //                //departmentId = department?.DepartmentId,
        //                //departmentName = department?.DepartmentName
        //            },
        //            role = role,
        //            hrAdmin = hrAdmin
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "Error occurred",
        //            error = ex.Message
        //        });
        //    }
        //}

        private string GenerateJwtToken(string email, string role)
        {
            var claims = new[]
            {
        new Claim(ClaimTypes.Email, email),
        new Claim(ClaimTypes.Role, role)
    };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }


        [HttpGet("employee/access-control")]
        public async Task<IActionResult> GetAccessControl(int designationId, int Employeeid)
        {
            try
            {
                var result = await _context.Set<EmployeeAccessResultsdto>()
                    .FromSqlRaw("EXEC GetEmployeeAccessAndAmount @desig, @empid",
                        new SqlParameter("@desig", designationId),
                        new SqlParameter("@empid", Employeeid))
                    .ToListAsync();

                var data = result.FirstOrDefault();

                return Ok(new
                {
                    status = data?.status ?? "no_access",
                    totalAmount = data?.TotalAmount ?? 0
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error occurred",
                    error = ex.Message
                });
            }
        }
        // HR-ADMIN profile update
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

        // all employee dashboard la display pannum 
        [HttpGet]
        public async Task<IActionResult> GetEmployees()
        {
            try
            {
                var employees = await _context.EmployeeMasters
                    .Select(e => new
                    {
                        e.EmpId,
                        e.EmployeeCode,
                        e.FullName,
                        e.EmailId,
                        e.DepartmentId,
                        e.DesignationId,
                        e.DateOfJoining
                    })
                    .ToListAsync();

                return Ok(employees);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching employees",
                    error = ex.Message
                });
            }
        }


        // HR- profile display pannum
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

        // employee register 
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> CreateEmployee([FromBody] EmployeeCreateDto dto)
        {
            try
            {
                var emailExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmailId == dto.EmailId);

                if (emailExists)
                    return BadRequest("Email already exists");

                var codeExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmployeeCode == dto.EmployeeCode);

                if (codeExists)
                    return BadRequest("Employee Code already exists");

                var employee = new EmployeeMaster
                {
                    EmployeeCode = dto.EmployeeCode,
                    FullName = dto.FullName,
                    EmailId = dto.EmailId,
                    Phone = dto.Phone,
                    EmergencyContact = dto.EmergencyContact,
                    DepartmentId = dto.DepartmentID,
                    DesignationId = dto.DesignationID,
                    DateOfJoining = dto.DateOfJoining,
                    CategoryId = dto.CategoryId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.EmployeeMasters.Add(employee);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Employee registered successfully",
                    employee.EmpId
                });
            }
            catch (Exception ex)
            {
                // log error (optional)
                return StatusCode(500, new
                {
                    message = "Something went wrong",
                    error = ex.Message
                });
            }
        }

        // dROPDOWN API FOR REGISTER 

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
        // employee personal details only added 
        [Authorize]
        [HttpPost("{id}/personal-details")]
        public async Task<IActionResult> SavePersonalDetails(int id, [FromBody] EmployeePersonalDetailsDto dto)
        {
            try
            {
                var employee = await _context.EmployeeMasters
                    .FirstOrDefaultAsync(e => e.EmpId == id);

                if (employee == null)
                    return NotFound("Employee not found");

                var personal = await _context.EmployeePersonalDetails
                    .FirstOrDefaultAsync(p => p.EmpId == id);

                if (personal == null)
                {
                    personal = new EmployeePersonalDetails
                    {
                        EmpId = id
                    };

                    _context.EmployeePersonalDetails.Add(personal);
                }

                personal.FirstName = dto.FirstName;
                personal.LastName = dto.LastName;
                personal.FullName = dto.FullName;
                personal.Religion = dto.Religion;
                personal.Mobile = dto.Mobile;
                personal.AlternateContact = dto.AlternateContact;

                if (dto.DateOfBirth.HasValue)
                {
                    personal.DateOfBirth = DateTime.SpecifyKind(dto.DateOfBirth.Value, DateTimeKind.Utc);
                }

                personal.MaritalStatus = dto.MaritalStatus;
                personal.BloodGroup = dto.BloodGroup;
                personal.Nationality = dto.Nationality;
                personal.Gender = dto.Gender;

                await _context.SaveChangesAsync();

                return Ok("Personal details saved successfully");
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


        // employee education only added database 
        [Authorize]
        [HttpPost("{id}/education/bulk")]
        public async Task<IActionResult> AddMultipleEducation(
     int id,
     [FromBody] List<EmployeeEducationDto> dtos)
        {
            try
            {
                if (dtos == null || !dtos.Any())
                    return BadRequest("Education list is empty");

                var employeeExists = await _context.EmployeeMasters
                    .AnyAsync(e => e.EmpId == id);

                if (!employeeExists)
                    return NotFound("Employee not found");

                var newEducations = new List<EmployeeEducation>();

                foreach (var dto in dtos)
                {
                    var exists = await _context.EmployeeEducations.AnyAsync(e =>
                        e.EmpId == id &&
                        e.Qualification == dto.Qualification &&
                        e.DegreeName == dto.DegreeName &&
                        e.University == dto.University &&
                        e.YearOfPassing == dto.YearOfPassing);

                    if (!exists)
                    {
                        newEducations.Add(new EmployeeEducation
                        {
                            EmpId = id,
                            Qualification = dto.Qualification,
                            DegreeName = dto.DegreeName,
                            University = dto.University,
                            YearOfPassing = dto.YearOfPassing,
                            Percentage = dto.Percentage,
                            Certifications = dto.Certifications
                        });
                    }
                }

                if (!newEducations.Any())
                    return BadRequest("All education records already exist");

                _context.EmployeeEducations.AddRange(newEducations);
                await _context.SaveChangesAsync();

                return Ok($"{newEducations.Count} education record(s) added successfully");
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


        // employee compensation only added database 
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

        // GET Employee Documents
        [HttpGet("{id}/documents")]
        public async Task<IActionResult> GetEmployeeDocuments(int id)
        {
            try
            {
                var documents = await _context.EmployeeDocuments
                    .FirstOrDefaultAsync(d => d.EmployeeId == id);

                if (documents == null)
                    return NotFound("Documents not found");

                return Ok(documents);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching employee documents",
                    error = ex.Message
                });
            }
        }

        // employee UploadDocuments only added database 
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


        // Get Employee Education  
        [HttpGet("{id}/education")]
        public async Task<ActionResult<EmployeeEducation>> GetEducation(int id)
        {
            try
            {
                var education = await _context.EmployeeEducations
                    .FirstOrDefaultAsync(e => e.EmpId == id);

                if (education == null)
                    return NotFound();

                return Ok(education);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while fetching education details",
                    error = ex.Message
                });
            }
        }


        // Get Employee Compensation 
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


        // ADD PAYROLL ENTRY + UPDATE SUMMARY
        [HttpPost("add-payroll")]
        public async Task<IActionResult> AddPayroll([FromBody] Payroll payroll)
        {
            var elementExists = await _context.PayElements
                .Include(e => e.PayrollHead)
                .FirstOrDefaultAsync(e => e.ElementId == payroll.ElementId);

            if (elementExists == null)
                return BadRequest("Invalid ElementId");

            // 🔹 Check existing payroll entry
            var existingPayroll = await _context.Payrolls
                .FirstOrDefaultAsync(x =>
                    x.EmpId == payroll.EmpId &&
                    x.ElementId == payroll.ElementId &&
                    x.PayrollMonth == payroll.PayrollMonth &&
                    x.FinancialYear == payroll.FinancialYear);

            if (existingPayroll != null)
            {
                // UPDATE amount if already exists
                existingPayroll.Amount = payroll.Amount;
                existingPayroll.CreatedDate = DateTime.UtcNow;

                _context.Payrolls.Update(existingPayroll);
            }
            else
            {
                // INSERT new payroll
                payroll.CreatedDate = DateTime.UtcNow;
                _context.Payrolls.Add(payroll);
            }

            // persist payroll row(s)
            await _context.SaveChangesAsync();

            // 🔹 Calculate totals
            var payrollData = await _context.Payrolls
                .Include(p => p.PayElement)
                .ThenInclude(e => e.PayrollHead)
                .Where(p => p.EmpId == payroll.EmpId &&
                            p.PayrollMonth == payroll.PayrollMonth &&
                            p.FinancialYear == payroll.FinancialYear)
                .ToListAsync();

            decimal gross = payrollData
                .Where(x => x.PayElement.PayrollHead.HeadType == 1)
                .Sum(x => x.Amount);

            decimal deductions = payrollData
                .Where(x => x.PayElement.PayrollHead.HeadType == 2)
                .Sum(x => x.Amount);

            decimal net = gross - deductions;
            decimal annualCtc = gross * 12;

            // 🔹 Upsert summary (atomic ON CONFLICT) to avoid race conditions / unique-constraint violations
            var now = DateTime.UtcNow;

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO employee_payroll_summary (empid, payrollmonth, ""FinancialYear"", ""MonthlyGross"", ""TotalDeductions"", ""NetTakeHome"", ""AnnualCtc"", ""CreatedAt"")
                VALUES ({payroll.EmpId}, {payroll.PayrollMonth}, {payroll.FinancialYear}, {gross}, {deductions}, {net}, {annualCtc}, {now})
                ON CONFLICT (empid, payrollmonth, ""FinancialYear"")
                DO UPDATE SET
                    ""MonthlyGross"" = EXCLUDED.""MonthlyGross"",
                    ""TotalDeductions"" = EXCLUDED.""TotalDeductions"",
                    ""NetTakeHome"" = EXCLUDED.""NetTakeHome"",
                    ""AnnualCtc"" = EXCLUDED.""AnnualCtc"",
                    ""CreatedAt"" = EXCLUDED.""CreatedAt"";
            ");

            // No additional SaveChanges required for the summary because the upsert was executed directly.

            return Ok(new
            {
                message = "Payroll Saved Successfully",
                gross,
                deductions,
                net,
                annualCtc
            });
        }


        // GET EMPLOYEE PAYROLL
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

        [HttpPost("generate-summary")]
        public async Task<IActionResult> GeneratePayrollSummary([FromBody] EmployeePayrollSummary model)
        {
            try
            {
                if (model == null)
                    return BadRequest("Invalid data");

                var summary = await _context.EmployeePayrollSummaries
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

                    await _context.EmployeePayrollSummaries.AddAsync(summary);
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

                var summary = await _context.EmployeePayrollSummaries
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


        [HttpGet("employee-payroll-history/{empId}")]
        public async Task<IActionResult> GetPayrollSummary(int empId, int month, string financialYear)
        {
            try
            {
                var summary = await _context.EmployeePayrollSummaries
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



        [HttpPost("generate-company-overview")]
        public async Task<IActionResult> GenerateCompanyOverview(int month, string year)
        {
            try
            {
                var summaries = await _context.EmployeePayrollSummaries
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

        [HttpGet("company-payroll-overview")]
        public async Task<IActionResult> GetCompanyPayrollOverview(int month, string financialYear)
        {
            try
            {
                var summaries = await _context.EmployeePayrollSummaries
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


        [HttpGet("payroll-summary/{empId}")]
        public async Task<IActionResult> GetPayrollSummary(int empId)
        {
            try
            {
                var data = await _context.EmployeePayrollSummaries
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
        [HttpPost("save-payroll-summary")]
        public async Task<IActionResult> SavePayrollSummary([FromBody] PayrollSummaryDto dto)
        {
            try
            {
                var summary = await _context.EmployeePayrollSummaries
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

                    _context.EmployeePayrollSummaries.Add(summary);
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
    