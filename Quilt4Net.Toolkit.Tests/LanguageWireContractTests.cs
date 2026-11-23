using System.Text.Json;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// #144 adds <see cref="Language.Code"/> to a wire model that a *server* also serializes, and the
/// two sides upgrade independently — the Server consumes the Toolkit as a published package, so
/// there is always a window where one end knows about the field and the other does not.
/// </summary>
public class LanguageWireContractTests
{
    // Matches how the client reads it: ReadFromJsonAsync uses JsonSerializerDefaults.Web.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Code_is_read_from_the_language_payload()
    {
        var json = """{"key":"e088a31e-c415-4f78-a00b-eb09a77c78d0","name":"Svenska","developer":false,"code":"sv"}""";

        var language = JsonSerializer.Deserialize<Language>(json, WebOptions);

        language.Code.Should().Be("sv");
        language.Name.Should().Be("Svenska");
    }

    [Fact]
    public void A_server_that_does_not_send_a_code_yields_null_rather_than_failing()
    {
        // The upgrade window that actually happens: this field ships in the Toolkit first, so every
        // server predates it until the Server bumps its package reference.
        var json = """{"key":"00000000-0000-0000-0000-000000000000","name":"English","developer":false}""";

        var language = JsonSerializer.Deserialize<Language>(json, WebOptions);

        language.Should().NotBeNull();
        language.Code.Should().BeNull();
    }

    [Fact]
    public void An_explicit_null_code_is_accepted()
    {
        // A server that knows the field but could not determine a code sends null rather than
        // guessing — that is the documented contract, so it must deserialize as cleanly as absence.
        var json = """{"key":"00000000-0000-0000-0000-000000000000","name":"English","code":null}""";

        var language = JsonSerializer.Deserialize<Language>(json, WebOptions);

        language.Code.Should().BeNull();
    }

    [Fact]
    public void A_full_language_response_round_trips()
    {
        var json = """
            {"languages":[
              {"key":"00000000-0000-0000-0000-000000000000","name":"English","developer":false,"code":"en"},
              {"key":"e088a31e-c415-4f78-a00b-eb09a77c78d0","name":"Svenska","developer":false,"code":"sv"},
              {"key":"24368a7b-a720-4115-8b4e-de1d2dd618cc","name":"Español","developer":false}
            ],"validTo":"2030-01-01T00:00:00Z"}
            """;

        var response = JsonSerializer.Deserialize<LanguageResponse>(json, WebOptions);

        response.Languages.Should().HaveCount(3);
        response.Languages.Select(x => x.Code).Should().Equal("en", "sv", null);
    }
}
