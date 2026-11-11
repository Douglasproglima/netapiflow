# ADR-0019: Visibilidade de pré-pipeline, Authorization via `DiagnosticSource`, Resource via filtro nativo

## Status
Aceito

## Issue
[#156](https://github.com/Douglasproglima/netapiflow/issues/156)

## Contexto
D-60 item D previa visibilidade de routing, model binding e filtros de ação, assumindo
`DiagnosticSource` como mecanismo único. A investigação da Fase 23 (tarefa 23.1, spike
descartável, D-101/R-23) encontrou um cenário diferente em três pontos.

**Routing**: só existe `Microsoft.AspNetCore.Routing.EndpointMatched`, evento único sem par
"antes", `DiagnosticSource.Write` clássico, payload é o próprio `HttpContext`. Sem par, não há
duração possível, só presença. `NetApiFlow` não tem como medir essa janela por nenhum outro
mecanismo: o middleware do próprio NetApiFlow já roda depois de `UseRouting()` ter resolvido o
endpoint, então não existe um "antes de rotear" que o middleware consiga observar.

**Estágio de ação, redundante**: `src/NetApiFlow/Middleware/FlowControllerActionFilter.cs` já
existe (registrado via `MvcOptions.Filters.Add<FlowControllerActionFilter>()`,
`IAsyncActionFilter`) e já envolve toda a execução de ação num step `ComponentType.Controller`.
Os eventos `BeforeAction`/`AfterAction` e os de filtro de ação (`BeforeOnActionExecuting`/etc.)
cobrem a mesma janela, não agregam nada que o produto não capture hoje. Confirmado pela própria
investigação: a janela e o `TraceId` batem exatamente com o que o filtro nativo já produz.

**Authorization e Resource, a lacuna real**: esses dois estágios rodam **antes** do estágio de
ação, nunca vistos hoje (`FlowControllerActionFilter` só vê o estágio de ação). O spike confirmou,
com um filtro global vazio registrado só dentro do projeto de spike (sem tocar nos samples):

- `BeforeOnAuthorization`/`AfterOnAuthorization` → payload
  `BeforeAuthorizationFilterOnAuthorizationEventData`/`AfterAuthorizationFilterOnAuthorizationEventData`,
  públicos (`Type.IsPublic = true`), implementam `IReadOnlyList<KeyValuePair<string,object>>`.
- `BeforeOnResourceExecution`/`AfterOnResourceExecution` → payload
  `BeforeResourceFilterOnResourceExecutionEventData`/`AfterResourceFilterOnResourceExecutionEventData`,
  mesmo padrão público.

Isso **corrige** a pesquisa original (que presumia tipo interno sem garantia de versionamento):
os tipos vêm de `Microsoft.AspNetCore.Mvc.Core`, já disponível via a mesma `FrameworkReference`
que o núcleo já tem, com garantia de API pública normal do .NET. R-23 (fragilidade de tipo
interno) é revisado: o risco real não é "o tipo pode mudar sem aviso", é "ler demais do payload"
(`AuthorizationFilterContext`/`ResourceExecutingContext`/`ResourceExecutedContext` dão acesso à
requisição inteira).

Correlação confirmada sob concorrência real (30 requisições simultâneas, `traceparent` W3C
distinto por requisição, 240 eventos observados, 0 cruzamento), `net8.0` e `net10.0`, idêntico.
Achado adicional: `Activity.Current` não é sempre a mesma `Activity` em todo evento (a maioria vê
a raiz da requisição, `BeforeActionMethod` especificamente vê uma filha por método de ação), mas
o `TraceId` sempre bate porque a filha herda da raiz. Nenhum desenho pode assumir "é sempre a
mesma `Activity`", só "é sempre o mesmo `TraceId`".

**Minimal API confirmado vazio**: nenhum dos eventos de filtro MVC (ação, authorization, resource)
aparece contra `SampleApi.MinimalMediatR`, só `EndpointMatched` (compartilhado) e quatro eventos
de hosting (`BeginRequest`/`EndRequest`/`HttpRequestIn.Start`/`Stop`, não úteis aqui: já
representam a mesma janela que a `Activity` raiz HTTP do ASP.NET Core já cobre). Model binding
não tem instrumentação em nenhum estilo.

Nenhum desses dois mecanismos novos é `Activity`/`ActivitySource` como `HttpClient`/Npgsql
(ADR-0012/ADR-0018): são um tipo de captura diferente, não encaixam em `ExternalActivitySource`.

## Decisão

`NetApiFlowOptions` ganha `CapturePrePipelineSteps` (`bool`, padrão `false`, opt-in explícito,
mesmo padrão de `CaptureSqlText`/`LogCorrelation.Enabled`). Quando ligado, dois mecanismos
independentes, cada um usando a ferramenta mais simples que resolve o próprio estágio:

**Resource filters**: filtro nativo novo, `FlowResourceFilter : IAsyncResourceFilter`, mesmo
arquivo/pasta de `FlowControllerActionFilter`, registrado com `Order` mínimo (roda primeiro entre
os filtros de recurso) pra envolver tudo que vem depois no pipeline, inclusive o próprio
`FlowControllerActionFilter` (aninhamento esperado e correto: "Resource" como pai de
"Controller", mesma relação que "Service" já tem com "Repository"). Zero `DiagnosticSource`, zero
reflexão: mesmo mecanismo simples e seguro que já existe pro estágio de ação, porque
`IAsyncResourceFilter` tem delegate `next()` que os outros filtros de recurso e o resto do
pipeline passam por dentro. `Component = "ResourceFilters"`, `Type = ComponentType.Service` (não
existe `ComponentType` próprio pra filtro, reaproveita o mais próximo em significado, decisão
registrada aqui para não inventar um `ComponentType` novo por uma única fase).

**Authorization filters**: `IAsyncAuthorizationFilter` não tem `next()` (não embrulha nada, só
decide autorizado/não autorizado), então não existe filtro nativo capaz de medir a duração de
*outros* filtros de autorização. Único caminho: `DiagnosticListener.AllListeners`, filtrado por
`"Microsoft.AspNetCore"`, cast forte pra
`BeforeAuthorizationFilterOnAuthorizationEventData`/`AfterAuthorizationFilterOnAuthorizationEventData`
(tipos públicos, confirmados pelo spike, não reflexão cega). Um `NetApiFlow.Step("AuthorizationFilters",
filter.GetType().Name, ComponentType.Service)` é aberto em `BeforeOnAuthorization` e guardado
(a `FlowStep` é um `readonly struct` sobre uma `Activity`, pode ser armazenada e descartada depois,
fora do bloco `using` convencional) até o `AfterOnAuthorization` correspondente chegar, correlacionado
por `(TraceId, referência do Filter)`, nunca só pela referência do `Filter` (pode ser singleton
reaproveitado entre requisições concorrentes). Cada instância de filtro de autorização vira o
próprio span: visibilidade por filtro, não um único "Authorization" agregado, porque é exatamente
esse nível de detalhe que falta hoje (mesma régua do D-60 item D).

**Curadoria de tag**: nenhum payload (`AuthorizationFilterContext`, `ResourceExecutingContext`,
`ResourceExecutedContext`, o `HttpContext` de `EndpointMatched`) é copiado além do que já é
necessário pro nome do `Component`/`Operation`. Nenhum header, corpo, query string, claim de
usuário autenticado: mesma régua de sempre (`.claude/rules/tracing.md`).

**Routing**: fora de escopo de produção. Sem duração possível (só presença), sinal fraco demais
pra justificar o custo de engenharia e o risco de ruído (confirmar `EndpointMatched` chegando,
sem nada útil pra mostrar além de "a rota casou", informação que o próprio `FlowTrace.RouteTemplate`
já carrega desde a Fase 1). Se o usuário quiser essa visibilidade no futuro, decisão nova,
separada.

**Model binding**: fora de escopo, confirmado impossível pela investigação, nenhuma instrumentação
existe hoje em nenhum estilo.

## Consequências

- R-23 revisado: risco real não é mais "tipo interno sem garantia de versionamento" (os tipos são
  públicos), é "ler demais de um payload que dá acesso à requisição inteira". Mitigação é
  curadoria de tag disciplinada, provada por teste, não uma limitação de API.
- Minimal API continua sem essa visibilidade: mesma assimetria que já existe hoje entre os dois
  estilos no produto (ex. `FlowControllerActionFilter` também é MVC-only).
- `ExternalActivitySource` não ganha um valor novo: este mecanismo é uma família de captura
  diferente (`DiagnosticSource` clássico, não `Activity`), opt-in próprio
  (`CapturePrePipelineSteps`), documentado como tal pra não confundir com Npgsql/HttpClient.
- Pareamento Before/After por `(TraceId, referência do Filter)` precisa de prova real sob
  concorrência (mesmo rigor do spike), tarefa da implementação de produção, não coberto pelo
  spike (o spike provou que os eventos disparam e o `TraceId` bate, não que o pareamento
  específico funciona sob carga).
- `FlowStep` armazenada fora do bloco `using` convencional (criada em `BeforeOnAuthorization`,
  descartada em `AfterOnAuthorization`) é um uso novo da struct, nunca testado antes neste padrão;
  precisa de teste específico confirmando que o aninhamento de `Activity.Parent` continua correto
  quando o `Dispose()` acontece fora do bloco onde a `FlowStep` nasceu.
