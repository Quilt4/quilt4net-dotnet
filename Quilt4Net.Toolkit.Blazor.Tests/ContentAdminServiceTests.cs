using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Blazor;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class ContentAdminServiceTests
{
    private static IOptions<ContentOptions> CreateOptions(string[] adminRoles = null, bool assumeAdmin = false)
    {
        var options = new ContentOptions { AssumeAdmin = assumeAdmin };
        if (adminRoles != null) options.AdminRoles = adminRoles;
        return Options.Create(options);
    }

    private static AuthenticationStateProvider CreateAuthProvider(string[] roles, bool isAuthenticated = true)
    {
        var claims = new List<Claim>();
        if (isAuthenticated)
        {
            claims.Add(new Claim(ClaimTypes.Name, "testuser"));
        }
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = isAuthenticated
            ? new ClaimsIdentity(claims, "test")
            : new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        return new FakeAuthStateProvider(principal);
    }

    [Fact]
    public async Task Returns_True_When_User_Has_Default_ContentAdmin_Role()
    {
        var service = new ContentAdminService(CreateOptions(), CreateAuthProvider(["ContentAdmin"]));

        var result = await service.IsContentAdminAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Returns_True_When_User_Has_Default_Developer_Role()
    {
        var service = new ContentAdminService(CreateOptions(), CreateAuthProvider(["Developer"]));

        var result = await service.IsContentAdminAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Returns_False_When_User_Has_No_Matching_Role()
    {
        var service = new ContentAdminService(CreateOptions(), CreateAuthProvider(["Viewer"]));

        var result = await service.IsContentAdminAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Returns_False_When_User_Is_Not_Authenticated()
    {
        var service = new ContentAdminService(CreateOptions(), CreateAuthProvider([], isAuthenticated: false));

        var result = await service.IsContentAdminAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Returns_False_When_No_AuthStateProvider_Configured()
    {
        var service = new ContentAdminService(CreateOptions(), authStateProvider: null);

        var result = await service.IsContentAdminAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Returns_True_When_AssumeAdmin_Is_True()
    {
        var service = new ContentAdminService(CreateOptions(assumeAdmin: true), authStateProvider: null);

        var result = await service.IsContentAdminAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AssumeAdmin_Overrides_Role_Check()
    {
        var service = new ContentAdminService(CreateOptions(assumeAdmin: true), CreateAuthProvider(["Viewer"]));

        var result = await service.IsContentAdminAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Respects_Custom_AdminRoles()
    {
        var service = new ContentAdminService(CreateOptions(["SuperAdmin"]), CreateAuthProvider(["SuperAdmin"]));

        var result = await service.IsContentAdminAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Custom_Roles_Exclude_Default_Roles()
    {
        var service = new ContentAdminService(CreateOptions(["SuperAdmin"]), CreateAuthProvider(["ContentAdmin"]));

        var result = await service.IsContentAdminAsync();

        result.Should().BeFalse();
    }

    private class FakeAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _principal;

        public FakeAuthStateProvider(ClaimsPrincipal principal) => _principal = principal;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_principal));
    }
}
