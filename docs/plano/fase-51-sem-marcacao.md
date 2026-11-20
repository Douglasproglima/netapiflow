# Fase 51: o grafo nunca vazio, e as camadas com uma linha (D-187)

Decisão do dono (20/11/2026): fazer os dois degraus baratos do rastreamento sem marcação. É capacidade
nova com API pública nova, então é **MINOR e vai para o `0.6.0`**, não para um PATCH. O mecanismo do
degrau 1 está no ADR-0026 (D-187, D-192).

- **Degrau 1.** Todo endpoint de Minimal API aparece no grafo sem marcação.
- **Degrau 2.** Um atalho público de uma linha para serviços, repositórios e handlers.

Branch `feature/51-sem-marcacao` a partir de `develop`. Esta fase mexe no Quick start dos dois READMEs,
que a Fase 50 também toca (Parte A, achado b): a Executora faz a Fase 50 Parte A primeiro, ou resolve
o conflito mantendo as duas mudanças.

## Ponto de parada: regra de recuo

A Executora para e relata, sem improvisar, se vir qualquer um destes sinais:

- Um endpoint que lança exceção não ficar com a etapa `Endpoint` em `Error` e como origem do erro, com
  e sem `UseExceptionHandler`, depois de tentar o desenho da Tarefa 51.4.
- O custo por requisição com a opção ligada passar de uma etapa: o benchmark publicado dá cerca de
  8 µs por etapa, e o resultado medido não pode ser o dobro disso.
- O evento `Microsoft.AspNetCore.Routing.EndpointMatched` não disparar, ou vir sem o endpoint, em
  qualquer um de `net8.0`, `net10.0` e `net11.0`.
- O `AotCoreVerification` ganhar um aviso.
- Uma classe do .NET, de pacote de terceiro ou do próprio NetApiFlow aparecer como etapa por causa do
  atalho do degrau 2.
- A etapa do endpoint abrir numa requisição que o NetApiFlow não rastreia, nos testes T7 ou T11 (D-192).

## Degrau 1: todo endpoint aparece sem marcação

### O fato

Num app do modelo padrão do .NET (Minimal API), sem `.WithNetApiFlowTracking()` o trace tem 0 etapas e
o grafo fica vazio. Controllers MVC já ganham a etapa pelo `FlowControllerActionFilter`.

### O que a investigação achou

A restrição conhecida: o `NetApiFlowMiddleware` entra antes do roteamento, pelo `IStartupFilter`, então
quando ele começa o endpoint ainda não foi escolhido. E o filtro de endpoint é montado rota por rota.

**Documentação oficial.**

- A página "Filters in Minimal API apps" (Microsoft Learn) só documenta `AddEndpointFilter`,
  `AddEndpointFilter<T>` e `AddEndpointFilterFactory`, por endpoint ou por grupo. Não descreve nenhuma
  forma de aplicar um filtro a todos os endpoints de Minimal API de uma vez.
- A página "ASP.NET Core Middleware" diz que o `WebApplication` adiciona o roteamento sozinho: "UseRouting
  is added second, if the user code didn't already call UseRouting and endpoints are configured".

**Código-fonte do ASP.NET Core.** `src/Http/Routing/src/EndpointRoutingMiddleware.cs`, branch `main` do
`dotnet/aspnetcore`: a constante `DiagnosticsEndpointMatchedKey` vale
`"Microsoft.AspNetCore.Routing.EndpointMatched"` (linha 22). O middleware escreve esse evento no
`DiagnosticListener`, com o `HttpContext` como payload, quando `IsEnabled` é verdadeiro (linhas 152 a
154 e 190 a 193), e só depois chama `_next(httpContext)` (linha 186). Ou seja, o evento sai logo
depois de o endpoint ser escolhido e antes de ele executar.

**Protótipo da Planejadora**, descartável, fora do repositório, com um listener nesse evento, um
`IStartupFilter` com um middleware externo e um `ActivitySource` de teste, em `net8.0` (8.0.34),
`net10.0` (10.0.15) e `net11.0` (11.0.0), iguais nos três:

- O evento dispara com o `HttpContext`, e o endpoint já está resolvido.
- Uma `Activity` aberta nesse ponto tem como pai a `Microsoft.AspNetCore.Hosting.HttpRequestIn`. O handler
  do endpoint enxerga essa `Activity` em `Activity.Current`, e uma `Activity` aberta dentro do handler
  tem a etapa como pai.
- O middleware externo, no `finally`, fecha a etapa, e `Activity.Current` volta para a
  `HttpRequestIn`.
