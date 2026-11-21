# ADR-0027: a decisão de rastrear passa para o `EndpointMatched`, e as rotas do NetApiFlow se identificam pelo endpoint

## Status
Aceito

## Discussion
[#386](https://github.com/Douglasproglima/netapiflow/discussions/386)

## Contexto

O NetApiFlow decide se rastreia uma requisição **antes** do pipeline do usuário, comparando o `Request.Path` cheio com
os padrões de `ExcludePaths`, `IncludePaths`, com o caminho do painel e com o `OpenApiDocumentPath`. O middleware
entra por `IStartupFilter`, antes do `UsePathBase` que o usuário chama, e o `UsePathBase` restaura `Path` e `PathBase`
ao terminar. Com `app.UsePathBase("/api")`, o caminho chega como `/api/flow/api/config` e nenhum padrão casa.

A Executora mediu a matriz inteira na Tarefa 53.0, em host real (Kestrel, sem `TestServer`), com 32 células
(`net8.0` e `net10.0`, Development e Production, com e sem `UsePathBase`):

- Sem `UsePathBase`: 56 de 56 requisições corretas.
- Com `UsePathBase("/api")`: 36 incorretas, iguais nos quatro TFM e ambiente. `ExcludePaths` não exclui, as rotas do
  painel (`config`, `traces`, `stream`) e do MCP (`initialize` e `tools/call`) são rastreadas, em Production elas
  respondem 404 e **mesmo assim** geram trace e cabeçalho, e o `OpenApiDocumentPath` é rastreado.
- **O mais grave:** `IncludePaths("/orders")` com `UsePathBase` deixa a rota do **usuário** fora do store. O defeito
  também apaga dado legítimo.
- Um segundo cliente SSE vê `TraceStarted` com `(unmatched)` e `Running` durante a conexão inteira do stream do painel.
- **Hospedagem:** o IIS (in-process e out-of-process, medido no IIS Express com o ANCM v2) não sofre, porque o
  framework define o `PathBase` antes do NetApiFlow, e o proxy que corta o prefixo também não. `UseForwardedHeaders`
  e `UsePathBase` do código do usuário entram sempre depois do NetApiFlow, e o `ASPNETCORE_PATHBASE` não aplica o
  prefixo. O defeito fica restrito ao `UsePathBase` explícito.

Opções, cada uma com um spike descartável, na mesma matriz:

| Opção | Resultado medido |
|---|---|
| **A. Adiar a abertura da trace até o `EndpointMatched`, e decidir pelo metadado de identidade nos endpoints do NetApiFlow e pelo caminho relativo no resto** | **Escolhida.** 28 de 28 corretas sem `PathBase` e 28 de 28 com. Cumpre os requisitos 1 a 4 |
| B. Abrir tudo no início e filtrar no fim pelo template do endpoint casado | Descartada: o cabeçalho sai e `TraceStarted Running` é publicado para a rota excluída e para o stream do painel, sem `TraceCompleted` (um fantasma `Running` no SSE). Sem `PathBase` também publica o que hoje é excluído, então o caminho comum paga |
| C. Adiar até o primeiro ponto em que o `PathBase` é conhecido | Não é um mecanismo à parte: esse ponto é o `EndpointMatched`, então é o A |
| D. Um middleware que o usuário chama depois do `UsePathBase` | Descartada: só o ponto do usuário vê o caminho certo, mas pede API pública nova e o trace teria de começar nele, perdendo o que vem antes |
| E. Ler o `PathBase` de outra fonte antes do pipeline | Não existe para o `UsePathBase` explícito. `ASPNETCORE_PATHBASE=/api` no Kestrel não muda nada |

## Evidência

- **Código:** `TracingRoutePolicy.ShouldTrace` recebe `context.Request.Path.Value`, e o `NetApiFlowMiddleware` o chama
  antes de `next`. A rota gravada na trace já vem do endpoint casado, lida no `finally`, e por isso escapa do problema.
- **Medição da Executora (Tarefa 53.0), com o mecanismo A:** requisitos 1 a 4 cumpridos. As rotas do painel, da API,
  do stream e do MCP nunca são rastreadas, por metadado e não por texto. `ExcludePaths`, `IncludePaths` e
  `OpenApiDocumentPath` valem contra o caminho relativo, inclusive o `IncludePaths` que antes apagava a rota do
  usuário. Para a rota excluída e para o stream do painel, nenhum cabeçalho e nenhum evento, medido por um cliente
  SSE. Custo medido grosseiramente num loop de 4000 requisições, com ruído alto: mediana de 86 e 85 µs por requisição
  hoje contra 91 e 98 µs com o A. É um indício, **não** um benchmark.
- **Falha o requisito 5:** sem endpoint casado (arquivo estático, 404) a decisão cai no caminho cheio, e o estático
  excluído por `/static/**` continua virando `(unmatched)` sob `PathBase`.
- **Não medido:** o efeito do A sobre `CapturePrePipelineSteps` e sobre o pai da `Activity` de entrada (a trace passa a
  abrir depois de alguns middlewares), e o requisito 7 (o spike não tem o `try/catch`).

## Decisão

Opção A, com uma emenda da auditoria (20/11/2026): o metadado de identidade é gravado por uma extensão pública, e não por um tipo `internal` lido com `InternalsVisibleTo`.

- **As rotas do NetApiFlow se identificam pelo endpoint, por um metadado que uma extensão pública grava.** O núcleo
  ganha `WithoutNetApiFlowTracking<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder`,
  simétrica à `WithNetApiFlowTracking()`. O painel (API REST, stream SSE, arquivos estáticos) e o MCP a chamam nos
  grupos deles, e esses endpoints **nunca** são rastreados, com ou sem `PathBase`.
- **O tipo do metadado fica `internal` no núcleo, e o pacote do MCP não ganha `InternalsVisibleTo`.** O `.nuspec` do
  painel e do MCP pede o núcleo `>=` a uma versão, então um tipo interno que mudasse num núcleo atualizado sozinho
  quebraria o pacote antigo, em tempo de execução, com `MissingMethodException`. É a mesma classe do D-140
  (MediatR). `InternalsVisibleTo` só é seguro com pacotes de versão travada igual, e os nossos não travam. Um método
  público só muda por SemVer.
- **A extensão é genérica sobre `IEndpointConventionBuilder`**, e não sobre `RouteHandlerBuilder` e `RouteGroupBuilder`
  como a `WithNetApiFlowTracking()`, porque assim cobre `MapGet`, grupo, `MapControllers()` e `MapHealthChecks()`.
- **O usuário ganha a exclusão por metadado.** A mesma extensão serve às rotas dele (health check, métricas), vale
  sob `PathBase` e **vence** `ExcludePaths`, `IncludePaths` e `WithNetApiFlowTracking()` no mesmo endpoint. Controller
  por atributo fica fora.
- **A trace abre no `EndpointMatched`**, o mesmo evento do roteamento que a Fase 51 já usa para a etapa `Endpoint`
  (ADR-0026). Ali o `Request.Path` é relativo ao `PathBase`, o endpoint é conhecido, e a política (`ExcludePaths`,
  `IncludePaths`, `OpenApiDocumentPath`) é avaliada contra esse caminho relativo.
- **O middleware, no início, só prepara:** guarda a hora de início da requisição, gera o id próprio da trace (ADR-0028),
  e registra um `Response.OnStarting` que escreve `X-NetApiFlow-TraceId`. Ele não publica nada.
- **A trace abre com a hora de início real e com a rota já conhecida.** Para uma requisição com endpoint casado, não
  existe mais o marcador `(unmatched)` no começo.
- **Requisição sem endpoint casado** (arquivo estático, 404): a decisão acontece no fim do pipeline, pelo caminho cheio,
  como hoje, e a trace é gravada como `(unmatched)`. Este é o **limite do requisito 5**, e o README o diz, nos dois
  idiomas, com o que o usuário faz (um padrão com o prefixo).
- **O cabeçalho sai só para a trace que será gravada.** O `OnStarting` consulta, no momento em que os cabeçalhos vão
  ao cliente, se a requisição foi aceita. É o mesmo mecanismo da Tarefa 50.13 (a resposta 500 sem o cabeçalho), e as
  duas não podem divergir.
- **As etapas criadas antes de a trace abrir não podem se perder** (`CapturePrePipelineSteps`). A forma, guardá-las no
  recorder pelo id e descarregá-las na abertura, ou descartá-las se a requisição for excluída, **é medida na Tarefa
  53.1 antes de qualquer código**, e se não houver forma, a Executora para e relata.
- **Falha da própria decisão** é logada e engolida, e o padrão é **não rastrear** (`.claude/rules/tracing.md`): na
  dúvida, nenhum dado sai.

## Consequências

- Cumpre os requisitos 1 a 4 da Fase 53, e o 5 fica como limite dito. Herda o **R-32**: `EndpointMatched` é evento de
  diagnóstico e não API formal, e funcionou nos dois TFMs medidos.
- **A Fase 57 ganha o primeiro terço de graça:** com a trace abrindo no `EndpointMatched`, ela já abre com a rota
  conhecida e o estado "Aguardando" deixa de ser `(unmatched)`. Sobra o blueprint, que precisa ser o último sucesso
  **anterior** à trace.
- **Depende da Fase 51**, que cria o listener do `EndpointMatched`, e **do ADR-0028**: o id e o estado ambiente nascem
  no início, e a trace abre depois. Por isso as etapas anteriores à abertura precisam de um lugar (a 53.1 mede).
- **Custo:** a medição de 86 para 91 e 98 µs é só um indício. A Tarefa 53.1 refaz com `tests/NetApiFlow.Benchmarks` e
  `[MemoryDiagnoser]`.
- **O README e o XML doc** de `OpenApiDocumentPath`, `ExcludePaths` e `IncludePaths` passam a dizer o que vale sob
  `PathBase`, nos dois idiomas. A frase "never traced" do `OpenApiDocumentPath` passa a ser verdadeira para requisição
  com endpoint casado, e o limite do estático é dito ao lado.
- **API pública nova: `WithoutNetApiFlowTracking()`.** Entra em `contratos.md`, no README e em `### Added` do
  `CHANGELOG.md`, com teste de que uma rota do usuário marcada, sob `PathBase`, não é rastreada. O requisito 6 da
  Fase 53 ("sem API pública nova") cai por decisão da auditoria, e o método público é o que protege o painel e o
  MCP de um núcleo atualizado sozinho.
- **O README diz o que foi medido e o que não foi:** Kestrel, IIS Express com o ANCM v2 e o nginx da Tarefa 53.0, e
  não o IIS completo nem o YARP. Não promete o que não se mediu.
