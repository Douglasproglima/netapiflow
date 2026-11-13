# Fase 27: Visibilidade de cancelamento

Item do roadmap de valor sem posição fixa (D-60): "distinguir cliente cancelando de timeout, hoje
tudo vira `TaskCanceledException` indistinguível". Toca núcleo (`src/NetApiFlow`) e contrato
público (`FlowStatus`, `docs/plano/contratos.md`), então provavelmente ganha ADR, decidida depois
da investigação da tarefa 27.1, **não travada agora**. Mesmo padrão já usado nas Fases 19
(Npgsql, D-91) e 23 (pré-pipeline, D-101): abrir só com uma tarefa de investigação antes de
qualquer código de produção ou ADR, porque desenhar o mecanismo de cima pra baixo sem confirmar o
comportamento real já causou retrabalho nesta sessão mais de uma vez.

## O que a pesquisa já confirmou

- `FlowStatus` (`src/NetApiFlow/Contracts/FlowStatus.cs`) só tem três valores:
  `Running`, `Success`, `Error`.
- `NetApiFlowMiddleware.InvokeAsync` (`src/NetApiFlow/Middleware/NetApiFlowMiddleware.cs`) captura
  qualquer exceção genericamente (`catch (Exception ex) { caughtException = ex; throw; }`) e, no
  `finally`, decide o status da trace com `caughtException is not null || context.Response.StatusCode
  >= 500 ? FlowStatus.Error : FlowStatus.Success`. Um `TaskCanceledException`/
  `OperationCanceledException` cai nesse mesmo ramo, sem nenhuma distinção de causa.
- Nenhuma leitura de `context.RequestAborted` em nenhum lugar do núcleo hoje. Nenhum uso de
  `RequestTimeouts` do ASP.NET Core.
- `FlowInterceptorProxy.CloseSpan` não trata `Task.IsCanceled` como caso distinto de `IsFaulted`:
  mesmo no nível de span individual, cancelamento nunca é uma condição própria.
- Schema de persistência: `status` é `varchar(16)` nos dois bancos
  (`src/NetApiFlow.Storage.PostgreSql/schema.sql`, `src/NetApiFlow.Storage.SqlServer/schema.sql`),
  sem `CHECK` constraint visto no grep inicial. Sinal de que um valor de enum novo não deveria
  exigir migração de schema, mas a tarefa 27.1 confirma isso na prática, não só por leitura do
  arquivo.
- Dashboard: `core/models.ts` define `FlowStatus` como `'Running' | 'Success' | 'Error'`, sem
  nenhum valor de cancelamento/timeout; `NODE_STATE_PRESENTATION`
  (`shared/node-state-presentation.ts`) só tem os seis estados já conhecidos, nenhum específico
  pra isso.

## Tarefa 27.1: investigação, encerrada (D-117)

Confirmado empiricamente, contra Kestrel real (não `TestServer`) nos dois TFMs:
`context.RequestAborted.IsCancellationRequested`, lido no ponto em que `NetApiFlowMiddleware`
captura a exceção, é `true` quando o cliente desconecta de verdade e `false` quando um
cancelamento interno dispara com o cliente ainda conectado. Confirmado também, via Testcontainers
nos dois bancos, que gravar um valor fora do enum atual na coluna `status` não exige migração nem
quebra constraint. Nomes mantidos: `Cancelled` (cliente), `Timeout` (resto).

## Tarefa 27.2: ADR e mudança de contrato

Decisão travada em [ADR-0020](../adr/0020-visibilidade-cancelamento.md), `contratos.md` já
atualizado pela Planejadora. `FlowStatus` ganha `Cancelled` e `Timeout`. `NetApiFlowMiddleware`
classifica no `finally`, antes da regra `Error`/`Success` já existente:

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

**Escopo adicional achado na revisão da Planejadora, não previsto na primeira versão deste
plano**: `TailSampleDecision.ShouldPersist` e `ErrorCount` (`ComponentStat`, `RouteStat`,
`TraceStats`, nas três implementações de `ITraceStore`) tratam hoje `FlowStatus.Error` como
"sempre conta". ADR-0020 decide: `Timeout` entra nesse grupo junto com `Error` (sempre persiste
sob tail sampling, sempre conta em `ErrorCount`); `Cancelled` nunca entra (amostragem normal,
nunca conta como erro). Os dois pontos (`TailSampleDecision` e as três implementações de
`ITraceStore`) fazem parte desta tarefa, não são opcionais.

## Tarefa 27.3: propagação pro dashboard

`NodeVisualState` (`core/models.ts`) e `NODE_STATE_PRESENTATION`
(`shared/node-state-presentation.ts`) ganham entrada(s) nova(s), com ícone e texto próprios,
distintos do estado `error` já existente. Filtro de status em `/history`
(`history.component.html`, hoje All/Success/Error/Running) ganha as opções novas.

Nota para a Executora: a legenda construída na Fase 25 (tarefa 25.2) já lê
`NODE_STATE_PRESENTATION` dinamicamente, sem lista hardcoded. Ela cresce sozinha quando esta
tarefa acrescenta uma entrada nova ao mapa, sem precisar editar o componente da legenda de novo.

## Tarefa 27.4: três `ITraceStore`

Confirmado com teste real (Testcontainers) que gravar e ler de volta `Cancelled`/`Timeout`
funciona nos três `ITraceStore` (`InMemoryTraceStore`, PostgreSQL, SQL Server), sem exigir
`schema.sql` novo (já provado na tarefa 27.1). Prova também, nos três, que `ErrorCount`
(`ComponentStat`/`RouteStat`/`TraceStats`) conta um trace `Timeout` e não conta um `Cancelled`,
conforme ADR-0020.

## Fora de escopo, de propósito

- Qualquer tentativa de diferenciar a causa do timeout (ex. timeout de servidor vs. timeout de uma
  dependência externa): D-60 só pede a distinção em dois grupos, cliente cancelou vs. o resto.
- Classificação no nível de span individual (`FlowInterceptorProxy.CloseSpan` continua só
  `Success`/`Error`): ADR-0020 limita a distinção à trace inteira, de propósito.
- Propagação pro `NetApiFlow.OpenTelemetry` ou pro `NetApiFlow.Mcp` além do que `FlowSpan`/
  `FlowTrace` já carregam de graça: sem tarefa dedicada nesta fase, a não ser que a Executora
  encontre um gap real equivalente ao já visto em fases anteriores (D-90, Fase 17).

## Aceite da fase

Critérios de pronto das quatro tarefas acima, mais um teste de integração provando os dois
cenários reais descritos na tarefa 27.1 (cliente cancela vira um valor, outro cancelamento vira o
outro). Build/test/format nos dois TFMs, Impeccable rodado em `/flow` e `/history`.

## Ao terminar

Resumo pra Planejadora: o que foi implementado (27.2 a 27.4), como verificar, o que ficou
pendente.