- Num controller o evento também dispara, e o endpoint traz `ControllerActionDescriptor` nos metadados.
  Num Minimal API ele traz `MethodInfo` e `HttpMethodMetadata`.

### Mecanismos considerados

| Mecanismo | Resultado |
|---|---|
| **A. Listener no evento `EndpointMatched`, etapa fechada pelo middleware externo** | **Escolhido.** Funciona nos três runtimes, não altera nenhum endpoint, e o fechamento reaproveita um middleware que já existe |
| B. Decorar o `EndpointDataSource`, trocando cada `RouteEndpoint` por outro com o `RequestDelegate` embrulhado | Descartado: troca instâncias de endpoint que outro código pode guardar, e tem de repassar o token de mudança do data source |
| C. Sintetizar uma raiz ao fechar o trace e reparentear as etapas de topo | Descartado: o SSE já publicou cada etapa quando ela terminou, e o pai delas mudaria depois |
| D. Um filtro de endpoint global | Não existe API documentada |
| E. Um `MapGroup` do usuário | Só vale para o que for mapeado dentro dele, e exige código do usuário |

O mecanismo A tem precedente neste repositório: `SqlClientCommandListener` e
`FlowAuthorizationFilterListener` também abrem etapa a partir de evento de `DiagnosticListener`. A decisão está no ADR-0026.

**O risco do mecanismo A, dito sem enfeite.** `EndpointMatched` é um evento de diagnóstico que está no
código-fonte, e eu não o encontrei descrito nas duas páginas da documentação oficial que li. Não é uma
API formal. A mitigação: o teste de host real da Tarefa 51.0 roda nos três runtimes e falha de forma
visível se o evento deixar de disparar, e `WithNetApiFlowTracking()` continua funcionando. Registrado
como R-32.

### Tarefa 51.0: reproduzir e provar, antes de corrigir

Teste de **host real** (`.claude/rules/testing.md`), visto falhando antes de qualquer mudança: um app com
`AddNetApiFlow()`, um endpoint de Minimal API **sem** marcação, uma requisição, e o trace lido do
`FlowRecorder` do host esperando uma etapa `Endpoint`. Hoje o trace tem 0 etapas.

### Tarefa 51.1: a opção

`NetApiFlowOptions.CaptureMinimalApiEndpoints`, padrão **`true`** (texto em `contratos.md`).

A decisão sai das opções **resolvidas**, nunca da lambda do `AddNetApiFlow` (a causa do D-175, que a
Fase 47 corrigiu): o listener é sempre registrado, e um hosted service assina o `DiagnosticListener` só
se a opção resolvida mandar, no mesmo molde do `SqlClientCommandListenerWarmupHostedService`. As
invariantes do `Start()` são as do `SqlClientCommandListener`: chamar duas vezes assina uma vez só,
`Start()` depois de `Dispose()` não assina, `Dispose()` sem `Start()` não lança, e um
`internal bool IsSubscribed` deixa o teste ler o estado.

Assinar o `DiagnosticListener` **com o predicado** de nome
(`value.Subscribe(observer, name => name == "Microsoft.AspNetCore.Routing.EndpointMatched")`), para o
hosting não gerar os outros eventos do `Microsoft.AspNetCore` por causa do NetApiFlow.

### Tarefa 51.2: abrir e fechar a etapa

O listener, ao receber o evento com o `HttpContext`:

- **só age numa requisição que o NetApiFlow rastreia.** O `NetApiFlowMiddleware` marca no `HttpContext`,
  sob uma chave interna, que o trace começou (`TryStartTrace` verdadeiro), e o listener só abre a etapa
  com essa marca. Não basta confiar em `NetApiFlow.Step` devolver etapa vazia sem trace: ele só faz
  isso quando nenhuma fonte tem ouvinte (`HasListeners`), e com um segundo `ActivityListener` na fonte
  `NetApiFlow` (o do `NetApiFlow.OpenTelemetry`) a `Activity` nasce mesmo sem trace ativo e vai para o
  exportador. Requisição em `ExcludePaths`, nas rotas do dashboard, no `OpenApiDocumentPath` ou com
  `Enabled = false` nunca ganha etapa (D-192);
- só age num `RouteEndpoint` que não seja de MVC nem de Razor Pages, isto é, sem `ActionDescriptor` nos
  metadados, e com `MethodInfo`, que é o que um Minimal API traz;
- abre a etapa `Endpoint` com o **mesmo** componente e a mesma operação que o `FlowEndpointFilter` grava
  (`endpoint.DisplayName ?? "Endpoint"` e `RoutePattern.RawText ?? componente`), extraídos para um só
  lugar, para os dois nunca divergirem;
