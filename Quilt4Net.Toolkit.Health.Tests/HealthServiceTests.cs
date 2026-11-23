using System.Diagnostics;
using Bogus;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Probe;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

public class HealthServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProvider = new(MockBehavior.Strict);
    private readonly Mock<Quilt4NetHealthApiOptions> _option = new(MockBehavior.Strict);
    private readonly Mock<ILogger<HealthService>> _logger = new(MockBehavior.Loose);
    private readonly Mock<IHostEnvironment> _hostEnvironment = new(MockBehavior.Strict);
    private readonly Mock<IHostedServiceProbeRegistry> _hostedServiceProbeRegistry = new(MockBehavior.Strict);

    public HealthServiceTests()
    {
        _hostedServiceProbeRegistry.Setup(x => x.GetProbesAsync())
            .Returns(() => Array.Empty<KeyValuePair<string, HealthComponent>>().ToAsyncEnumerable());
    }

    [Fact]
    public async Task NoComponents()
    {
        //Arrange
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, _option.Object, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(true, true, HealthStatus.Healthy)]
    [InlineData(false, true, HealthStatus.Unhealthy)]
    [InlineData(true, false, HealthStatus.Healthy)]
    [InlineData(false, false, HealthStatus.Degraded)]
    public async Task OneComponent(bool success, bool essential, HealthStatus expectedStatus)
    {
        //Arrange
        var message = new Faker().Random.AlphaNumeric(12);
        var component = new Component
        {
            Name = "a",
            Essential = essential,
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = success, Message = message }),
        };
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(component);
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(expectedStatus);
        result.Components.Single().Key.Should().Be(component.Name);
        result.Components.Single().Value.Status.Should().Be(expectedStatus);
        result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
    }

    [Theory]
    [InlineData(true, HealthStatus.Unhealthy)]
    [InlineData(false, HealthStatus.Degraded)]
    public async Task ComponentWithException(bool essential, HealthStatus expectedStatus)
    {
        //Arrange
        var component = new Component
        {
            Name = "a",
            Essential = essential,
            CheckAsync = _ => throw new InvalidOperationException("some issue"),
        };
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(component);
        _hostEnvironment.Setup(x => x.EnvironmentName).Returns("Production");
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(expectedStatus);
        result.Components.Single().Key.Should().Be(component.Name);
        result.Components.Single().Value.Status.Should().Be(expectedStatus);
        result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("Hidden exception.");
    }

    [Theory]
    [InlineData(ExceptionDetailLevel.Hidden)]
    [InlineData(ExceptionDetailLevel.Message)]
    [InlineData(ExceptionDetailLevel.StackTrace)]
    public async Task CustomExceptionDataLevel(ExceptionDetailLevel exceptionDetailLevel)
    {
        //Arrange
        var component = new Component
        {
            Name = "a",
            Essential = true,
            CheckAsync = _ => throw new InvalidOperationException("some issue"),
        };
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(component);
        option.ExceptionDetail = exceptionDetailLevel;
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Components.Single().Key.Should().Be(component.Name);
        result.Components.Single().Value.Status.Should().Be(HealthStatus.Unhealthy);
        switch (exceptionDetailLevel)
        {
            case ExceptionDetailLevel.Hidden:
                result.Components.Single().Value.Details.Count.Should().Be(2);
                result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("Hidden exception.");
                result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "exception.stacktrace").Value.Should().BeNull();
                break;
            case ExceptionDetailLevel.Message:
                result.Components.Single().Value.Details.Count.Should().Be(2);
                result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("some issue");
                result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "exception.stacktrace").Value.Should().BeNull();
                break;
            case ExceptionDetailLevel.StackTrace:
                result.Components.Single().Value.Details.Count.Should().Be(3);
                result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("some issue");
                result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "exception.stacktrace").Value.Should().StartWith("   at");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(exceptionDetailLevel), exceptionDetailLevel, null);
        }
    }

    [Theory]
    [InlineData("Production", ExceptionDetailLevel.Hidden)]
    [InlineData("Development", ExceptionDetailLevel.StackTrace)]
    [InlineData("AllOtherEnvironments", ExceptionDetailLevel.Message)]
    public async Task DefaultExceptionDataLevel(string environment, ExceptionDetailLevel exceptionDetailLevel)
    {
        //Arrange
        var component = new Component
        {
            Name = "a",
            Essential = true,
            CheckAsync = _ => throw new InvalidOperationException("some issue"),
        };
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(component);
        _hostEnvironment.Setup(x => x.EnvironmentName).Returns(environment);
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Components.Single().Key.Should().Be(component.Name);
        result.Components.Single().Value.Status.Should().Be(HealthStatus.Unhealthy);
        switch (exceptionDetailLevel)
        {
            case ExceptionDetailLevel.Hidden:
                result.Components.Single().Value.Details.Count.Should().Be(2);
                result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("Hidden exception.");
                result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "exception.stacktrace").Value.Should().BeNull();
                break;
            case ExceptionDetailLevel.Message:
                result.Components.Single().Value.Details.Count.Should().Be(2);
                result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("some issue");
                result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "exception.stacktrace").Value.Should().BeNull();
                break;
            case ExceptionDetailLevel.StackTrace:
                result.Components.Single().Value.Details.Count.Should().Be(3);
                result.Components.Single().Value.Details.First(x => x.Key == "exception.message").Value.Should().StartWith("some issue");
                result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "exception.stacktrace").Value.Should().StartWith("   at");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(option.ExceptionDetail), option.ExceptionDetail, null);
        }
    }

    [Theory]
    [InlineData(true, true, HealthStatus.Healthy)]
    [InlineData(false, true, HealthStatus.Unhealthy)]
    [InlineData(true, false, HealthStatus.Healthy)]
    [InlineData(false, false, HealthStatus.Degraded)]
    public async Task OneComponentFromService(bool success, bool essential, HealthStatus expectedStatus)
    {
        //Arrange
        var message = new Faker().Random.AlphaNumeric(12);
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponentService<OneComponentService>();
        _serviceProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns(new OneComponentService("One", success, essential, message));
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(expectedStatus);
        result.Components.Single().Key.Should().Be("One");
        result.Components.Single().Value.Status.Should().Be(expectedStatus);
        result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.Single().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
    }

    [Theory]
    [InlineData(true, true, HealthStatus.Healthy)]
    [InlineData(false, true, HealthStatus.Unhealthy)]
    [InlineData(true, false, HealthStatus.Healthy)]
    [InlineData(false, false, HealthStatus.Degraded)]
    public async Task TwoComponentFromServiceAndManual(bool success, bool essential, HealthStatus expectedStatus)
    {
        //Arrange
        var message = new Faker().Random.AlphaNumeric(12);
        var option = new Quilt4NetHealthApiOptions();
        var component = new Component
        {
            Name = "Other",
            Essential = essential,
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = success, Message = message }),
        };
        option.AddComponent(component);
        option.AddComponentService<OneComponentService>();
        _serviceProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns(new OneComponentService("One", success, essential, message));
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(expectedStatus);
        result.Components.First().Key.Should().Be("One");
        result.Components.First().Value.Status.Should().Be(expectedStatus);
        result.Components.First().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.First().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
        result.Components.Last().Key.Should().Be("Other");
        result.Components.Last().Value.Status.Should().Be(expectedStatus);
        result.Components.Last().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.Last().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
    }

    [Fact]
    public async Task ComponentsWithSameName()
    {
        //Arrange
        var message = new Faker().Random.AlphaNumeric(12);
        var option = new Quilt4NetHealthApiOptions();
        var component = new Component
        {
            Name = "One",
            Essential = true,
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true, Message = message }),
        };
        option.AddComponent(component);
        option.AddComponentService<OneComponentService>();
        _serviceProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns(new OneComponentService("One", true, true, message));
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Components.First().Key.Should().Be("One.0");
        result.Components.First().Value.Status.Should().Be(HealthStatus.Healthy);
        result.Components.First().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.First().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
        result.Components.Last().Key.Should().Be("One.1");
        result.Components.Last().Value.Status.Should().Be(HealthStatus.Healthy);
        result.Components.Last().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.Last().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ComponentWithNoName(string name)
    {
        //Arrange
        var message = new Faker().Random.AlphaNumeric(12);
        var option = new Quilt4NetHealthApiOptions();
        var component = new Component
        {
            Name = name,
            Essential = true,
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true, Message = message }),
        };
        option.AddComponent(component);
        option.AddComponentService<OneComponentService>();
        _serviceProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns(new OneComponentService(name, true, true, message));
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Components.First().Key.Should().Be("Component.0");
        result.Components.First().Value.Status.Should().Be(HealthStatus.Healthy);
        result.Components.First().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.First().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
        result.Components.Last().Key.Should().Be("Component.1");
        result.Components.Last().Value.Status.Should().Be(HealthStatus.Healthy);
        result.Components.Last().Value.Details.FirstOrDefault(x => x.Key == "elapsed").Value.Should().NotBeNull();
        result.Components.Last().Value.Details.FirstOrDefault(x => x.Key == "message").Value.Should().Be(message);
    }

    [Fact]
    public async Task ManyWithSameName()
    {
        //Arrange
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponentService<ManyComponentService>();
        _serviceProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns(new ManyComponentService("A", 5));
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Components.First().Key.Should().Be("A.0");
        result.Components.Where(x => x.Key.StartsWith("A.")).Should().HaveCount(5);
        _ = result.Components.Select((x, i) => x.Key.Should().Be($"A.{i}")).ToArray();
    }

    [Fact]
    public async Task ChecksShouldRunInParallel()
    {
        //Arrange
        var sw = Stopwatch.StartNew();
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponentService<ManyComponentService>();
        _serviceProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns(new ManyComponentService("A", 10, TimeSpan.FromSeconds(1)));
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = (await sut.GetStatusAsync(null, true, CancellationToken.None).ToArrayAsync()).ToHealthResponse();

        //Assert
        result.Components.Count.Should().Be(10);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ChecksShouldBeStreamed()
    {
        //Arrange
        var sw = Stopwatch.StartNew();
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "Slow",
            CheckAsync = async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                return new CheckResult { Success = true, Message = "Slow component." };
            }
        });
        option.AddComponent(new Component { Name = "Fast", CheckAsync = _ => Task.FromResult(new CheckResult { Success = true, Message = "Fast component." }) });
        var sut = new HealthService(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object);

        //Act
        var result = await sut.GetStatusAsync(null, true, CancellationToken.None).FirstAsync();

        //Assert
        result.Key.Should().Be("Fast");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}