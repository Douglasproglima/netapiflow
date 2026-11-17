# ADR-0022: Pacote `NetApiFlow.Testing`, asserção de trace sem dependência de framework

## Status
Aceito

## Discussion
[#253](https://github.com/Douglasproglima/netapiflow/discussions/253)

## Contexto

Item 16 do `roadmap-valor.md` (D-132, segunda auditoria externa do site, seção 4): o único
caminho de adoção corporativa real identificado é uma asserção legível sobre uma trace, porque
entra no pipeline de CI da empresa mesmo que o dashboard nunca seja aberto. `NetApiFlow.GetRecentTrace`
já existe desde a Fase 24 (D-58 item 6), público, lendo `FlowTraceDetail` do buffer em memória sem
esperar a fila de persistência. Falta só a camada de asserção em cima dele.

Decisão de forma em aberto: a DSL deveria depender de Shouldly (ADR-0008, já o padrão interno
deste projeto) ou ser livre de framework? `NetApiFlow.Testing` é consumido pelo **projeto do
usuário**, não por este repositório: forçar Shouldly como dependência transitiva imporia uma
escolha de framework de asserção a quem já usa xUnit puro, NUnit ou MSTest com seus próprios
`Assert`. O valor do pacote está em **ler a trace**, não em competir com o `Assert` que o usuário
já tem.

## Decisão

`NetApiFlow.Testing` é um pacote novo, **sem nenhuma dependência de terceiro**, só referenciando
`NetApiFlow` (núcleo). Confirmado livre no nuget.org (`api.nuget.org/v3-flatcontainer/netapiflow.testing/index.json`
devolve `BlobNotFound`; busca na API de pesquisa devolve `totalHits: 0`), mesmo cuidado da D-72
para `NetApiFlow.Mcp`.

Superfície pública (congelada em `docs/plano/contratos.md`, implementada na Fase 39):

```csharp
namespace NetApiFlow.Testing;

public static class FlowTraceAssertions
{
    public static FlowTraceDetail ShouldHaveStatus(this FlowTraceDetail trace, FlowStatus status);
    public static FlowTraceDetail ShouldHaveNoErrors(this FlowTraceDetail trace);
    public static FlowTraceDetail ShouldCallAtMost(this FlowTraceDetail trace, ComponentType type, int maxCount);
    public static FlowTraceDetail ShouldNeverCall(this FlowTraceDetail trace, string component, string? operation = null);
    public static FlowTraceDetail ShouldHaveCalled(this FlowTraceDetail trace, string component, string? operation = null);
}

public sealed class FlowAssertionException : Exception
{
    public FlowAssertionException(string message);
}

public static class FlowTestingHttpResponseExtensions
{
    public static FlowTraceDetail GetFlowTrace(this HttpResponseMessage response, IServiceProvider services);
}
```

Cada asserção devolve a própria `FlowTraceDetail` (encadeamento fluente) e lança
`FlowAssertionException` com mensagem acionável na falha, deixando o framework de teste do usuário
(xUnit, NUnit, MSTest, ou o que for) tratar a exceção não capturada como falha, sem nenhuma
dependência adicional. `GetFlowTrace` fecha o ciclo que o roadmap descreve ("integração com
`WebApplicationFactory` reaproveitando `NetApiFlow.GetRecentTrace`"): lê `X-NetApiFlow-TraceId` da
resposta, chama `NetApiFlow.GetRecentTrace`, e lança `FlowAssertionException` se vier nulo (trace
já envelheceu do buffer, ou tracing desligado), em vez de obrigar o usuário a repetir essa dança
em cada teste.

## Consequências

- Mais fácil: qualquer projeto consegue escrever `response.GetFlowTrace(factory.Services).ShouldCallAtMost(ComponentType.Database, 3)`
  sem adotar Shouldly nem nenhuma outra biblioteca de asserção.
- Mais difícil: qualquer asserção nova precisa manter a mesma filosofia (sem dependência), o que
  limita a DSL a comparações simples sobre o grafo de spans, nunca um motor de regras genérico.
- Não reabrir sem justificativa técnica nova: adicionar uma dependência de terceiro a este pacote
  (mesmo uma pequena) exige voltar a este ADR, não só adicionar no `Directory.Packages.props`.
