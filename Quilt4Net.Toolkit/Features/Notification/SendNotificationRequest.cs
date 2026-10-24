namespace Quilt4Net.Toolkit.Features.Notification;

/// <summary>
/// A notification to send to Quilt4Net.
/// </summary>
public record SendNotificationRequest
{
    /// <summary>What kind of notification this is, such as <c>alarm</c> or <c>info</c>.</summary>
    public required string Category { get; init; }

    /// <summary>The text to show.</summary>
    public required string Message { get; init; }

    /// <summary>The sending application. Filled in by the client when not set.</summary>
    public string Application { get; init; }

    /// <summary>The sending environment. Filled in by the client when not set.</summary>
    public string Environment { get; init; }

    /// <summary>The sending machine or instance. Filled in with the machine name when not set.</summary>
    public string Instance { get; init; }
}
