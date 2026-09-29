using System.Diagnostics.CodeAnalysis;
using FileMutation.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FileMutation.Infrastructure.Events;

/// <summary>Chains handlers onto batch events.</summary>
public static class EventHandlerRegistration
{
    /// <summary>Registers THandler as a singleton (TryAddSingleton, so one instance serves every
    /// event type it subscribes to) and subscribes it to TEvent. No reflection, no assembly
    /// scanning: each call closes one generic registration at compile time. The first call also
    /// registers the dispatcher hosted service (TryAddEnumerable).</summary>
    /// <remarks>
    /// <para>A subscription matches the event's exact runtime type, so a handler subscribed to an
    /// abstract base such as <see cref="BatchEvent"/> receives nothing. Subscribe once per concrete
    /// event type instead.</para>
    /// <para>The handler is a singleton, so every dependency of THandler must be a singleton too.
    /// Handlers run one at a time on the dispatcher's thread, in registration order.</para>
    /// <para>The host must register logging; the dispatcher reports handler failures through it.</para>
    /// <para>THandler carries a trim annotation (public constructors) that the container's own
    /// registration requires; every call site names a concrete type, so it is invisible to callers.</para>
    /// </remarks>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <typeparam name="THandler">The handler that reacts to <typeparamref name="TEvent"/>.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TEvent : BatchEvent
        where THandler : class, IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<THandler>();
        services.AddSingleton<IEventSubscription>(new EventSubscription<TEvent>(
            typeof(THandler).Name,
            static provider => provider.GetRequiredService<THandler>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EventDispatcherService>());
        return services;
    }
}
