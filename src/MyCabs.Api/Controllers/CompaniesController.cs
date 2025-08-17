using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;


namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _svc;
    private readonly IFinanceService _finance;
    public CompaniesController(ICompanyService svc, IFinanceService finance) { _svc = svc; _finance = finance; }

    [HttpGet]
    public async Task<IActionResult> GetCompanies([FromQuery] CompaniesQuery q)
    {
        var (items, total) = await _svc.GetCompaniesAsync(q);
        var page = q.Page <= 0 ? 1 : q.Page;
        var pageSize = q.PageSize <= 0 ? 10 : q.PageSize;
        var payload = new PagedResult<CompanyDto>(items, page, pageSize, total);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var dto = await _svc.GetByIdAsync(id);
        if (dto is null) return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, dto));
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/services")]
    public async Task<IActionResult> AddService(string id, [FromBody] AddCompanyServiceDto dto)
    {
        await _svc.AddServiceAsync(id, dto);
        return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Service added" }));
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpGet("{id}/wallet")]
    public async Task<IActionResult> GetWallet(string id)
 => Ok(ApiEnvelope.Ok(HttpContext, await _finance.GetCompanyWalletAsync(id)));

    [Authorize(Roles = "Company,Admin")]
    [HttpGet("{id}/transactions")]
    public async Task<IActionResult> GetTransactions(string id, [FromQuery] TransactionsQuery q)
    { var (items, total) = await _finance.GetCompanyTransactionsAsync(id, q); return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<TransactionDto>(items, q.Page, q.PageSize, total))); }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/wallet/topup")]
    public async Task<IActionResult> TopUp(string id, [FromBody] TopUpDto dto)
    { await _finance.TopUpCompanyAsync(id, dto); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Topup completed" })); }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/pay-salary")]
    public async Task<IActionResult> PaySalary(string id, [FromBody] PaySalaryDto dto)
    { var (ok, err) = await _finance.PaySalaryAsync(id, dto); if (!ok && err == "INSUFFICIENT_FUNDS") return BadRequest(ApiEnvelope.Fail(HttpContext, "INSUFFICIENT_FUNDS", "Company wallet has insufficient funds", 400)); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Salary paid" })); }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/membership/pay")]
    public async Task<IActionResult> PayMembership(string id, [FromBody] PayMembershipDto dto)
    { var (ok, err) = await _finance.PayMembershipAsync(id, dto); if (!ok && err == "INSUFFICIENT_FUNDS") return BadRequest(ApiEnvelope.Fail(HttpContext, "INSUFFICIENT_FUNDS", "Company wallet has insufficient funds", 400)); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Membership updated" })); }
}