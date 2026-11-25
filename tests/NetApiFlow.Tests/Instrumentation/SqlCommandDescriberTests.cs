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
    [InlineData("SELECT Id FROM dbo.Users WHERE Id = @id", "SELECT Users")]
    [InlineData("SELECT [u].[Id] FROM [dbo].[Users] AS [u]", "SELECT Users")]
    [InlineData("SELECT Id FROM Users WHERE Id = @id", "SELECT Users")]
    [InlineData("SELECT id FROM public.orders WHERE id = $1", "SELECT orders")]
    [InlineData("SELECT o.\"Id\" FROM \"public\".\"orders\" AS o", "SELECT orders")]
    [InlineData("SELECT o.\"Id\" FROM public.\"orders\" AS o", "SELECT orders")]
    [InlineData("SELECT Id FROM master.dbo.Users", "SELECT Users")]
    [InlineData("SELECT Id FROM [master] . [dbo] . [Users]", "SELECT Users")]
    [InlineData("INSERT INTO [dbo].[Orders] ([Total]) VALUES (@p0)", "INSERT Orders")]
    [InlineData("UPDATE dbo.Orders SET Total = @p0 WHERE Id = @p1", "UPDATE Orders")]
    [InlineData("DELETE FROM `shop`.`orders` WHERE id = ?", "DELETE orders")]
    [InlineData("SELECT Id FROM dbo.Users WHERE Email = 'someone@example.com'", "SELECT Users")]
    public void Describe_WithCommandText_MatchesTheEntityFrameworkCoreInterceptor(string commandText, string expected)
    {
        SqlCommandDescriber.Describe(commandText).ShouldBe(expected);
        SqlCommandDescriber.Describe(commandText).ShouldBe(FlowDbCommandInterceptor.DescribeCommand(commandText));
    }
}