- guarda a etapa em `HttpContext.Items`, sob uma chave interna.

O `NetApiFlowMiddleware` fecha essa etapa no `finally` da requisição, antes de fechar o trace.

A duração da etapa vai do roteamento ao fim da requisição, então inclui os middlewares que ficam entre
os dois (autenticação, por exemplo). O `FlowEndpointFilter` mede só os filtros e o handler. A diferença é
aceita e vai para a documentação.

### Tarefa 51.3: nunca duas etapas

- `FlowEndpointFilter` vira no-op quando a etapa implícita do endpoint já está aberta na requisição.
  `.WithNetApiFlowTracking()` continua público e continua valendo: é o que garante a etapa quando a
  opção está desligada.
- Ações MVC continuam só com a etapa do `FlowControllerActionFilter`.

### Tarefa 51.4: o erro tem de se comportar como hoje

O `FlowEndpointFilter` marca erro quando o handler lança, porque está dentro do endpoint. A etapa
implícita é fechada de fora, então a Executora precisa reproduzir isso:

- exceção que chega ao `NetApiFlowMiddleware`: etapa em `Error` e como origem do erro;
- exceção traduzida por `UseExceptionHandler`: o `IExceptionHandlerFeature` do `HttpContext` traz o erro
  original, e é dele que a etapa tira o tipo e a mensagem (sanitizada pelo `ErrorMessageSanitizer`).

**Pronto quando:** os dois casos têm teste de host real. Se a paridade não vier, parar e relatar.

### Tarefa 51.5: o custo

Medir de novo com `tests/NetApiFlow.Benchmarks`, antes e depois, um endpoint de Minimal API sem etapa
marcada. Colar os números no resumo. Se o custo por requisição passar do dobro do publicado por etapa,
é ponto de parada. Se o número publicado no README mudar, atualizar os dois idiomas.

### Os testes do degrau 1 (host real, em `net8.0` e `net10.0`)

| # | Cenário | Esperado |
|---|---|---|
| T1 | Endpoint de Minimal API sem marcação | Uma etapa `Endpoint`, com o componente e a operação do filtro |
| T2 | O mesmo endpoint com `.WithNetApiFlowTracking()` | Continua com uma só |
| T3 | Ação de MVC | Só a etapa do controller, nenhuma `Endpoint` |
| T4 | Opção em `false`, no código e só na configuração | Sem marcação, 0 etapas. Com marcação, 1 |
| T5 | Endpoint dentro de `MapGroup(...).WithNetApiFlowTracking()` | Uma etapa |
| T6 | Endpoint que lança, com e sem `UseExceptionHandler` | Etapa em `Error`, origem do erro (Tarefa 51.4) |
| T7 | Requisição ao dashboard, ao `OpenApiDocumentPath` e a um caminho de `ExcludePaths`, e com `Enabled = false` | Sem trace e sem etapa |
| T8 | 16 requisições concorrentes | Cada trace com uma etapa `Endpoint`, nenhuma cruzada |
| T9 | Uma `NetApiFlow.Step` dentro do handler | `ParentSpanId` igual ao da etapa `Endpoint` |
| T10 | `MapHealthChecks` e arquivos estáticos | Sem exceção e sem etapa duplicada. A Executora lista no resumo quais outros endpoints passam a ter etapa |
| T11 | Os mesmos casos do T7 com um segundo `ActivityListener` gravando a fonte `NetApiFlow`, o cenário do OpenTelemetry | Nenhuma `Activity` de etapa é criada: a marca do middleware impede a abertura |

## Degrau 2: serviços e repositórios com uma linha

### O fato

Isso já funciona com `DecorateFlowTracked(predicado)`, mas o usuário precisa escrever o predicado, e o
mecanismo tem três armadilhas (`src/NetApiFlow/Marking/FlowTrackedServiceCollectionExtensions.cs`):

1. **Ordem.** Varre o `IServiceCollection` na hora da chamada. Chamado antes dos registros, não faz
   nada e não avisa.
2. **Alcance.** O predicado do exemplo do README, `type.Name.EndsWith("Service")`, também pega tipos que
   não são do usuário.
3. **Fábricas.** Um registro por fábrica é ignorado em silêncio.

### A armadilha 2 foi confirmada pela Planejadora

Um app com `AddNetApiFlow()`, `AddAuthorization()` e
`DecorateFlowTracked(type => type.Name.EndsWith("Service"))`:

- **`net8.0`:** `IAuthorizationService` resolve para um `generatedProxy`. O `DefaultAuthorizationService`
  do framework ganhou proxy e etapa. É o exemplo da auditoria, confirmado.
