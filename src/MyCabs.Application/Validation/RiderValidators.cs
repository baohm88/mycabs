using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class CreateRatingDtoValidator : AbstractValidator<CreateRatingDto>
{
    public CreateRatingDtoValidator()
    {
        RuleFor(x => x.TargetType).NotEmpty().Must(t => t is "company" or "driver");
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.Stars).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}
