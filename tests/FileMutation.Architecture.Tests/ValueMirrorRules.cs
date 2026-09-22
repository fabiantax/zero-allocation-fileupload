using System.Globalization;
using System.Text.Json;
using Xunit;

namespace FileMutation.Architecture.Tests;

/// <summary>
/// Protects named values from being mirrored as folded literals across source projects or in the
/// override-only application configuration.
/// </summary>
public sealed class ValueMirrorRules
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly Lazy<SourceFacts> Facts = new(LoadSourceFacts);

    [Fact]
    public void Folded_literals_are_not_repeated_across_projects_when_a_named_constant_exists()
    {
        var allowlist = ReadAllowlist();
        var failures = new List<string>();

        foreach (var values in Facts.Value.Literals.GroupBy(literal => literal.ValueKey))
        {
            var namedConstants = values.Where(literal => literal.NamedConstant is not null).ToList();
            if (namedConstants.Count == 0 || allowlist.Contains(CrossProjectAllowlistKey(values.Key)))
            {
                continue;
            }

            var sitesByProject = values
                .GroupBy(literal => literal.ProjectName)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            for (var leftIndex = 0; leftIndex < sitesByProject.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < sitesByProject.Count; rightIndex++)
                {
                    var left = FirstSite(sitesByProject[leftIndex]);
                    var right = FirstSite(sitesByProject[rightIndex]);
                    var namedConstant = FirstSite(namedConstants);

                    failures.Add(
                        $"{DisplayValue(values.Key)} is repeated at {left.Location} and {right.Location}; " +
                        $"the named const {namedConstant.NamedConstant} is declared at {namedConstant.Location}. " +
                        $"Use that const instead of restating its value.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Application_configuration_does_not_mirror_a_folded_code_literal()
    {
        var allowlist = ReadAllowlist();
        var codeValues = Facts.Value.Literals
            .Where(literal => literal.ValueKey.StartsWith("number:", StringComparison.Ordinal))
            .GroupBy(literal => literal.ValueKey)
            .ToDictionary(
                group => group.Key,
                FirstSite,
                StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var configuredValue in ReadNumericConfigurationValues())
        {
            if (allowlist.Contains(CodeToConfigAllowlistKey(configuredValue.ValueKey)) ||
                !codeValues.TryGetValue(configuredValue.ValueKey, out var codeLiteral))
            {
                continue;
            }

            failures.Add(
                $"{configuredValue.Location} ({DisplayValue(configuredValue.ValueKey)}) mirrors the folded " +
                $"code literal at {codeLiteral.Location}. Keep defaults in code and use appsettings.json only " +
                "for genuine overrides.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static SourceFacts LoadSourceFacts()
    {
        var literals = SourceProjectPaths()
            .SelectMany(projectPath => ExtractLiteralSites(
                Path.GetFileNameWithoutExtension(projectPath),
                Path.GetDirectoryName(projectPath)!))
            .ToList();

        return new SourceFacts(literals);
    }

    private static IEnumerable<LiteralSite> ExtractLiteralSites(string projectName, string projectDirectory)
    {
        foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedPath(path) && !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var tokens = Tokenize(File.ReadAllText(filePath));
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (token.Kind == TokenKind.String && token.Text.Length >= 4)
                {
                    yield return NewLiteralSite(projectName, filePath, token, "string:" + token.Text, tokens, index);
                    continue;
                }

                if (token.Kind != TokenKind.Number || !TryReadProduct(tokens, index, out var valueKey, out var lastIndex))
                {
                    continue;
                }

                yield return NewLiteralSite(projectName, filePath, token, valueKey, tokens, index);
                index = lastIndex;
            }
        }
    }

    private static LiteralSite NewLiteralSite(
        string projectName,
        string filePath,
        SourceToken token,
        string valueKey,
        IReadOnlyList<SourceToken> tokens,
        int tokenIndex) => new(
        projectName,
        RelativePath(filePath) + ":" + token.Line,
        valueKey,
        NamedConstantOf(tokens, tokenIndex));

    private static bool TryReadProduct(
        IReadOnlyList<SourceToken> tokens,
        int firstIndex,
        out string valueKey,
        out int lastIndex)
    {
        if (!TryParseNumber(tokens[firstIndex].Text, out var value))
        {
            valueKey = string.Empty;
            lastIndex = firstIndex;
            return false;
        }

        lastIndex = firstIndex;
        while (lastIndex + 2 < tokens.Count &&
               tokens[lastIndex + 1].Kind == TokenKind.Asterisk &&
               tokens[lastIndex + 2].Kind == TokenKind.Number)
        {
            if (!TryParseNumber(tokens[lastIndex + 2].Text, out var factor))
            {
                valueKey = string.Empty;
                return false;
            }

            try
            {
                value *= factor;
            }
            catch (OverflowException)
            {
                valueKey = string.Empty;
                return false;
            }

            lastIndex += 2;
        }

        valueKey = NumericValueKey(value);
        return true;
    }

    private static string? NamedConstantOf(IReadOnlyList<SourceToken> tokens, int literalIndex)
    {
        var statementStart = literalIndex;
        while (statementStart > 0 && tokens[statementStart - 1].Kind is not (TokenKind.Semicolon or TokenKind.OpenBrace or TokenKind.CloseBrace))
        {
            statementStart--;
        }

        var equalsIndex = -1;
        for (var index = statementStart; index < literalIndex; index++)
        {
            if (tokens[index].Kind == TokenKind.Equals)
            {
                equalsIndex = index;
                break;
            }
        }

        if (equalsIndex < 0 ||
            !tokens.Skip(statementStart).Take(equalsIndex - statementStart).Any(token => token.Text == "const"))
        {
            return null;
        }

        for (var index = equalsIndex - 1; index >= statementStart; index--)
        {
            if (tokens[index].Kind == TokenKind.Identifier)
            {
                return tokens[index].Text;
            }
        }

        return null;
    }

    private static IEnumerable<ConfiguredValue> ReadNumericConfigurationValues()
    {
        var configurationPath = Path.Combine(RepositoryRoot, "src", "FileMutation.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));

        return EnumerateLeaves(document.RootElement, "$")
            .Where(value => value.ValueKind == JsonValueKind.Number)
            .Select(value => new ConfiguredValue(
                RelativePath(configurationPath) + ":" + value.Path,
                NumericValueKey(decimal.Parse(value.Value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture))))
            .ToList();
    }

    private static IEnumerable<JsonLeaf> EnumerateLeaves(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var leaf in EnumerateLeaves(property.Value, path + "." + property.Name))
                    {
                        yield return leaf;
                    }
                }

                yield break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var leaf in EnumerateLeaves(item, $"{path}[{index++}]"))
                    {
                        yield return leaf;
                    }
                }

                yield break;

            default:
                yield return new JsonLeaf(path, element.ValueKind, element);
                yield break;
        }
    }

    private static IReadOnlySet<string> ReadAllowlist()
    {
        var allowlistPath = Path.Combine(RepositoryRoot, "tests", "FileMutation.Architecture.Tests", "ValueMirrorRules.allowlist");
        var entries = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(allowlistPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || !parts[1].EndsWith(')') || !parts[1].Contains("(#", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid mirror allowlist entry '{line}'. Use <rule>:<kind>:<value> | reason (#issue).");
            }

            entries.Add(parts[0]);
        }

        return entries;
    }

    private static IEnumerable<string> SourceProjectPaths() => Directory
        .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
        .Where(path => !IsGeneratedPath(path))
        .OrderBy(path => path, StringComparer.Ordinal);

    private static IReadOnlyList<SourceToken> Tokenize(string source)
    {
        var tokens = new List<SourceToken>();
        var index = 0;
        var line = 1;

        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                if (source[index] == '\n')
                {
                    line++;
                }

                index++;
                continue;
            }

            if (source[index..].StartsWith("//", StringComparison.Ordinal))
            {
                index = source.IndexOf('\n', index);
                if (index < 0)
                {
                    break;
                }

                line++;
                index++;
                continue;
            }

            if (source[index..].StartsWith("/*", StringComparison.Ordinal))
            {
                var commentEnd = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = commentEnd < 0 ? source.Length : commentEnd + 2;
                line += source[index..end].Count(character => character == '\n');
                index = end;
                continue;
            }

            if (source[index] == '"')
            {
                var startLine = line;
                var verbatim = index > 0 && source[index - 1] == '@';
                var value = ReadString(source, ref index, ref line, verbatim);
                tokens.Add(new SourceToken(TokenKind.String, value, startLine));
                continue;
            }

            if (char.IsAsciiLetter(source[index]) || source[index] == '_')
            {
                var start = index++;
                while (index < source.Length && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_'))
                {
                    index++;
                }

                tokens.Add(new SourceToken(TokenKind.Identifier, source[start..index], line));
                continue;
            }

            if (char.IsAsciiDigit(source[index]))
            {
                var start = index++;
                while (index < source.Length && (char.IsAsciiLetterOrDigit(source[index]) || source[index] is '_' or '.'))
                {
                    index++;
                }

                tokens.Add(new SourceToken(TokenKind.Number, source[start..index], line));
                continue;
            }

            tokens.Add(new SourceToken(source[index] switch
            {
                '*' => TokenKind.Asterisk,
                '=' => TokenKind.Equals,
                ';' => TokenKind.Semicolon,
                '{' => TokenKind.OpenBrace,
                '}' => TokenKind.CloseBrace,
                _ => TokenKind.Other
            }, source[index].ToString(), line));
            index++;
        }

        return tokens;
    }

    private static string ReadString(string source, ref int index, ref int line, bool verbatim)
    {
        var value = new System.Text.StringBuilder();
        index++;

        while (index < source.Length)
        {
            if (source[index] == '\n')
            {
                line++;
            }

            if (source[index] == '"')
            {
                if (verbatim && index + 1 < source.Length && source[index + 1] == '"')
                {
                    value.Append('"');
                    index += 2;
                    continue;
                }

                index++;
                break;
            }

            if (!verbatim && source[index] == '\\' && index + 1 < source.Length)
            {
                value.Append(source[index + 1]);
                index += 2;
                continue;
            }

            value.Append(source[index++]);
        }

        return value.ToString();
    }

    private static bool TryParseNumber(string text, out decimal value)
    {
        var normalized = text.Replace("_", string.Empty, StringComparison.Ordinal)
            .TrimEnd('u', 'U', 'l', 'L', 'f', 'F', 'd', 'D', 'm', 'M');
        return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NumericValueKey(decimal value) => "number:" + value.ToString(CultureInfo.InvariantCulture);

    private static string CrossProjectAllowlistKey(string valueKey) => "cross-project:" + valueKey;

    private static string CodeToConfigAllowlistKey(string valueKey) => "code-to-config:" + valueKey;

    private static string DisplayValue(string valueKey) => valueKey.StartsWith("number:", StringComparison.Ordinal)
        ? valueKey["number:".Length..]
        : '"' + valueKey["string:".Length..] + '"';

    private static LiteralSite FirstSite(IEnumerable<LiteralSite> sites) => sites
        .OrderBy(site => site.Location, StringComparer.Ordinal)
        .First();

    private static bool IsGeneratedPath(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));

    private static string RelativePath(string path) => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileMutation.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("FileMutation.sln was not found above the test assembly.");
    }

    private sealed record SourceFacts(IReadOnlyList<LiteralSite> Literals);

    private sealed record LiteralSite(string ProjectName, string Location, string ValueKey, string? NamedConstant);

    private sealed record ConfiguredValue(string Location, string ValueKey);

    private sealed record JsonLeaf(string Path, JsonValueKind ValueKind, JsonElement Value);

    private sealed record SourceToken(TokenKind Kind, string Text, int Line);

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        Asterisk,
        Equals,
        Semicolon,
        OpenBrace,
        CloseBrace,
        Other
    }
}
