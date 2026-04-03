using HRPortal.API.Data;
using HRPortal.API.DTOs;
using HRPortal.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CashVoucherController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CashVoucherController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CashVoucherDto dto)
        {
            var voucher = new CashVoucher
            {
                
                VoucherNo = "CV-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                Date = dto.Date,
                PaidTo = dto.PaidTo,
                Towards = dto.Towards,
                Amount = dto.Amount,
                AmountInWords = dto.AmountInWords
            };

            _context.CashVouchers.Add(voucher);
            await _context.SaveChangesAsync();

            return Ok(voucher);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _context.CashVouchers.ToListAsync());
        }
    }
}