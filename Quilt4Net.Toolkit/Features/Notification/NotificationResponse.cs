namespace Quilt4Net.Toolkit.Features.Notification;

/// <summary>
/// A notification as Quilt4Net stored it.
/// </summary>
/// <param name="Id">The notification's id, which a subscriber reads on from.</param>
/// <param name="Category">What kind of notification it is.</param>
/// <param name="Message">The text to show.</param>
/// <param name="Application">The sending application, if known.</param>
/// <param name="Environment">The sending environment, if known.</param>
/// <param name="Instance">The sending machine or instance, if known.</param>
/// <param name="CreatedUtc">When the server stored it.</param>
public record NotificationResponse(string Id, string Category, string Message, string Application, string Environment, string Instance, DateTime CreatedUtc);
