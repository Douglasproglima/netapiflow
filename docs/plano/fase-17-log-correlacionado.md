# Fase 17: Log e exceção correlacionados por etapa

Projetos tocados: `src/NetApiFlow` (núcleo, `FlowSpan`, `NetApiFlowOptions`, `LimitsOptions`, novo
`ILoggerProvider`), `src/NetApiFlow.Dashboard` (span details panel), `src/NetApiFlow.Mcp` (campo
chega de graça via `FlowSpan`, só conferir paridade).

Pré-requisito: Fase 1 aceita (`FlowStep`/`Activity`, ADR-0002). Item 5 do roadmap D-58, topo do
ranking de valor de entrega depois da Fase 16 (D-85).

Contrato congelado em `docs/plano/contratos.md`: `LogEntry`, `FlowSpan.Logs`,
`LogCorrelationOptions`, `LimitsOptions.MaxLogEntriesPerSpan`. Decisões de segurança e escopo
travadas em ADR-0017 e D-85 (opt-in, `MinLevel Warning`, reaproveitar `ErrorMessageSanitizer`),
não renegociáveis dentro desta fase. **Confirmadas com o usuário antes do ADR**, por tocarem
diretamente a regra dura de `.claude/rules/tracing.md` sobre tags só explícitas e nunca capturar
dado sensível.

## Contexto (ler antes de implementar)

Hoje `FlowStep.MarkError(exception)` grava `ErrorType`/`ErrorMessage` no span, mas só quando o
usuário chama essa API explicitamente. Nenhuma mensagem de log comum (`_logger.LogWarning(...)`,
`_logger.LogError(ex, ...)`) escrita durante a etapa chega ao dashboard hoje. Esta fase fecha essa
lacuna: toda chamada de `ILogger` que aconteça enquanto um span `"NetApiFlow"` está ativo (via
`Activity.Current`), a partir do `MinLevel` configurado, vira um `LogEntry` dentro daquele span.

**É um mecanismo de captura novo**, paralelo ao `ActivityListener` que o núcleo já usa (ADR-0002):
um `ILoggerProvider` interno, registrado ao lado dos provedores de log que a aplicação já tem
(nunca os substitui, nunca impede o log de ir pro destino de sempre do usuário).

## Fora de escopo, de propósito (D-85, ADR-0017)

- Nenhuma mudança em `ITraceStore`: `LogEntry` é parte de `FlowSpan`, passa pelos métodos que já
  existem.
- Nenhuma propagação para `NetApiFlow.OpenTelemetry`: a exportação lê a `Activity` nativa
  (ADR-0016), que não carrega `LogEntry`. `Activity.AddEvent` seria o caminho natural se pedido
  depois, fora de escopo agora.
- Nenhuma tool nova em `NetApiFlow.Mcp`: o campo `logs` chega de graça em `FlowSpan`, só confirmar
  que o DTO da tool `get_trace` não filtra o campo por acidente.
- Nenhuma sanitização nova: reaproveita `ErrorMessageSanitizer` tal como está. Se ele se mostrar
  insuficiente para texto de log de verdade, é achado para relatar à Planejadora, não motivo para
  inventar um sanitizer próprio dentro desta fase.

## Tarefas

### 17.1: `LogEntry`, `FlowSpan.Logs`, `LogCorrelationOptions`, `LimitsOptions.MaxLogEntriesPerSpan`

Novo `src/NetApiFlow/Contracts/LogEntry.cs` (seguindo a convenção de pasta de D-84). `FlowSpan`
ganha `IReadOnlyList<LogEntry> Logs { get; init; } = []`. Novo
`src/NetApiFlow/Configuration/LogCorrelationOptions.cs`. `NetApiFlowOptions` ganha
`LogCorrelationOptions LogCorrelation { get; } = new();`. `LimitsOptions` ganha
`MaxLogEntriesPerSpan` (padrão 20). `NetApiFlowOptionsValidator` não precisa de checagem nova
(`MinLevel` é um enum existente do `Microsoft.Extensions.Logging`, sempre um valor válido;
`MaxLogEntriesPerSpan <= 0` segue o mesmo padrão de erro de `MaxSpansPerTrace`, adicionar a
checagem).

**Pronto quando:** `dotnet build` limpo nos dois TFMs, forma exatamente igual ao congelado em
`contratos.md`.

### 17.2: `FlowLoggerProvider`/`FlowLogger`

