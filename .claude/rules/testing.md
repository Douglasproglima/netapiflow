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
- Banco de dados real em teste unitário. Só `NetApiFlow.Storage.Tests` usa banco real, via
  Testcontainers.
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
- **Testcontainers** só em `NetApiFlow.Storage.Tests` (PostgreSQL e SQL Server).
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
- Nunca usar banco real fora de `NetApiFlow.Storage.Tests`.
- Sempre usar `{}` em `if`, `else`, `foreach`, `while`: padrão do time, sem exceções.
