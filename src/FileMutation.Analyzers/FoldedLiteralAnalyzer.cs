using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FileMutation.Analyzers;

/// <summary>Refuses a folded literal that restates a named constant in the same compilation.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FoldedLiteralAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The identifier used by the folded-literal diagnostic.</summary>
    public const string DiagnosticId = "FM001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Literal restates a named constant",
        "{0} restates {1} ({2}) — reference the constant instead",
        "FileMutation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var constants = CollectConstants(startContext.Compilation);
            startContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeLiteral(nodeContext, constants),
                SyntaxKind.NumericLiteralExpression,
                SyntaxKind.StringLiteralExpression);
        });
    }

    private static void AnalyzeLiteral(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<KnownConstant> constants)
    {
        if (constants.IsEmpty || context.Node is not LiteralExpressionSyntax literal)
        {
            return;
        }

        if (literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            AnalyzeStringLiteral(context, literal, constants);
            return;
        }

        AnalyzeNumericLiteral(context, literal, constants);
    }

    private static void AnalyzeStringLiteral(
        SyntaxNodeAnalysisContext context,
        LiteralExpressionSyntax literal,
        ImmutableArray<KnownConstant> constants)
    {
        var text = literal.Token.ValueText;
        if (text.Length < 4 || IsExcluded(literal))
        {
            return;
        }

        ReportIfRestated(context, literal, new FoldedValue(text), constants, literal.ToString());
    }

    private static void AnalyzeNumericLiteral(
        SyntaxNodeAnalysisContext context,
        LiteralExpressionSyntax literal,
        ImmutableArray<KnownConstant> constants)
    {
        var expression = GetFoldedNumericExpression(context.SemanticModel, literal, context.CancellationToken);
        if (!IsFirstNumericLiteral(expression, literal) || IsExcluded(expression) ||
            ReferencesNamedConstant(context.SemanticModel, expression, context.CancellationToken))
        {
            return;
        }

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.Value is not { } value || !FoldedValue.TryCreate(value, out var foldedValue) ||
            foldedValue.IsTrivialNumber)
        {
            return;
        }

        ReportIfRestated(context, expression, foldedValue, constants, expression.ToString());
    }

    private static void ReportIfRestated(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        FoldedValue value,
        ImmutableArray<KnownConstant> constants,
        string displayText)
    {
        var declaration = GetEnclosingConstDeclaration(context.SemanticModel, expression, context.CancellationToken);
        var restated = constants.FirstOrDefault(constant =>
            !SymbolEqualityComparer.Default.Equals(constant.Symbol, declaration) && constant.Value.Equals(value));
        if (restated is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            expression.GetLocation(),
            displayText,
            restated.Symbol.Name,
            NamespaceName(restated.Symbol)));
    }

    private static ImmutableArray<KnownConstant> CollectConstants(Compilation compilation)
    {
        var constants = ImmutableArray.CreateBuilder<KnownConstant>();
        CollectConstants(compilation.Assembly.GlobalNamespace, constants);
        return constants.ToImmutable();
    }

    private static void CollectConstants(INamespaceSymbol namespaceSymbol, ImmutableArray<KnownConstant>.Builder constants)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                CollectConstants(childNamespace, constants);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectConstants(type, constants);
            }
        }
    }

    private static void CollectConstants(INamedTypeSymbol type, ImmutableArray<KnownConstant>.Builder constants)
    {
        if (type.TypeKind != TypeKind.Enum)
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => field.IsConst))
            {
                if (field.ConstantValue is { } value && FoldedValue.TryCreate(value, out var foldedValue))
                {
                    constants.Add(new KnownConstant(field, foldedValue));
                }
            }
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            CollectConstants(nestedType, constants);
        }
    }

    private static ExpressionSyntax GetFoldedNumericExpression(
        SemanticModel semanticModel,
        LiteralExpressionSyntax literal,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax expression = literal;
        while (expression.Parent is ExpressionSyntax parent &&
               semanticModel.GetConstantValue(parent, cancellationToken).HasValue)
        {
            expression = parent;
        }

        return expression;
    }

    private static bool IsFirstNumericLiteral(ExpressionSyntax expression, LiteralExpressionSyntax literal) =>
        expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(node => node.IsKind(SyntaxKind.NumericLiteralExpression)) == literal;

    private static bool IsExcluded(ExpressionSyntax expression) =>
        expression.AncestorsAndSelf().OfType<EnumMemberDeclarationSyntax>().Any() ||
        expression.AncestorsAndSelf().OfType<ArrayRankSpecifierSyntax>().Any();

    private static bool ReferencesNamedConstant(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        CancellationToken cancellationToken) =>
        expression.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier =>
            IsConst(GetReferencedSymbol(semanticModel, identifier, cancellationToken)));

    private static ISymbol? GetConstDeclaration(
        SemanticModel semanticModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
        if (IsConst(declaredSymbol))
        {
            return declaredSymbol;
        }

        var referencedSymbol = GetReferencedSymbol(semanticModel, node, cancellationToken);
        return IsConst(referencedSymbol) ? referencedSymbol : null;
    }

    private static ISymbol? GetEnclosingConstDeclaration(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        var declarator = expression.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        return declarator is null ? null : GetConstDeclaration(semanticModel, declarator, cancellationToken);
    }

    private static ISymbol? GetReferencedSymbol(
        SemanticModel semanticModel,
        SyntaxNode node,
        CancellationToken cancellationToken) =>
        semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;

    private static bool IsConst(ISymbol? symbol) => symbol is IFieldSymbol { IsConst: true } ||
        symbol is ILocalSymbol { IsConst: true };

    private static string NamespaceName(ISymbol symbol)
    {
        var namespaceName = symbol.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName) ? "global namespace" : namespaceName;
    }

    private sealed class KnownConstant(IFieldSymbol symbol, FoldedValue value)
    {
        internal IFieldSymbol Symbol { get; } = symbol;

        internal FoldedValue Value { get; } = value;
    }

    private sealed class FoldedValue : IEquatable<FoldedValue>
    {
        private readonly decimal? _number;
        private readonly string? _text;

        internal FoldedValue(string text) => _text = text;

        private FoldedValue(decimal number) => _number = number;

        internal bool IsTrivialNumber => _number is 0 or 1 or -1;

        internal static bool TryCreate(object value, out FoldedValue foldedValue)
        {
            if (value is string text)
            {
                foldedValue = new FoldedValue(text);
                return true;
            }

            if (value is IConvertible convertible && IsNumeric(convertible.GetTypeCode()))
            {
                try
                {
                    foldedValue = new FoldedValue(convertible.ToDecimal(CultureInfo.InvariantCulture));
                    return true;
                }
                catch (OverflowException)
                {
                    // Values outside decimal's range cannot be compared across numeric types safely.
                }
            }

            foldedValue = null!;
            return false;
        }

        public bool Equals(FoldedValue? other) =>
            other is not null && _number == other._number && string.Equals(_text, other._text, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as FoldedValue);

        public override int GetHashCode() =>
            (_number?.GetHashCode() ?? 0) ^ (_text?.GetHashCode() ?? 0);

        private static bool IsNumeric(TypeCode typeCode) =>
            typeCode is >= TypeCode.SByte and <= TypeCode.Decimal;
    }
}
