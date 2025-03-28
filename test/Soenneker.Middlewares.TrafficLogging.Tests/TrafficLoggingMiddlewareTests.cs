using Soenneker.Middlewares.TrafficLogging.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Middlewares.TrafficLogging.Tests;

[Collection("Collection")]
public class TrafficLoggingMiddlewareTests : FixturedUnitTest
{
    private readonly ITrafficLoggingMiddleware _util;

    public TrafficLoggingMiddlewareTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<ITrafficLoggingMiddleware>(true);
    }

    [Fact]
    public void Default()
    {

    }
}
