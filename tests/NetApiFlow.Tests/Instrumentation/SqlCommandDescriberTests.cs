using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Instrumentation;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class SqlCommandDescriberTests
{
    [Theory]
    [InlineData("SELECT u.\"Id\" FROM \"Users\" AS u WHERE u.\"Email\" = $1", "SELECT Users")]
    [InlineData("INSERT INTO \"Orders\" (\"Id\", \"Total\") VALUES ($1, $2)", "INSERT Orders")]
    [InlineData("UPDATE \"Orders\" SET \"Total\" = $1 WHERE \"Id\" = $2", "UPDATE Orders")]
    [InlineData("DELETE FROM \"Orders\" WHERE \"Id\" = $1", "DELETE Orders")]
    [InlineData("CREATE TABLE \"Users\" (\"Id\" integer)", "SQL Unknown")]
    public void Describe_WithCommandText_MatchesTheEntityFrameworkCoreInterceptor(string commandText, string expected)
    {
        SqlCommandDescriber.Describe(commandText).ShouldBe(expected);
        SqlCommandDescriber.Describe(commandText).ShouldBe(FlowDbCommandInterceptor.DescribeCommand(commandText));
    }
}
