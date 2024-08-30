using System.Threading.Tasks;
using DotNetCoreFunctional.UnionTypes.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DotNetCoreFunctional.Analyzers.Tests;

public class UnionTypeSwitchAnalyzerSuppressorTests
{
    [Fact]
    public async Task SuppressesWhenCasesExhaustive()
    {
        var context = TestContextFactory.CreateContext<UnionTypeSwitchExpressionSuppressor>();
        context
            .TestState
            .Sources
            .AddRange(
                [
                    (
                        "AllCasesSwitch.cs",
                        SourceTextFactory.CreateSourceText("../../../TestSources/AllCasesSwitch.cs")
                    ),
                    (
                        "ClosedTestType.cs",
                        SourceTextFactory.CreateSourceText("../../../TestSources/ClosedTestType.cs")
                    ),
                    (
                        "GenericClosedTestType.cs",
                        SourceTextFactory.CreateSourceText(
                            "../../../TestSources/GenericClosedTestType.cs"
                        )
                    )
                ]
            );

        context.CompilerDiagnostics = CompilerDiagnostics.Warnings;
        context.DisabledDiagnostics.AddRange(["CS1591"]);
        context
            .ExpectedDiagnostics
            .AddRange(
                [
                    DiagnosticResult
                        .CompilerWarning(UnionTypeSwitchExpressionSuppressor.SuppressedDiagnosticId)
                        .WithSpan("AllCasesSwitch.cs", 17, 26, 17, 32)
                        .WithArguments("_")
                        .WithIsSuppressed(true),
                    DiagnosticResult
                        .CompilerWarning(UnionTypeSwitchExpressionSuppressor.SuppressedDiagnosticId)
                        .WithLocation("AllCasesSwitch.cs", 28, 33)
                        .WithArguments("_")
                        .WithIsSuppressed(true)
                ]
            );

        await context.RunAsync();
    }
}
