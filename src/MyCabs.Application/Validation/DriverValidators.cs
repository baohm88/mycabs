using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class DriverApplyDtoValidator : AbstractValidator<DriverApplyDto>
{
    public DriverApplyDtoValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}

public class InvitationRespondDtoValidator : AbstractValidator<InvitationRespondDto>
{
    public InvitationRespondDtoValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => a is "Accept" or "Decline")
            .WithMessage("Action must be Accept or Decline");
    }
}