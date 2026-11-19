# Fase 47: hotfix, opções ativadas só pela configuração não ligam o recurso (D-175)

**Não iniciar antes do `0.5.0` publicado.** O bug já está em `0.4.4`, publicado de verdade; esta
fase vira hotfix clássico (`hotfix/0.5.1-...`, branch a partir de `main`), mesmo padrão das Fases
34/35/42, não correção direta em `develop`. Decisão do dono do projeto: não entra no `0.5.0`, que
sai com a limitação documentada nos dois READMEs.

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
`AddNetApiFlow`. Os três serviços (`FlowLoggerProvider`, `FlowResourceFilter` mais
`FlowAuthorizationFilterListener`) passam a ser registrados **sempre**, incondicionalmente, igual
ao `SqlClientCommandListener` já é.

**Pronto quando:** `AddNetApiFlow` não tem mais nenhum `if` gating registro de serviço por opção.
Teste de arquitetura (ou `grep`) confirmando que `probedOptions` não existe mais no arquivo.

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
  host subindo normalmente já prova isso na prática; documentar que foi verificado.
- Teste confirmando que, com `LogCorrelation.Enabled = false` (padrão), o custo por chamada de
  log não passa de uma checagem booleana: `IsEnabled` devolve `false` antes de qualquer alocação
  (`LogEntry`, sanitização, etc.).

**Pronto quando:** `LogCorrelation_EnabledOnlyInConfiguration_CorrelatesLogToTheActiveSpan` passa.
Teste novo confirmando que `LogCorrelation.Enabled = false` (padrão) continua sem logs
correlacionados mesmo com `FlowLoggerProvider` sempre registrado (não regredir o comportamento
atual quando a opção está desligada).

## Tarefa 47.3: `FlowAuthorizationFilterListener`, separar construção de ativação

`FlowAuthorizationFilterListener` (`src/NetApiFlow/Instrumentation/FlowAuthorizationFilterListener.cs`)
hoje assina `DiagnosticListener.AllListeners.Subscribe(this)` direto no construtor. Registrar
sempre, sem mudar isso, ligaria o listener pra todo mundo, opção ou não.

Mesmo padrão de `SqlClientCommandListener`: mover a assinatura pra um método `Start()` separado,
chamado condicionalmente. `FlowAuthorizationFilterListenerWarmupHostedService`
(`src/NetApiFlow/DependencyInjection/FlowAuthorizationFilterListenerWarmupHostedService.cs`), hoje
só um `GC.KeepAlive(listener)` que depende de nunca ser registrado quando a opção está desligada,
vira uma cópia do padrão de `SqlClientCommandListenerWarmupHostedService`: injeta
`IOptions<NetApiFlowOptions>`, em `StartAsync` chama `listener.Start()` só se
`options.Value.CapturePrePipelineSteps` for verdadeiro.

`FlowResourceFilter` (`src/NetApiFlow/Middleware/FlowResourceFilter.cs`) hoje sempre abre o step
quando o filtro roda, sem checar opção nenhuma (dependia de nunca ser adicionado a
`MvcOptions.Filters` quando desligado). Com o filtro sempre adicionado, `OnResourceExecutionAsync`
precisa injetar `IOptions<NetApiFlowOptions>` e, com `CapturePrePipelineSteps` desligado, chamar
`await next()` direto, sem abrir `NetApiFlow.Step(...)`.

**Pronto quando:** `PrePipelineSteps_EnabledOnlyInConfiguration_ProducesAuthorizationAndResourceFilterSpans`
passa. Teste novo confirmando que, com `CapturePrePipelineSteps = false` (padrão), nenhum span de
`AuthorizationFilters`/`ResourceFilters` aparece, mesmo com os dois serviços sempre registrados
(não regredir o comportamento atual quando a opção está desligada).

## Tarefa 47.4: fumaça com pacote real, antes de publicar

Seguindo o passo novo da skill `/release` (D-176): pacote empacotado de verdade
(`-p:Version=<próxima>-verify.<n>`, nunca `ProjectReference`), `AddNetApiFlow()` sem lambda
nenhuma, `LogCorrelation.Enabled`/`CapturePrePipelineSteps` só via `appsettings.json`, em
`Development`, com uma requisição real confirmando os dois recursos ativos.

**Pronto quando:** fumaça documentada em `tests/manual-verification/`, mesmo padrão dos READMEs
já existentes, confirmando os dois recursos funcionando contra o pacote publicado, não só contra
`ProjectReference`.

## Fora de escopo

- Qualquer quarta opção ou recurso novo com o mesmo formato (`bool`/flag ligando um serviço): não
  é desta fase, mas a Tarefa 47.1 remove a causa estrutural (`probedOptions`) que deixaria essa
  classe de bug voltar.
- Atualizar os dois READMEs removendo a limitação documentada pro `0.5.0`: parte do commit desta
  fase, não uma tarefa própria, já que é consequência direta das tarefas acima.

## Versionamento e fluxo

- `Directory.Build.props`: `<Version>0.5.0</Version>` → `<Version>0.5.1</Version>` (ajustar pro
  número real vigente quando esta fase rodar, se `0.5.0` não for mais a última tag).
- `CHANGELOG.md`: entrada em `### Fixed`, descrita em termos de usuário (opção ligada só por
  configuração agora ativa o recurso correspondente), sem citar número de tarefa nem fase.
- Branch a partir de `main`, mesmo padrão das Fases 34/35/42.
- Ao terminar: abrir a PR contra `main` e parar. Merge, tag e publicação são sempre manuais,
  mesmo em hotfix.

## Relatório final

Os dois testes de reprodução antes (falhando) e depois (passando) da correção. Os dois testes
novos de "comportamento desligado não regride". Resultado da fumaça com pacote real. Confirmação
de que `probedOptions` não existe mais em nenhum lugar do arquivo.
