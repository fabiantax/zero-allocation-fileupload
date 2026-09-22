using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using FileMutation.Api.Contracts;
using FileMutation.Application;
using FileMutation.Domain;
using FileMutation.Infrastructure;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using ReflectionAssembly = System.Reflection.Assembly;

namespace FileMutation.Architecture.Tests;

public sealed class LayeringRules
{
    private static readonly ReflectionAssembly DomainAssembly = typeof(FileName).Assembly;
    private static readonly ReflectionAssembly ApplicationAssembly = typeof(FileAcceptance).Assembly;
    private static readonly ReflectionAssembly InfrastructureAssembly = typeof(DateAndRandomSequenceMutator).Assembly;
    private static readonly ReflectionAssembly ApiAssembly = typeof(MutateFileRequest).Assembly;

    private static readonly ArchitectureModel Architecture = new ArchLoader()
        .LoadAssemblies(
            DomainAssembly,
            ApplicationAssembly,
            InfrastructureAssembly,
            ApiAssembly)
        .Build();

    private static readonly IObjectProvider<IType> DomainLayer =
        Types().That().ResideInAssembly(DomainAssembly);

    private static readonly IObjectProvider<IType> ApplicationLayer =
        Types().That().ResideInAssembly(ApplicationAssembly);

    private static readonly IObjectProvider<IType> PortTypes =
        Types().That().ResideInNamespace("FileMutation.Domain.Ports");

    [Fact]
    public void Domain_depends_only_on_itself_and_the_BCL()
    {
        // Protects the domain from coupling to framework or outer-layer concerns.
        Types().That().Are(DomainLayer).Should()
            .OnlyDependOn(Types().That().ResideInAssembly(DomainAssembly)
                .Or().ResideInNamespaceMatching("^System(\\..*)?$"))
            .Check(Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_outer_layers()
    {
        // Protects the inward dependency direction of the application layer.
        Types().That().Are(ApplicationLayer).Should()
            .NotDependOnAny(Types().That().ResideInAssembly(InfrastructureAssembly)
                .Or().ResideInAssembly(ApiAssembly))
            .Check(Architecture);
    }

    [Fact]
    public void AspNetCore_types_do_not_appear_in_the_core()
    {
        // Protects the domain and application from HTTP framework coupling.
        Types().That().Are(DomainLayer).Or().Are(ApplicationLayer).Should()
            .NotDependOnAny(Types().That().ResideInNamespaceMatching("^Microsoft\\.AspNetCore(\\..*)?$"))
            .Check(Architecture);
    }

    [Fact]
    public void Port_interfaces_live_in_the_domain()
    {
        // Protects ports as domain-owned abstractions rather than outer-layer contracts.
        Interfaces().That().ResideInNamespace("FileMutation.Domain.Ports").Should()
            .ResideInAssembly(DomainAssembly)
            .Check(Architecture);
    }

    [Fact]
    public void Port_implementations_do_not_live_in_the_domain()
    {
        // Protects dependency inversion by keeping port adapters outside the domain.
        Classes().That().AreAssignableTo(PortTypes).Should()
            .NotResideInAssembly(DomainAssembly)
            .Check(Architecture);
    }
}
