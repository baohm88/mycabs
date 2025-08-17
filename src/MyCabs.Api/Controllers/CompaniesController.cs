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
    public CompaniesController(ICompanyService svc) { _svc = svc; }

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
}