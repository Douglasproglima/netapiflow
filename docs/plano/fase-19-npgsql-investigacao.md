# Fase 19: Interop Npgsql (R-06)

Projetos tocados: `src/NetApiFlow` (núcleo: `FlowActivityListener`, `ExternalActivitySource`),
`tests/NetApiFlow.IntegrationTests`. `tests/NetApiFlow.NpgsqlInteropSpike` (tarefa 19.1, já
concluída) é descartável, sem relação com as tarefas de produção abaixo.

Pré-requisito: Fase 2 aceita (`NetApiFlow.EntityFrameworkCore`, `FlowDbCommandInterceptor`).
Último item do "ataque aos riscos em aberto" (D-88), depois da Fase 18. R-06 estava aberto desde
a Fase 2, nunca investigado de verdade até esta fase.

Contrato congelado em `docs/plano/contratos.md`: `ExternalActivitySource.Npgsql`. Decisão de
arquitetura (dedup por cadeia de `Parent`, decidida em `Sample`, curadoria de tag) travada em
ADR-0018/D-92, não renegociável dentro desta fase.

## Tarefa 19.1: Spike de investigação, concluída (D-92)

Já rodada e relatada. Resultado resumido (detalhe completo em D-92 e ADR-0018):

- A fonte `"Npgsql"` emite `Activity` sozinha, sem nenhum pacote extra (premissa original do
  risco estava errada).
- Cadeia de `Parent` estável sob pool de conexão, multiplexing e concorrência real
  (`Task.WhenAll`), `net8.0` e `net10.0`, mais de 3800 `Activity` observadas, zero divergência.
- `db.statement` não vaza valor de parâmetro quando o EF Core parametriza de verdade; um valor de
  `const`/`static readonly` vira literal embutido no SQL pelo próprio EF Core, mesmo risco de
  sempre do `CaptureSqlText`, não um vazamento novo.
- Sem `NetApiFlow.EntityFrameworkCore`, a `Activity` nativa não tem `Parent` da fonte
  `"NetApiFlow"`: nada a deduplicar nesse caso, ingestão direta.

`tests/NetApiFlow.NpgsqlInteropSpike`: remover a `PackageReference`/`PackageVersion` de
`Npgsql.OpenTelemetry` e o override de `OpenTelemetry.Api` em `Directory.Packages.props` (a
premissa que justificava os dois caiu). O projeto continua existindo como spike descartável, sem
precisar ser apagado: documenta a investigação, não precisa virar teste permanente.

## Tarefa 19.2: `ExternalActivitySource.Npgsql` e dedup em `Sample`

`ExternalActivitySource` ganha `Npgsql = 2` (`[Flags]`, mesma forma do contrato).
`FlowActivityListener.ShouldListenTo` passa a observar `"Npgsql"` quando o flag está ligado,
mesmo padrão de `"System.Net.Http"` (ADR-0012).

Dedup acontece em `Sample`, não em `ActivityStarted` (mesmo princípio de custo de R-05): quando
`options.Source.Name == "Npgsql"` e `Activity.Current?.Source.Name == FlowActivitySource.Name`,
devolver `ActivitySamplingResult.None`. Sem esse `Parent`, seguir a amostragem normal (tracing
ligado + trace ativo, mesma checagem que já existe para as outras fontes).

**Pronto quando:** teste unitário cobre a decisão de `Sample` isoladamente (sem precisar de
Postgres real): com um `Activity.Current` da fonte `"NetApiFlow"` ativo, uma `Activity` criada na
fonte `"Npgsql"` recebe `ActivitySamplingResult.None`; sem esse `Parent`, recebe
`AllDataAndRecorded` (com trace ativo e tracing ligado).

## Tarefa 19.3: `BuildNpgsqlSpan`/`CompleteNpgsqlSpan`

