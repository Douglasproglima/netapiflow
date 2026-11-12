# ADR-0017: Correlação de log e exceção por etapa, via `ILoggerProvider` opt-in

## Status
Aceito

## Discussion
[#189](https://github.com/Douglasproglima/netapiflow/discussions/189)

## Contexto
D-58 item 5 registrou "log e exceção correlacionados por etapa (escopo do `ILogger` dentro do
span)" como item do roadmap pós `0.1.0`. A dor real: hoje, quando uma etapa falha, o dashboard
mostra `ErrorType`/`ErrorMessage` (via `FlowStep.MarkError`, chamada explícita do usuário) mas
nenhuma das mensagens de log que o próprio código já escreve via `ILogger` durante aquela etapa.
Quem investiga um erro acaba abrindo dois lugares (o dashboard e o log estruturado da aplicação)
para montar o quadro completo.

Isso é um mecanismo de captura novo, diferente do que o núcleo já usa (ADR-0002,
`ActivitySource`/`ActivityListener`): em vez de observar `Activity`, observa chamada de
`ILogger.Log(...)`, via um `ILoggerProvider` registrado na pipeline de logging padrão do
ASP.NET Core, lado a lado com os provedores que a aplicação já tem (Console, Debug, Application
Insights, Serilog, etc.), sem substituir nenhum.

Tensão real com `.claude/rules/tracing.md`: "Tags são só as explicitamente adicionadas via
`FlowStep.Tag(...)`" e "nenhum span ou trace grava corpo de requisição/resposta... em nenhuma
circunstância, nem atrás de flag de configuração". Mensagem de log é texto livre, escrito pelo
código do usuário, que pode conter qualquer coisa, inclusive o mesmo tipo de dado sensível que
essas regras protegem. A regra de tags-só-explícitas é sobre **tags estruturadas automáticas**
(ex. não inventar uma tag com o corpo da requisição); não proíbe uma feature nova, deliberada,
com sua própria governança de segurança, desde que seja um campo separado (não uma extensão
silenciosa de `Tags`).

## Decisão
`FlowSpan` ganha um campo novo, `Logs: IReadOnlyList<LogEntry>` (nunca populando `Tags`, feature
separada, registrada em `contratos.md`). `NetApiFlowOptions` ganha `LogCorrelationOptions
LogCorrelation { get; }`:

```csharp
public sealed class LogCorrelationOptions
{
    public bool Enabled { get; set; } // padrão false, opt-in explícito
    public LogLevel MinLevel { get; set; } = LogLevel.Warning; // padrão: só Warning e acima
}
```

Opt-in por padrão (`Enabled = false`): confirmado com o usuário antes desta ADR (D-85), mesma
régua de outras features que observam mais do app do que o mínimo (`ExternalSources`,
`IncludeHttpClientSource`, `TailSampling`). `MinLevel` padrão `Warning`: reduz volume e ruído,
foca no que ajuda a diagnosticar (confirmado com o usuário).

Mecanismo: um `ILoggerProvider` interno (`FlowLoggerProvider`/`FlowLogger`), registrado via
`services.AddSingleton<ILoggerProvider, FlowLoggerProvider>()` dentro de `AddNetApiFlow(...)`
só quando `LogCorrelation.Enabled` é `true`. A cada chamada de log que atinja `MinLevel`, se
`Activity.Current` existir e pertencer à fonte `"NetApiFlow"` (span real, não qualquer `Activity`
do processo), o provider resolve `TraceId`/`SpanId` do `Activity.Current` e chama
`FlowRecorder.AddLogEntry(traceId, spanId, entry)`, com `entry` já sanitizado.

Sanitização: **reaproveita `ErrorMessageSanitizer`** (o mesmo já usado por
`FlowStep.MarkError`), aplicado à mensagem de log formatada e à mensagem da exceção (quando a
chamada de log inclui uma). Nenhuma sanitização nova é inventada para esta feature; a mitigação
do risco de dado sensível em texto livre é a mesma já testada e em produção desde a Fase 1.

Limite novo, mesmo molde dos seis limites da seção 6.7 do prompt: `LimitsOptions` ganha
`MaxLogEntriesPerSpan` (padrão 20). Entrada além do limite é descartada e contada
(`netapiflow.logs.dropped`, `Meter("NetApiFlow")`), nunca lança exceção.

`LogEntry` nunca é gravado fora de um `Activity` da fonte `"NetApiFlow"`: log emitido fora de
qualquer etapa rastreada (startup, background job não instrumentado) não vira `LogEntry` de
span nenhum, simplesmente não é observado por este mecanismo.

## Consequências
- Risco residual aceito conscientemente (D-85): sanitização por regex nunca é garantia absoluta
  contra todo tipo de dado sensível em texto livre, diferente de tag estruturada onde o próprio
  programador decide o que entra. Mitigado por opt-in explícito, `MinLevel` reduzindo volume, e
  o mesmo sanitizer já testado. Documentado no README como trade-off consciente, não escondido.
- `Tags` continua sendo só o que o usuário adiciona explicitamente via `FlowStep.Tag(...)`; esta
  ADR não expande o que entra em `Tags`, cria um campo novo e paralelo.
- Nenhuma mudança em `ITraceStore`: `LogEntry` é parte de `FlowSpan`, passa pelos mesmos métodos
  (`SaveAsync`, `GetAsync`, etc.) sem precisar de método novo.
- `NetApiFlow.Mcp` e `NetApiFlow.OpenTelemetry` recebem o campo automaticamente via `FlowSpan`
  (MCP) ou não recebem nada (OTLP exporta a `Activity` nativa, que não carrega `LogEntry`; ver
  nota em `docs/plano/fase-17-log-correlacionado.md` sobre isso ficar fora de escopo desta fase).
