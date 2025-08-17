using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface ICompanyService
{
    Task<(IEnumerable<CompanyDto> Items, long Total)> GetCompaniesAsync(CompaniesQuery q);
    Task<CompanyDto?> GetByIdAsync(string id);
    Task AddServiceAsync(string companyId, AddCompanyServiceDto dto);
}

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _repo;
    public CompanyService(ICompanyRepository repo) { _repo = repo; }

    public async Task<(IEnumerable<CompanyDto> Items, long Total)> GetCompaniesAsync(CompaniesQuery q)
    {
        var (items, total) = await _repo.FindAsync(q.Page, q.PageSize, q.Search, q.Plan, q.ServiceType, q.Sort);
        return (items.Select(CompanyDto.FromEntity), total);
    }

    public async Task<CompanyDto?> GetByIdAsync(string id)
    {
        var c = await _repo.GetByIdAsync(id);
        return c is null ? null : CompanyDto.FromEntity(c);
    }

    public async Task AddServiceAsync(string companyId, AddCompanyServiceDto dto)
    {
        var item = new Domain.Entities.CompanyServiceItem
        {
            Type = dto.Type,
            Title = dto.Title,
            BasePrice = dto.BasePrice
        };
        await _repo.AddServiceAsync(companyId, item);
    }
}