---
paths:
  - "tests/**/*.cs"
---

# Testes (.NET)

## Objetivo e prioridade

Provar os critérios de aceite de cada fase (seção 13 do `netapiflow-prompt.md`), não perseguir
100% de cobertura. Meta de cobertura de linha é só em dois lugares (D-10): `NetApiFlow` (núcleo,
80%) e o `graph-builder` do Angular (80%, ver `.claude/rules/frontend.md`). O resto é provado por
teste de integração e pelos quatro/dez cenários que cada fase exige.

**Prioridade de cobertura:**
1. Os cenários de aceite de cada fase (seção 13 do prompt): são o que bloqueia a fase seguinte.
2. Regras duras de `.claude/rules/tracing.md`: nunca capturar payload, nunca propagar exceção da
   instrumentação, nunca I/O síncrono de persistência na thread da requisição.
3. Hierarquia de span sob `async`/`await`, `Task.Run`, `Task.WhenAll`: é o diferencial técnico
   do projeto e o mais fácil de quebrar sem perceber.
4. Limites e descarte (seção 6.7 do prompt).

**Não fazer:**
- Banco de dados real em teste unitário. Só `NetApiFlow.Storage.Tests`,
  `NetApiFlow.Npgsql.IntegrationTests` e `NetApiFlow.SqlClient.IntegrationTests` usam banco real,
  via Testcontainers. Os dois últimos existem porque `NetApiFlow.IntegrationTests` continua
  documentado como só `InMemoryTraceStore` (seção abaixo) e porque validar a interop com o driver
  real (Npgsql no ADR-0018, `Microsoft.Data.SqlClient` na Fase 37) não é teste de `ITraceStore`:
  cada um fica no seu projeto, isolado do runner Windows por falta de Docker.
- Testar implementação em vez de comportamento (ex.: verificar que um método privado foi chamado
  em vez de verificar o efeito observável).
- Mock excessivo que torna o teste frágil a refactor que não muda comportamento.

## Frameworks

- **xUnit** com `[Fact]` e `[Theory]` (v2, não a API do v3 como `TestContext.Current`).
- **NSubstitute** para mocks (ADR-0008). `Substitute.For<T>()`, não `new Mock<T>()`.
- **Shouldly** para asserções fluentes (ADR-0008). `result.ShouldNotBeNull()`,
  `result.Status.ShouldBe(FlowStatus.Success)`.
- **Microsoft.AspNetCore.Mvc.Testing** (`WebApplicationFactory<Program>`) para
  `NetApiFlow.IntegrationTests`.
- **Testcontainers** só em `NetApiFlow.Storage.Tests` (PostgreSQL e SQL Server) e nos dois projetos
  de interop com driver (`NetApiFlow.Npgsql.IntegrationTests`, `NetApiFlow.SqlClient.IntegrationTests`).
- **BenchmarkDotNet** só em `tests/NetApiFlow.Benchmarks` (Fase 7, medição de overhead).

## Nomenclatura de métodos de teste

Padrão: `MethodName_Scenario_ExpectedResult`, em inglês (D-02, difere do padrão em português
usado em outros projetos do autor).

```csharp
[Fact]
public async Task Step_WhenTracingDisabled_DoesNotAllocate() { }

[Fact]
public async Task SaveAsync_WhenQueueIsFull_IncrementsDroppedCounter() { }

[Theory]
[InlineData("/api/**", "/api/orders/1", true)]
[InlineData("/api/**", "/health", false)]
public void Matches_WithGlobPattern_ReturnsExpectedResult(string pattern, string path, bool expected) { }
```

## Estrutura da classe de teste

