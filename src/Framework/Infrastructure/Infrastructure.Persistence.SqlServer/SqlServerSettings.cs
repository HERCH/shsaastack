using Common;
using Common.Configuration;
using Common.Extensions;

namespace Infrastructure.Persistence.SqlServer;

/// <summary>
///     Provides settings for SQL Server persistence
/// </summary>
public class SqlServerSettings
{
    public SqlServerSettings()
    {
        ConnectionString = string.Empty;
        CommandTimeout = 30;
    }

    public string ConnectionString { get; set; }

    public int CommandTimeout { get; set; }

    public static SqlServerSettings LoadFromSettings(IConfigurationSettings settings)
    {
        var sqlServerSettings = new SqlServerSettings();
        settings.Platform.GetSection("ApplicationServices:Persistence:SqlServer").Bind(sqlServerSettings);
        return sqlServerSettings;
    }

    public Result<Error> Validate()
    {
        if (ConnectionString.IsInvalidParameter(value => value.HasValue(), nameof(ConnectionString),
                Resources.SqlServerStore_MissingConnectionString, out var error))
        {
            return error;
        }

        return Result.Ok;
    }
}
