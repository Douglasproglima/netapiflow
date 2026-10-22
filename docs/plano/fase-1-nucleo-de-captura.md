# Fase 1: Núcleo de captura

Projeto: `src/NetApiFlow`. Testes: `tests/NetApiFlow.Tests` (unitário) e
`tests/NetApiFlow.IntegrationTests` (integração, `WebApplicationFactory`).

Pré-requisito: Fase 0 aceita. Superfície pública obrigatória: `docs/plano/contratos.md`, seção 1.
Regras duras desta fase: `.claude/rules/tracing.md`.

## Tarefas

### 1.1: Modelo de domínio

`FlowTrace`, `FlowSpan`, `ComponentType`, `FlowStatus`, conforme seção 6.4 do prompt. Namespace
próprio (ex.: `NetApiFlow.Domain`), sem nenhum `using` de `Microsoft.AspNetCore.*`, EF Core ou
driver de banco.

**Pronto quando:** teste de arquitetura (`tests/NetApiFlow.Tests`, pode usar reflexão simples
sobre `Assembly.GetReferencedAssemblies()` ou um pacote leve de teste de arquitetura) prova a
ausência dessas referências no assembly do núcleo.

### 1.2: `NetApiFlowOptions`

Binding da seção `"NetApiFlow"` do `appsettings.json`, com `ValidateOnStart`. Propriedades da
seção 9 do prompt: `IncludePaths`, `ExcludePaths`, `SlowThreshold`, `Retention.MaxTraces`,
`Retention.MaxAge`, `Limits.MaxSpansPerTrace`, `CaptureSqlText`, `Enabled`.

**Pronto quando:** configuração inválida (ex.: `Retention.MaxTraces <= 0`) derruba o boot da
aplicação com uma `OptionsValidationException` de mensagem clara, antes de atender qualquer
requisição.

### 1.3: Glob de rotas (D-08)

Matcher próprio para `IncludePaths`/`ExcludePaths`, suportando `*` (um segmento) e `**` (qualquer
profundidade), sobre o path da requisição.

**Pronto quando:** `[Theory]` cobre: `*` casando um segmento só, `**` casando vários,
`ExcludePaths` vencendo `IncludePaths` quando os dois casam a mesma rota, e a rota do próprio
dashboard (`/flow/**` ou o `DashboardPath` configurado) sempre excluída mesmo sem configuração
explícita.

### 1.4: `FlowRecorder`

Mantém os traces em andamento em memória. Limite de 500 traces simultâneos (seção 6.7): acima
disso, nova requisição não é rastreada e um contador é incrementado (exposto na tarefa 1.11).
Limite de 300 spans por trace: span extra é descartado e o trace marcado `Truncated = true`.

**Pronto quando:** teste estoura cada um dos dois limites e confere o contador correspondente e a
flag `Truncated`.

### 1.5: `ActivitySource` e `NetApiFlow.Step(...)` (D-09)

`ActivitySource` estática chamada `"NetApiFlow"`. `NetApiFlow.Step(component, operation, type)`
inicia uma `Activity` e devolve `FlowStep` (`readonly struct`, contrato em `contratos.md`).

**Pronto quando:** span chamado via `Step` aparece no recorder com componente, operação e tipo
corretos; com `NetApiFlowOptions.Enabled = false`, a chamada não aloca (medir com
`[MemoryDiagnoser]` do BenchmarkDotNet ou, no mínimo, teste de contagem de alocação via
`GC.GetAllocatedBytesForCurrentThread()`).

### 1.6: `FlowActivityListener` (risco R-05)

Decide amostragem no callback `Sample`, não depois: `ActivitySamplingResult.None` cedo quando
`Enabled` é falso ou o limite de 500 traces em andamento foi atingido.

**Pronto quando:** teste prova que, com `Enabled = false`, `Sample` devolve `None` sem instanciar
a `Activity` completa.

### 1.7: Middleware

Registrado por `AddNetApiFlow`. Decide via o matcher da tarefa 1.3 se rastreia, abre o trace no
recorder ao entrar, fecha ao sair com status HTTP e **route template** (nunca a URI real: usar
`HttpContext.GetEndpoint()?.Metadata` ou `RouteData` para obter o template, não
`HttpContext.Request.Path`). Devolve o header `X-NetApiFlow-TraceId`.

