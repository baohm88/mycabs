using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class AddCompanyServiceDtoValidator : AbstractValidator<AddCompanyServiceDto>
{
    public AddCompanyServiceDtoValidator()
    {
        RuleFor(x => x.Type).NotEmpty().Must(t => new[] { "taxi", "xe_om", "hang_hoa", "tour" }.Contains(t));
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
    }
}