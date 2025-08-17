using MyCabs.Domain.Entities;

namespace MyCabs.Application;

public interface IJwtTokenService {
    string Generate(User user);
}