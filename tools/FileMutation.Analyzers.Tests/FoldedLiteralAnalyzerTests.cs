using FileMutation.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace FileMutation.Analyzers.Tests;

public sealed class FoldedLiteralAnalyzerTests
{
    [Fact]
    public async Task Reports_a_folded_literal_with_the_declared_constant_name()
    {
        var test = CreateTest("""
            namespace FileMutation.Domain;

            public static class Limits
            {
                public const int SegmentSize = 16 * 1024;
                public static int BufferSize = {|#0:16 * 1024|};
            }
            """);
        test.ExpectedDiagnostics.Add(Diagnostic(FoldedLiteralAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("16 * 1024", "SegmentSize", "FileMutation.Domain"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Reports_each_equivalent_spelling_of_a_folded_literal()
    {
        var test = CreateTest("""
            namespace FileMutation.Domain;

            public static class Limits
            {
                public const int MaximumFileBytes = 10485760;
                public static int Folded = {|#0:10 * 1024 * 1024|};
                public static int Direct = {|#1:10485760|};
            }
            """);
        test.ExpectedDiagnostics.Add(Diagnostic(FoldedLiteralAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("10 * 1024 * 1024", "MaximumFileBytes", "FileMutation.Domain"));
        test.ExpectedDiagnostics.Add(Diagnostic(FoldedLiteralAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("10485760", "MaximumFileBytes", "FileMutation.Domain"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Reports_a_repeated_string_literal_of_four_or_more_characters()
    {
        var test = CreateTest("""
            namespace FileMutation.Domain;

            public static class Extensions
            {
                public const string Text = ".txt";
                public static string Value = {|#0:".txt"|};
            }
            """);
        test.ExpectedDiagnostics.Add(Diagnostic(FoldedLiteralAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("\".txt\"", "Text", "FileMutation.Domain"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Does_not_report_a_reference_to_a_constant()
    {
        var test = CreateTest("""
            namespace FileMutation.Domain;

            public static class Limits
            {
                public const int SegmentSize = 16 * 1024;
                public static int BufferSize = SegmentSize;
            }
            """);

        await test.RunAsync();
    }

    [Fact]
    public async Task Does_not_report_trivial_literals_or_enum_initializers()
    {
        var test = CreateTest("""
            namespace FileMutation.Domain;

            public static class Limits
            {
                public const int Zero = 0;
                public const int One = 1;
                public const int MinusOne = -1;
                public const int SegmentSize = 16 * 1024;
                public static int ZeroValue = 0;
                public static int OneValue = 1;
                public static int MinusOneValue = -1;
                public static int[] Values = new int[16 * 1024];
            }

            public enum Values
            {
                Segment = 16 * 1024
            }
            """);

        await test.RunAsync();
    }

    [Fact]
    public async Task Does_not_report_generated_code()
    {
        var test = CreateTest("""
            namespace FileMutation.Domain;

            public static class HandwrittenCode
            {
            }
            """);
        test.TestState.Sources.Add(("Generated.g.cs", """
            namespace FileMutation.Domain;

            public static class GeneratedCode
            {
                public const int SegmentSize = 16 * 1024;
                public static int BufferSize = 16 * 1024;
            }
            """));

        await test.RunAsync();
    }

    private static CSharpAnalyzerTest<FoldedLiteralAnalyzer, DefaultVerifier> CreateTest(string source) => new()
    {
        TestCode = source
    };

    private static DiagnosticResult Diagnostic(string id) => new(id, DiagnosticSeverity.Warning);
}