- **`net10.0`:** o framework renomeou a classe para `DefaultAuthorizationServiceImpl`, que o predicado não
  pega, então esse exemplo específico não reproduz lá.
- **Nos dois:** o predicado pega o `HttpsConfigurationService` do Kestrel e **quatro serviços hospedados
  do próprio NetApiFlow** (`RetentionService`, `NetApiFlowWarmupHostedService`,
  `SqlClientCommandListenerWarmupHostedService`, `FlowAuthorizationFilterListenerWarmupHostedService`),
  todos registrados como `IHostedService`. O host sobe sem erro, mas as chamadas de
  `StartAsync`/`StopAsync` passam por proxy.

### Tarefa 51.6: o atalho público

`services.TrackFlowByConvention(Action<FlowConventionOptions>? configure = null)`, texto em
`contratos.md`. O que ele garante:

- **Convenções de nome**, cada uma com o `ComponentType` certo: `Service` vira `ComponentType.Service`,
  `Repository` vira `Repository`, `Handler` vira `UseCase`.
- **Só tipos do app.** Sem configuração, só o assembly de entrada. Qualquer `IncludeAssembly(...)` ou
  `IncludeNamespacePrefix(...)` troca o escopo pelos informados, o que cobre o app em camadas, com os
  repositórios noutro assembly. Nunca tipos do .NET, de pacotes de terceiros nem do próprio NetApiFlow.
- **Handler de MediatR fica de fora.** Registro cujo tipo de serviço está no namespace `MediatR` não
  ganha proxy: o `NetApiFlow.MediatR` já cria a etapa `UseCase`, e a mesma classe nunca ganha duas.
- **A ordem.** O atalho aplica na hora, como o `DecorateFlowTracked`. Mais um hosted service que, ao
  subir o host, escaneia o mesmo `IServiceCollection` de novo e **avisa em log**, nomeando os tipos, se
  achar um registro do app que casa com a convenção e não foi decorado, o que acontece quando o atalho
  foi chamado antes dos registros. A mensagem diz para chamá-lo depois dos registros.
- **Fábricas.** Um registro por fábrica cujo tipo de serviço é do app e casa com a convenção não pode ser
  decorado, porque o tipo concreto não se conhece sem chamar a fábrica. O atalho **avisa em log**,
  nomeando o serviço e dizendo para registrar por tipo ou usar `AddFlowTracked<,>`.
- **Continua opcional.** O `AddNetApiFlow` nunca liga isso sozinho.
- Herda `[RequiresDynamicCode]` e `[RequiresUnreferencedCode]` do `DecorateFlowTracked`. O
  `AddNetApiFlow()` continua AOT-limpo.

**Por que não trocar o `IServiceProviderFactory`** para aplicar "no fim do registro": é o que o
`builder.ConfigureContainer` permitiria, mas substituir a fábrica de provedor desliga em silêncio quem
usa Autofac ou outro contêiner. Descartado.

### Tarefa 51.7: o `DecorateFlowTracked` deixa de pegar o NetApiFlow

`DecorateFlowTracked` passa a ignorar registros cujo tipo concreto seja do próprio NetApiFlow, qualquer
que seja o predicado. Ninguém quer proxy nos serviços hospedados do pacote. Mudança de comportamento
pequena e segura, sem mudar a assinatura. O exemplo do README, que usa `EndsWith("Service")` e
`EndsWith("Repository")`, passa a usar o atalho, e o exemplo com predicado ganha a condição de assembly
(`type.Assembly == typeof(Program).Assembly &&`).

### Os testes do degrau 2 (host real)

| # | Cenário | Esperado |
|---|---|---|
| S1 | `OrderService`, `OrderRepository` e `CreateOrderHandler` no assembly de entrada | Etapas `Service`, `Repository` e `UseCase`, com o `ComponentType` certo |
| S2 | O que não é do app: tipo do framework, do Kestrel, do NetApiFlow | Nenhum descritor trocado por proxy |
| S3 | Atalho chamado antes dos registros | Aviso em log nomeando os tipos |
| S4 | Registro por fábrica | Aviso em log nomeando o serviço |
| S5 | `AddNetApiFlow()` sozinho | Nada decorado |
| S6 | `DecorateFlowTracked(type => type.Name.EndsWith("Service"))` | Os quatro serviços hospedados do NetApiFlow continuam sem proxy |
| S7 | Handler de MediatR com o `NetApiFlow.MediatR` | Uma etapa `UseCase`, nunca duas |
| S8 | App em camadas, repositório noutro assembly | Sem configuração não pega, com `IncludeAssembly` pega |

### Tarefa 51.8: o estado vazio do grafo depois do degrau 1