```csharp
using NetApiFlow.Domain;
using NSubstitute;
using Shouldly;
using Xunit;

public class FlowRecorderTests
{
    private readonly ITraceStore _storeMock;
    private readonly FlowRecorder _subject; // SUT sempre chamado de _subject

    public FlowRecorderTests()
    {
        _storeMock = Substitute.For<ITraceStore>();
        _subject = new FlowRecorder(_storeMock, Options.Create(new NetApiFlowOptions()));
    }

    [Fact]
    public async Task StartTrace_WhenBelowLimit_TracksTrace()
    {
        // Arrange
        var traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        // Act
        _subject.StartTrace(traceId, "/api/orders/{id}", CancellationToken.None);

        // Assert
        _subject.ActiveTraceCount.ShouldBe(1);
    }
}
```

## Mock de dependência assíncrona (NSubstitute)

```csharp
_storeMock.SaveAsync(Arg.Any<FlowTrace>(), Arg.Any<IReadOnlyList<FlowSpan>>(), Arg.Any<CancellationToken>())
    .Returns(ValueTask.CompletedTask);

_storeMock.GetAsync(traceId, Arg.Any<CancellationToken>())
    .Returns(new ValueTask<FlowTrace?>(expectedTrace));

await _storeMock.Received(1).SaveAsync(Arg.Any<FlowTrace>(), Arg.Any<IReadOnlyList<FlowSpan>>(), Arg.Any<CancellationToken>());
```

## Opção que liga um recurso: testar o efeito, nunca só o valor resolvido (D-176)

Teste de uma opção que ativa um listener, filtro ou provider (`ExternalSources`,
`LogCorrelation.Enabled`, `CapturePrePipelineSteps`, qualquer flag parecida) confere o efeito
observável através de um host real (`WebApplicationFactory`/`Host.CreateDefaultBuilder` mais uma
requisição de verdade: span gravado, log correlacionado, listener realmente assinado), nunca só
`options.Value.X.ShouldBeTrue()`. Confirmar que a opção chegou com o valor certo não prova que o
recurso correspondente foi de fato ligado.

Essa lacuna específica escondeu dois achados reais: o recurso do `SqlClient`
(`ExternalActivitySource.SqlClient`) passou pelas PRs #266 e #268 com teste verde sem funcionar no
padrão de `Development` (D-148), e `LogCorrelation.Enabled`/`CapturePrePipelineSteps` ligados só
pela configuração (sem lambda em `AddNetApiFlow`) nunca ativavam o recurso correspondente,
publicado desde a primeira versão de cada um (D-175). Nos dois casos o teste existente conferia o
valor da opção, nunca o comportamento que ela deveria acionar.

## Interop com driver: o comando roda dentro de um step real (D-180)

Teste de interop com um driver de banco (Npgsql, `Microsoft.Data.SqlClient`) roda o comando com a
forma do uso real: um host de verdade, uma requisição de verdade que passa por um step do NetApiFlow
(uma action de um controller MVC, no mínimo), e o trace lido do `FlowRecorder` do host. Montar o
trace com uma `Activity` raiz solta e rodar o comando embaixo dela deixa a regra de descarte de
comando SQL sem nada em volta, que é o único caso em que ela nunca decide nada.

Foi assim que o descarte largo demais do ADR-0018 ficou escondido nos dois drivers (D-179): todo
teste de interop passava, e todo SQL direto dentro de um controller sumia do trace. O mesmo vale para
a forma de rodar: os dois projetos de interop precisam executar no CI (`ci.yml`, fora do Windows).
Teste que existe e não roda não prova nada, e o do SqlClient nunca rodou no CI desde que existe.

## O teste com o pacote real inclui o caminho do usuário (D-186)

Antes de publicar, o teste com o pacote real não termina no registro das opções. Ele segue o caminho de
quem chega ao produto pela primeira vez:

- um app novo de `dotnet new webapi`, com os pacotes de `local-nuget-feed` (nunca `ProjectReference`);
- o README seguido ao pé da letra, linha por linha, sem completar o que faltar com o que a gente sabe;
- cada bloco de código do README colado num app novo, na ordem e no lugar que o texto manda, e compilado. Um `using` num bloco que não abre o arquivo dá `CS1529` quando o leitor cola, e uma sonda com o `using` no topo não prova isso (D-247);
- o documento OpenAPI do app conferido: nenhuma rota do NetApiFlow nele;
- cada tela do dashboard e da demo usada num navegador de verdade (Playwright), nos dois idiomas e nos
  dois temas, não só status HTTP. Um grafo em branco responde 200.

