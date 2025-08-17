using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class RegisterDtoValidator : AbstractValidator<RegisterDto> {
    public RegisterDtoValidator() {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password needs an uppercase letter")
            .Matches("[a-z]").WithMessage("Password needs a lowercase letter")
            .Matches("[0-9]").WithMessage("Password needs a digit");
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Role).NotEmpty().Must(r => new[]{"Admin","Rider","Driver","Company"}.Contains(r));
    }
}

public class LoginDtoValidator : AbstractValidator<LoginDto> {
    public LoginDtoValidator() {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}