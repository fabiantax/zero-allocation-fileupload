using FileMutation.Api.Contracts;
using FileMutation.Application;
using FileMutation.Domain;
using FileMutation.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ReflectionAssembly = System.Reflection.Assembly;

namespace FileMutation.Architecture.Tests;

public sealed class PortRules
{
    private static readonly ReflectionAssembly DomainAssembly = typeof(FileName).Assembly;
    private static readonly ReflectionAssembly ApplicationAssembly = typeof(FileAcceptance).Assembly;
    private static readonly ReflectionAssembly InfrastructureAssembly = typeof(ServiceCollectionExtensions).Assembly;
    private static readonly ReflectionAssembly ApiAssembly = typeof(MutateFileRequest).Assembly;
    private static readonly Type[] Ports = DomainAssembly.GetTypes()
        .Where(type => type.IsInterface && type.Namespace == "FileMutation.Domain.Ports")
        .ToArray();
    private static readonly Type[] SolutionTypes =
    [
        .. DomainAssembly.GetTypes(),
        .. ApplicationAssembly.GetTypes(),
        .. InfrastructureAssembly.GetTypes(),
        .. ApiAssembly.GetTypes(),
    ];

    [Fact]
    public void Every_port_has_at_least_one_implementation_in_the_solution()
    {
        var withoutImplementation = Ports.Where(port => !SolutionTypes.Any(type =>
            type.IsClass && !type.IsAbstract && port.IsAssignableFrom(type))).ToArray();

        Assert.True(
            withoutImplementation.Length == 0,
            $"Ports without implementations: {string.Join(", ", withoutImplementation.Select(type => type.Name))}");
    }

    [Fact]
    public void Every_port_is_registered_by_AddFileMutationInfrastructure()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddFileMutationInfrastructure()
            .BuildServiceProvider();
        var withoutRegistration = Ports.Where(port => provider.GetService(port) is null).ToArray();

        Assert.True(
            withoutRegistration.Length == 0,
            $"Ports not resolved from Infrastructure DI: {string.Join(", ", withoutRegistration.Select(type => type.Name))}");
    }
}
