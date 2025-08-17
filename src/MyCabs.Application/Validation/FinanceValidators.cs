using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class TopUpDtoValidator : AbstractValidator<TopUpDto>
{ public TopUpDtoValidator() { RuleFor(x => x.Amount).GreaterThan(0); } }

public class PaySalaryDtoValidator : AbstractValidator<PaySalaryDto>
{
    public PaySalaryDtoValidator()
    { RuleFor(x => x.DriverId).NotEmpty(); RuleFor(x => x.Amount).GreaterThan(0); }
}

public class PayMembershipDtoValidator : AbstractValidator<PayMembershipDto>
{
    public PayMembershipDtoValidator()
    {
        RuleFor(x => x.Plan).NotEmpty().Must(p => new[] { "Free", "Basic", "Premium" }.Contains(p));
        RuleFor(x => x.BillingCycle).NotEmpty().Must(c => c is "monthly" or "quarterly");
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    }
}