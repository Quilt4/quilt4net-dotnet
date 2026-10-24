namespace Quilt4Net.Toolkit;

/// <summary>
/// Options for the Quilt4Net notification client.
/// </summary>
public record NotificationOptions
{
    /// <summary>
    /// Address of the Quilt4Net server. Defaulted on the type so an unbound
    /// <c>IOptions&lt;NotificationOptions&gt;</c> still carries a usable URL.
    /// </summary>
    public string Quilt4NetAddress { get; set; } = "https://quilt4net.com/";

    /// <summary>
    /// Team API key used for every call. Sending needs <c>notification:write</c>, which is granted
    /// per key rather than by access level — an ordinary application key will not have it.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Application name sent with each notification. When not set, the entry assembly's name is used.
    /// </summary>
    public string Application { get; set; }

    /// <summary>
    /// Environment name sent with each notification. When not set, the host environment's name is
    /// used, if the host registers one.
    /// </summary>
    public string Environment { get; set; }
}
