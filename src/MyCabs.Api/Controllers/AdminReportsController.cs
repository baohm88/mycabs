using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Api.Common;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/admin/reports")]
[Authorize] // TODO: thêm policy/role Admin
public class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _svc;
    public AdminReportsController(IAdminReportService svc) { _svc = svc; }

    [HttpGet("overview")] // ?from=2025-01-01&to=2025-01-31
    public async Task<IActionResult> Overview([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var data = await _svc.OverviewAsync(new DateRangeQuery(from, to));
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("tx-daily")] // time-series daily
    public async Task<IActionResult> TxDaily([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var data = await _svc.TransactionsDailyAsync(new DateRangeQuery(from, to));
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("top-companies")] // ?limit=10
    public async Task<IActionResult> TopCompanies([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 10)
    {
        var data = await _svc.TopCompaniesAsync(new DateRangeQuery(from, to), limit);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("top-drivers")] // ?limit=10
    public async Task<IActionResult> TopDrivers([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 10)
    {
        var data = await _svc.TopDriversAsync(new DateRangeQuery(from, to), limit);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("low-wallets")] // ?threshold=200000&limit=20&ownerType=Company
    public async Task<IActionResult> LowWallets([FromQuery] decimal? threshold = null, [FromQuery] int limit = 20, [FromQuery] string ownerType = "Company")
    {
        var data = await _svc.LowWalletsAsync(threshold, limit, ownerType);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }
}