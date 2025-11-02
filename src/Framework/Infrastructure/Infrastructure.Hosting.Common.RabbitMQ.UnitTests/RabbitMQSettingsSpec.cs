using Common.Configuration;
using FluentAssertions;
using Infrastructure.Hosting.Common.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Infrastructure.Hosting.Common.RabbitMQ.UnitTests;

[Trait("Category", "Unit")]
public class RabbitMQSettingsSpec
{
    [Fact]
    public void WhenCreated_ThenDefaultValuesAreSet()
    {
        // Arrange & Act
        var settings = new RabbitMQSettings();

        // Assert
        settings.HostName.Should().BeEmpty();
        settings.Port.Should().Be(5672);
        settings.Username.Should().BeEmpty();
        settings.Password.Should().BeEmpty();
        settings.VirtualHost.Should().Be("/");
    }

    [Fact]
    public void WhenValidate_GivenMissingHostName_ThenReturnsError()
    {
        // Arrange
        var settings = new RabbitMQSettings
        {
            HostName = string.Empty,
            Username = "user",
            Password = "pass"
        };

        // Act
        var result = settings.Validate();

        // Assert
        result.Should().BeError();
    }

    [Fact]
    public void WhenValidate_GivenMissingUsername_ThenReturnsError()
    {
        // Arrange
        var settings = new RabbitMQSettings
        {
            HostName = "localhost",
            Username = string.Empty,
            Password = "pass"
        };

        // Act
        var result = settings.Validate();

        // Assert
        result.Should().BeError();
    }

    [Fact]
    public void WhenValidate_GivenMissingPassword_ThenReturnsError()
    {
        // Arrange
        var settings = new RabbitMQSettings
        {
            HostName = "localhost",
            Username = "user",
            Password = string.Empty
        };

        // Act
        var result = settings.Validate();

        // Assert
        result.Should().BeError();
    }

    [Fact]
    public void WhenValidate_GivenAllRequiredValues_ThenReturnsSuccess()
    {
        // Arrange
        var settings = new RabbitMQSettings
        {
            HostName = "localhost",
            Port = 5672,
            Username = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        // Act
        var result = settings.Validate();

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void WhenLoadFromSettings_GivenValidConfiguration_ThenLoadsSettings()
    {
        // Arrange
        var configurationData = new Dictionary<string, string>
        {
            { "ApplicationServices:RabbitMQ:HostName", "testhost" },
            { "ApplicationServices:RabbitMQ:Port", "5673" },
            { "ApplicationServices:RabbitMQ:Username", "testuser" },
            { "ApplicationServices:RabbitMQ:Password", "testpass" },
            { "ApplicationServices:RabbitMQ:VirtualHost", "/test" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData!)
            .Build();

        var configSettings = Mock.Of<IConfigurationSettings>(cs => cs.Platform == configuration);

        // Act
        var settings = RabbitMQSettings.LoadFromSettings(configSettings);

        // Assert
        settings.HostName.Should().Be("testhost");
        settings.Port.Should().Be(5673);
        settings.Username.Should().Be("testuser");
        settings.Password.Should().Be("testpass");
        settings.VirtualHost.Should().Be("/test");
    }
}
