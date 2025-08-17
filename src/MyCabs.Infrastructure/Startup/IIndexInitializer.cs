namespace MyCabs.Infrastructure.Startup;

public interface IIndexInitializer
{
    Task EnsureIndexesAsync();
}