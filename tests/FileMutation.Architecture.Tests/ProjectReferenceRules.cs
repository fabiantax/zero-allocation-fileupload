using System.Xml.Linq;
using Xunit;

namespace FileMutation.Architecture.Tests;

/// <summary>
/// ArchUnitNET analyses compiled type usage, so a project reference that is declared but never
/// used is invisible to it. These rules read the .csproj files directly, which is the only place
/// such a reference can be seen.
/// </summary>
public sealed class ProjectReferenceRules
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    // Protects the dependency rule at the project level, where an unused reference still counts.
    [InlineData("src/FileMutation.Domain", new string[0])]
    [InlineData("src/FileMutation.Application", new[] { "FileMutation.Domain" })]
    [InlineData("src/FileMutation.Infrastructure", new[] { "FileMutation.Domain" })]
    public void Project_references_point_only_inward(string projectDirectory, string[] allowed)
    {
        var actual = ProjectReferencesOf(projectDirectory);

        Assert.Equal(allowed.OrderBy(name => name), actual.OrderBy(name => name));
    }

    private static IReadOnlyList<string> ProjectReferencesOf(string projectDirectory)
    {
        var directory = Path.Combine(RepositoryRoot, projectDirectory);
        var projectFile = Directory.EnumerateFiles(directory, "*.csproj").Single();

        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .ToList();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileMutation.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("FileMutation.sln was not found above the test assembly.");
    }
}
