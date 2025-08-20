using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class RequestEmailOtpValidator : AbstractValidator<RequestEmailOtpDto>
{
    public RequestEmailOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Purpose).NotEmpty().Must(p => p is "verify_email" or "reset_password");
    }
}

public class VerifyEmailOtpValidator : AbstractValidator<VerifyEmailOtpDto>
{
    public VerifyEmailOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Purpose).NotEmpty().Must(p => p is "verify_email" or "reset_password");
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}

public class ResetPasswordWithOtpValidator : AbstractValidator<ResetPasswordWithOtpDto>
{
    public ResetPasswordWithOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
    }
}