Espelhar a estrutura já usada para `BuildHttpClientSpan`/`CompleteHttpClientSpan`
(`FlowActivityListener.cs`): nome genérico no `ActivityStarted`, nome real finalizado no
`ActivityStopped`. `Component = "Npgsql"`, `Type = ComponentType.Database`. `Operation` derivado
de `db.statement` por um método interno equivalente a
`FlowDbCommandInterceptor.DescribeCommand` ("{CommandType} {MainTable}" via regex, mesma forma,
duplicado no núcleo porque o núcleo não pode depender do pacote `NetApiFlow.EntityFrameworkCore`).
Status de erro via `Activity.Status`/`ActivityStatusCode` nativo do .NET (não parsing de tag),
mesmo mecanismo que `FlowStep.MarkError` já usa.

Curadoria de tag, não cópia: `FlowSpan.Tags["sql"]` só recebe o `db.statement` cru quando
`NetApiFlowOptions.CaptureSqlText` está ligado, mesma régua do `FlowDbCommandInterceptor`.
Nenhuma outra tag nativa (`db.connection_string`, `db.user`, `net.peer.ip`, `net.peer.port`,
etc.) é copiada para `FlowSpan`, em nenhuma circunstância.

**Pronto quando:** teste de integração com Postgres real (Testcontainers) cobre: consulta EF Core
com `NetApiFlow.EntityFrameworkCore` ativo e `ExternalActivitySource.Npgsql` ligado não gera span
duplicado (só o do `FlowDbCommandInterceptor` aparece); consulta Npgsql puro (sem EF Core) gera
exatamente um span `Database`/`"Npgsql"` com `Operation` correta; `CaptureSqlText = false` (padrão)
não grava `sql` em `Tags`, `CaptureSqlText = true` grava; uma consulta que falha de verdade (ex.
tabela inexistente) marca o span como `FlowStatus.Error` (confirma o comportamento de
`Activity.Status` não testado pelo spike).

## Tarefa 19.4: Prova do risco mitigado (R-06)

Teste de concorrência real equivalente ao spike (`Task.WhenAll`, múltiplas consultas), agora
contra o código de produção, não o descartável: confirma que nenhum span duplicado aparece sob
carga concorrente real, mesmo rigor do D-54 (rodar em loop antes de considerar estável).

**Pronto quando:** teste descrito acima passa de forma consistente (mínimo 10 execuções em loop,
sem falha intermitente). R-06 passa de "Planejado" para "Mitigado" na revisão da Planejadora.

## Tarefa 19.5: Documentação

README e README.pt-BR.md ganham uma seção curta sobre `ExternalActivitySource.Npgsql`, mesmo
padrão da seção de `HttpClient` já existente: o que é, como ligar, a nota sobre
`CaptureSqlText` controlando o texto completo do SQL (reaproveitar a seção "Sensitive data" já
existente, só adicionar a referência). `CHANGELOG.md`, seção `[Unreleased]` → `### Added`.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente. `CHANGELOG.md`
atualizado.

## Fora de escopo, de propósito (ADR-0018)

- `NpgsqlDataSource` registrado via DI do ASP.NET, transação explícita (`BeginTransaction`),
  `NpgsqlBatch`: não cobertos pelo spike. Se a tarefa 19.3 encontrar comportamento diferente do
  esperado nesses casos durante a implementação, é achado novo, parar e relatar antes de
  prosseguir, não assumir que o mesmo padrão vale sem testar.
- MassTransit: continua fora (ADR-0012), falta de pesquisa própria, sem relação com esta fase.
- Qualquer mudança em `NetApiFlow.EntityFrameworkCore`: `FlowDbCommandInterceptor` não muda,
  continua sendo a fonte de verdade quando está ativo.

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. R-06 mitigado e provado. `tests/NetApiFlow.NpgsqlInteropSpike` sem o pacote
`Npgsql.OpenTelemetry`/override de `OpenTelemetry.Api`. Nenhuma mudança em `ITraceStore`,
`NetApiFlow.EntityFrameworkCore`, `NetApiFlow.Mcp` ou `NetApiFlow.OpenTelemetry`.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como gerar
uma consulta real com e sem `NetApiFlow.EntityFrameworkCore` para ver a dedup funcionando),
confirmação do comportamento de `Activity.Status` numa falha real (não coberto pelo spike), o que
ficou pendente. A Planejadora roda `/revisar-fase 19` antes de decidir a próxima fatia do roadmap
(`docs/plano/roadmap-valor.md`).
