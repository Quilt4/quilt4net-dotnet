using Quilt4Net.Toolkit.Features.FeatureToggle;

namespace Quilt4Net.Toolkit.Blazor.Features.Configuration;

/// <summary>Which entries <c>RemoteConfigurationAdmin</c> lists: the same filter as the configuration page on Quilt4Net Web.</summary>
internal enum ConfigurationKind
{
    All,
    FeatureToggles,
    OtherValues
}

/// <summary>
/// A feature toggle is a configuration entry whose value is true or false; there is no other
/// difference. This is the one place in the component that says so.
/// </summary>
internal static class ConfigurationKindFilter
{
    /// <summary>The value type stored for a toggle read with <c>GetToggleAsync</c>.</summary>
    public const string FeatureToggleValueType = "Boolean";

    public static bool IsFeatureToggle(ConfigurationResponse entry) =>
        string.Equals(entry?.ValueType, FeatureToggleValueType, StringComparison.Ordinal);

    public static bool Matches(ConfigurationResponse entry, ConfigurationKind kind) => kind switch
    {
        ConfigurationKind.FeatureToggles => IsFeatureToggle(entry),
        ConfigurationKind.OtherValues => !IsFeatureToggle(entry),
        _ => true
    };
}
