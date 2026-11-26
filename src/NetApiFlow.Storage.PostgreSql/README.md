# NetApiFlow.Storage.PostgreSql

Persists NetApiFlow traces to PostgreSQL instead of memory, with automatic schema creation, or with
the schema created by you from `schema.sql` when `AutoCreateSchema` is `false`.

The registration is in the namespace `NetApiFlow.Storage.PostgreSql`. Call it after `AddNetApiFlow`:

```csharp
using NetApiFlow.Storage.PostgreSql;

builder.Services.AddNetApiFlowPostgreSqlStorage(connectionString);
```

`schema.sql` is not inside this package. It is in the repository, at the tag of the version you
installed: `https://github.com/Douglasproglima/netapiflow/tree/v<version>/src/NetApiFlow.Storage.PostgreSql`.
If you changed `TablePrefix`, replace `netapiflow_` in the script first.

The retention (`Retention.MaxAge`) is applied by a background pass that runs every 5 minutes, and the
first pass comes 5 minutes after the app starts.

Install it at the same version as `NetApiFlow`: this package asks for the core at exactly its own version.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and storage configuration.
