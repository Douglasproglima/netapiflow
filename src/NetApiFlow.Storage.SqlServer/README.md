# NetApiFlow.Storage.SqlServer

Persists NetApiFlow traces to SQL Server instead of memory, with automatic schema creation, or with
the schema created by you from `schema.sql` when `AutoCreateSchema` is `false`.

The registration is in the namespace `NetApiFlow.Storage.SqlServer`. Call it after `AddNetApiFlow`:

```csharp
using NetApiFlow.Storage.SqlServer;

builder.Services.AddNetApiFlowSqlServerStorage(connectionString);
```

`schema.sql` is not inside this package. It is in the repository, at the tag of the version you
installed: `https://github.com/Douglasproglima/netapiflow/tree/v<version>/src/NetApiFlow.Storage.SqlServer`.
If you changed `TablePrefix`, replace `netapiflow_` in the script first.

This package depends on `Microsoft.Data.SqlClient` 5.2.2. With that version,
`ExternalActivitySource.SqlClient` records no SQL command: the driver reports its events in a form
NetApiFlow cannot read, and the host logs that once at `Information`. To record SQL commands, add
`Microsoft.Data.SqlClient` 6.0 or later to your own project (6.1.8 was tested).

The retention (`Retention.MaxAge`) is applied by a background pass that runs every 5 minutes, and the
first pass comes 5 minutes after the app starts.

Install it at the same version as `NetApiFlow`: this package asks for the core at exactly its own version.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and storage configuration.