A Tarefa 50.2 faz o grafo de um trace sem etapa explicar como marcar um endpoint
(`.WithNetApiFlowTracking()`). Depois do degrau 1 esse texto fica errado para o caso comum: um endpoint de
Minimal API passa a ter a etapa `Endpoint` sozinho, então um trace sem etapa já não significa "falta
marcar". O `merge-auditor` apontou, por leitura de código, que a condição do estado vazio
(`trace.spans.length === 0 && trace.status !== 'Running'`, `flow-page.component.html`) também pega os
traces `(unmatched)`, de 404 e de arquivo estático, que não têm rota para marcar.

**Trabalho:** com o degrau 1 pronto, a Executora reproduz os três casos no navegador antes de mexer e
ajusta o texto para dizer a causa certa de cada um:

- trace `(unmatched)`: a requisição não casou com nenhuma rota, e não há endpoint para marcar;
- trace sem etapa de uma rota que não é de Minimal API (controller, por exemplo), ou com
  `CaptureMinimalApiEndpoints = false`: o texto da 50.2 continua valendo;
- trace de Minimal API com a captura ligada e mesmo assim sem etapa: não deve acontecer, e se acontecer é
  defeito do degrau 1, não texto de ajuda.

Os dois idiomas, com teste de componente para cada caso, visto falhando antes. As duas frases do README
que falam do grafo vazio (`README.md` e `README.pt-BR.md`, perto de "the graph stays empty"), que a 50.2
já corrige para o que vale hoje, são conferidas de novo nesta tarefa.

## Documentação, nos dois idiomas

O Quick start mostra o grafo funcionando **sem marcação** (degrau 1) e o atalho de uma linha, chamado
depois dos registros, para as camadas (degrau 2). A tabela "Marking forms" explica que
`.WithNetApiFlowTracking()` só é necessário com a opção desligada. A seção de limitações do README
deixa de mencionar a necessidade de marcar o endpoint. O README em português acompanha o mesmo
conteúdo, sem travessão.

`CHANGELOG.md`, `[Unreleased]`, em `### Added`: o endpoint de Minimal API passa a ter etapa sem
marcação (com a opção para desligar) e o atalho de uma linha para as camadas. Em `### Changed`, o
comportamento padrão novo, cada endpoint de Minimal API ganha uma etapa por requisição, com
`CaptureMinimalApiEndpoints = false` para voltar ao de antes, e o `DecorateFlowTracked` ignorar os tipos
do próprio NetApiFlow (D-192).

## Pronto quando

- Um app novo do `dotnet new webapi`, com as três linhas do Quick start, mostra o nó do endpoint sem
  nenhuma marcação.
- Com o atalho e um `IOrderService`/`IOrderRepository` de exemplo, o grafo mostra Endpoint, Service e
  Repository.
- Nenhuma classe do .NET aparece como etapa.
- Tudo provado por teste de host real (`.claude/rules/testing.md`) e pelo teste com o pacote real da
  skill `/release`, no caminho do usuário (D-186): `dotnet new webapi`, o Quick start seguido ao pé da
  letra, e as telas em navegador de verdade.

## Critério que vale para a fase inteira

- Build, testes, formato e lint limpos nos dois TFMs e no Angular.
- `tests/manual-verification/AotCoreVerification` continua com zero aviso.
- Os testes novos entram em `ci.yml` e `release.yml`, na mesma lista (D-183).

## Fora de escopo

- Rastrear serviço e repositório sem interface registrada no DI: o degrau caro do item 2 do roadmap,
  que continua exigindo ADR e fase própria.
- Escolher Minimal API só pelo usuário: decidir quais outros tipos de endpoint ganham etapa fica para o
  que o teste T10 mostrar.

## Versionamento e fluxo

- MINOR, `0.6.0` (decisão do dono). Duas APIs públicas novas: `CaptureMinimalApiEndpoints` e
  `TrackFlowByConvention` com `FlowConventionOptions`.
- Branch `feature/51-sem-marcacao` a partir de `develop`. Commits pequenos, em inglês, uma linha, sem
  linha de coautor.
- Ao terminar: abrir a PR contra `develop` e parar. Tem código, então o merge exige confirmação do dono.

## Relatório final

Os testes T1 a T11 e S1 a S8: a saída de cada um falhando antes e passando depois. O resultado do
benchmark antes e depois. A lista de endpoints que passaram a ter etapa além do Minimal API (T10). O
`AotCoreVerification`. O resultado do teste com o pacote real no caminho do usuário, com a captura de
tela do grafo sem marcação. Qual arquivo de teste prova o R-32.
