using System.Reflection;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Tests.Localization;

/// <summary>
/// Tests for <see cref="CapabilityText"/>: every capability name and reason code reported below the UI
/// has a resource string, so no capability text reaches the user unlocalized (E13-S08).
/// </summary>
public sealed class CapabilityTextTests
{
    public static TheoryData<string> KnownCapabilityNames()
        => [.. typeof(KnownCapabilities)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (CapabilityDefinition)field.GetValue(null)!)
            .Select(capability => capability.Name)];

    public static TheoryData<string> ReasonCodes()
        => [.. typeof(CapabilityReasons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)];

    public static TheoryData<string> ReasonFactories()
        => [.. typeof(CapabilityReasons)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(CapabilityReason))
            .Select(method => method.Name)];

    [Theory]
    [MemberData(nameof(KnownCapabilityNames))]
    public void EveryKnownCapability_HasAName(string name)
    {
        AssertHasString(CapabilityText.NameKey(new CapabilityDefinition("test.id", name)));
    }

    [Theory]
    [MemberData(nameof(ReasonCodes))]
    public void EveryReasonCode_HasAString(string code)
    {
        AssertHasString(CapabilityText.ReasonKey(code));
    }

    [Theory]
    [MemberData(nameof(ReasonFactories))]
    public void EveryReasonFactory_FillsItsString(string factoryName)
    {
        var factory = typeof(CapabilityReasons).GetMethod(factoryName)!;
        var arguments = factory.GetParameters().Select(parameter => (object)$"<{parameter.Name}>").ToArray();
        var reason = (CapabilityReason)factory.Invoke(null, arguments)!;

        var text = CapabilityText.Describe(CreateLocalizer(), KnownCapabilities.VideoAnalysis, reason);

        Assert.DoesNotContain("!", text, StringComparison.Ordinal);
        Assert.All(arguments, argument => Assert.Contains((string)argument, text, StringComparison.Ordinal));
    }

    private static void AssertHasString(string key)
    {
        var value = CreateLocalizer().Get(key);
        Assert.False(value.StartsWith('!'), $"Strings.resx has no '{key}'.");
    }

    private static Localizer CreateLocalizer() => TestLocalizer.EnUs();
}