Novo `src/NetApiFlow/Instrumentation/FlowLoggerProvider.cs` (`ILoggerProvider`) e
`FlowLogger.cs` (`ILogger`). A cada chamada de log que atinja `LogCorrelation.MinLevel`:
verificar `Activity.Current is { } activity && activity.Source.Name == "NetApiFlow"` (mesmo nome
já usado por `FlowActivitySource`); se verdadeiro, montar um `LogEntry` (timestamp UTC,
`level.ToString()`, mensagem formatada via o `Func<TState, Exception?, string>` padrão do
`ILogger`, `ExceptionType`/`ExceptionMessage` quando a chamada incluiu uma exceção) e chamar
`FlowRecorder.AddLogEntry(activity.TraceId.ToHexString(), activity.SpanId.ToHexString(), entry)`
(novo método em `FlowRecorder`/`ActiveTrace`, mesmo padrão de `SetTag`, bounded por
`MaxLogEntriesPerSpan`, descarta e conta além do limite, novo contador
`FlowMetrics.DroppedLogs` → `netapiflow.logs.dropped`). Mensagem e mensagem de exceção passam
por `ErrorMessageSanitizer.Sanitize(...)` antes de entrar no `LogEntry` (mesmo sanitizador,
mesmo truncamento de 300 caracteres).

**Pronto quando:** teste unitário cobre: log abaixo de `MinLevel` não vira `LogEntry`; log numa
`Activity` que não é da fonte `"NetApiFlow"` (ou sem `Activity.Current`) não vira `LogEntry`, não
lança exceção; log com exceção popula `ExceptionType`/`ExceptionMessage`; mensagem com e-mail/CPF
chega sanitizada (prova R-20); além de `MaxLogEntriesPerSpan` entradas, as extras são descartadas
e contadas, nunca lançam exceção.

### 17.3: Wiring (opt-in)

`AddNetApiFlow(...)` registra `services.AddSingleton<ILoggerProvider, FlowLoggerProvider>()`
**só quando `LogCorrelation.Enabled` é `true`** (ler a opção já configurada no momento do
registro, mesmo padrão condicional já usado para outras features opt-in). Com `Enabled = false`
(padrão), nenhum provider novo é registrado, zero overhead, comportamento idêntico ao de antes
desta fase.

**Pronto quando:** teste de integração confirma que, com `LogCorrelation.Enabled = false`
(padrão), nenhum `ILoggerProvider` do NetApiFlow está registrado no host; com `Enabled = true`, um
log emitido dentro de um `FlowStep` real (via `TestServer`, span de verdade, não só `Activity`
manual) aparece no `GET /traces/{traceId}` do span certo.

### 17.4: Dashboard, span details panel

`flow-details-panel.component` ganha uma seção "Logs" (visível só quando `logs.length > 0`),
listando timestamp relativo, selo de nível (`Warning`/`Error`/`Critical`, cor+ícone+texto, nunca
só cor, seção 7.2 do prompt) e mensagem; quando `exceptionType` não é nulo, mostra junto, mesmo
tratamento visual já usado para o erro do span.

**Pronto quando:** teste do componente cobre: seção não aparece com `logs: []`; aparece e
renderiza nível/mensagem/exceção corretamente com dado real. `npx impeccable detect` rodado
contra `/flow?trace=<id>` com um trace que tenha `logs` populado, nos dois temas e nos dois
viewports do padrão (`.claude/rules/design-quality.md`), contagem de avisos antes/depois no
resumo da fase.

### 17.5: Paridade com `NetApiFlow.Mcp` e documentação

Conferir que a tool `get_trace` do `NetApiFlow.Mcp` devolve `logs` sem filtro extra (o DTO da
tool provavelmente já mapeia `FlowSpan` inteiro; só confirmar, não é tarefa de implementação se
já funcionar). README e README.pt-BR.md ganham uma seção curta: o que é, como ligar
(`LogCorrelation.Enabled`), o aviso do R-20 (sanitização não é garantia absoluta, por isso
opt-in). `CHANGELOG.md`, seção `[Unreleased]` → `### Added`.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente, aviso do R-20
visível. `CHANGELOG.md` atualizado. Nota no resumo da fase confirmando o resultado da checagem de
paridade do MCP (funcionou de graça, ou precisou de ajuste pontual).

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. `npm run lint`/`format:check`/`test:ci`/`build` limpos. R-20 provado (sanitização
confirmada, não uma garantia nova inventada). Nenhuma mudança em `ITraceStore`,
`NetApiFlow.OpenTelemetry`. Com `LogCorrelation.Enabled = false` (padrão), comportamento
idêntico ao de antes desta fase, provado por teste, não assumido.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como ligar
`LogCorrelation` no sample e ver um log real correlacionado no dashboard), contagem de avisos do
Impeccable antes/depois, resultado da checagem de paridade do MCP, o que ficou pendente. A
Planejadora roda `/revisar-fase 17` antes de decidir a próxima fatia do roadmap
(`docs/plano/roadmap-valor.md`).
