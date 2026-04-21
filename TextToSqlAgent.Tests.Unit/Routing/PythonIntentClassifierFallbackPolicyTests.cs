using FluentAssertions;
using TextToSqlAgent.Application.Routing;

namespace TextToSqlAgent.Tests.Unit.Routing;

public class PythonIntentClassifierFallbackPolicyTests
{
    [Fact]
    public void ShouldFallbackWhenSidecarIsAdvisoryOnly()
    {
        var shouldFallback = PythonIntentClassifier.ShouldUseDotNetFallback("ml", "ready", advisoryOnly: true);

        shouldFallback.Should().BeTrue();
    }

    [Fact]
    public void ShouldNotFallbackWhenSidecarIsReadyAndNonAdvisory()
    {
        var shouldFallback = PythonIntentClassifier.ShouldUseDotNetFallback("ml", "ready", advisoryOnly: false);

        shouldFallback.Should().BeFalse();
    }

    [Fact]
    public void ShouldNotFallbackForSafetyOverrideEvenWhenAdvisory()
    {
        var shouldFallback = PythonIntentClassifier.ShouldUseDotNetFallback("safety_override", "degraded", advisoryOnly: true);

        shouldFallback.Should().BeFalse();
    }

    [Fact]
    public void ShouldFallbackWhenSidecarUsesRuleFallback()
    {
        var shouldFallback = PythonIntentClassifier.ShouldUseDotNetFallback("rule_fallback", "ready", advisoryOnly: false);

        shouldFallback.Should().BeTrue();
    }
}
