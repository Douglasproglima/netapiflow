# Fase 6: Storages persistentes

Projetos: `src/NetApiFlow.Storage.PostgreSql`, `src/NetApiFlow.Storage.SqlServer`. Testes:
`tests/NetApiFlow.Storage.Tests` (Testcontainers).

Pré-requisito: Fase 1 aceita (implementa `ITraceStore`, contrato em `docs/plano/contratos.md`,
seção 1). Pode rodar em paralelo com as Fases 3-5, já que não depende delas.

## Tarefas

### 6.1: Schema e criação na primeira execução

Tabelas com prefixo configurável, padrão `netapiflow_` (ex.: `netapiflow_traces`,
`netapiflow_spans`). Criação automática na primeira execução (`CREATE TABLE IF NOT EXISTS` /
`IF NOT EXISTS` equivalente) **e** um script `.sql` equivalente disponível em
`src/NetApiFlow.Storage.PostgreSql/schema.sql` / `src/NetApiFlow.Storage.SqlServer/schema.sql`,
para quem preferir migração controlada por DBA em vez de criação automática.

**Pronto quando:** rodar a aplicação pela primeira vez contra um banco vazio cria as tabelas sem
erro; rodar o script `.sql` manualmente produz o mesmo schema.

### 6.2: `NetApiFlow.Storage.PostgreSql` (D-05)

ADO.NET direto com `Npgsql`. Implementa `ITraceStore` inteira (`SaveAsync`, `GetAsync`,
`QueryAsync`, `GetComponentStatsAsync`, `GetLastSuccessfulAsync`, `ApplyRetentionAsync`).
`SaveAsync` usa `NpgsqlBinaryImporter` (`COPY`) para a escrita em lote de trace + spans.

**Pronto quando:** cada método de `ITraceStore` tem cobertura em `NetApiFlow.Storage.Tests`
(tarefa 6.5).

### 6.3: `NetApiFlow.Storage.SqlServer`

ADO.NET direto com `Microsoft.Data.SqlClient`. Mesma superfície de `ITraceStore`. `SaveAsync` usa
`SqlBulkCopy` para a escrita em lote.

**Pronto quando:** idem à tarefa 6.2, no banco SQL Server.

### 6.4: Retenção nos dois storages

`ApplyRetentionAsync` remove o que passar de `RetentionPolicy.MaxTraces` ou `MaxAge` (o que vier
primeiro), de forma consistente com a retenção em memória da Fase 1 (tarefa 1.9).

**Pronto quando:** teste insere traces antigos e novos nos dois bancos, roda a retenção, confere
que só os antigos somem e os spans associados também são removidos (sem registro órfão).

### 6.5: `NetApiFlow.Storage.Tests` com Testcontainers

Container PostgreSQL e container SQL Server, um `Fixture` xUnit por banco (`IAsyncLifetime` ou
`ICollectionFixture`, subindo o container uma vez por classe/coleção, não por teste).

**Pronto quando:** suíte completa verde no CI (o `ci.yml` da Fase 0 precisa ter Docker disponível
no runner `ubuntu-latest`. Confirmar que o runner `windows-latest` roda só os testes que não
exigem Testcontainers, ou pular esse projeto de teste no Windows se Docker não estiver
disponível lá, documentando a decisão no resumo da fase).

## Aceite da fase

Testcontainers verdes para os dois bancos. Retenção removendo traces antigos nos dois.

## Ao terminar

Resumo para a Planejadora, incluindo em qual(is) sistema(s) operacionais do CI os testes de
Testcontainers rodam de fato. `/revisar-fase 6` confere rodando a suíte localmente antes de
liberar a Fase 7.
