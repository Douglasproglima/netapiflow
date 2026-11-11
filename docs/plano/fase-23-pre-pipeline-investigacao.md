# Fase 23: Visibilidade de pré-pipeline, investigação (R-23)

Projeto novo e descartável: `tests/NetApiFlow.PrePipelineInteropSpike`. Nenhum código de
produção nesta fase. `tests/NetApiFlow.IntegrationTests` não é tocado.

Item 1 do roadmap de valor (D-60 item D), depois da Fase 22. R-23 aberto nesta fase, nunca
investigado antes.

## Contexto: por que investigar antes de desenhar

D-60 item D previa visibilidade de routing, model binding e filtros via `DiagnosticSource`.
Pesquisa da Planejadora (contra o código-fonte real do `dotnet/aspnetcore`, não memória de
treinamento) encontrou um cenário mais restrito:

- **Routing**: só `Microsoft.AspNetCore.Routing.EndpointMatched`, um evento único (sem par
  "antes"), `DiagnosticSource.Write` clássico, payload é o próprio `HttpContext`. Sem duração
  possível, só presença.
- **Filtros de ação MVC**: 30 eventos reais (`BeforeAction`/`AfterAction`,
  `BeforeActionFilterOnActionExecuting`, etc.), todos `DiagnosticSource.Write` com payload de
  **tipo interno** do `dotnet/aspnetcore` (não público, sem garantia de versionamento
  semântico). Só existem para Controllers; Minimal API não tem equivalente nenhum.
- **Model binding**: nenhuma instrumentação embutida, em nenhum dos dois estilos.

Nenhum desses é `Activity`/`ActivitySource` como `HttpClient`/Npgsql (ADR-0012/ADR-0018). Captar
qualquer um exige `DiagnosticListener.AllListeners` com reflexão sobre tipo interno, mecanismo
mais frágil que o `ActivityListener` já usado no resto do produto (R-23). A correlação com
`Activity.Current` no momento desses eventos não tem fonte que confirme nem contradiga: fica pra
verificar, não assumir, mesmo lição do D-69/D-91.

## Tarefa 23.1: spike de investigação

Projeto xUnit próprio, com `ProjectReference` pra `samples/SampleApi.Controllers` e
`samples/SampleApi.MinimalMediatR` (os dois, pra comparar). Sobe cada sample via
`WebApplicationFactory`, assina `DiagnosticListener.AllListeners`, filtra pelo nome
`"Microsoft.AspNetCore"`, e faz requisições HTTP reais contra endpoints de cada sample.

Três perguntas a responder com evidência real, uma por uma:

1. **Correlação.** No momento em que `EndpointMatched` e pelo menos dois eventos de filtro
   (`BeforeAction`, um de `OnActionExecuting`) chegam no assinante, `Activity.Current` é a
   `Activity` correta (mesmo `TraceId` da requisição HTTP em curso)? Testar sequencial e sob
   concorrência real (`Task.WhenAll`, mesma ordem de grandeza do spike da Fase 19), `net8.0` e
   `net10.0`.
2. **Shape real do payload.** Pra `EndpointMatched` e os mesmos dois eventos de filtro, dump via
   reflexão (nome do tipo do payload, nomes de propriedade pública e não pública) em vez de
   assumir os nomes documentados. Confirmar se o tipo e o shape são idênticos entre `net8.0` e
   `net10.0`, ou se há qualquer divergência.
3. **Minimal API confirmado vazio.** Rodar a mesma assinatura contra `SampleApi.MinimalMediatR`
   e confirmar que nenhum dos 30 eventos de filtro MVC chega (só `EndpointMatched`, que é
   compartilhado), fechando a suposição da pesquisa com prova própria em vez de só documentação
   de terceiro.

Medição complementar, não bloqueante pro ponto de parada: custo de só assinar
`DiagnosticListener.AllListeners` e filtrar por nome, comparado a um processo sem assinatura
nenhuma (mesmo espírito do R-05, decidir cedo e barato é o padrão do produto, mas aqui a pergunta
é se a própria assinatura já tem custo visível antes de qualquer processamento).

**Ponto de parada explícito**: a Executora reporta os achados reais (correlação sob concorrência,
shape real do payload nos dois TFMs, confirmação vazia em Minimal API, custo da assinatura) antes
de qualquer ADR ou código de produção ser travado. Tarefas 23.2 em diante não estão especificadas
neste arquivo de propósito: dependem do que a investigação encontrar. Se a correlação não se
sustentar sob concorrência, ou o shape divergir entre TFMs, isso muda o desenho inteiro, mesmo
padrão que já aconteceu no D-91/D-92.

## Fora de escopo desta tarefa

- Qualquer código em `src/NetApiFlow`, `NetApiFlow.EntityFrameworkCore` ou qualquer pacote.
- Decidir já o `ExternalActivitySource`/flag de opt-in que o mecanismo final vai usar: essa é uma
  decisão de ADR, pendente do resultado da investigação.
- Model binding: pesquisa já fechou essa pergunta (sem instrumentação possível), nenhum spike
  precisa confirmar de novo.

## Ao terminar

Resumo pra Planejadora: os achados reais das três perguntas, o custo medido da assinatura, e
qualquer coisa inesperada (mesmo critério do D-92: corrigir a premissa publicamente, não só
seguir em frente). Aguarda decisão da Planejadora sobre as tarefas 23.2 em diante antes de
qualquer implementação.
