# Fase 23: Visibilidade de pré-pipeline (R-23)

Projetos tocados: `src/NetApiFlow` (núcleo), `tests/NetApiFlow.Tests`,
`tests/NetApiFlow.IntegrationTests`. `tests/NetApiFlow.PrePipelineInteropSpike` (tarefa 23.1, já
concluída, duas rodadas) é descartável, sem relação direta com as tarefas de produção abaixo.

Item 1 do roadmap de valor (D-58/D-60 item D), depois da Fase 22. R-23 aberto nesta fase, nunca
investigado antes. Contrato congelado em `docs/plano/contratos.md`:
`NetApiFlowOptions.CapturePrePipelineSteps`. Decisão de arquitetura travada em ADR-0019/D-102, não
renegociável dentro desta fase.

## Tarefa 23.1: spike de investigação, concluída em duas rodadas (D-101/D-102)

Resultado resumido (detalhe completo em D-101/D-102 e ADR-0019):

- Routing (`EndpointMatched`) só tem presença, sem duração possível.
- `src/NetApiFlow/Middleware/FlowControllerActionFilter.cs` já cobre o estágio de ação: os
  eventos desse estágio (`BeforeAction`/`OnActionExecuting`/etc.) são redundantes, não entram no
  desenho final.
- Authorization e Resource filters (únicos estágios antes da ação nunca vistos hoje) têm payload
  **público** (`BeforeAuthorizationFilterOnAuthorizationEventData`,
  `AfterResourceFilterOnResourceExecutionEventData`, etc., `Microsoft.AspNetCore.Mvc.Core`),
  confirmado por reflexão, idêntico em `net8.0`/`net10.0`. Correlação por `TraceId` confirmada sob
  30 requisições concorrentes reais, zero cruzamento.
- Minimal API confirmado vazio: nenhum evento de filtro MVC, só `EndpointMatched` e quatro
  eventos de hosting (não úteis, mesma janela que a `Activity` raiz HTTP já cobre).
- Model binding: sem instrumentação em nenhum estilo, fora de alcance, confirmado.

`tests/NetApiFlow.PrePipelineInteropSpike` continua existindo como spike descartável, documenta a
investigação, não precisa virar teste permanente nem ser apagado.

## Tarefa 23.2: `NetApiFlowOptions.CapturePrePipelineSteps` e `FlowResourceFilter`

`CapturePrePipelineSteps` (`bool`, padrão `false`), mesmo padrão de opt-in de
`CaptureSqlText`/`LogCorrelation.Enabled`.

`FlowResourceFilter : IAsyncResourceFilter`, mesma pasta/padrão de `FlowControllerActionFilter`
(`src/NetApiFlow/Middleware/`), registrado condicionalmente (só quando `CapturePrePipelineSteps`
está ligado) com `Order` mínimo entre os filtros de recurso, pra embrulhar tudo que vem depois no
pipeline via `next()`. `Component = "ResourceFilters"`, `Operation` com o nome do filtro de
recurso mais específico disponível (ou um nome genérico se não houver um claro, decisão de
implementação), `Type = ComponentType.Service`. Zero `DiagnosticSource`, zero reflexão: mesmo
mecanismo de `FlowControllerActionFilter`, só copiado pro estágio de recurso.

**Pronto quando:** com `CapturePrePipelineSteps = true`, uma requisição real contra
`SampleApi.Controllers` produz um span `"ResourceFilters"` que é pai do span `"Controller"` já
existente (aninhamento, não duplicação). Com `CapturePrePipelineSteps = false` (padrão), nenhum
span novo aparece, zero mudança de comportamento pra quem não ligou a flag.

## Tarefa 23.3: Authorization filters via `DiagnosticListener`

