namespace MyCabs.Application.DTOs;

public record RegisterDto(string Email, string Password, string FullName, string Role);
public record LoginDto(string Email, string Password);