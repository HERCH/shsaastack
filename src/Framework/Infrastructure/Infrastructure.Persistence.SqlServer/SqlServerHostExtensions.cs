using Common.Configuration;
using Infrastructure.Hosting.Common.Extensions;
using Infrastructure.Persistence.Interfaces;
using Infrastructure.Persistence.SqlServer.ApplicationServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence.SqlServer;

/// <summary>
///     Provides extension methods for conditionally registering SQL Server persistence services
/// </summary>
public static class SqlServerHostExtensions
{
    private const string SqlServerEnabledSettingName = "ApplicationServices:Persistence:SqlServer:Enabled";

    /// <summary>
    ///     Registers SQL Server stores if enabled in configuration
    /// </summary>
    public static void RegisterSqlServerStoreIfEnabled(this IServiceCollection services,
        IConfiguration configuration, bool isMultiTenanted)
    {
        var useSqlServer = configuration.GetValue<bool>(SqlServerEnabledSettingName);
        if (!useSqlServer)
        {
            return;
        }

        // Register SqlServerStore for IDataStore and IEventStore
        services.AddForPlatform<IDataStore, IEventStore, SqlServerStore>(c =>
            SqlServerStore.Create(SqlServerSettings.LoadFromSettings(
                c.GetRequiredServiceForPlatform<IConfigurationSettings>())));

        if (isMultiTenanted)
        {
            services.AddPerHttpRequest<IDataStore, IEventStore, SqlServerStore>(c =>
                SqlServerStore.Create(SqlServerSettings.LoadFromSettings(
                    c.GetRequiredService<IConfigurationSettings>())));
        }
        else
        {
            services.AddSingleton<IDataStore, IEventStore, SqlServerStore>(c =>
                SqlServerStore.Create(SqlServerSettings.LoadFromSettings(
                    c.GetRequiredServiceForPlatform<IConfigurationSettings>())));
        }

        // Register FileSystemBlobStore for IBlobStore
        services.AddForPlatform<IBlobStore, FileSystemBlobStore>(c =>
            FileSystemBlobStore.Create(c.GetRequiredServiceForPlatform<IConfigurationSettings>()));

        if (isMultiTenanted)
        {
            services.AddPerHttpRequest<IBlobStore, FileSystemBlobStore>(c =>
                FileSystemBlobStore.Create(c.GetRequiredService<IConfigurationSettings>()));
        }
        else
        {
            services.AddSingleton<IBlobStore, FileSystemBlobStore>(c =>
                FileSystemBlobStore.Create(c.GetRequiredServiceForPlatform<IConfigurationSettings>()));
        }
    }
}
