using FileMutation.Domain.Ports;
using FileMutation.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Singleton claim: the publisher's only state is a thread-safe channel and an Interlocked
        // count, and its only dependency (the options instance) is itself a singleton.
        services.TryAddSingleton<EventBusOptions>();
        services.AddSingleton<ChannelEventPublisher>();
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<ChannelEventPublisher>());
        services.AddSingleton<IEventStream>(provider => provider.GetRequiredService<ChannelEventPublisher>());
        return services;
    }
}
