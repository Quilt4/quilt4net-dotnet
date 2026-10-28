namespace Quilt4Net.Toolkit.Features.Notification;

/// <summary>
/// Sends notifications to Quilt4Net, where subscribers such as the Quilt4Net app pick them up.
/// </summary>
public interface INotificationClient
{
    /// <summary>
    /// Sends a notification. The receiving app decides from <paramref name="category"/> how loudly to
    /// alert, so pick categories the team has agreed on, such as <c>alarm</c> or <c>info</c>.
    /// </summary>
    /// <param name="category">What kind of notification this is. Required, at most 100 characters.</param>
    /// <param name="message">The text to show. Required, at most 4000 characters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The notification as the server stored it.</returns>
    /// <exception cref="NotificationClientException">The server refused or failed the call.</exception>
    Task<NotificationResponse> SendAsync(string category, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a notification with every field spelled out. Application, environment and instance left
    /// unset are filled in from the options, the entry assembly, the host environment and the machine name.
    /// </summary>
    /// <param name="request">The notification to send.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The notification as the server stored it.</returns>
    /// <exception cref="NotificationClientException">The server refused or failed the call.</exception>
    Task<NotificationResponse> SendAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The team's notification categories, to pick from (#147). Null when the server cannot list them: one
    /// from before #147, or a key without <c>notification:read</c>. Any category can still be sent; one the
    /// team does not have is added. A default implementation keeps implementers building.
    /// </summary>
    Task<string[]> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<string[]>(null);
}
