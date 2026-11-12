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

## Tarefa 27.1: investigação (antes de qualquer código de produção ou ADR)

1. Confirmar empiricamente, com um cenário de sample controlado (ex. um endpoint que aguarda um
   `CancellationTokenSource` de timeout interno vs. um teste que fecha a conexão do cliente no
   meio da requisição), que `context.RequestAborted.IsCancellationRequested`, lido no ponto em que
   `NetApiFlowMiddleware` captura a exceção, distingue de forma confiável "cliente cancelou" de
   "cancelamento por outro motivo" nos dois `TFM`s (`net8.0`, `net10.0`).
2. Confirmar na prática (não só por leitura de `schema.sql`) que gravar um valor novo na coluna
   `status` dos dois bancos não exige migração nem quebra nenhuma constraint existente.
3. Propor os nomes finais dos valores novos de `FlowStatus` (ex. `Cancelled` pro cancelamento vindo
   do cliente, `Timeout` pro resto) antes de travar o ADR da tarefa 27.2. Reportar o achado real à
   Planejadora: se a heurística do item 1 não se confirmar de forma confiável, é motivo para
   redesenhar a distinção, não pra forçar a implementação original.

## Tarefa 27.2: ADR e mudança de contrato (depois da investigação)

`FlowStatus` ganha os valores confirmados em 27.1. `NetApiFlowMiddleware` classifica
`caughtException is OperationCanceledException` como cancelamento do cliente quando
`context.RequestAborted.IsCancellationRequested` é `true`, como o outro valor (timeout) caso
contrário. Heurística exata sujeita ao achado real da tarefa 27.1, não a este texto.
`docs/plano/contratos.md` (linha do enum `FlowStatus`, seção 1) atualizado pela Planejadora antes
da implementação desta tarefa.

## Tarefa 27.3: propagação pro dashboard

`NodeVisualState` (`core/models.ts`) e `NODE_STATE_PRESENTATION`
(`shared/node-state-presentation.ts`) ganham entrada(s) nova(s), com ícone e texto próprios,
distintos do estado `error` já existente. Filtro de status em `/history`
(`history.component.html`, hoje All/Success/Error/Running) ganha as opções novas.

Nota para a Executora: a legenda construída na Fase 25 (tarefa 25.2) já lê
`NODE_STATE_PRESENTATION` dinamicamente, sem lista hardcoded. Ela cresce sozinha quando esta
tarefa acrescenta uma entrada nova ao mapa, sem precisar editar o componente da legenda de novo.

## Tarefa 27.4: três `ITraceStore` (condicionada ao achado da tarefa 27.1)

Se a tarefa 27.1 confirmar que a coluna `status` aceita o valor novo sem mudança de schema (o que
o grep inicial já sugere), esta tarefa só precisa confirmar com teste real (Testcontainers) que
gravar e ler de volta o status novo funciona nos três `ITraceStore`
(`InMemoryTraceStore`, PostgreSQL, SQL Server), sem exigir `schema.sql` novo. Se a investigação
achar o contrário, reportar antes de implementar: pode virar uma migração de schema de verdade,
fora do escopo originalmente previsto para esta fase.

## Fora de escopo, de propósito

- Qualquer tentativa de diferenciar a causa do timeout (ex. timeout de servidor vs. timeout de uma
  dependência externa): D-60 só pede a distinção em dois grupos, cliente cancelou vs. o resto.
- Propagação pro `NetApiFlow.OpenTelemetry` ou pro `NetApiFlow.Mcp` além do que `FlowSpan`/
  `FlowTrace` já carregam de graça: sem tarefa dedicada nesta fase, a não ser que a Executora
  encontre um gap real equivalente ao já visto em fases anteriores (D-90, Fase 17).

## Aceite da fase

Critérios de pronto das quatro tarefas acima, mais um teste de integração provando os dois
cenários reais descritos na tarefa 27.1 (cliente cancela vira um valor, outro cancelamento vira o
outro). Build/test/format nos dois TFMs, Impeccable rodado em `/flow` e `/history`.

## Ao terminar

Resumo pra Planejadora: achado da investigação 27.1 (a heurística se confirmou? os nomes finais
do enum? precisou de migração de schema?), o que foi implementado, como verificar, o que ficou
pendente. A Planejadora decide, com o achado em mãos, se a mudança de contrato merece um ADR
formal (a expectativa registrada em D-106 é que sim, por mexer num contrato público congelado com
um mecanismo de classificação novo) antes de aceitar a fase.
