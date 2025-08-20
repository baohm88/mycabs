namespace MyCabs.Application.DTOs;

public record RequestEmailOtpDto(string Email, string Purpose); // verify_email | reset_password
public record VerifyEmailOtpDto(string Email, string Purpose, string Code);
public record ResetPasswordWithOtpDto(string Email, string Code, string NewPassword);