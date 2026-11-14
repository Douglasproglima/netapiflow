# ADR-0020: `FlowStatus` ganha `Cancelled` e `Timeout`, separados de `Error`

## Status
Aceito

## Discussion
[#203](https://github.com/Douglasproglima/netapiflow/discussions/203)

## Contexto

D-60 registrou a lacuna: hoje qualquer cancelamento, de origem nenhuma identificada, vira um
`TaskCanceledException`/`OperationCanceledException` indistinguível de qualquer outra falha, e
`NetApiFlowMiddleware` classifica isso como `FlowStatus.Error`. Um cliente que fecha a aba ou
perde conexão no meio da requisição não diz nada sobre a saúde da API, mas hoje infla a mesma
métrica de erro que um bug real do servidor.

A investigação da tarefa 27.1 (branch `bugfix/fase-27-visibilidade-cancelamento`) confirmou,
contra um host Kestrel real (não `TestServer`, mesma razão do `DashboardSseTests`) e contra os
dois bancos via Testcontainers:

- `context.RequestAborted.IsCancellationRequested`, lido no ponto em que `NetApiFlowMiddleware`
  já captura a exceção, é `true` quando o cliente desconecta de verdade no meio da requisição, e
  `false` quando um `CancellationTokenSource` interno dispara com o cliente ainda conectado. A
  distinção é confiável nos dois TFMs (`net8.0`, `net10.0`).
- Gravar um valor de `status` fora dos três já existentes, direto na coluna (sem passar pelo
  enum), funciona sem migração e sem violar nenhuma constraint nos dois bancos (PostgreSQL 16,
  SQL Server 2022).

Revisão de código da Planejadora, antes de travar esta decisão, achou mais dois lugares que uma
reclassificação simples afetaria em silêncio, nenhum coberto pelo plano original da fase:

- `TailSampleDecision.ShouldPersist`: um `FlowStatus.Error` sempre é persistido, independente da
  amostragem configurada. Reclassificar cancelamento/timeout pra fora de `Error` tiraria os dois
  dessa garantia, sem nenhuma decisão explícita sobre se isso é certo.
- `ErrorCount` em `ComponentStat`/`RouteStat`/`TraceStats` (as três implementações de
  `ITraceStore`): conta só `FlowStatus.Error`. A mesma reclassificação silenciosamente abaixaria a
  taxa de erro de qualquer rota com cancelamento ou timeout, sem nenhuma decisão explícita sobre
  se isso reflete a realidade que o usuário da ferramenta quer ver.

## Decisão

`FlowStatus` ganha dois valores novos: `Cancelled` e `Timeout`.

`NetApiFlowMiddleware` classifica no `finally`, antes da regra `Error`/`Success` já existente:

```csharp
var status = caughtException switch
{
    OperationCanceledException when context.RequestAborted.IsCancellationRequested => FlowStatus.Cancelled,
    OperationCanceledException => FlowStatus.Timeout,
    not null => FlowStatus.Error,
    null when context.Response.StatusCode >= 500 => FlowStatus.Error,
    _ => FlowStatus.Success,
};
```

Escopo deliberadamente limitado ao status da **trace inteira**. O nível de span individual
(`FlowInterceptorProxy.CloseSpan` e o resto do caminho de `FlowActivityListener`) continua só
`Success`/`Error`: a investigação confirmou que a mesma ambiguidade existe lá, mas D-60 pediu
visibilidade no nível de execução completa, não por etapa. Se um span individual precisar da
mesma distinção no futuro, é decisão nova, separada, fora do escopo desta ADR.

**`Cancelled` não conta como erro, `Timeout` conta.** Critério: o que separa ruído de sinal pra
quem está depurando um serviço de produção.

- Um cliente cancelando (`Cancelled`) não indica problema nenhum do lado do servidor. Contar isso
  como erro é exatamente a queixa original do D-60: infla a taxa de erro com algo não acionável.
- Um cancelamento interno disparando (`Timeout`) costuma indicar um problema real (dependência
  lenta, possível N+1, servidor sobrecarregado). Excluir isso da taxa de erro esconderia
  exatamente o tipo de incidente que a ferramenta existe pra ajudar a achar, trocando um
  problema (ruído) por outro pior (ponto cego).

Consequência direta nos dois lugares achados na revisão:

- `ErrorCount` (`ComponentStat`, `RouteStat`, `TraceStats`, nas três implementações de
  `ITraceStore`) passa a contar `FlowStatus.Error` **e** `FlowStatus.Timeout`, nunca
  `FlowStatus.Cancelled`.
- `TailSampleDecision.ShouldPersist` passa a sempre persistir `FlowStatus.Error` **e**
  `FlowStatus.Timeout`, igual a hoje. `FlowStatus.Cancelled` segue a mesma amostragem de
  `FlowStatus.Success`: é churn de produção normal, não precisa de cobertura garantida, e manter
  fora da garantia evita que cancelamento de cliente lote o armazenamento à custa de traces
  realmente raras e valiosas de se investigar depois.

`core/models.ts` (dashboard) ganha os dois valores no tipo `FlowStatus`, e
`NODE_STATE_PRESENTATION` ganha entrada própria pra cada um, com ícone e texto distintos do
`error` já existente (nunca só cor, seção 7.2 do prompt). Filtro de status em `/history` ganha as
duas opções novas.

Persistência: `status` continua `varchar`/`nvarchar` sem `CHECK` constraint nos dois bancos,
confirmado pela tarefa 27.1. Nenhuma migração de schema.

## Consequências

- Taxa de erro de `/metrics`/`/insights` muda de comportamento pra qualquer rota com
  cancelamento de cliente real: ela cai, porque esses casos saem do numerador sem sair do
  denominador (`TotalTraces` continua contando tudo). Isso é intencional, é o objetivo desta ADR,
  mas é uma mudança visível pra quem já olhava esse número antes da fase.
- Tail sampling (`NetApiFlowOptions.TailSampling`) muda de comportamento pro mesmo grupo:
  cancelamento de cliente deixa de ter persistência garantida, timeout continua garantido.
  Afeta só quem já tem `TailSampling.Enabled = true`.
- Span individual continua sem a distinção (só `Success`/`Error`), assimetria deliberada entre
  trace e span que fica registrada aqui, não descoberta por acidente numa fase futura.
- `ExternalActivitySource`/OTLP (`NetApiFlow.OpenTelemetry`) não são tocados por esta ADR: a
  classificação acontece só na trace HTTP principal, nenhum span de driver externo carrega
  `Cancelled`/`Timeout`.
