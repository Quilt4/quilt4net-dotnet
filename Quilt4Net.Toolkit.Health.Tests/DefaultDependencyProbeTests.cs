using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Health.Dependency;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

public class DefaultDependencyProbeTests
{
    private static readonly Dependency Dependency = new() { Name = "dep", Uri = new Uri("https://dependency.test/"), Essential = false };

    [Fact]
    public async Task NonSuccessResponse_ReturnsUnhealthyWithStatusCode_DoesNotThrow()
    {
        //Arrange
        var logger = new CapturingLogger<DefaultDependencyProbe>();
        var probe = new DefaultDependencyProbe(new Quilt4NetHealthApiOptions(), logger)
        {
            HandlerFactory = () => new StubHandler(HttpStatusCode.TooManyRequests, "Too Many Requests", "text/plain")
        };

        //Act
        var content = await probe.ProbeAsync(Dependency, TestContext.Current.CancellationToken);

        //Assert
        content.Status.Should().Be(HealthStatus.Unhealthy);
        content.Components.Should().ContainKey("Probe");
        var component = content.Components["Probe"];
        component.Status.Should().Be(HealthStatus.Unhealthy);
        component.Details.Should().ContainKey("StatusCode");
        component.Details["StatusCode"].Should().Be("429");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SuccessResponse_ReturnsParsedContent_NoWarning()
    {
        //Arrange
        const string json = """{"Status":"Healthy","Components":{"Db":{"Status":"Healthy"}}}""";
        var options = new Quilt4NetHealthApiOptions { Certificate = new CertificateCheckOptions { DependencyCheckEnabled = false } };
        var logger = new CapturingLogger<DefaultDependencyProbe>();
        var probe = new DefaultDependencyProbe(options, logger)
        {
            HandlerFactory = () => new StubHandler(HttpStatusCode.OK, json, "application/json")
        };

        //Act
        var content = await probe.ProbeAsync(Dependency, TestContext.Current.CancellationToken);

        //Assert
        content.Status.Should().Be(HealthStatus.Healthy);
        content.Components.Should().ContainKey("Db");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task TransportFailure_ReturnsUnhealthy_DoesNotThrow()
    {
        //Arrange
        var logger = new CapturingLogger<DefaultDependencyProbe>();
        var probe = new DefaultDependencyProbe(new Quilt4NetHealthApiOptions(), logger)
        {
            HandlerFactory = () => new ThrowingHandler(new HttpRequestException("boom"))
        };

        //Act
        var content = await probe.ProbeAsync(Dependency, TestContext.Current.CancellationToken);

        //Assert
        content.Status.Should().Be(HealthStatus.Unhealthy);
        content.Components.Should().ContainKey("Probe");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        private readonly string _mediaType;

        public StubHandler(HttpStatusCode status, string content, string mediaType)
        {
            _status = status;
            _content = content;
            _mediaType = mediaType;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_content, Encoding.UTF8, _mediaType) });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
