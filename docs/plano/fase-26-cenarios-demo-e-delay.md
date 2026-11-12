# Fase 26: Cenários de demonstração e atraso artificial configurável

Projeto tocado: `samples/SampleApi.Controllers`, `samples/SampleApi.MinimalMediatR` e
`src/NetApiFlow.Dashboard/ClientApp` (painel de ação, `features/runner`). **Nenhuma mudança em
`src/NetApiFlow` (núcleo), em `NetApiFlowOptions` ou em `docs/plano/contratos.md`.**

Decisão de arquitetura travada em D-106, com justificativa do próprio usuário: um atraso
artificial no `NetApiFlowMiddleware`, mesmo opt-in e mesmo restrito a `Development`, violaria
`.claude/rules/tracing.md` ("nunca derrubar, travar ou atrasar a API do usuário", regra dura, sem
exceção por conveniência). É uma categoria de recurso diferente de todo opt-in hoje existente no
núcleo (`ExternalSources`, `CapturePrePipelineSteps`, `TailSampling`, `LogCorrelation`): todos só
observam, nenhum deles muda o runtime da requisição do usuário de propósito. Por isso o mecanismo
de atraso vive inteiramente nos samples, como middleware comum do ASP.NET Core, sem nenhum
envolvimento do pacote NetApiFlow.

Posição normativa (`docs/design/flow-workspace/guide.md`, seção 8): aba "Cenários" no painel de
ação, ao lado da aba "Personalizado" (o formulário atual do `EndpointFormComponent` vira essa
aba, sem mudança de comportamento); o controle de atraso artificial fica dentro da aba
"Cenários".

Hoje `EndpointFormComponent` (`features/runner/endpoint-form.component.ts`) é um formulário único:
lê o OpenAPI do host (`loadOpenApiDocument`), monta campos por endpoint (`buildEndpointForm`),
dispara via `api.callEndpoint(method, path, body)`, emite `traceDispatched` com o id lido do
header `X-NetApiFlow-TraceId` da resposta. Esse fluxo não muda.

## Tarefa 26.1: investigação dos endpoints de sample (antes das tarefas seguintes)

Confirmar quais endpoints reais de `samples/SampleApi.Controllers` e
`samples/SampleApi.MinimalMediatR` já servem de base pra cada cenário desejado (sucesso rápido,
lento, erro, N+1) e quais faltam. Pesquisa já levantou que "lento" tem um precedente isolado
(`OrderService.GetSlowlyAsync`, `Task.Delay(300ms)` fixo, exposto via `GetOrderSlowlyQuery`), mas
não achou nenhum endpoint dedicado a demonstrar erro de propósito nem um cenário de N+1
clicável de um jeito direto. Reportar o achado real (quais endpoints servem, quais faltam, se
algum precisa ser criado nos dois samples ou só num) antes de prosseguir pras tarefas 26.2/26.3,
mesmo padrão de investigação-primeiro já usado nas Fases 19 e 23.

## Tarefa 26.2: middleware de atraso artificial (só nos samples)

Middleware comum do ASP.NET Core, registrado só em `Program.cs` dos dois samples (nunca em
`NetApiFlow`, nunca em nenhum pacote publicado), lendo um header de requisição dedicado (ex.
`X-NetApiFlow-Demo-Delay-Ms`, nome exato a confirmar) e aplicando `Task.Delay(delayMs,
cancellationToken)` antes de `next()`. Header, não query string: a rota do NetApiFlow é sempre
gravada como template (`.claude/rules/tracing.md`), então nenhum dos dois vazaria no trace, mas
header mantém a URL exibida no formulário limpa.

Este middleware não tem relação nenhuma com `NetApiFlow.Step`, `FlowStep.Tag` ou qualquer span:
é atraso de verdade no pipeline do próprio sample, antes de qualquer instrumentação rodar, pra
produzir uma trace realisticamente lenta sem precisar de carga real.

**Pronto quando:** dispando uma requisição pelo dashboard com o header configurado (ex. `250`,
`1000`, `3000` ms), a trace resultante mostra a duração extra de verdade (comparável disparando a
mesma requisição sem o header).

## Tarefa 26.3: aba "Cenários" no painel de ação

`EndpointFormComponent` ganha duas abas: "Personalizado" (comportamento atual, sem mudança) e
"Cenários". A aba "Cenários" lista um conjunto fixo de botões, cada um despachando uma requisição
pré-definida ({método, caminho, corpo, atraso artificial opcional}), reaproveitando
`api.callEndpoint` e o mesmo `output traceDispatched` que já existe. **Nenhuma rota nova em
`ApiService`**. O controle de atraso artificial (tarefa 26.2) fica dentro desta aba, como um
seletor aplicado ao cenário escolhido antes de disparar (ou, se fizer sentido na implementação,
também disponível pra qualquer requisição da aba "Personalizado", decisão da Executora).

Lista de cenários (quantos, quais, rótulo exato de cada um) é decisão de implementação informada
pelo achado da tarefa 26.1, não travada por este plano.

## Fora de escopo, de propósito

- Qualquer alteração em `NetApiFlowOptions`, `NetApiFlowMiddleware` ou `docs/plano/contratos.md`.
- Persistir cenários customizados criados pelo próprio usuário do dashboard: lista fixa definida
  em código é suficiente pra "pré-prontos" (YAGNI).
- Qualquer cenário que precise de um endpoint que não existe em nenhum dos dois samples e que a
  tarefa 26.1 não tenha decidido criar.

## Aceite da fase

Critérios de pronto das três tarefas acima. Build/test/format dos dois lados (`dotnet build`,
`dotnet test`, `dotnet format` nos samples tocados; `npm run lint`, `npm run format:check`,
`npm run test:ci`, `npm run build` no dashboard) limpos. Detector Impeccable rodado no painel de
ação (`/runner` e o mesmo painel embutido em `/flow`), tema claro e escuro, desktop e `390x844`.

## Ao terminar

Resumo pra Planejadora: achado da investigação 26.1, lista final de cenários implementados, como
verificar manualmente (disparar cada cenário, conferir trace resultante no grafo), o que ficou
pendente. A Planejadora roda `/revisar-fase 26` antes de liberar a Fase 27.
