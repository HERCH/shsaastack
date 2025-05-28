using Application.Interfaces;
using Common;

namespace Infrastructure.External.IntegrationTests;

public class TestCaller : ICallerContext
{
    public TestCaller(string? tenantId = null)
    {
        TenantId = tenantId;
    }

    public Optional<ICallerContext.CallerAuthorization> Authorization =>
        Optional<ICallerContext.CallerAuthorization>.None;

    public string CallerId => "acallerid";

    public string CallId => "acallid";

    public ICallerContext.CallerFeatures Features => new();

    public DatacenterLocation HostRegion => DatacenterLocations.Local;

    public bool IsAuthenticated => false;

    public bool IsServiceAccount => false;

    public ICallerContext.CallerRoles Roles => new();

    public Optional<string> TenantId { get; }
}