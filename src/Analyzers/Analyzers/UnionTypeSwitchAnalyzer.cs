using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotNetCoreFunctional.UnionTypes.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnionTypeSwitchAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "NF0001";

    private static readonly LocalizableString Title = new LocalizableResourceString(
        nameof(Resources.NF0001Title),
        Resources.ResourceManager,
        typeof(Resources)
    );

    private static readonly LocalizableString MessageFormat = new LocalizableResourceString(
        nameof(Resources.NF0001MessageFormat),
        Resources.ResourceManager,
        typeof(Resources)
    );

    private static readonly LocalizableString Description = new LocalizableResourceString(
        nameof(Resources.NF0001Description),
        Resources.ResourceManager,
        typeof(Resources)
    );

    internal static readonly DiagnosticDescriptor Rule =
        new(
            DiagnosticId,
            Title,
            MessageFormat,
            "UnionTypes",
            DiagnosticSeverity.Error,
            true,
            Description
        );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        if (!Debugger.IsAttached)
            Debugger.Launch();

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(
            AnalyzeSwitchExpression,
            OperationKind.SwitchExpression,
            OperationKind.Switch
        );
    }

    private void AnalyzeSwitchExpression(OperationAnalysisContext context)
    {
        if (
            !TryToParseSwitch(
                context.Operation,
                out var switchCaseOperations,
                out var referenceOperationType
            )
        )
            return;

        var referenceNamespace = referenceOperationType!.ContainingNamespace;
        var unionTypeChecker = ResolveUnionTypeChecker(referenceOperationType);

        var childTypes = referenceNamespace
            .GetTypeMembers()
            .Where(unionTypeChecker)
            .Select(GetComparableNamedTypeSymbol)
            .ToArray();

        var switchCaseTypes = switchCaseOperations
            .Select(
                op =>
                    op switch
                    {
                        ITypePatternOperation { MatchedType: INamedTypeSymbol matchedType }
                            => matchedType,
                        IDeclarationPatternOperation { MatchedType: INamedTypeSymbol matchedType }
                            => matchedType,
                        _ => null
                    }
            )
            .OfType<INamedTypeSymbol>()
            .Select(GetComparableNamedTypeSymbol)
            .ToArray();

        if (
            switchCaseOperations.Length > 0
            && (
                switchCaseOperations.Last()
                    is IDiscardPatternOperation
                        or IDefaultCaseClauseOperation
                || (
                    childTypes.Length == switchCaseTypes.Length
                    && childTypes.Intersect(switchCaseTypes, SymbolEqualityComparer.Default).Count()
                        == childTypes.Length
                )
            )
        )
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            context.Operation.Syntax.GetLocation(),
            referenceOperationType
        );

        context.ReportDiagnostic(diagnostic);
    }

    private bool TryToParseSwitch(
        IOperation operation,
        out ImmutableArray<IOperation> caseOperations,
        out INamedTypeSymbol? switchValueType
    )
    {
        switch (operation)
        {
            case ISwitchExpressionOperation switchExpressionOperation:
                caseOperations = GetSwitchArmOperations(switchExpressionOperation);
                return TryGetNamedSwitchValueType(
                    switchExpressionOperation.Value,
                    out switchValueType
                );
            case ISwitchOperation switchOperation:
                caseOperations = GetSwitchStatementCaseOperations(switchOperation);
                return TryGetNamedSwitchValueType(switchOperation.Value, out switchValueType);

            default:
                caseOperations = ImmutableArray<IOperation>.Empty;
                switchValueType = default;
                return false;
        }
    }

    private bool TryGetNamedSwitchValueType(
        IOperation valueParameter,
        out INamedTypeSymbol? switchValueType
    )
    {
        if (
            valueParameter
                is not IParameterReferenceOperation
                {
                    Type: INamedTypeSymbol { TypeKind: TypeKind.Class } referenceOperationType
                }
            || referenceOperationType
                .GetAttributes()
                .All(a => a is not { AttributeClass.Name: "ClosedAttribute" })
        )
        {
            switchValueType = default;
            return false;
        }

        switchValueType = referenceOperationType;
        return true;
    }

    private ImmutableArray<IOperation> GetSwitchArmOperations(
        ISwitchExpressionOperation switchExpressionOperation
    )
    {
        return switchExpressionOperation
            .Arms
            .Select(arm => arm.ChildOperations)
            .SelectMany(
                ops =>
                    ops.Select(
                            o =>
                                o switch
                                {
                                    ITypePatternOperation tpo => (IOperation)tpo,
                                    IDeclarationPatternOperation dpo => dpo,
                                    IDiscardPatternOperation def => def,
                                    _ => null
                                }
                        )
                        .Where(o => o is not null)
            )
            .Except([null])
            .Cast<IOperation>()
            .ToImmutableArray();
    }

    private ImmutableArray<IOperation> GetSwitchStatementCaseOperations(
        ISwitchOperation switchOperation
    )
    {
        return switchOperation
            .Cases
            .Select(sc => sc.Clauses)
            .SelectMany(
                clauses =>
                    clauses.Select(
                        cco =>
                            cco switch
                            {
                                IPatternCaseClauseOperation { Pattern: ITypePatternOperation tpo }
                                    => (IOperation)tpo,
                                IPatternCaseClauseOperation
                                {
                                    Pattern: IDeclarationPatternOperation dpo
                                }
                                    => dpo,
                                IDefaultCaseClauseOperation dpo => dpo,
                                _ => null
                            }
                    )
            )
            .Except([null])
            .Cast<IOperation>()
            .ToImmutableArray();
    }

    private bool TryGetSwitchExpressionOperation(
        IOperation operation,
        out INamedTypeSymbol? namedTypeSymbol
    )
    {
        var switchOperation = operation switch
        {
            ISwitchOperation so => so.Value,
            ISwitchExpressionOperation seo => seo,
            _ => null
        };

        namedTypeSymbol = switchOperation
            is { Type: INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } nts }
            ? nts
            : null;

        return namedTypeSymbol is not null;
    }

    private static bool ValueTypeChecker(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol caseTypeSymbol
    )
    {
        return false;
    }

    private static bool ReferenceTypeChecker(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol caseTypeSymbol
    )
    {
        var comparableTypeSymbol = GetComparableNamedTypeSymbol(typeSymbol);

        return caseTypeSymbol.BaseType is { } baseType
            && comparableTypeSymbol.Equals(
                GetComparableNamedTypeSymbol(baseType),
                SymbolEqualityComparer.Default
            );
    }

    private static Func<INamedTypeSymbol, bool> ResolveUnionTypeChecker(
        INamedTypeSymbol referenceTypeSymbol
    )
    {
        return referenceTypeSymbol switch
        {
            { TypeKind: TypeKind.Class }
                => implementationTypeSymbol =>
                    ReferenceTypeChecker(referenceTypeSymbol, implementationTypeSymbol),
            { TypeKind: TypeKind.Struct }
                => castTypeSymbol => ValueTypeChecker(referenceTypeSymbol, castTypeSymbol)
        };
    }

    private static INamedTypeSymbol GetComparableNamedTypeSymbol(INamedTypeSymbol typeSymbol)
    {
        return !typeSymbol.IsGenericType ? typeSymbol : typeSymbol.ConstructUnboundGenericType();
    }
}
