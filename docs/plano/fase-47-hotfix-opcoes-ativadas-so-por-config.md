# Fase 47: opções ligadas só pela configuração não ligam o recurso (D-175, D-177)

**Esta fase bloqueia o `0.5.0`.** O bug já está em `0.4.4`, publicado de verdade, mas o `0.5.0`
ainda não saiu, então a correção entra nele e não vira hotfix. Branch
`bugfix/47-opcoes-so-por-config` a partir de `develop`, PR contra `develop`, e a branch
`release/0.5.0` só abre com esta fase mergeada. Decisão do dono do projeto (D-177). Não existe
`0.5.1`.

O nome deste arquivo ainda diz "hotfix", de quando o plano era esse (D-175). Fica como está de
propósito: o D-175 cita o nome, e `decisoes.md` não se edita.

## Ponto de parada: regra de recuo

A correção é pequena e segue um padrão que já funciona (`SqlClientCommandListener`, PR #305). Se a
Executora achar que ela é maior do que esta fase estima, **ela para e relata, sem tentar resolver
por conta própria**. Sinais de que é maior:

- dependência circular ao subir o `ILoggerFactory` com o `FlowLoggerProvider` sempre registrado
  (Tarefa 47.2);
- regressão no caminho desligado, quer dizer, um dos testes "desligado não regride" que não fecha
  com uma mudança pequena (Tarefas 47.2 e 47.3);
- custo por requisição, com as opções desligadas, que não seja desprezível (Tarefa 47.3).

Nesse caso o `0.5.0` sai com a limitação documentada nos dois READMEs, e a correção vai para a
próxima versão normal. Hotfix `0.5.1` não existe em nenhum cenário (D-177).

## O achado

`LogCorrelation.Enabled` e `CapturePrePipelineSteps`, ligados só pela seção de configuração
`NetApiFlow` (sem lambda em `AddNetApiFlow`), mudam o valor da opção resolvida (confirmado por
`IOptions<NetApiFlowOptions>`) mas não ativam o recurso correspondente. Publicado desde a primeira
versão de cada opção.

Causa raiz, confirmada lendo `NetApiFlowServiceCollectionExtensions.AddNetApiFlow`
(`src/NetApiFlow/DependencyInjection/NetApiFlowServiceCollectionExtensions.cs`):

```csharp
var probedOptions = new NetApiFlowOptions();
configure?.Invoke(probedOptions);
if (probedOptions.LogCorrelation.Enabled)
{
    services.AddSingleton<ILoggerProvider, FlowLoggerProvider>();
}

services.AddSingleton<SqlClientCommandListener>();
services.AddHostedService<SqlClientCommandListenerWarmupHostedService>();

if (probedOptions.CapturePrePipelineSteps)
{
    services.Configure<MvcOptions>(options => options.Filters.Add<FlowResourceFilter>());
    services.AddSingleton<FlowAuthorizationFilterListener>();
    services.AddHostedService<FlowAuthorizationFilterListenerWarmupHostedService>();
}
```

`probedOptions` é um `NetApiFlowOptions` descartável que só recebe a lambda do chamador. Quando a
única fonte é a configuração (`ConfigurationBinder`, resolvido só depois, via
`IOptions<NetApiFlowOptions>` em tempo de execução), `probedOptions` nunca vê a mudança, e o
serviço correspondente nem entra no container de DI.

`SqlClientCommandListener` (linhas acima, já sem `if`) nunca sofre disso, porque a Fase 37/D-148
já usa o padrão certo: sempre registrado, e
`SqlClientCommandListenerWarmupHostedService.StartAsync` lê `IOptions<NetApiFlowOptions>` e só
chama `listener.Start()` se o resolvido mandar. A decisão acontece depois da configuração estar
resolvida, nunca antes. Esta fase estende o mesmo padrão pros outros dois casos.

## Os dois testes de reprodução (preservar, não reescrever do zero)

Escritos pela Executora durante a investigação, confirmados falhando contra `0.4.4`. Adicionar a
`tests/NetApiFlow.IntegrationTests/` (ou arquivo equivalente, a Executora decide o nome exato,
mantendo o conteúdo):

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

[ApiController]
[Route("api/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

public class ConfigOnlyOptionsRepro
{
    private sealed class AllowAllFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
    }

    [Fact]
    public async Task LogCorrelation_EnabledOnlyInConfiguration_CorrelatesLogToTheActiveSpan()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["NetApiFlow:LogCorrelation:Enabled"] = "true",
                        ["NetApiFlow:LogCorrelation:MinLevel"] = "Warning",
                    }));
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/warn", (ILoggerFactory loggerFactory) =>
                        {
                            using var step = NetApiFlow.Step("WarnController", "Warn");
                            loggerFactory.CreateLogger("WarnController")
                                .Log(LogLevel.Warning, new EventId(0), "something happened", null, (s, e) => s);
                            return Results.Ok();
                        });
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Start();

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spans = new ConcurrentBag<FlowSpan>();
        recorder.SpanCompleted += spans.Add;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/warn");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        spans.Single().Logs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task PrePipelineSteps_EnabledOnlyInConfiguration_ProducesAuthorizationAndResourceFilterSpans()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["NetApiFlow:CapturePrePipelineSteps"] = "true" }));
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(PingController).Assembly);
                    services.AddNetApiFlow(options => options.IncludePaths("/api/**"));
                    services.Configure<MvcOptions>(options => options.Filters.Add(new AllowAllFilter()));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spans = new ConcurrentBag<FlowSpan>();
        recorder.SpanCompleted += spans.Add;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/ping");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        spans.ShouldContain(span => span.Component == "AuthorizationFilters");
        spans.ShouldContain(span => span.Component == "ResourceFilters");
    }
}
```

**Pronto quando (parte 1):** os dois testes acima commitados e vistos falhando contra o código
atual (antes de qualquer correção), confirmando a reprodução de novo nesta branch.

## Tarefa 47.1: remover `probedOptions` por inteiro

Sem `probedOptions`, sem `new NetApiFlowOptions()` descartável em lugar nenhum de
`AddNetApiFlow`. O `FlowLoggerProvider` e o `FlowAuthorizationFilterListener` passam a ser
registrados **sempre**, incondicionalmente, igual ao `SqlClientCommandListener` já é. O
`FlowResourceFilter` também sai de qualquer `if` do `AddNetApiFlow`, mas por outro caminho
(Tarefa 47.3).

**Pronto quando:** `AddNetApiFlow` não tem mais nenhum `if` que decide registro de serviço por
opção. Teste de arquitetura (ou `grep`) confirmando que `probedOptions` não existe mais no arquivo.

## Tarefa 47.2: `FlowLoggerProvider`/`FlowLogger`, checagem barata quando desligado

`FlowLogger.IsEnabled` (`src/NetApiFlow/Instrumentation/FlowLogger.cs`) hoje checa só
`options.Value.LogCorrelation.MinLevel`, nunca `LogCorrelation.Enabled`. Registrar
`FlowLoggerProvider` sempre, sem acrescentar esse check, ligaria log correlacionado por padrão
pra todo mundo (já que o resto do `IsEnabled` só depende do `MinLevel`, que tem valor padrão
`Warning`). Acrescentar `options.Value.LogCorrelation.Enabled &&` no início de `IsEnabled`.

Prova exigida pela auditoria, não só o efeito funcional:
- Teste confirmando que construir `ILoggerFactory` com `FlowLoggerProvider` sempre presente não
  cria dependência circular (`FlowLoggerProvider` depende de `FlowRecorder`/`IOptions`; nenhum
  dos dois pode depender, direta ou indiretamente, de `ILoggerFactory`/`ILogger<T>`). Um teste de
  host subindo normalmente já prova isso na prática; documentar que foi verificado. Pela leitura
  do código não há ciclo: `FlowRecorder` só recebe `IOptions<NetApiFlowOptions>`, e a cadeia das
  opções (`NetApiFlowDevelopmentDefaults`, `NetApiFlowOptionsSetup`,
  `NetApiFlowOptionsValidator`) não recebe `ILogger` nem `ILoggerFactory`. O teste continua
  exigido porque é ele que prova em execução.
- Teste confirmando que, com `LogCorrelation.Enabled = false` (padrão), o custo por chamada de
  log não passa de uma checagem booleana: `IsEnabled` devolve `false` antes de qualquer alocação
  (`LogEntry`, sanitização, etc.).

**Pronto quando:** `LogCorrelation_EnabledOnlyInConfiguration_CorrelatesLogToTheActiveSpan` passa.
Teste novo confirmando que `LogCorrelation.Enabled = false` (padrão) continua sem logs
correlacionados mesmo com `FlowLoggerProvider` sempre registrado (não regredir o comportamento
atual quando a opção está desligada).

O `<summary>` do `FlowLoggerProvider` diz que ele só é registrado com a opção ligada, o que deixa
de ser verdade: corrigir a frase no mesmo commit, sem citar fase nem decisão.

## Tarefa 47.3: pré-pipeline, listener sempre registrado e filtro decidido pelas opções resolvidas

Três peças, e as três decidem **depois** que a configuração já foi resolvida, nunca antes.

### O listener de Authorization

`FlowAuthorizationFilterListener` (`src/NetApiFlow/Instrumentation/FlowAuthorizationFilterListener.cs`)
hoje assina `DiagnosticListener.AllListeners.Subscribe(this)` direto no construtor. Registrado
sempre e sem mudar isso, ele ligaria para todo mundo, opção ou não.

Mesmo padrão do `SqlClientCommandListener`: a assinatura vai para um `Start()` separado, com as
mesmas invariantes dele. Chamar `Start()` duas vezes assina uma vez só. `Start()` depois de
`Dispose()` não assina. `Dispose()` sem `Start()` não lança. Uma propriedade
`internal bool IsSubscribed`, como a do `SqlClientCommandListener`, deixa o teste ler o estado.

`FlowAuthorizationFilterListenerWarmupHostedService`
(`src/NetApiFlow/DependencyInjection/FlowAuthorizationFilterListenerWarmupHostedService.cs`), hoje
só um `GC.KeepAlive(listener)` que dependia de nunca ser registrado com a opção desligada, vira uma
cópia do `SqlClientCommandListenerWarmupHostedService`: recebe `IOptions<NetApiFlowOptions>` e
`ILogger`, e em `StartAsync` chama `listener.Start()` só se `CapturePrePipelineSteps` resolvido for
verdadeiro, com a exceção capturada e logada, nunca propagada. O `<summary>` dele, que diz que só é
registrado com a opção ligada, deixa de ser verdade: corrigir no mesmo commit.

### A chamada por requisição no middleware (achado desta rodada)

`NetApiFlowMiddleware` chama
`context.RequestServices?.GetService<FlowAuthorizationFilterListener>()?.CloseOrphanedSteps(...)`
no `finally` de toda requisição rastreada. Hoje, com a opção desligada, o listener não está
registrado e a chamada é pulada. Com o listener sempre registrado ela passa a rodar em todas, e
`CloseOrphanedSteps` enumera `_pendingSteps.Keys`.

Medido num laço de uma thread, nesta máquina, em `net8.0`, `net10.0` e `net11.0`: enumerar as
chaves de um `ConcurrentDictionary` vazio custa 261 a 304 ns por chamada, e checar `IsEmpty` antes
custa o mesmo (248 a 263 ns). Um flag custa 5 a 8 ns. Nenhum dos três aloca.

Exigido: `CloseOrphanedSteps` devolve na primeira linha enquanto o listener não está assinado
(`IsSubscribed` falso). Sem isso, quem não usa a opção paga a enumeração em toda requisição.

### O filtro de recurso

O plano anterior mandava registrar sempre o `FlowResourceFilter` e checar a opção a cada
requisição. Trocado, porque isso não custa zero.

A documentação oficial só descreve `TypeFilterAttribute.IsReusable` como uma dica de que a
instância pode ser reaproveitada, sem garantia, e não informa o valor padrão nem diz o que
`Filters.Add<T>()` registra (páginas lidas: a referência da API de `TypeFilterAttribute.IsReusable`
e de `FilterCollection.Add`, e "Filters in ASP.NET Core"). Por isso a medição, num app descartável
fora do repositório, com um filtro e uma opção de brinquedo e 200 requisições sequenciais em
Kestrel. O resultado foi igual em `net8.0` (8.0.34), `net10.0` (10.0.15) e `net11.0` (11.0.0):

| Desenho | Entrada em `MvcOptions.Filters` | Instâncias do filtro | Execuções do filtro |
|---|---|---|---|
| `Filters.Add<T>()` sempre | `TypeFilterAttribute`, `IsReusable` falso | 200 | 200 |
| `Filters.AddService<T>()` sempre, singleton no DI | `ServiceFilterAttribute`, `IsReusable` falso | 1 | 200 |
| `IConfigureOptions<MvcOptions>` lendo `IOptions<>`, opção desligada | nenhuma | 0 | 0 |
| o mesmo setup, opção ligada só pela configuração | `TypeFilterAttribute` | 200 | 200 |

Registrar sempre custaria uma instância do filtro por requisição (ou uma execução, no formato de
serviço) em todo app MVC com a opção desligada. O pacote já paga uma instância por requisição com o
`FlowControllerActionFilter`, registrado do mesmo jeito (`Filters.Add<T>()`, em `AddNetApiFlow`),
então registrar sempre o segundo dobraria isso sem ninguém ter pedido a opção. O setup pelas opções
resolvidas custa zero desligado, e a última linha mostra que ele enxerga o valor que veio só da
configuração, que é exatamente o caso do bug.

Não achei problema concreto nesse desenho. A cadeia de opções não depende de `MvcOptions`, então
resolver `IOptions<NetApiFlowOptions>` de dentro da configuração de `MvcOptions` não cria ciclo.

O desenho: uma `IConfigureOptions<MvcOptions>` interna (nome sugerido
`FlowResourceFilterMvcOptionsSetup`, em `DependencyInjection/`, registrada no `AddNetApiFlow`)
recebe `IOptions<NetApiFlowOptions>` e só chama `options.Filters.Add<FlowResourceFilter>()` quando
`CapturePrePipelineSteps` resolvido está ligado. `FlowResourceFilter` não muda: continua sem checar
opção, e o `<summary>` dele ("Only registered when `CapturePrePipelineSteps` is enabled") continua
verdadeiro. A medição usou um filtro de brinquedo, então a prova do desenho de verdade é o teste de
reprodução.

**Pronto quando:**

- `PrePipelineSteps_EnabledOnlyInConfiguration_ProducesAuthorizationAndResourceFilterSpans` passa.
- Com a opção desligada (padrão), três coisas provadas por um host real, não só pelo valor da
  opção (`.claude/rules/testing.md`, D-176): nenhum span `AuthorizationFilters` ou
  `ResourceFilters` depois de uma requisição; `MvcOptions.Filters` sem entrada para
  `FlowResourceFilter`; o listener resolvido do DI com `IsSubscribed` falso.
- Um teste por invariante de `Start()` (as três listadas acima).
- `CloseOrphanedSteps` com o listener não assinado devolve sem tocar no dicionário. O tempo não
  vira teste permanente, porque teste de tempo é instável: a Executora mede com um laço simples
  antes e depois da guarda e cola os números no resumo final.
- `AddNetApiFlow_CalledTwice_DoesNotDuplicateRegistrations` muda. Hoje espera 4 hosted services
  (`PersistenceWriter`, `RetentionService`, `NetApiFlowWarmupHostedService` e
  `SqlClientCommandListenerWarmupHostedService`), e o hosted service do listener passa a ser
  incondicional. Pela leitura do `AddNetApiFlow` a conta vira 5. A Executora confere rodando o
  teste e ajusta o número, sem mudar o que ele prova (chamar duas vezes não duplica registro), e
  acrescenta que o setup de `MvcOptions` do filtro também aparece uma vez só.

## Tarefa 47.4: fumaça com pacote real, e a receita do release

É a fumaça do passo 4 da skill `/release` (D-176), rodada já nesta fase. Vale tudo o que o passo
exige: pacote empacotado de verdade (`-p:Version=0.5.0-verify.<n>`, nunca o número real, ver
`.claude/rules/packaging.md` e D-174), `AddNetApiFlow()` sem lambda nenhuma, em `Development`, com
storage real e pelo menos uma requisição real.

A receita fica em `tests/manual-verification/`, reutilizável: um app mais uma seção no `README.md`
da pasta com os comandos exatos (empacotar, subir o container, rodar os dois alvos, rodar o
controle). O release roda a mesma receita de novo com a versão final, sem reescrever nada. Quando
ela existir, a Planejadora faz o passo 4 da skill apontar para ela.

O que o app precisa ter para exercitar esta correção:

- um controller, porque os passos de pré-pipeline só existem em MVC;
- `LogCorrelation.Enabled` e `CapturePrePipelineSteps` ligados só no `appsettings.json`;
- um endpoint que escreve um log de aviso dentro de um `NetApiFlow.Step(...)` e roda um
  `SqlCommand` direto, o que cobre de novo o listener do SqlClient da PR #305;
- SQL Server real em container, `mcr.microsoft.com/mssql/server:2022-latest` (a imagem dos testes
  de integração), e o `Microsoft.Data.SqlClient` do app em `6.0` ou mais novo. O piso do pacote de
  storage é o `5.2.2` e não sobe o do app (`CHANGELOG.md`). A fumaça do D-173 usou o `7.1.2`.

O que a fumaça confere, lendo o trace gravado e nunca um evento interno do `FlowRecorder` (que é o
que os testes de reprodução já fazem): os spans `AuthorizationFilters` e `ResourceFilters`, o log
de aviso anexado ao span do step e o span de banco do `SqlCommand` direto.

**Controle desligado.** O mesmo app sem as duas chaves: nenhum span `AuthorizationFilters` ou
`ResourceFilters` e nenhum log correlacionado, provado com pacote de verdade. O risco desta
correção é o caminho desligado, e rodar só o ligado não prova isso.

**Matriz.** `net8.0`, que resolve o asset `lib/net8.0`, e `net11.0`, que resolve o `lib/net10.0`,
porque o terceiro TFM só chega no `0.6.0` (D-173). Os dois alvos, nos dois modos.

**Pronto quando:** a receita está documentada em `tests/manual-verification/` e foi rodada nos dois
alvos, ligada e desligada, com o resultado no resumo final.

## Critério que vale para a fase inteira

`tests/manual-verification/AotCoreVerification` continua com zero aviso (`dotnet publish -c Release`
na pasta, ver o `README.md` de `tests/manual-verification/`). A `0.4.3` deixou o `AddNetApiFlow`
limpo, e esta fase mexe exatamente nessa função: registros novos e um tipo novo de setup.

## Fora de escopo

- Qualquer quarta opção ou recurso novo com o mesmo formato (`bool`/flag ligando um serviço): não
  é desta fase, mas a Tarefa 47.1 remove a causa estrutural (`probedOptions`) que deixaria essa
  classe de bug voltar.
- O terceiro TFM (`net11.0` de verdade em `Directory.Build.props`): é o item principal do `0.6.0`
  (D-173). A matriz da fumaça só usa um app `net11.0` consumindo o asset `lib/net10.0`.

## Versionamento e fluxo

- Branch `bugfix/47-opcoes-so-por-config` a partir de `develop` atualizado. O `pr-lint` só aceita
  minúscula, dígito, ponto e hífen no nome.
- `Directory.Build.props` não muda. O número `0.5.0` é posto pelo release (passo 7 da skill
  `/release`), não por esta fase.
- `CHANGELOG.md`: entrada em `[Unreleased]`, na seção `### Fixed` que já existe, descrita em termos
  de usuário (opção ligada só pela configuração agora ativa o recurso correspondente), sem citar
  número de tarefa nem fase.
- Os dois READMEs perdem o item de limitação no mesmo PR: o que começa com "`LogCorrelation.Enabled`
  and `CapturePrePipelineSteps` only take effect when set in code" em `README.md` e o equivalente em
  `README.pt-BR.md` (`git grep -n "only take effect when set in code"` e
  `git grep -n "só têm efeito quando definidos em código"`). Os exemplos que ligam as opções na
  lambda continuam valendo.
- Ao terminar: abrir a PR contra `develop` e parar. Tem código, então o merge exige confirmação do
  dono.
- Depois do merge, o release `0.5.0` segue a skill `/release`, e o passo 4 repete a fumaça da
  Tarefa 47.4 com a versão final.

## Relatório final

Os dois testes de reprodução antes (falhando) e depois (passando) da correção. Os testes novos de
"desligado não regride" (spans, `MvcOptions.Filters`, `IsSubscribed`, log). A medição do
`CloseOrphanedSteps` antes e depois da guarda. O número real de hosted services do teste
`CalledTwice`. O resultado da fumaça nos dois alvos, ligada e desligada. O resultado do
`AotCoreVerification`. A confirmação de que `probedOptions` não existe mais no arquivo.