**Pronto quando:** teste de integração com rota parametrizada (`/api/orders/{id}`) prova que o
trace grava `"/api/orders/{id}"`, nunca `"/api/orders/42"`.

### 1.8: Eventos e `IFlowEventListener`

Fan-out isolado: cada listener registrado roda independente, uma falha em um não afeta os outros
nem a requisição. Os seis tipos de evento da seção 6.5 do prompt.

**Pronto quando:** teste com um `IFlowEventListener` fake que sempre lança exceção, registrado
junto de um que funciona, prova que o segundo recebe o evento normalmente e a resposta HTTP da
requisição original não é afetada.

### 1.9: `InMemoryTraceStore`, `PersistenceWriter`, retenção

`Channel` bounded em 1000 traces consumido por um `BackgroundService` (`PersistenceWriter`). A
thread da requisição nunca faz I/O de persistência: `SaveAsync` do `ITraceStore` só é chamado
pelo writer, nunca pelo middleware diretamente. Retenção via `PeriodicTimer`: remove o que passar
de `Retention.MaxTraces` (1000) ou `Retention.MaxAge` (24h), o que vier primeiro.

**Pronto quando:** teste enche a fila do `Channel` além do limite e confere o contador de traces
descartados (tarefa 1.11); teste de retenção insere traces antigos e novos, roda a retenção, e
confere que só os antigos somem.

### 1.10: Sanitização de mensagem de erro (risco R-07)

Truncar em 300 caracteres **antes** de sanitizar. Sanitizar: e-mail, CPF, CNPJ, sequência
numérica longa (ex.: mais de 11 dígitos seguidos) e os pares `password=`, `token=`,
`authorization`, `api-key`, `secret` (case-insensitive). `[GeneratedRegex]` com
`RegexOptions.NonBacktracking` onde a sintaxe permitir, `matchTimeout` explícito no que não
permitir.

**Pronto quando:** teste com exemplo de cada categoria prova a sanitização; teste com entrada
patológica (string longa desenhada para causar backtracking catastrófico) completa dentro de um
teto de tempo (`Stopwatch`, ex.: menos de 100ms).

### 1.11: Métricas

`Meter` chamado `"NetApiFlow"`, com os contadores: traces descartados (limite de 500 atingido),
spans descartados (limite de 300 por trace), falhas de persistência (fila cheia ou exceção do
store).

**Pronto quando:** teste com `MeterListener` lê os três contadores e confere que incrementam nos
cenários das tarefas 1.4 e 1.9.

### 1.12: `AddNetApiFlow(...)`

Extensão de `IServiceCollection` que registra tudo: options, recorder, listener, middleware,
writer, store em memória como padrão. Chamada dupla não duplica registro (usar `TryAdd*` onde
fizer sentido, ou um guard por marker service).

**Pronto quando:** teste resolve o `IServiceProvider` depois de chamar `AddNetApiFlow` duas vezes
e confere que não há `BackgroundService` ou `ActivityListener` duplicado.

### 1.13: Testes de integração (`WebApplicationFactory`)

Quatro cenários obrigatórios (critério de aceite da fase, seção 13 do prompt):

1. Requisição real a um endpoint de teste produz um trace com spans aninhados corretamente.
2. Spans criados dentro de `Task.WhenAll` (chamadas paralelas) têm o `ParentSpanId` correto,
   provando que `Activity.Current` flui por `AsyncLocal` como esperado.
3. Exceção lançada dentro de um span marca esse span com `IsErrorOrigin = true`, `Status = Error`,
   e os spans ancestrais ficam `Status = Error` (sem `IsErrorOrigin`).
4. Falha forçada dentro da própria instrumentação (ex.: um `IFlowEventListener` fake que lança,
   ou um recorder que simula erro interno) não impede a requisição de responder normalmente.

**Pronto quando:** os quatro cenários passam, rodando nos dois TFMs (`net8.0` e `net10.0`, D-11).

## Aceite da fase

Os quatro cenários da tarefa 1.13, verdes nos dois TFMs. Nenhum dos quatro pode ser considerado
opcional.

## Ao terminar

Resumo para a Planejadora. `/revisar-fase 1` confere cada cenário rodando os testes de
integração de verdade, não só lendo o código.