A auditoria do dono achou sete problemas (D-185) que as rodadas anteriores deixaram passar por conferir
só diff, CI e status HTTP.

## Mudança numa rota do painel passa pela demo (D-251)

A demo estática (`npm run build:demo`) responde cada chamada da API do painel por um arquivo de `demo-fixtures/`, achado pela URL com a query. Um parâmetro novo, uma rota nova ou uma forma de resposta nova muda a chave, e a demo dá 404 sem que nenhum teste do painel perceba, porque esses testes mockam a API.

Toda tarefa que muda o que o painel pede à API, ou o que ela responde, abre a demo construída e prova que as traces dela carregam sem erro no console. O número de traces conferidas vai dito (zero erros em N de N), e a demo responde com a semântica nova, não ignorando o parâmetro. Aconteceu na 57.3: o `before` do blueprint deu 404 em 26 de 26 traces da demo.

## Clone ou worktree novo: construir o Angular antes

Todo teste cujo host usa o Dashboard ou os samples (o `IntegrationTests` e o `PrePipelineInteropSpike`,
por exemplo) falha num clone ou worktree novo com `Could not load the embedded file manifest ... for
assembly 'NetApiFlow.Dashboard'`, porque `ClientApp/dist` não existe e a UI não é embutida. A mensagem
não aponta a causa. Antes de rodar, na ordem do CI: `npm ci` e `npm run build` em
`src/NetApiFlow.Dashboard/ClientApp`, `dotnet build NetApiFlow.slnx -c Release`, e só então
`dotnet test ... --no-build`. Falha com essa mensagem num ambiente novo é falta do passo, não defeito
do teste.

## Smoke tests de integração (`NetApiFlow.IntegrationTests`)

Sobem a API de teste inteira com `WebApplicationFactory<Program>`, validando o pipeline real
(middleware, `ActivityListener`, DI). Não usam banco externo: o `ITraceStore` padrão é o
`InMemoryTraceStore`. Testes de SSE usam `HttpClient` com
`HttpCompletionOption.ResponseHeadersRead` e leem o stream manualmente.

## Regras

- SUT sempre nomeado `_subject`.
- Mocks inicializados no construtor da classe de teste.
- Agrupar testes por método com `#region` quando a classe tiver muitos `[Fact]`/`[Theory]`.
- Sempre usar `CancellationToken.None` em chamada de teste que não testa cancelamento; testar
  cancelamento explicitamente com `CancellationToken` já cancelado quando o comportamento
  importa (ex.: `Assert.ThrowsAnyAsync<OperationCanceledException>`).
- Verificar chamada com `await _mock.Received(1).Metodo(...)` quando o efeito colateral importa,
  não só o valor de retorno.
- Teste de glob (`IncludePaths`/`ExcludePaths`) e de sanitização usa `[Theory]` com `[InlineData]`
  ou `[MemberData]`, nunca um `[Fact]` por combinação.
- Nunca usar banco real fora de `NetApiFlow.Storage.Tests`, `NetApiFlow.Npgsql.IntegrationTests` e
  `NetApiFlow.SqlClient.IntegrationTests`.
- Todo projeto de teste do `.slnx`, exceto o de Benchmarks, roda no `ci.yml` e no `release.yml`
  (D-183, D-193). Paridade entre as duas listas não basta: conferir que a lista cobre o `.slnx`.
  Projeto de teste novo entra nos dois na mesma PR. O do SqlClient ficou fora do CI, o do Npgsql
  fora do `release.yml`, e três projetos (Mcp, OpenTelemetry e o spike de pré-pipeline) fora dos
  dois, por falta dessa regra.
- Sempre usar `{}` em `if`, `else`, `foreach`, `while`: padrão do time, sem exceções.
