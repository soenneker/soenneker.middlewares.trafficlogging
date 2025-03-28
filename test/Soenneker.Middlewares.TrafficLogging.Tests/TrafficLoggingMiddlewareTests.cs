using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Middlewares.TrafficLogging.Tests;

[Collection("Collection")]
public class TrafficLoggingMiddlewareTests : FixturedUnitTest
{
    public TrafficLoggingMiddlewareTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
    }

    [Fact]
    public void Default()
    {

    }
}
