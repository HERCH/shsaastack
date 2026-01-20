using Infrastructure.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Web.Hosting.Common.Extensions;

/// <summary>
///     Provides extension methods for conditionally registering SQL Server services
/// </summary>
public static class SqlServerHostExtensions
{
    /// <summary>
    ///     Registers SQL Server stores if enabled in configuration
    /// </summary>
    public static void RegisterSqlServerStoreIfEnabled(this IServiceCollection services,
        IConfiguration configuration, bool isMultiTenanted)
    {
        services.RegisterSqlServerStoreIfEnabled(configuration, isMultiTenanted);
    }
}