Novo assinante interno, só ativo quando `CapturePrePipelineSteps` está ligado, inscrito em
`DiagnosticListener.AllListeners` filtrando por `"Microsoft.AspNetCore"`. Em
`BeforeOnAuthorization`, cast forte pra `BeforeAuthorizationFilterOnAuthorizationEventData`
(tipo público confirmado pelo spike), abre `NetApiFlow.Step("AuthorizationFilters",
filter.GetType().Name, ComponentType.Service)` e guarda a `FlowStep` resultante (struct, pode ser
armazenada fora do bloco `using` convencional). Em `AfterOnAuthorization`, cast forte pra
`AfterAuthorizationFilterOnAuthorizationEventData`, localiza a `FlowStep` guardada e descarta.

**Pareamento Before/After por `(TraceId, referência do `Filter`)`, nunca só pela referência do
`Filter`** (pode ser singleton reaproveitado entre requisições concorrentes, o spike não provou
isso, só que o evento dispara e o `TraceId` bate). Teste de concorrência real (múltiplas
requisições simultâneas, cada uma com um filtro de autorização que efetivamente atrasa um pouco,
ex. `Task.Delay` curto) precisa provar que nenhuma `FlowStep` é fechada pelo par errado.

Curadoria de tag: só o nome do filtro (`Component`/`Operation`). Nenhum campo de
`AuthorizationFilterContext` (resultado da autorização, usuário autenticado, claims, etc.) é lido
ou copiado, em nenhuma circunstância.

**Pronto quando:** com `CapturePrePipelineSteps = true` e um filtro de autorização real registrado
(mesma técnica do spike, filtro de teste próprio), o span `"AuthorizationFilters"` aparece como
irmão anterior do span `"ResourceFilters"` (autorização roda antes de recurso), com duração real
medida. Sob concorrência real, nenhuma `FlowStep` é fechada pelo par errado. Teste de curadoria
confirma que nenhum campo de `AuthorizationFilterContext` além do nome do filtro chega no
`FlowSpan`.

## Tarefa 23.4: prova de erro/exceção

Um filtro de autorização ou recurso que lança exceção real marca o span correspondente como
`FlowStatus.Error` (mesmo mecanismo de `FlowStep.MarkError` já usado em
`FlowControllerActionFilter`). Para `FlowResourceFilter`, igual ao padrão já existente (captura a
exceção do `next()`, marca e relança). Para o caminho via `DiagnosticListener`, confirmar que uma
exceção lançada dentro de um filtro de autorização ainda dispara `AfterOnAuthorization` (ou se o
comportamento é diferente, ex. a exceção pula direto pro pipeline de erro sem o evento "depois"),
e tratar os dois casos.

**Pronto quando:** teste com um filtro de autorização/recurso que lança de propósito confirma o
span correspondente marcado como erro, com `ErrorType`/`ErrorMessage` preenchidos.

## Fora de escopo, de propósito (ADR-0019)

- Routing (`EndpointMatched`): sem duração possível, sinal fraco demais. Se o usuário quiser essa
  visibilidade no futuro, decisão nova.
- Model binding: confirmado impossível pela investigação.
- Minimal API: sem equivalente de Authorization/Resource filter instrumentado pelo framework,
  mesma assimetria que já existe hoje com `FlowControllerActionFilter`.
- Qualquer mudança em `FlowControllerActionFilter` ou `ExternalActivitySource`: continuam
  exatamente como estão.

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. R-23 mitigado e provado. `NetApiFlow.Mcp`/`NetApiFlow.Dashboard` sem mudança nenhuma
(os novos spans aparecem automaticamente em qualquer consumidor de `FlowSpan`, mesmo contrato).

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como gerar
uma requisição real com `CapturePrePipelineSteps` ligado e ver os dois spans novos aparecendo
aninhados), confirmação do comportamento de exceção dentro de um filtro de autorização (tarefa
23.4), o que ficou pendente. A Planejadora roda `/revisar-fase 23` antes de decidir o próximo item
do roadmap de valor (`docs/plano/roadmap-valor.md`).
