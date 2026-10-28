namespace Quilt4Net.Toolkit.Features.Content;

/// <summary>One language in the content cache (#144): how much is held, how much of it is defaults, and the last bulk load.</summary>
/// <param name="LanguageKey">The language; <see cref="Guid.Empty"/> for the default language.</param>
/// <param name="Count">How many values are cached for it.</param>
/// <param name="Defaults">How many of them are the fallback default, not a value the server confirmed.</param>
/// <param name="FreshUntil">When the first of its values needs reloading; null when nothing is cached.</param>
/// <param name="LastWarmUp">The last bulk load for the language; null when it has not had one.</param>
public sealed record ContentCacheLanguageStatus(Guid LanguageKey, int Count, int Defaults, DateTime? FreshUntil, ContentWarmUp LastWarmUp);

/// <summary>A bulk load of one language's content.</summary>
/// <param name="Utc">When it ran.</param>
/// <param name="Succeeded">Whether the server's values were loaded.</param>
/// <param name="Items">How many values it loaded.</param>
/// <param name="Problem">Why it did not load: the server's answer or the error. Null when it did.</param>
public sealed record ContentWarmUp(DateTime Utc, bool Succeeded, int Items, string Problem);

/// <summary>One cached content value, for looking it up on a status page.</summary>
/// <param name="Key">The content key.</param>
/// <param name="Application">The application it is cached for; empty for the shared one.</param>
/// <param name="LanguageKey">The language it is cached for.</param>
/// <param name="Value">The text.</param>
/// <param name="ValidTo">When it stops being fresh.</param>
/// <param name="IsDefault">The value is the fallback default, not one the server confirmed.</param>
/// <param name="FallbackReason">Why the server served another language's or stage's value, when it did.</param>
public sealed record ContentCacheEntry(string Key, string Application, Guid LanguageKey, string Value, DateTime ValidTo, bool IsDefault, string FallbackReason);
