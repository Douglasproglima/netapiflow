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

## Interop com driver: vários comandos no mesmo método, como um repositório faz (D-284)

Teste de interop que roda cada comando no próprio método async esconde o vazamento de contexto. O `AsyncLocal` é isolado por método async: um passo de SQL que deixa o `Activity.Current` preso no contexto de quem chamou só aparece quando o mesmo método roda dois comandos, ou um comando e um `Flow.Step` depois dele. A auditoria achou assim, com o pacote real, que o SqlClient gravava um passo só para quatro comandos e punha o pai errado em tudo que vinha depois, e os testes passavam desde sempre porque cada um usava o próprio `RunAsync`.

O teste de interop de um driver tem pelo menos um caso com vários comandos no mesmo método async, e afirma o `Activity.Current` depois do `await`, o pai de cada passo e a ordem. Medido pela auditoria. A Planejadora não repetiu.

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

**Um 404 sintético da demo é barulhento (D-264).** O interceptor escreve `The demo has no recording for <url>` no console quando não há gravação. Sem isso a demo respondia 404 calada, o painel seguia mudo e uma demo quebrada passava verde. O job `built-demo` do `ci.yml` abre cada trace do manifest e as telas `/history`, `/components` e `/runner` num Chrome sem janela, em todo PR que não é só de documentação, e falha com qualquer erro de console, HTTP 400 ou mais ou pedido sem resposta.

**A demo entrega o corpo já interpretado (D-294).** No Runner da demo, a resposta chega como o objeto gravado e não como texto, e o código do painel que supõe `string` quebra (`slice is not a function`, achado na 60.3). O teste de unidade com texto real não viu isso, e quem viu foi rodar o roteiro contra a demo construída. Código do painel que lê o corpo da resposta do Runner aceita qualquer forma de corpo e tem teste para o corpo já interpretado.

## Teste de MCP: o cliente oficial, na revisão que ele negocia, e a medida em muitas sessões (D-282)

Teste de host do MCP não fica só com um cliente escrito à mão e uma revisão do protocolo. Ele roda também com o cliente oficial do SDK (`ModelContextProtocol`), na revisão que ele negocia por padrão: hoje `2026-07-28` em `Stateless` e em `StatefulForInitializeClients`, e `2025-11-25` em `Stateful`, nos três modos de sessão e nos dois TFMs. Cada revisão traz métodos que a anterior não tem (o `subscriptions/listen` da `2026-07-28`, por exemplo), e uma lista de permitidos provada numa revisão só não prova as outras.

Um critério que depende da ordem ou do momento das mensagens se mede em muitas sessões, e não em uma. A `notifications/initialized` chegou ao filtro sem `HttpContext` em 1 ou 2 de cada 20 sessões nos modos com sessão, porque o POST dela responde `202` antes de a mensagem ser tratada. Uma sessão só dava verde na maioria das vezes.

Medido pela Executora na Fase 71 e lido pela Planejadora no corpo da #487. A Planejadora não repetiu a medição.

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

## O vermelho de cada projeto: o passo de teste para no primeiro (D-295)

O passo de teste do `ci.yml` roda os projetos com `set -e` e para no primeiro que falha, então um run vermelho mostra o vermelho de **um** projeto só. Quando um defeito é guardado por testes em mais de um projeto, a prova de "visto falhando antes" de cada um precisa do run dele. A correção entra em etapas, uma por run (um push por run, como na mutação), e cada run mostra o vermelho do projeto que ainda não foi corrigido. A auditoria lê esses vermelhos nos logs, e um projeto cujo vermelho nunca apareceu não tem prova.

## O texto do `CHANGELOG.md` é contrato do job `comparison` (D-310)

O `tests/real-package/expected-differences.json` guarda, em cada linha, uma frase que tem de aparecer **palavra por palavra** no `CHANGELOG.md`, na seção `[Unreleased]` e nas seções de versão acima da versão-base de `tests/real-package/previous-published-version.txt` (o espaço em branco é normalizado, D-315). Uma frase que só aparece na versão-base ou abaixo não conta, e a seção da versão-base ausente é erro. O job `comparison` falha com `NOT ANNOUNCED` quando a frase some. Reescrever uma entrada do `CHANGELOG`, mesmo só para dizer melhor, pode tirar a frase, e foi o que aconteceu com "Each request is now its own trace" na D-304. O renome do release (`[Unreleased]` vira `[0.6.0]`) é seguro por causa da leitura das seções acima da base (D-315).

Um PR que só mexe no `CHANGELOG.md` conta como só de documentação para o `detect-changes` do `ci.yml`, que só liga o `real-package` para os READMEs, então o job não roda e a falha aparece no PR seguinte que não é só de documentação. Antes de editar uma entrada do `[Unreleased]`, conferir as frases de `expected-differences.json` contra o texto novo. Hoje são nove linhas e cinco frases distintas.

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
- O passo do runtime 11 (D-267) roda a mesma lista de nove projetos, na build de `net10.0`, com `DOTNET_ROLL_FORWARD=LatestMajor` e
  `NETAPIFLOW_EXPECT_RUNTIME_MAJOR=11`, só no `ubuntu-latest`, no `ci.yml` e no `release.yml`. Projeto de teste novo entra nele na
  mesma PR em que entra na lista normal. O README diz "no Linux" porque é o que o passo prova, e o texto muda junto no dia em que
  ele rodar no Windows.
- Passo de CI cuja prova depende do ambiente (runtime, sistema operacional, variável) tem um guarda que lê o ambiente e falha se for
  outro, e uma vermelha vista com o ambiente trocado (D-267). Sem o guarda, o passo é teatro: na Fase 69, sem o roll-forward, os
  outros 372 testes do `NetApiFlow.Tests` passam no runtime 10, e só o `ExpectedRuntimeTests` reprova.
- O job do pacote real (D-254, D-259) tem **uma definição só**, `.github/workflows/real-package.yml` (`workflow_call`), chamada pelo
  `ci.yml` e pelo `release.yml`, então a paridade dele é estrutural. No `ci.yml` o chamador sempre chama e passa `run` (verdadeiro
  quando o PR não é só de documentação ou mexe no README), e o job do workflow chamado tem `if: inputs.run`: assim o check
  `real-package / real-package` existe também num PR só de docs, como Skipped. Um `if` no job chamador faria o check não nascer, e um
  check obrigatório que não nasce trava o PR. No `release.yml` o job é pré-requisito da publicação (`needs`).
  O workflow tem dois jobs, `real-package` e `comparison`, e os dois são checks obrigatórios no `develop` (`real-package / real-package`
  e `real-package / comparison`). As etapas de pack ficam numa ação composta, `.github/actions/pack-packages`, usada pelos dois. O
  `comparison` roda o mesmo roteiro contra o build e contra o último pacote publicado
  (`tests/real-package/previous-published-version.txt`), e uma diferença só passa se estiver em
  `tests/real-package/expected-differences.json` com o texto da entrada do CHANGELOG que a anuncia. PR que muda comportamento
  visível do caminho quente acrescenta a linha, e diferença que ninguém planejou é regressão (D-257, D-262).
- Toda PR que corrige um defeito acrescenta a sua linha em `tests/regression-guards/guards.json`, ou diz na descrição que o defeito
  não tem guarda e por quê (D-266). O passo `Check that every regression guard still exists and passes`, do `build-test`, falha se um
  teste listado sumir, for renomeado ou estiver pulado. Uma linha cujo teste é renomeado muda no mesmo commit do rename.
- Sempre usar `{}` em `if`, `else`, `foreach`, `while`: padrão do time, sem exceções.
