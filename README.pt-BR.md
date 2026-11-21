# NetApiFlow

Veja, ao vivo, o caminho que cada requisição HTTP percorre dentro da sua própria API ASP.NET Core: Controller/Endpoint, Handler, Services, Repositories, SQL. Com duração de cada etapa, onde ela falhou, o que ficou lento e o que nunca chegou a rodar.

[![NuGet](https://img.shields.io/nuget/v/NetApiFlow.svg)](https://www.nuget.org/packages/NetApiFlow)
[![CI](https://github.com/Douglasproglima/netapiflow/actions/workflows/ci.yml/badge.svg)](https://github.com/Douglasproglima/netapiflow/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 8 | .NET 10](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)

🇺🇸 [Read in English](README.md)

![O grafo de fluxo acendendo ao vivo durante uma requisição](docs/images/flow-demo.gif)

> O GIF acima foi gravado a partir de `samples/SampleApi.Controllers` com o dashboard montado.
> Veja [Início rápido](#início-rápido) para reproduzir você mesmo.

[Site](https://douglasproglima.github.io/netapiflow-site/) · [Demo ao vivo](https://douglasproglima.github.io/netapiflow-site/demo/)

## O que é, e o que não é

O NetApiFlow instrumenta o caminho de chamadas interno da sua própria API, não latência externa,
não métrica de infraestrutura, não tracing distribuído entre serviços. Ele responde "onde *esta*
requisição gastou tempo, dentro do *meu* código", para um único processo ASP.NET Core.

Ele **não é**:

- Um sistema de tracing distribuído (sem propagação de contexto entre serviços). O
  `ActivityListener` próprio do NetApiFlow convive bem com o SDK do OpenTelemetry, sem conflito
  (veja
  [`docs/adr/0002-activitysource-nativo-sem-otel-sdk.md`](docs/adr/0002-activitysource-nativo-sem-otel-sdk.md)),
  e o `NetApiFlow.OpenTelemetry` exporta seus traces para qualquer backend OTLP (veja
  [Exportação OTLP](#exportação-otlp)).
- Um APM (sem alerta, sem dashboard além do que ele mesmo entrega, sem armazenamento de métrica de
  longo prazo além do que você configurar via um pacote de storage).
- Um profiler (ele mede as etapas que você marca, não cada chamada de método do processo).

## Início rápido

```bash
dotnet add package NetApiFlow
dotnet add package NetApiFlow.Dashboard
```

```csharp
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();

var app = builder.Build();
app.UseNetApiFlowDashboard(); // montado em /flow por padrão

app.MapGet("/hello", () => "Hello")
    .WithNetApiFlowTracking();

app.Run();
```

Controllers MVC viram uma etapa sozinhos. Um endpoint de Minimal API só vira com
`.WithNetApiFlowTracking()`, no endpoint ou num grupo inteiro
(`app.MapGroup("/api").WithNetApiFlowTracking()`). O modelo padrão `dotnet new webapi` usa Minimal
API, então acrescente a chamada ao `MapGet` dele. Sem ela, a requisição é gravada e aparece na
lista, mas com zero etapas. O dashboard avisa e mostra como marcar uma, em vez de desenhar um grafo.

Abra `http://localhost:<porta>/flow` e envie uma requisição para o endpoint. Veja
[Segurança do dashboard em produção](#segurança-do-dashboard-em-produção) antes de publicar isso
em qualquer lugar além da sua própria máquina.

## Formas de marcar etapas

O NetApiFlow precisa saber onde uma etapa termina e a próxima começa. Escolha a forma que couber
em cada camada. Elas se combinam livremente na mesma requisição.

| Forma | O que faz | Quando usar |
|---|---|---|
| `.WithNetApiFlowTracking()` | Envolve um endpoint de Minimal API, ou todos os endpoints de um `MapGroup`, numa etapa `Endpoint` | Apps com Minimal API. Controllers MVC não precisam |
| `NetApiFlow.Step(...)` | Abre e fecha uma etapa manualmente em volta de um trecho de código | Qualquer código, sem depender de DI |
| `AddFlowTracked<TInterface, TImpl>(...)` | Envolve toda chamada à implementação de uma interface numa etapa, via proxy dinâmico | Uma interface de repositório/serviço já registrada no DI |
| `DecorateFlowTracked(predicate, ...)` | O mesmo de cima, aplicado em lote a todo registro que bater com um predicado | Muitas interfaces que seguem uma convenção de nome (ex.: `I*Repository`) |
| `NetApiFlow.MediatR` | Um pipeline behavior que envolve todo `IRequest` tratado pelo MediatR | Projetos que já usam MediatR para comandos/queries |
| `NetApiFlow.EntityFrameworkCore` | Um `DbCommandInterceptor` que marca cada comando SQL como uma etapa `Database` | Projetos que usam EF Core |

Forma manual, usada explicitamente:

```csharp
using NetApiFlow;
using static NetApiFlow.NetApiFlow;

public async Task<Order?> FindAsync(int id, CancellationToken cancellationToken)
{
    using var step = Step(nameof(OrderRepository), nameof(FindAsync), ComponentType.Repository);
    return await _dbContext.Orders.FindAsync([id], cancellationToken);
}
```

O `using static` é o que permite escrever `Step(...)` sem qualificar. Escrever
`NetApiFlow.Step(...)` só compila de dentro de um namespace seu: a partir da global namespace, que
é onde um `Program.cs` de top-level statements coloca o seu código, o nome `NetApiFlow` resolve
para o namespace do pacote, não para a classe, e o compilador acusa `CS0234`.

Forma automática, de `samples/SampleApi.Controllers/Program.cs`. `OrderService`/`OrderRepository`
não têm nenhum código do NetApiFlow. Toda chamada a eles é rastreada só por causa deste registro:

```csharp
using NetApiFlow.Marking;

builder.Services.AddFlowTracked<IOrderRepository, OrderRepository>(ComponentType.Repository);
builder.Services.AddFlowTracked<IOrderService, OrderService>(ComponentType.Service);
```

O `DecorateFlowTracked` faz o mesmo em lote, para toda interface já registrada no DI cujo tipo de
implementação bater com um predicado. Escala para um projeto real com muitos repositórios e
serviços, por convenção de nome, em vez de uma chamada por interface:

```csharp
using NetApiFlow.Marking;

builder.Services.DecorateFlowTracked(type => type.Name.EndsWith("Repository"), ComponentType.Repository);
builder.Services.DecorateFlowTracked(type => type.Name.EndsWith("Service"), ComponentType.Service);
```

O predicado recebe o tipo de implementação concreta registrado no DI, nunca a interface. Uma
convenção de nome que só bate na interface (`IOrderRepository`) não funciona.

### Comandos de banco do EF Core

`NetApiFlow.EntityFrameworkCore` exige dois passos, não um. `AddNetApiFlowEntityFrameworkCore()`
só registra o `FlowDbCommandInterceptor` no DI. Anexar o interceptor a cada `DbContext` é com
você, porque o EF Core não tem registro global de interceptor:

```csharp
using NetApiFlow.EntityFrameworkCore;

builder.Services.AddNetApiFlowEntityFrameworkCore();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>());
});
```

O pacote funciona com EF Core 8.0.10 ou superior, no `net8.0` e no `net10.0`. Foi testado no
EF Core 8, 9 e 10.

### Tags numa etapa

Uma etapa aberta com `NetApiFlow.Step(...)` aceita tags livres, chave e valor, gravadas naquele
span:

```csharp
using NetApiFlow;
using static NetApiFlow.NetApiFlow;

using var step = Step(nameof(OrderService), nameof(ConfirmAsync), ComponentType.Service);
step.Tag("order.status", order.Status);
step.Tag("payment.provider", provider.Name);
```

Chame `Tag` em qualquer momento enquanto a etapa está aberta, não só logo depois de abri-la: as
tags são lidas quando a etapa termina. Chave que começa com `netapiflow.` é reservada e nunca é
gravada. `Limits.MaxTagsPerSpan` limita quantas tags um span guarda (as que passam do limite são
descartadas e contadas), e `Limits.MaxTagValueLength` trunca um valor longo sem perder a tag. Os
dois estão em [Configuração](#configuração).

> **Valor de tag nunca é sanitizado.** Todo outro campo que o NetApiFlow grava é estrutural ou
> passa pelo sanitizador de mensagem de erro. Tag é texto livre que você escolheu guardar, então
> fica persistido e é servido exatamente como foi escrito. Use para identificador e rótulo curto,
> nunca para corpo de requisição, dado de usuário ou credencial.

As tags viajam em `FlowSpan.Tags`: `GET /traces/{traceId}`, `NetApiFlow.GetRecentTrace`, a tool
`get_trace` do `NetApiFlow.Mcp` e os dois pacotes de storage carregam todas elas. A interface do
dashboard não mostra tag nesta versão.

## Asserção de fluxo em teste

Um teste de integração construído sobre `WebApplicationFactory<TEntryPoint>` já tem a resposta
que a própria requisição acabou de produzir. `NetApiFlow.GetRecentTrace` lê essa trace direto do
buffer em memória, mantido enquanto a persistência ainda não processou. Não precisa de
`Task.Delay` nem de laço de retentativa esperando a escrita assíncrona terminar:

```csharp
using NetApiFlow;
using static NetApiFlow.NetApiFlow;

public class OrdersEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetOrder_DoesNotTriggerMoreThanOneDatabaseQuery()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var trace = GetRecentTrace(factory.Services, traceId);

        Assert.NotNull(trace);
        Assert.Equal(1, trace.Spans.Count(s => s.Type == ComponentType.Database));
    }
}
```

`GetRecentTrace` devolve um `FlowTraceDetail` puro (o `FlowTrace` e os `FlowSpan` que ele
registrou), não uma DSL de asserção. A consulta sobre `trace.Spans` é LINQ comum, para expressar
qualquer regra ("no máximo N consultas de banco", "esta rota nunca chama aquele repositório",
"nenhuma etapa falhou"). A biblioteca de asserção é a que o projeto de teste já usa, incluindo o
`Assert` do próprio xUnit. Devolve `null` quando a trace já saiu do buffer em memória (500 traces
completadas entre a chamada e a asserção) ou quando o NetApiFlow nunca foi registrado. Nunca
devolve `null` só porque a chamada HTTP falhou.

### `NetApiFlow.Testing`

O mesmo teste, escrito com o pacote opcional `NetApiFlow.Testing`, que dispensa a leitura do
header e a checagem de nulo, e nomeia a regra em vez de escrever o LINQ:

```csharp
using NetApiFlow;
using NetApiFlow.Testing;

public class OrdersEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetOrder_DoesNotTriggerMoreThanOneDatabaseQuery()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.GetFlowTrace(factory.Services)
            .ShouldHaveNoErrors()
            .ShouldCallAtMost(ComponentType.Database, 1)
            .ShouldHaveCalled("OrderRepository", "FindAsync")
            .ShouldNeverCall("PaymentGateway");
    }
}
```

São cinco asserções, todas encadeáveis: `ShouldHaveStatus`, `ShouldHaveNoErrors`,
`ShouldCallAtMost`, `ShouldHaveCalled` e `ShouldNeverCall`. `ShouldHaveCalled`/`ShouldNeverCall`
casam só pelo componente quando a operação é omitida, ou pelo par exato quando ela é informada.

O pacote não referencia nada além do próprio `NetApiFlow`, então não impõe biblioteca de asserção
nenhuma ao seu projeto de teste. Uma asserção que falha lança `FlowAssertionException` com
mensagem dizendo o que esperava e o que achou, por exemplo
`Expected at most 1 Database calls, found 5.`. Qualquer framework de teste reporta isso como
falha. O `GetFlowTrace` lança a mesma exceção, em vez de devolver `null`, quando a resposta não
traz o header `X-NetApiFlow-TraceId` ou a trace já saiu do buffer.

## Pacotes opcionais

| Pacote | Acrescenta |
|---|---|
| [`NetApiFlow.Dashboard`](https://www.nuget.org/packages/NetApiFlow.Dashboard) | A API REST/SSE e a UI Angular embutida |
| [`NetApiFlow.MediatR`](https://www.nuget.org/packages/NetApiFlow.MediatR) | Rastreamento automático de requests do MediatR |
| [`NetApiFlow.EntityFrameworkCore`](https://www.nuget.org/packages/NetApiFlow.EntityFrameworkCore) | Rastreamento de comandos de banco do EF Core, depois de anexar o interceptor ao seu `DbContext` |
| [`NetApiFlow.Storage.PostgreSql`](https://www.nuget.org/packages/NetApiFlow.Storage.PostgreSql) | Persiste traces no PostgreSQL em vez de memória |
| [`NetApiFlow.Storage.SqlServer`](https://www.nuget.org/packages/NetApiFlow.Storage.SqlServer) | Persiste traces no SQL Server em vez de memória |
| [`NetApiFlow.Mcp`](https://www.nuget.org/packages/NetApiFlow.Mcp) | Um servidor MCP só leitura para um agente de IA consultar traces direto |
| [`NetApiFlow.OpenTelemetry`](https://www.nuget.org/packages/NetApiFlow.OpenTelemetry) | Exporta traces para qualquer backend OTLP (Jaeger, Tempo, Aspire Dashboard, Honeycomb, um coletor) |
| [`NetApiFlow.Testing`](https://www.nuget.org/packages/NetApiFlow.Testing) | Asserções sobre o fluxo gravado, para teste de integração |

O núcleo (`NetApiFlow`) não tem nenhuma dependência além do shared framework do ASP.NET Core.
Cada item acima é opcional.

## Como ler o grafo

Cada nó é uma etapa. A cor reforça o estado, mas nunca é o único sinal: cada estado também tem um
ícone e um texto próprios, para o grafo continuar legível para quem tem daltonismo ou numa
impressão em preto e branco.

| Estado | Significado |
|---|---|
| ⏳ Aguardando | Inferido de uma execução bem-sucedida anterior deste endpoint (o "blueprint"); a requisição atual ainda não chegou nesta etapa |
| ▶ Em execução | Executando agora |
| ✓ Concluído | Terminou sem erro, abaixo do limiar de lentidão |
| ⚠ Lento | Terminou sem erro, no limiar configurado de lentidão ou acima dele |
| ✕ Erro | Terminou com exceção, ou uma etapa descendente falhou |
| ∅ Não executado | O blueprint esperava esta etapa, mas a requisição terminou sem alcançá-la (ex.: um retorno antecipado, um ramo que nunca foi seguido) |

Um nó `Database` também mostra um selo de N+1, por exemplo `⚠ ×12`, quando 3 ou mais etapas
`Database` do mesmo trace compartilham o mesmo componente e a mesma operação, mesmo sob pais
diferentes. É essa a forma que um N+1 clássico produz. Uma consulta por iteração do laço, mesmo
formato de SQL, cada uma filha de uma instância diferente da etapa em loop.

Um trace concluído também mostra quanto da sua duração total não está coberto por nenhuma etapa
registrada, por exemplo "30% do tempo (55 ms de 183 ms) não está coberto por nenhuma etapa
registrada". A conta é a união dos intervalos `[startedAt, endedAt)` de cada etapa, não uma soma
ingênua das durações. Assim, uma etapa aninhada ou duas branches paralelas de um `Task.WhenAll`
nunca são contadas em dobro.

Esses seis estados aparecem numa legenda fixa no canto inferior esquerdo do canvas, para não
precisar lembrar o que cada ícone quer dizer. No canto inferior direito ficam os botões de mais
zoom, menos zoom e "ajustar à tela", com a porcentagem atual. Roda do mouse, arrastar e pinça
continuam funcionando igual. Os botões só tornam o mesmo zoom alcançável sem gesto.

Enquanto uma trace ainda está rodando, aparece um botão "Pausar ao vivo" no cabeçalho do grafo.
Pausar congela o grafo onde ele está, para você inspecionar uma etapa sem a imagem se mexer. Os
eventos que chegam durante a pausa entram numa fila, não são descartados, e "Retomar ao vivo"
aplica a fila inteira na ordem certa. É outra coisa, diferente do Replay: a pausa continua na
trace em andamento, o Replay percorre uma trace que já terminou.

A faixa de métricas acima do grafo tem um cartão de latência p95 com um sparkline das últimas 50
execuções. O número de p95 vem do `GET /metrics`, calculado sobre a janela inteira. O sparkline é
a forma da série recente ao lado dele. Os dois atualizam conforme as traces vão terminando.

## Testar um endpoint

"Testar um endpoint" (na barra superior do `/flow`, ou a página `/runner`) envia uma requisição de
verdade para a sua API a partir do dashboard, para o grafo ter o que desenhar sem você sair do
navegador nem abrir um cliente REST.

O painel monta o formulário a partir do documento OpenAPI da sua própria aplicação: escolha um
endpoint, preencha parâmetros de rota e de query e um corpo JSON, envie. Isso exige um
`MapOpenApi()` (ou endpoint OpenAPI equivalente) na aplicação hospedeira, e avisa quando não
consegue carregar o documento. As rotas do próprio dashboard ficam de fora da lista.

Quando o documento OpenAPI declara um esquema de segurança (`http`/`bearer`, `apiKey`, `oauth2`,
`openIdConnect`), aparece uma seção "Autorizar" acima do formulário da requisição. Cole um valor
no campo mascarado de cada esquema. Um preview ao lado mostra o header ou o parâmetro de consulta
exato que vai ser enviado. O valor certo é aplicado automaticamente em toda requisição cuja rota
exige aquele esquema, e um header adicionado à mão sempre vence o automático. O valor colado vive
só no `sessionStorage` da aba atual: fechar a aba apaga o valor, e nada é gravado em disco.

## Exportar e importar uma trace

Uma trace aberta no `/flow` pode ser baixada como arquivo `.json` ("Baixar trace (.json)") e
aberta de novo em qualquer outra instância do dashboard ("Importar trace (.json)"). Os dois
acontecem inteiramente no navegador: exportar serializa a trace que já está em memória, importar
lê o arquivo localmente, e nenhum dos dois faz chamada HTTP em nenhum sentido. Quem recebe o
arquivo não precisa de acesso à API que gerou a trace, nem ao banco dela. É isso que torna uma
trace mandável para um colega ou colável numa conversa com um agente de IA.

Uma trace importada é desenhada pelos mesmos grafo, waterfall, cartões de métrica e feed de
evento de uma trace ao vivo, leva um selo visível de "Trace importado" enquanto estiver aberta, e
tem uma ação "Voltar aos traces ao vivo" para sair. Ela nunca é persistida: não entra no
`ITraceStore` e nunca aparece em `/history`, `/insights`, `/metrics` nem `/architecture`, então a
trace de outra pessoa não contamina as suas estatísticas. Um arquivo que não é uma exportação do
NetApiFlow mostra uma mensagem de erro clara, nunca uma tela em branco.

Clicar num span de uma trace importada monta os ancestrais e os filhos a partir do próprio
arquivo, sem ida ao servidor. Esse detalhe não traz duração mediana nem diferença para a mediana:
as duas vêm de execuções anteriores da mesma operação, e a instância que lê o arquivo não tem
histórico de uma trace que ela nunca gravou.

## Comparar duas execuções

Duas execuções da mesma rota podem ser comparadas lado a lado. No `/history`, marque exatamente
duas linhas e clique em "Comparar". Isso abre `/diff?a=<traceIdA>&b=<traceIdB>`, uma URL
compartilhável enquanto as duas execuções ainda existirem no `ITraceStore` de quem abrir o link.

As etapas são casadas por componente e operação, nunca por span id (um span id é único de uma
execução e nunca se repete em duas). Uma etapa repetida dentro da mesma execução, que é o que um
laço N+1 produz, é somada numa linha só, com a contagem de cada lado, em vez de encher a tabela
de linhas quase iguais. Uma etapa que só uma das duas execuções rodou aparece como "Não
executado" do outro lado, com o mesmo ícone e o mesmo texto que o grafo já usa, nunca como etapa
casada com duração zero. Etapa casada mostra a duração dos dois lados e a diferença entre elas.

Comparar duas execuções de rotas diferentes continua funcionando, só que toda etapa cai de um
lado só. A página marca isso como aviso, porque raramente é o que se queria fazer.

## Anotação de deploy

Um marco de deploy no `/history` ajuda a correlacionar um incidente investigado depois com um
deploy feito por outra pessoa, às vezes em outra máquina, em outro dia. Registre um logo depois do
deploy terminar, como uma etapa do seu pipeline de release:

```bash
curl -X POST https://sua-api.exemplo.com/flow/api/deploys \
  -H "Content-Type: application/json" \
  -d '{"label": "v1.4.2"}'
```

O servidor atribui o horário. Um relógio de cliente alguns segundos adiantado ou atrasado nunca
desloca a posição do marco na linha do tempo. O marco aparece no `/history` como uma linha de
largura total, na ordem cronológica entre as execuções ao redor dele, visível para quem abrir o
dashboard, não só para o navegador que criou o marco. Nesta versão não existe tela no próprio
dashboard para criar um marco, nem como editar ou apagar um marco já salvo.

## Preferências do dashboard

A interface do dashboard vem em português (pt-BR) por padrão. Troque para inglês pelo seletor de
idioma na barra superior. Ela também segue a preferência clara/escura do sistema automaticamente,
com um alternador manual ao lado do seletor de idioma. As duas escolhas sobrevivem a um
recarregamento. A versão do pacote mostrada vem sempre de uma chamada real a `GET /config`, então
bate com o que está instalado de fato.

Na listagem do histórico (`/history`), cada cabeçalho de coluna ordena o resultado inteiro no
servidor, não só a página atual. Combine "Só lentas" com ordenar por Duração (decrescente) para
ver as execuções mais lentas de todo o histórico.

## Insights

`/insights` é uma visão agregada sobre toda rota rastreável da API hospedeira, numa janela de
tempo escolhida (últimas 24 horas, últimos 7 dias por padrão, ou últimos 30 dias): as rotas mais
lentas por p95, as rotas com mais erros, e rotas mortas, aquelas que existem na API hospedeira mas
não rodaram na janela escolhida. As três vêm de `GET /metrics/routes` e `GET /endpoints`. Rotas
mais lentas e top erros são duas ordenações diferentes do mesmo payload, e rotas mortas é o
dashboard cruzando esse payload com toda rota que a API hospedeira expõe.

## Mapa de arquitetura

`/architecture` é o grafo estático de chamadas de uma rota (método HTTP e route template),
montado a partir de todas as execuções registradas numa janela de tempo escolhida, não só a
última: a mesma pergunta que o `/blueprint` responde a partir de uma trace ("o que essa rota
normalmente faz"), generalizada para a janela inteira. Escolha uma rota e uma janela. Um passo que
não rodou em toda execução mostra o percentual de ocorrência no próprio texto (ex.:
`TryGetAsync (14%)`), para um caminho condicional como um acerto de cache se destacar. Exporte o
grafo como Mermaid ou SVG, os dois gerados no navegador a partir do dado já carregado, sem
requisição extra.

> **O mapa reflete o que foi persistido, não necessariamente toda execução real.** Com a
> amostragem tail-based (abaixo) ligada e um `SuccessSampleRate` baixo, a maioria das execuções
> persistidas de uma rota é de erro ou lentas, então o mapa pende para esse caminho em vez do
> caminho de sucesso típico. Consequência esperada da amostragem, não um bug.

## Configuração

```csharp
builder.Services.AddNetApiFlow(options =>
{
    options.Enabled = true; // chave geral; false pula toda checagem a custo quase zero
    options.SlowThreshold = TimeSpan.FromMilliseconds(250);
    options.CaptureSqlText = false; // nunca captura valor de parâmetro, só habilita o texto do SQL em si
    options.Retention.MaxTraces = 1000;
    options.Retention.MaxAge = TimeSpan.FromHours(24);
    options.Limits.MaxSpansPerTrace = 300;
    options.Limits.MaxTagsPerSpan = 20; // valor padrão mostrado; tag além do limite é descartada e contada
    options.Limits.MaxTagValueLength = 4000; // valor padrão mostrado; valor maior é truncado, a tag não se perde
    options.IncludePaths("/api/**"); // só rastreia rota que bater; por padrão rastreia tudo
    options.ExcludePaths("/api/health"); // excluída mesmo que também bata com um padrão de inclusão
    options.ExternalSources = ExternalActivitySource.HttpClient | ExternalActivitySource.Npgsql; // opt-in; padrão é None
    options.CapturePrePipelineSteps = true; // opt-in; padrão é false, ver abaixo
    options.TailSampling.Enabled = true; // opt-in; ver Amostragem tail-based abaixo
    options.TailSampling.SuccessSampleRate = 0.1; // valor padrão mostrado
    options.LogCorrelation.Enabled = true; // opt-in; ver Correlação de log abaixo
    options.Limits.MaxLogEntriesPerSpan = 20; // valor padrão mostrado
    options.OpenApiDocumentPath = "/swagger/v1/swagger.json"; // padrão é /openapi/v1.json
});
```

`OpenApiDocumentPath` diz ao painel "Testar um endpoint" do dashboard onde a sua aplicação serve
o documento OpenAPI. O padrão é o que o `MapOpenApi()` serve. Aponte para
`/swagger/v1/swagger.json` no Swashbuckle, ou para onde você servir o documento. O painel lista os
seus endpoints a partir desse documento, então um caminho errado deixa o painel sem nada a mostrar.
As requisições a esse caminho nunca são rastreadas, então o documento que o dashboard busca não
aparece nos seus traces.

`CapturePrePipelineSteps` grava o que roda antes do seu controller ser chamado, em rotas de MVC
Controller: uma etapa por filtro de autorização, no componente `AuthorizationFilters`, com o nome
do tipo do filtro como operação, e uma etapa em volta do estágio de Resource filter, no
componente `ResourceFilters`, com o nome da action como operação. Essa última vira a mãe da etapa
`Controller` que já existe no grafo, então os estágios aninham em vez de duplicar. É a resposta
para "a requisição levou 400 ms mas o meu controller levou só 20 ms, onde foi o resto" quando o
tempo foi para a autorização ou para um resource filter, como output caching. Vem desligado por
padrão e não muda nada para quem não ligou. Rota de Minimal API não tem estágio de filtro do MVC,
então não ganha etapa nova de nenhum jeito.

`ExternalSources` ingere a `Activity` que o runtime .NET e os drivers de banco mais comuns já
produzem, sem nenhuma mudança de código nos seus serviços.

Em `Development` as três vêm ligadas por padrão, então uma instalação nova já mostra HTTP de saída
e SQL sem configurar nada. Em qualquer outro ambiente o padrão continua `None`. O que você
declarar explicitamente sempre vence, por configuração ou por código, inclusive voltar o valor
para `ExternalActivitySource.None` em desenvolvimento.

`HttpClient` cobre chamada de saída (ADR-0012). A URL de saída é reduzida a
`scheme://host[:porta]/path` antes de ser gravada. Query string e fragment nunca são capturados, a
mesma garantia de qualquer outro span.

`Npgsql` cobre comando SQL (ADR-0018), gravado como span `Database` nomeado pelo tipo do comando e
pela tabela principal, por exemplo `SELECT Users`. Quando você também usa
`NetApiFlow.EntityFrameworkCore`, os dois nunca gravam o mesmo comando duas vezes: a `Activity` do
próprio driver é descartada só quando um step `Database` já envolve a chamada, que é exatamente
o que o `FlowDbCommandInterceptor` abre. Dentro de um controller, um serviço, um repositório ou
qualquer outro step, o comando é gravado como filho desse step. Um step `Database` manual em volta
de SQL direto engole os comandos do driver que ficam dentro dele. Sem esse pacote (Npgsql puro, ou
Dapper sobre Npgsql), a `Activity` do driver é o único registro da chamada e é ingerida
normalmente. O texto
completo do SQL só chega ao span com `CaptureSqlText` ligado, mesma regra do interceptor do EF
Core.

`SqlClient` faz o mesmo para SQL Server, então uma consulta Dapper ou um `SqlCommand` cru aparece
como span `Database` em vez de deixar um buraco no fluxo. Contra o `NetApiFlow.EntityFrameworkCore`
vale a mesma regra do Npgsql (descartado só quando um step `Database` já envolve o comando), e a
regra do `CaptureSqlText` vale do mesmo jeito. Uma diferença que vale saber: diferente do Npgsql, o `Microsoft.Data.SqlClient` não publica
`ActivitySource` própria, então o NetApiFlow lê os eventos de diagnóstico do driver. Esses eventos
só trazem um payload legível a partir do `Microsoft.Data.SqlClient` 6.0. Se a sua aplicação fixa
um driver mais antigo, a flag é aceita mas não grava nada, e o NetApiFlow registra isso uma vez,
em nível `Information`, dizendo qual versão é necessária. Melhor que te deixar com a feature
ligada e o fluxo vazio.

Montando o dashboard num caminho diferente do padrão, e protegendo-o:

```csharp
app.UseNetApiFlowDashboard("/internal/flow")
    .RequireAuthorization("AdminOnly"); // ou registre um IFlowAccessFilter no DI
```

Persistindo num banco de verdade em vez de memória (chamar depois de `AddNetApiFlow`, substitui o
storage padrão):

```csharp
builder.Services.AddNetApiFlowPostgreSqlStorage(
    connectionString,
    options => options.TablePrefix = "netapiflow_"); // valor padrão mostrado
```

`NetApiFlow.Storage.SqlServer` espelha a mesma forma (`AddNetApiFlowSqlServerStorage`).

### Rodando sem permissão de DDL

Por padrão o storage cria e atualiza as próprias tabelas no primeiro uso, com um `CREATE TABLE`, um
`ALTER TABLE` e um `UPDATE` no banco da sua aplicação. Se o usuário de banco da aplicação não pode
rodar DDL, desligue isso e crie as tabelas você mesmo:

```csharp
builder.Services.AddNetApiFlowPostgreSqlStorage(
    connectionString,
    options => options.AutoCreateSchema = false);
```

Com `AutoCreateSchema = false` nada é criado nem alterado. No primeiro uso o storage só confere se as
tabelas e a coluna `route_key` existem. Se faltar algo, ele registra um erro que diz o que falta e
onde está o script, e não derruba a sua requisição. Rode o `schema.sql` você mesmo:
`src/NetApiFlow.Storage.PostgreSql/schema.sql` ou `src/NetApiFlow.Storage.SqlServer/schema.sql` no
repositório, na tag da versão que você instalou
(`https://github.com/Douglasproglima/netapiflow/tree/v<versão>/src`). Se você mudou o `TablePrefix`,
troque `netapiflow_` no script antes. O script cria o que falta e atualiza tabelas gravadas por uma
versão anterior, e pode ser rodado de novo sem problema. O script do SQL Server separa os lotes com
`GO`, então rode com `sqlcmd`, SSMS ou Azure Data Studio.

Enquanto as tabelas não existem, os traces que a sua aplicação produz não podem ser gravados. O
storage registra um aviso com o tipo e a mensagem da exceção, descarta por 30 segundos os traces que
chegam e tenta de novo. Os traces descartados são contados em `netapiflow.persistence.failures`.
Quando uma gravação volta a funcionar, ele registra uma linha com quantos traces não foram gravados.
O mesmo acontece quando o banco está inalcançável. A exceção completa é registrada em `Debug`.

## Segurança do dashboard em produção

> **O dashboard mostra detalhe interno de implementação da sua API: nome de componente, nome de
> operação, nome de tabela SQL, mensagem de erro.** Trate a exposição dele com o mesmo cuidado de
> qualquer outro endpoint de diagnóstico interno.
>
> Fora de `IHostEnvironment.IsDevelopment()`, toda rota do dashboard (REST, SSE e a própria UI)
> responde **404**, a menos que você configure explicitamente um dos dois:
>
> - `.RequireAuthorization("SuaPolicy")`, encadeado em `app.UseNetApiFlowDashboard(...)`.
> - Um `IFlowAccessFilter` próprio registrado no DI, checado em toda requisição,
>   independente do ambiente.
>
> Não existe padrão "parcialmente aberto". Sem nenhum dos dois configurados, produção sempre
> devolve 404.
>
> **Uma aplicação com `FallbackPolicy` que exige usuário autenticado** (comum em API com JWT)
> recebe **401** no dashboard, em qualquer ambiente: o middleware de autorização responde antes
> da checagem do próprio dashboard, e o navegador não manda o token bearer ao navegar para
> `/flow`. Em Development, deixe o dashboard passar:
>
> ```csharp
> var dashboard = app.UseNetApiFlowDashboard();
> if (app.Environment.IsDevelopment())
> {
>     dashboard.AllowAnonymous();
> }
> ```
>
> Fora de Development, encadeie só `.RequireAuthorization("SuaPolicy")`, ou encadeie
> `.AllowAnonymous()` e registre um `IFlowAccessFilter`, que passa a decidir toda requisição.
> Encadear `.AllowAnonymous()` junto com `.RequireAuthorization()` não autoriza nada: o
> `AllowAnonymous` vence a policy, então sem `IFlowAccessFilter` toda rota responde 404.
>
> Quando a aplicação sobe com um `FallbackPolicy` e um dashboard sem `.AllowAnonymous()` nem
> `.RequireAuthorization()`, o NetApiFlow registra um aviso que nomeia o 401 e o caminho para
> resolver, para ele não ficar sem explicação.

### Quais `Host` e `Origin` são aceitos

O dashboard e o endpoint do MCP conferem o `Host` e a `Origin` de toda requisição, em todo método: a
API REST, o stream ao vivo, os arquivos e o transporte do MCP. A conferência roda depois do seu
próprio pipeline, então o `UseForwardedHeaders` já definiu o `Host`. Uma requisição recusada recebe
**403**, com uma mensagem que diz o motivo e o que mudar.

- **Em Development**, o `Host` tem de ser `localhost`, `127.0.0.1` ou `[::1]`, ou um nome que você
  declara na configuração `AllowedHosts`. Um `"*"` não conta como declarar. É isso que barra o DNS
  rebinding: uma página de outro site, cujo nome resolve para a sua máquina, chega ao dashboard com
  esse nome como `Host`, então só o `Host` consegue recusá-la.
- **Em qualquer ambiente**, uma requisição com `Origin` tem de ter o host e a porta do `Host` que a
  aplicação enxerga. O esquema não é comparado. Uma requisição sem `Origin` passa, como um `GET` da
  mesma página ou um cliente que não é navegador, e um `Origin: null` é recusado.
- **Fora de Development**, o `Host` segue o seu próprio `AllowedHosts` e o `UseForwardedHeaders`.

Para abrir o dashboard em Development por um nome que não seja localhost, por exemplo do celular
pelo endereço da sua máquina na rede local, ou `host.docker.internal`, declare o nome:

```json
{ "AllowedHosts": "localhost;192.168.1.13" }
```

Uma entrada é um nome sem porta, e `*.example.com` casa subdomínios. O ASP.NET Core lê a mesma
configuração no próprio filtro de hosts, então, depois que a lista nomeia hosts específicos, mantenha
`localhost` nela, senão a aplicação responde 400 ao próprio `localhost`.

Atrás de um proxy reverso a aplicação precisa enxergar o `Host` original. Senão a `Origin` que o
navegador manda não casa com ele e um `DELETE` recebe 403. Mantenha o `Host` no proxy
(`proxy_set_header Host $http_host;` no nginx), ou chame o `UseForwardedHeaders` com
`ForwardedHeaders.XForwardedHost` antes do dashboard e deixe o proxy mandar o `X-Forwarded-Host`.
Um proxy que termina o TLS não precisa de nada para o esquema, porque ele não é comparado.

### O cabeçalho com o trace id

O NetApiFlow escreve `X-NetApiFlow-TraceId` na resposta de toda requisição rastreada, em qualquer
ambiente. Ele carrega o trace id, o mesmo valor que o cabeçalho `traceparent` do W3C já carrega, e é
por ele que o `NetApiFlow.Testing` acha o trace de uma resposta. Saber um trace id não abre o
dashboard: as rotas dele continuam atrás das regras acima.

Para manter o trace id fora das suas respostas, desligue o cabeçalho:

```csharp
builder.Services.AddNetApiFlow(options => options.EmitTraceIdHeader = false);
```

Ou no `appsettings.json`: `"NetApiFlow": { "EmitTraceIdHeader": false }`. As requisições continuam
rastreadas. O `GetFlowTrace` do `NetApiFlow.Testing` lê esse cabeçalho, então ele lança exceção
enquanto o cabeçalho estiver desligado.

### Cabeçalhos de segurança

Toda resposta do dashboard e do endpoint do MCP carrega `X-Content-Type-Options: nosniff`,
`Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` e `Content-Security-Policy: frame-ancestors
'none'`. Isso inclui o 403 e o 404 da conferência de acesso. Nenhuma outra página consegue mostrar o
dashboard num frame, então nada esconde o botão de apagar embaixo de um clique.

A página do dashboard em si carrega uma política completa:

```text
default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'
```

O `script-src` não tem `'unsafe-inline'`: o dashboard só roda os arquivos de script que ele traz. O
`style-src` tem, de propósito. O Angular acrescenta os estilos de cada componente como elementos
`<style>` quando a página roda, são dezenas, e um hash para cada um mudaria a cada build. Um estilo
inline não executa código. Se o seu próprio middleware define um `Content-Security-Policy` nessas
rotas, o do dashboard o substitui.

## Servidor MCP

```bash
dotnet add package NetApiFlow.Mcp
```

```csharp
builder.Services.AddNetApiFlowMcp();

var app = builder.Build();
app.UseNetApiFlowMcp(); // montado em /flow/mcp por padrão
```

Três tools só leitura, sobre o mesmo `ITraceStore` que o dashboard lê: `list_recent_traces`,
`get_trace` e `get_component_stats`. Nenhuma tool de escrita existe nesta versão. Nada apaga um
trace nem dispara um replay via MCP.

> **Mesma regra de segurança do dashboard.** Fora de `IHostEnvironment.IsDevelopment()`, toda
> rota do MCP responde **404**, a menos que você configure explicitamente
> `.RequireAuthorization(...)` ou um `IFlowAccessFilter` próprio, igual a
> [Segurança do dashboard em produção](#segurança-do-dashboard-em-produção). Um agente de IA
> lendo dado de trace tem o mesmo risco de exposição que o dashboard visual.
>
> O endpoint também recusa, com **403**, uma requisição cujo `Host` ou `Origin` não seja o dele. Em
> Development o `Host` tem de ser `localhost`, `127.0.0.1`, `[::1]` ou um nome declarado em
> `AllowedHosts`, e uma `Origin` tem de casar com o `Host` que a aplicação enxerga. Um cliente MCP
> que não é navegador, como o MCP Inspector ou o cliente C# oficial, não manda `Origin`, então não é
> afetado. Os detalhes, e o que configurar atrás de um proxy reverso, estão em
> [Quais `Host` e `Origin` são aceitos](#quais-host-e-origin-são-aceitos). As respostas dele carregam
> os mesmos [cabeçalhos de segurança](#cabeçalhos-de-segurança) do dashboard.

Veja `samples/SampleApi.Controllers/README.md` para como apontar um cliente MCP real (ex.:
Claude Code) para uma instância rodando.

## Exportação OTLP

```bash
dotnet add package NetApiFlow.OpenTelemetry
```

```csharp
builder.Services.AddNetApiFlowOtlpExport();
```

O núcleo do NetApiFlow já produz `Activity` reais na `ActivitySource` `"NetApiFlow"` (ADR-0002).
`AddNetApiFlowOtlpExport` aponta o SDK do OpenTelemetry para essa fonte e exporta via OTLP, para
qualquer backend compatível: Jaeger, Tempo, Aspire Dashboard, Honeycomb, ou um OpenTelemetry
Collector. Não existe serialização própria do NetApiFlow aqui. O SDK exporta a mesma `Activity`
que o dashboard lê.

```csharp
builder.Services.AddNetApiFlowOtlpExport(options =>
{
    options.IncludeHttpClientSource = true; // padrão: false, ver aviso abaixo
    options.ConfigureOtlpExporter = otlp => otlp.Endpoint = new Uri("http://localhost:4317");
});
```

> **`IncludeHttpClientSource` exporta a URL de saída inteira, inclusive query string.**
> Habilitá-lo também observa a `ActivitySource` nativa `"System.Net.Http"`, espelhando
> `NetApiFlowOptions.ExternalSources`, para o trace exportado ter os mesmos spans de HTTP de
> saída que o dashboard mostra. Essa `Activity` nativa nunca passa pelo sanitizador de URL do
> NetApiFlow (que só sanitiza o `FlowSpan`). O backend OTel recebe a URL como está. O padrão é
> `false` por esse motivo.

> **O volume exportado pode ser maior que o que o dashboard mostra.** O SDK do OpenTelemetry
> amostra de forma independente da decisão de amostragem do próprio NetApiFlow (tracing
> desligado, limite de 500 traces atingido). Uma requisição que o NetApiFlow descarta cedo ainda
> pode ser exportada. Nenhum dos dois pipelines bloqueia o outro. Isso é o resultado esperado de
> dois `ActivityListener` independentes observando a mesma fonte, não um bug.

## Amostragem tail-based

```csharp
builder.Services.AddNetApiFlow(options =>
{
    options.TailSampling.Enabled = true; // padrão: false, toda trace completa é persistida
    options.TailSampling.SuccessSampleRate = 0.1; // valor padrão mostrado, guarda 10% do resto
});
```

Desligado por padrão. Toda trace completa é persistida, igual a antes dessa opção existir.
Ligado, a decisão é tomada uma vez, quando a trace está pronta para persistência, antes de
chegar ao `ITraceStore`:

- Trace com erro, ou cuja requisição estourou tempo com o cliente ainda conectado, é sempre
  persistida. Cliente desconectando antes da requisição terminar não conta como erro aqui: segue
  a mesma amostragem de uma trace de sucesso, porque um cliente desistindo não diz nada sobre o
  servidor.
- Trace no limiar de `SlowThreshold` ou acima dele é sempre persistida. É o mesmo limiar já
  usado para colorir a UI, não um segundo conceito de "lento".
- Qualquer outra trace (sucesso, não lenta, sem desconexão de cliente) é persistida com
  probabilidade `SuccessSampleRate`.

> **A visão ao vivo nunca muda.** `GET /traces/live` e o stream SSE leem do gravador em memória,
> não do storage. Uma trace descartada pela amostragem continua aparecendo enquanto roda. Só o
> histórico persistido é afetado.

> **`/blueprint`, `/architecture` e as métricas agregadas passam a refletir a amostra, não o
> tráfego real, uma vez ligado.** Um `SuccessSampleRate` baixo pode deixar o `/blueprint` sem
> nenhuma trace de sucesso recente para repetir, faz o `/architecture` pender para as execuções de
> erro e lentas, que são sempre guardadas, e `/metrics`, `/metrics/components` e `/metrics/routes`
> contam só o que foi guardado. Esse é o resultado esperado da amostragem, não um bug. `GET
> /config` expõe o estado atual para o `/history` mostrar uma faixa visível quando a amostragem
> está ligada.

## Correlação de log

```csharp
builder.Services.AddNetApiFlow(options =>
{
    options.LogCorrelation.Enabled = true; // padrão: false
    options.LogCorrelation.MinLevel = LogLevel.Warning; // valor padrão mostrado
});
```

Desligado por padrão. Ligado, toda chamada de `ILogger` feita no nível de `MinLevel` ou acima,
enquanto um `FlowStep` está ativo, é correlacionada ao `Logs` daquele span. Fica ao lado do erro
do span, sem precisar abrir uma segunda ferramenta para ver o que a aplicação logou durante
aquela etapa. O mecanismo é um segundo `ILoggerProvider`, registrado ao lado dos provedores que a
sua aplicação já tem (Console, Application Insights etc). Ele nunca substitui nem filtra esses
provedores. O log continua indo para o destino de sempre, sem mudança. Uma chamada de log fora de
uma etapa rastreada (startup, um job de background não instrumentado) nunca é observada por este
provider.

**Com Serilog, passe `writeToProviders: true`.** O `builder.Host.UseSerilog(...)` troca a fábrica
de logs e, por padrão, não repassa os eventos aos outros provedores registrados, então o trace
fica sem logs mesmo com o console ainda imprimindo:

```csharp
builder.Host.UseSerilog(
    (context, configuration) => configuration.WriteTo.Console(),
    writeToProviders: true);
```

> **A sanitização aqui é o mesmo regex melhor esforço já usado em `FlowStep.MarkError`, não uma
> garantia nova ou mais forte.** Mensagem de log é texto livre escrito pelo seu próprio código, e
> pode em princípio conter qualquer coisa, inclusive dado que o sanitizador baseado em regex não
> reconhece. Por isso a feature nasce desligada, e `MinLevel` nasce em `Warning`: reduza o raio de
> impacto antes de decidir se o trade-off vale a pena para a sua aplicação.

## Dados sensíveis

O NetApiFlow nunca captura, em nenhuma configuração:

- Corpo de requisição ou resposta.
- Argumento de método ou valor de retorno.
- Header ou query string (rota é sempre gravada como template, ex.: `/api/orders/{id}`, nunca a
  URI resolvida).
- Valor de parâmetro SQL, mesmo com `CaptureSqlText = true` (essa flag só captura o texto do
  comando e a tabela principal envolvida).
- Query string ou fragment de URL de saída, quando `ExternalSources` está habilitado (mesma
  redução a `scheme://host[:porta]/path` usada em rota).
- String de conexão, usuário do banco ou endereço do servidor, quando
  `ExternalActivitySource.Npgsql` está habilitado. O driver reporta todos eles na própria
  `Activity`; nenhum é copiado para o span.

> **Uma requisição que não casa com nenhuma rota é gravada como `(unmatched)`, nunca o caminho
> real.** Um 404 de uma URL digitada errado e uma requisição bem-sucedida pra um arquivo estático
> acabam os dois gravados como `(unmatched)`, sem distinção entre eles. É a mesma garantia de
> template acima, só que pro caso em que ainda não existe template nenhum. Se arquivo estático ou
> o próprio documento OpenAPI estiverem poluindo o histórico com ruído de `(unmatched)`,
> `ExcludePaths` tira os dois do rastreamento de vez.

Mensagem de erro é sanitizada e depois truncada em 300 caracteres, em toda saída: o dashboard, as
ferramentas do MCP, o trace gravado, o log correlacionado e a tag `netapiflow.error.message` da
`Activity`, que é o que um exportador do OpenTelemetry envia. Sanitizar primeiro quer dizer que um
segredo cortado pelo limite não deixa pedaço nenhum.

O sanitizador vem ligado por padrão e é um filtro de formas conhecidas, não uma garantia de que
nenhum segredo sobrevive. O que ele remove: e-mail, CPF, CNPJ, sequência numérica longa, um JWT (três
segmentos base64url começando por `eyJ`), a senha dentro de uma URL (`esquema://usuario:senha@host`), e
o valor de qualquer par `chave=valor`, `chave: valor` ou `"chave": "valor"` em JSON cuja chave
contenha `password`, `passwd`, `pwd`, `token`, `secret`, `api-key` (também `api_key` e `apikey`),
`passphrase`, `private_key` ou `authorization`, em qualquer caixa. Chave entre colchetes,
como `config[password]`, também conta. As chaves do Azure `AccountKey`, `SharedAccessKey`,
`SharedAccessSignature` contam, e um `sig=` numa URL assinada também.

Como um valor termina:

- Valor entre aspas some inteiro, aspa escapada incluída. Quando é uma string entre aspas em JSON, o
  JSON em volta continua válido.
- O valor de `Authorization` inclui o esquema (`Bearer`, `Basic`, `Digest`, AWS) e os parâmetros.
  Um cabeçalho `Cookie:` ou `Set-Cookie:` perde o resto da linha.
- Um `Bearer <token>` solto é removido.
- Valor sem aspas termina no espaço ou no `;`, como um par de connection string. Então
  `password=ab;cd` deixa `;cd`.

Toda mensagem com uma dessas palavras seguida de `:` ou `=` perde o que vem depois, até uma
inofensiva como `Unexpected token: '}'`, porque um segredo não se distingue dela e vazar um é a
falha pior. Pelo mesmo motivo, `the bearer of the card` perde a palavra depois de `bearer`. Veja
[`docs/adr/0007-nunca-capturar-payload.md`](docs/adr/0007-nunca-capturar-payload.md).

O que ele não toca, ou toca só em parte:

- `Basic <base64>` sozinho, sem `Authorization`.
- `credentials: ...`, como em `Invalid credentials: ana`.
- Cookie no formato `name=value` sem o prefixo `Cookie:`, como `.AspNetCore.Cookies=...`.
- Valor sem aspas com espaço: ele é cortado no primeiro espaço, então `password=duas palavras` deixa
  `palavras`. Só um valor entre aspas pode ter espaço.
- Valor JSON que não é string: `{"token":12345}` vira `{"token":[redacted]`, e a chave de fechamento
  vai junto.
- Senha com `@` sem percent-encoding numa URL: `postgres://u:p@ss@host/db` deixa o que vem depois do
  primeiro `@`.

A única exceção é o que você mesmo escreve: as tags de uma etapa (veja
[Tags numa etapa](#tags-numa-etapa)) são gravadas e servidas exatamente como foram passadas, sem
nenhuma sanitização.

## Desempenho e overhead

Medido com `tests/NetApiFlow.Benchmarks` (BenchmarkDotNet), comparando o custo de uma requisição
contra um pipeline mínimo do ASP.NET Core com o NetApiFlow **ausente** (pacote não instalado),
**desligado** (`Enabled = false`) e **ligado** (opções padrão), em dois endpoints: `Simple`
(nenhuma etapa marcada) e `Nested` (quatro chamadas aninhadas de `NetApiFlow.Step`, espelhando
uma cadeia Controller→Service→Repository→Database).

| Cenário | Simple (média) | Nested, 4 etapas (média) |
|---|---|---|
| Ausente (pacote não instalado) | 6,22 μs | 11,79 μs |
| Desligado (`Enabled = false`) | 9,17 μs | 12,84 μs |
| Ligado (opções padrão) | 27,00 μs | 43,84 μs |

Ou seja, rastrear uma requisição com opções padrão custa cerca de 20 μs num endpoint sem
nenhuma etapa marcada, e mais uns 32 μs com quatro etapas aninhadas, neste hardware. Desligar o
rastreamento (`Enabled = false`) mantém a maior parte desse custo longe (o próprio registro do
listener tem um overhead pequeno e quase constante) sem precisar remover o pacote.

Método: cada cenário (ausente/desligado/ligado) roda no seu próprio processo, isolando o registro
do listener de `ActivitySource` do .NET, que é por processo: assim um cenário não contamina o
outro. Os relatórios completos do BenchmarkDotNet, com hardware e runtime, são gerados em
`tests/NetApiFlow.Benchmarks/BenchmarkDotNet.Artifacts/results/` a cada execução.

## Limitações conhecidas

- `Activity.Current` (e portanto o aninhamento de etapas) se perde através de
  `ExecutionContext.SuppressFlow()` ou um thread pool próprio que não propaga o contexto. Etapas
  criadas depois desse ponto são gravadas corretamente, mas como uma nova raiz, não aninhadas sob
  o trace da requisição.
- O rastreamento automático (`AddFlowTracked`/`DecorateFlowTracked`) não dá suporte a retorno do
  tipo `IAsyncEnumerable<T>` nesta versão.
- O rastreamento automático também não funciona sob Native AOT. Tanto `AddFlowTracked` quanto
  `DecorateFlowTracked` montam o proxy com `DispatchProxy`, que gera um tipo em tempo de execução, e
  o `DecorateFlowTracked` ainda casa tipos de implementação por reflexão. Os dois têm
  `[RequiresDynamicCode]` (e o `DecorateFlowTracked` também `[RequiresUnreferencedCode]`), então
  você recebe um aviso `IL3050`/`IL2026` na build em vez de uma falha na subida da aplicação. A
  forma de marcação que continua segura sob Native AOT e trimming é o `NetApiFlow.Step(...)`: uma
  aplicação que chama só `AddNetApiFlow()` e `Step(...)` publica com zero aviso de AOT ou trimming
  no net8.0 e no net10.0, e existe uma verificação no repositório que prova isso.
- O interceptor do `NetApiFlow.EntityFrameworkCore` é outra conversa. O interceptor em si não emite
  aviso nenhum, mas o próprio EF Core declara que não é totalmente compatível com Native AOT, e uma
  aplicação publicada que o usa acusa isso: `IL2026`/`IL3050` no construtor do `DbContext` e
  `IL2104`/`IL3053` nos assemblies do EF Core. Essa limitação é do EF Core, não do NetApiFlow, e
  nenhuma mudança aqui tira ela. Sob Native AOT, marque o acesso a banco com `NetApiFlow.Step(...)`
  se você quer uma publicação sem aviso.
- A visão ao vivo é por processo, não por cluster. O `GET /traces/live` lê o gravador em memória da
  réplica que respondeu àquela chamada. Atrás de um balanceador sem sticky session, o `/flow` mostra
  só o tráfego que aquela réplica atendeu, e o navegador pode cair em outra réplica já na
  requisição seguinte. O histórico persistido não sofre disso: com um pacote de storage
  configurado, todas as réplicas escrevem no mesmo banco, então o `GET /traces/{traceId}` e a tela
  de histórico acham a trace independentemente de qual réplica gravou.
- O `net8.0` saiu do ciclo de vida oficial da Microsoft em 10 de novembro de 2026, e o NetApiFlow
  continua publicando um artefato para ele. Ele fica por enquanto porque ainda não há para onde ir:
  a próxima LTS, o `net12.0`, só deve sair em novembro de 2027, então tirar o `net8.0` hoje
  deixaria o `net10.0` como alvo único. O `net8.0` só deixa de ser publicado quando o `net12.0`
  puder substituí-lo. Manter significa que o pacote tem um alvo que não recebe mais correção da
  Microsoft, o que vale saber se a sua própria política proíbe isso.
- Um app `net11.0` usa hoje o artefato do `net10.0`. Isso foi testado contra um SQL Server real,
  com o pacote de storage e o `Microsoft.Data.SqlClient` 7.1.2. Um artefato próprio para o
  `net11.0` está planejado.
- `net9.0` (STS) não é um alvo suportado. O multi-targeting cobre `net8.0` e `net10.0` (as duas
  LTS); quem consome via `net9.0` resolve para o artefato de `net8.0`, incluindo a implementação
  manual de SSE usada lá em vez do `TypedResults.ServerSentEvents` nativo do `net10.0`.

## Contribuindo

Issues e pull requests são bem-vindos. Este projeto segue Conventional Commits e Gitflow. Veja
`docs/gitflow.md` antes de enviar um PR.

## Apoie o projeto

O NetApiFlow é livre e de código aberto. Se ele economizou seu tempo, considere pagar um café
para o autor: chave PIX `douglasproglima@gmail.com`.

## Licença

MIT. Veja [LICENSE](LICENSE).

## Inspiração

O NetApiFlow é inspirado na ideia por trás do [apiflow](https://github.com/joshuamaia/apiflow)
(Spring Boot): visualizar o caminho de uma requisição numa API em tempo real. Nenhum código,
arquivo ou asset daquele projeto foi copiado ou portado. Tudo aqui foi escrito do zero, para
.NET.
