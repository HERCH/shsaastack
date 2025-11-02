using Common;
using Common.Configuration;
using Common.Extensions;

namespace Infrastructure.Hosting.Common.RabbitMQ;

/// <summary>
///     Provides settings for RabbitMQ
/// </summary>
public class RabbitMQSettings : ISettings
{
    public RabbitMQSettings()
    {
        HostName = string.Empty;
        Port = 5672;
        Username = string.Empty;
        Password = string.Empty;
        VirtualHost = "/";
    }

    public string HostName { get; set; }

    public string Password { get; set; }

    public int Port { get; set; }

    public string Username { get; set; }

    public string VirtualHost { get; set; }

    public static RabbitMQSettings LoadFromSettings(IConfigurationSettings settings)
    {
        var rabbitMQSettings = new RabbitMQSettings();
        settings.Platform.GetSection("ApplicationServices:RabbitMQ").Bind(rabbitMQSettings);
        return rabbitMQSettings;
    }

    public Result<Error> Validate()
    {
        if (HostName.IsInvalidParameter(value => value.HasValue(), nameof(HostName),
                Resources.RabbitMQStore_MissingConnectionString, out var error1))
        {
            return error1;
        }

        if (Username.IsInvalidParameter(value => value.HasValue(), nameof(Username),
                Resources.RabbitMQStore_MissingConnectionString, out var error2))
        {
            return error2;
        }

        if (Password.IsInvalidParameter(value => value.HasValue(), nameof(Password),
                Resources.RabbitMQStore_MissingConnectionString, out var error3))
        {
            return error3;
        }

        return Result.Ok;
    }
}
