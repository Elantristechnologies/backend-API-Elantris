
using HRPortal.API.Data;
using HRPortal.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            if (vehicle == null)
                return BadRequest("Invalid vehicle payload");

            // Ensure EF does not try to insert an explicit value for the identity column.
            // Some clients may send a non-zero or non-default VehicleId which causes Postgres
            // to reject the insert when the column is database-generated.
            vehicle.VehicleId = 0;

            try
            {
                _context.Vehicles.Add(vehicle);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx)
            {
                // Return a controlled error payload with the DB message for diagnostics
                return BadRequest(new { message = "Database error saving vehicle", detail = dbEx.InnerException?.Message ?? dbEx.Message });
            }

            return Ok(vehicle);
        }


        [HttpGet("all")]
        public async Task<IActionResult> GetAllVehicles()
        {
            var vehicles = await _context.Vehicles.ToListAsync();
            return Ok(vehicles);
        }
        // GET all purposes
        [HttpGet("purposes")]
        public async Task<IActionResult> GetPurposes()
        {
            var purposes = await _context.Purposes.ToListAsync();
            return Ok(purposes);
        }

        // ADD purpose
        [HttpPost("all Purpose")]
        public async Task<IActionResult> AddPurpose(Purpose purpose)
        {
            _context.Purposes.Add(purpose);
            await _context.SaveChangesAsync();

            return Ok(purpose);
        }

        // DELETE purpose
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePurpose(int id)
        {
            var purpose = await _context.Purposes.FindAsync(id);

            if (purpose == null)
                return NotFound();

            _context.Purposes.Remove(purpose);
            await _context.SaveChangesAsync();

            return Ok("Purpose deleted");
        }

        [HttpPost("create-claim")]
        public async Task<IActionResult> CreateClaim(ClaimMaster claim)
        {
            _context.ClaimMasters.Add(claim);
            await _context.SaveChangesAsync();

            return Ok(claim);
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
            travel.TravelId = 0;

            // ⭐ IMPORTANT FIX
            travel.FromLocation = travel.FromLocation ?? "-";
            travel.ToLocation = travel.ToLocation ?? "-";
            travel.Purpose = travel.Purpose ?? "-";
            travel.Remarks = travel.Remarks ?? "-";

            try
            {
                _context.ClaimTravels.Add(travel);
                await _context.SaveChangesAsync();

                return Ok(travel);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.InnerException?.Message ?? ex.Message);
            }
        }


        [HttpPost("add-food")]
        public async Task<IActionResult> AddFood(ClaimFood food)
        {
            _context.ClaimFoods.Add(food);
            await _context.SaveChangesAsync();

            return Ok(food);
        }

        [HttpPost("add-expense")]
        public async Task<IActionResult> AddExpense(ClaimExpense expense)
        {
            _context.ClaimExpenses.Add(expense);
            await _context.SaveChangesAsync();

            return Ok(expense);
        }


        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var filePath = Path.Combine(folder, file.FileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { path = file.FileName });
        }


        [HttpGet("month/{month}")]
        public async Task<IActionResult> GetClaimsByMonth(string month)
        {
            var data = await _context.ClaimMasters
                .Where(x => x.ClaimMonth.ToString().Contains(month))
                .ToListAsync();

            return Ok(data);
        }


        [HttpGet("get-claims")]
        public async Task<IActionResult> GetClaims(int claimId, DateTime date)
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

        [HttpGet("get-claims-by-year")]
        public async Task<IActionResult> GetClaimsByYear(int year)
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

        [HttpDelete("delete-claim/{id}")]
        public async Task<IActionResult> DeleteClaim(int id)
        {
            var claim = await _context.TravelClaims.FindAsync(id);

            if (claim == null)
                return NotFound("Claim not found");

            _context.TravelClaims.Remove(claim);
            await _context.SaveChangesAsync();

            return Ok("Claim deleted successfully");
        }

        [HttpGet("total-expense")]
        public async Task<IActionResult> GetTotalExpense(int empId, DateTime date)
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


        [HttpPut("update-claim/{id}")]
        public async Task<IActionResult> UpdateClaim(int id, [FromBody] TravelClaim updatedClaim)
        {
            if (id != updatedClaim.ClaimId)
                return BadRequest("Claim ID mismatch");

            var claim = await _context.TravelClaims.FirstOrDefaultAsync(x => x.ClaimId == id);

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

        [HttpGet("get-claim/{id}")]
        public async Task<IActionResult> GetClaim(int id)
        {
            var claim = await _context.ClaimTravels
                .FirstOrDefaultAsync(x => x.ClaimId == id);

            if (claim == null)
                return NotFound();

            return Ok(claim);
        }

        [HttpGet("get-drafts")]
        public async Task<IActionResult> GetDraftClaims()
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

        [HttpDelete("delete-draft/{id}")]
        public async Task<IActionResult> DeleteDraft(int id)
        {
            var draft = await _context.DraftClaims.FindAsync(id);

            if (draft == null)
                return NotFound("Draft not found");

            _context.DraftClaims.Remove(draft);
            await _context.SaveChangesAsync();

            return Ok("Draft deleted successfully");
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

        [HttpPost("add-local-purchase")]
        public async Task<IActionResult> AddLocalPurchase(LocalPurchase model)
        {
            _context.LocalPurchases.Add(model);
            await _context.SaveChangesAsync();
            return Ok(model);
        }

        [HttpGet("get-all-local-purchases")]
        public async Task<IActionResult> GetAllLocalPurchases()
        {
            var data = await _context.LocalPurchases
                .OrderByDescending(x => x.PurchaseId)
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("get-local-purchase/{id}")]
        public async Task<IActionResult> GetLocalPurchase(int id)
        {
            var data = await _context.LocalPurchases.FindAsync(id);

            if (data == null)
                return NotFound();

            return Ok(data);
        }

        [HttpPut("update-local-purchase/{id}")]
        public async Task<IActionResult> UpdateLocalPurchase(int id, LocalPurchase purchase)
        {
            var data = await _context.LocalPurchases.FindAsync(id);

            if (data == null)
                return NotFound();

            data.CustomerName = purchase.CustomerName;
            data.Location = purchase.Location;
            data.Amount = purchase.Amount;
            data.Remarks = purchase.Remarks;

            await _context.SaveChangesAsync();

            return Ok(data);
        }

        [HttpDelete("delete-local-purchase/{id}")]
        public async Task<IActionResult> DeleteLocalPurchase(int id)
        {
            var data = await _context.LocalPurchases.FindAsync(id);

            if (data == null)
                return NotFound();

            _context.LocalPurchases.Remove(data);
            await _context.SaveChangesAsync();

            return Ok("Deleted successfully");
        }

        [HttpGet("get-local-purchase-by-year/{year}")]
        public async Task<IActionResult> GetLocalPurchaseByYear(int year)
        {
            var data = await _context.LocalPurchases
                .Where(x => x.Year == year)
                .ToListAsync();

            return Ok(data);
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

        [HttpPost("received")]
        public async Task<IActionResult> AddReceived(ReceivedAmount model)
        {
            model.Id = 0;

            // do not overwrite admin_id
            _context.ReceivedAmounts.Add(model);
            await _context.SaveChangesAsync();

            return Ok(model);
        }



        [HttpGet("received")]
        public async Task<IActionResult> GetReceived()
        {
            var data = await _context.ReceivedAmounts
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Ok(data);
        }
        //[HttpGet("received")]
        //public async Task<IActionResult> GetReceived()
        //{
        //    var data = await _context.ReceivedAmounts
        //                             .AsNoTracking()
        //                             .OrderByDescending(x => x.Id)
        //                             .ToListAsync();

        //    return Ok(data);
        //}
        //[HttpGet("spent")]
        //public async Task<IActionResult> GetSpent()
        //{
        //    var data = await _context.SpentAmounts
        //        .Where(x => x.Date != DateTime.MinValue)
        //        .Where(x => x.IsActive == "1")
        //        .OrderByDescending(x => x.Date)
        //        .ToListAsync();

        //    return Ok(data);
        //}





        // update api


        //[HttpPut("spent/{id}")]
        //public async Task<IActionResult> UpdateSpent(int id, SpentAmount model)
        //{
        //    var data = await _context.SpentAmounts.FindAsync(id);

        //    if (data == null)
        //    {
        //        return NotFound();
        //    }

        //    data.Amount = model.Amount;
        //    data.Date = model.Date;

        //    // ⭐ Add these lines
        //    data.PurchaseItem = model.PurchaseItem;
        //    data.BillFile = model.BillFile;
        //    data.Remarks = model.Remarks;

        //    await _context.SaveChangesAsync();

        //    return Ok(data);
        //}


        //delite api 


        //[HttpGet("summary")]
        //public async Task<IActionResult> GetSummary()
        //{
        //    var currentMonth = DateTime.Now.Month;
        //    var currentYear = DateTime.Now.Year;

        //    var monthReceived = await _context.ReceivedAmounts
        //        .Where(x => x.Date.Month == currentMonth && x.Date.Year == currentYear)
        //        .SumAsync(x => (decimal?)x.Amount) ?? 0;

        //    var yearReceived = await _context.ReceivedAmounts
        //        .Where(x => x.Date.Year == currentYear)
        //        .SumAsync(x => (decimal?)x.Amount) ?? 0;

        //    var monthExpense = await _context.SpentAmounts
        //        .Where(x => x.IsActive[0])
        //        .Where(x => x.Date.Month == currentMonth && x.Date.Year == currentYear)
        //        .SumAsync(x => (decimal?)x.Amount) ?? 0;

        //    var yearExpense = await _context.SpentAmounts
        //        .Where(x => x.IsActive[0])
        //        .Where(x => x.Date.Year == currentYear)
        //        .SumAsync(x => (decimal?)x.Amount) ?? 0;

        //    return Ok(new
        //    {
        //        MonthReceived = monthReceived,
        //        YearReceived = yearReceived,
        //        MonthExpense = monthExpense,
        //        YearExpense = yearExpense
        //    });
        //}

        //[HttpDelete("spent/{id}")]
        //public async Task<IActionResult> DeleteSpent(int id)
        //{
        //    var data = await _context.SpentAmounts.FindAsync(id);

        //    if (data == null)
        //        return NotFound();

        //    data.IsActive = new BitArray(new[] { false });

        //    await _context.SaveChangesAsync();

        //    return Ok();
        //}


        //[HttpGet("spent")]
        //public async Task<IActionResult> GetSpent()
        //{
        //    var data = await _context.SpentAmounts
        //        .Where(x => x.IsActive[0])   // only active (bit = 1)
        //        .OrderByDescending(x => x.Date)
        //        .ToListAsync();

        //    return Ok(data);
        //}

        //// Add Spent Amount
        //[httppost("spent")]
        //public async task<iactionresult> addspent(spentamount model)
        //{
        //    model.isactive = new bitarray(new[] { true });

        //    _context.spentamounts.add(model);
        //    await _context.savechangesasync();

        //    return ok(model);
        //}


        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var currentMonth = DateTime.Now.Month;
            var currentYear = DateTime.Now.Year;

            var monthReceived = await _context.ReceivedAmounts
                .Where(x => x.Date.Month == currentMonth && x.Date.Year == currentYear)
                .SumAsync(x => (decimal?)x.Amount) ?? 0;

            var yearReceived = await _context.ReceivedAmounts
                .Where(x => x.Date.Year == currentYear)
                .SumAsync(x => (decimal?)x.Amount) ?? 0;

            var monthExpense = await _context.SpentAmounts
                .Where(x => x.IsActive)
                .Where(x => x.Date.Month == currentMonth && x.Date.Year == currentYear)
                .SumAsync(x => (decimal?)x.Amount) ?? 0;

            var yearExpense = await _context.SpentAmounts
                .Where(x => x.IsActive)
                .Where(x => x.Date.Year == currentYear)
                .SumAsync(x => (decimal?)x.Amount) ?? 0;

            return Ok(new
            {
                MonthReceived = monthReceived,
                YearReceived = yearReceived,
                MonthExpense = monthExpense,
                YearExpense = yearExpense
            });
        }

        

        [HttpGet("spent")]
        public async Task<IActionResult> GetSpents()
        {
            var data = await _context.SpentAmounts
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            return Ok(data);
        }


        [HttpGet("spent_new")]
        public async Task<IActionResult> GetSpent()
        {
            var data = await _context.SpentDetails
                .FromSqlRaw("SELECT * FROM get_spent_details()")
                .ToListAsync();

            return Ok(data);
        }


        [HttpDelete("spent/{id}")]
        public async Task<IActionResult> DeleteSpent(int id)
        {
            var data = await _context.SpentAmounts.FindAsync(id);

            if (data == null)
                return NotFound();

            data.IsActive = false;   // soft delete

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("spent")]
        public async Task<IActionResult> AddSpent(SpentAmount model)
        {
            model.IsActive = true;

            _context.SpentAmounts.Add(model);
            await _context.SaveChangesAsync();

            return Ok(model);
        }



 



        //-------------api for purchaase type--------

        // GET: api/purchasetype
        // GET purchase types
        [HttpGet("purchase-types")]
        public async Task<IActionResult> Get()
        {
            var data = await _context.PurchaseTypes
                                     .OrderBy(x => x.Name)
                                     .ToListAsync();

            return Ok(data);
        }

        // POST: api/purchasetype
        [HttpPost("purchase-type")]
        public async Task<IActionResult> Post([FromBody] PurchaseType model)
        {
            if (string.IsNullOrEmpty(model.Name))
                return BadRequest("Name is required");

            _context.PurchaseTypes.Add(model);
            await _context.SaveChangesAsync();

            return Ok(model);
        }



    }
}

