using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using FileMutation.Api.Contracts;
using FileMutation.Application;
using FileMutation.Domain;
using FileMutation.Domain.Events;
using FileMutation.Infrastructure;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using ReflectionAssembly = System.Reflection.Assembly;

namespace FileMutation.Architecture.Tests;

public sealed class EventRules
{
    private static readonly ReflectionAssembly DomainAssembly = typeof(FileName).Assembly;

    private static readonly ArchitectureModel Architecture = new ArchLoader()
        .LoadAssemblies(
            DomainAssembly,
            typeof(FileAcceptance).Assembly,
            typeof(ServiceCollectionExtensions).Assembly,
            typeof(MutateFileRequest).Assembly)
        .Build();

    [Fact]
    public void Event_records_live_in_the_domain_events_namespace()
    {
        // Infrastructure dispatches events and cannot reference Application or Api, so an event
        // declared there could never be published or handled across the layers.
        Classes().That().AreAssignableTo(typeof(BatchEvent)).Should()
            .ResideInNamespace("FileMutation.Domain.Events")
            .Check(Architecture);
    }

    [Fact]
    public void Event_records_are_sealed_or_abstract()
    {
        // A concrete unsealed event lets a subclass masquerade as its parent in a handler lookup.
        Classes().That().AreAssignableTo(typeof(BatchEvent)).Should()
            .BeSealed().OrShould().BeAbstract()
            .Check(Architecture);
    }

    [Fact]
    public void Domain_does_not_reference_channels()
    {
        // The queue is an Infrastructure mechanism; Domain sees only IEventPublisher. Checked on the
        // assembly's references because ArchUnitNET matches namespaces only for types it loaded, so a
        // rule naming System.Threading.Channels matches nothing and can never fail (verified by planting).
        var referenced = DomainAssembly.GetReferencedAssemblies().Select(name => name.Name);

        Assert.DoesNotContain("System.Threading.Channels", referenced);
    }
}
