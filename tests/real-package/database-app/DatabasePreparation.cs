using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;

public static partial class DatabasePreparation
{
    private const string DefaultTablePrefix = "netapiflow_";

    public static async Task PrepareAsync(DatabaseSettings settings)
    {
        if (settings.SchemaScriptPath is not null)
        {
            var script = (await File.ReadAllTextAsync(settings.SchemaScriptPath)).Replace(DefaultTablePrefix, settings.TablePrefix, StringComparison.Ordinal);
            foreach (var batch in BatchSeparator().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch)))
            {
                await ExecuteAsync(settings, batch);
            }
        }

        await ExecuteAsync(settings, settings.Kind == "postgres" ? PostgresTables : SqlServerTables);
    }

    private static async Task ExecuteAsync(DatabaseSettings settings, string commandText)
    {
        if (settings.Kind == "postgres")
        {
            await using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
            await using var command = dataSource.CreateCommand(commandText);
            await command.ExecuteNonQueryAsync();
            return;
        }

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var sqlCommand = new SqlCommand(commandText, connection);
        await sqlCommand.ExecuteNonQueryAsync();
    }

    private const string PostgresTables =
        "CREATE TABLE IF NOT EXISTS users (id int PRIMARY KEY); CREATE TABLE IF NOT EXISTS orders (id int PRIMARY KEY);";

    private const string SqlServerTables =
        "IF OBJECT_ID('dbo.users', 'U') IS NULL CREATE TABLE dbo.users (id int PRIMARY KEY); IF OBJECT_ID('dbo.orders', 'U') IS NULL CREATE TABLE dbo.orders (id int PRIMARY KEY);";

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline)]
    private static partial Regex BatchSeparator();
}
