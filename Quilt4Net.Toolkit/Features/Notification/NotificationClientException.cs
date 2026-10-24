namespace Quilt4Net.Toolkit.Features.Notification;

/// <summary>
/// Thrown when the Quilt4Net server refuses or fails a notification call.
/// </summary>
/// <remarks>
/// The client throws rather than dropping the notification quietly: a notification is usually sent
/// because something is wrong, and a sender that believes it raised an alarm when it did not is the
/// worst outcome. The caller decides whether to retry, log or carry on.
/// </remarks>
public class NotificationClientException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What the server said.</param>
    /// <param name="statusCode">The HTTP status code returned.</param>
    public NotificationClientException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code the server returned.</summary>
    public int StatusCode { get; }
}
