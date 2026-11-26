var builder = WebApplication.CreateBuilder(args);
const string connectionString = "unused";
PostgreSqlBlock.Register(builder, connectionString);
PostgreSqlNoDdlBlock.Register(builder, connectionString);
SqlServerBlock.Register(builder, connectionString);
MediatRBlock.Register(builder);
InstrumentationBlock.Register(builder);
TenantBlock.Register(builder);
Net8DocumentPathBlock.Register(builder);
