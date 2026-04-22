using Soenneker.Tests.HostedUnit;

namespace Soenneker.Middlewares.TrafficLogging.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public class TrafficLoggingMiddlewareTests : HostedUnitTest
{
    public TrafficLoggingMiddlewareTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
