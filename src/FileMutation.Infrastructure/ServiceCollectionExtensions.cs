using FileMutation.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace FileMutation.Infrastructure;

/// <summary>Registers the Infrastructure adapters behind their domain-owned ports.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the adapters owned by this assembly; the host owns time.</summary>
    public static IServiceCollection AddFileMutationInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRandomSequenceGenerator, CryptoRandomSequenceGenerator>();
        services.AddSingleton<IFileMutator, DateAndRandomSequenceMutator>();
        return services;
    }
}
