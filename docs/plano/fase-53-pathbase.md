# Fase 53: o NetApiFlow não conhece o `PathBase` do app (D-204, R-33)

A Executora achou isto ao provar a Tarefa 50.4 (o `OpenApiDocumentPath`) num host real com
`app.UsePathBase("/api")`: o documento OpenAPI era rastreado. A Planejadora reproduziu de novo contra o
pacote publicado e o problema é maior e anterior à 50.4.

**Esta fase começa por investigação.** Nenhuma correção entra antes da Tarefa 53.0 provar o que
falha e do ADR que a Planejadora escreve depois dela.

## O fato

App .NET 10 novo, `NetApiFlow` e `NetApiFlow.Dashboard` `0.5.2` do nuget.org,
`AddNetApiFlow(o => o.ExcludePaths("/health"))`, em Development. O mesmo código, com e sem
`app.UsePathBase("/api")`:

| Requisição | Sem `UsePathBase` | Com `UsePathBase("/api")` |
|---|---|---|
| `GET /orders` (marcada) | rastreada | rastreada |
| `GET /health` (em `ExcludePaths`) | fora | **rastreada** |
| `GET /flow/api/config` (rota do painel) | fora | **rastreada** |
| `GET /flow/api/stream` (SSE) | fora | **rastreada**, e o trace dura a conexão inteira |

O store, com `UsePathBase`, ficou com `/flow/api/config`, `/flow/api/stream`, `/health` e `/orders`.

O painel trata o `PathBase` de propósito (`DashboardConfig.HostPathBase`), então esse é um cenário que o
produto suporta e que tem um furo.

**O que não foi medido:** o MCP (`/flow/mcp`), o `IncludePaths` e o `OpenApiDocumentPath` por si só. A
leitura do código diz que sofrem do mesmo, e a Tarefa 53.0 mede.

## A causa

O `NetApiFlowMiddleware` entra no pipeline por `IStartupFilter`, antes do `UsePathBase` que o usuário
chama. A decisão `policy.ShouldTrace(context.Request.Path)` roda nesse ponto, com o caminho cheio
(`/api/flow/api/config`). O padrão do painel (`/flow/**`) e os do usuário não casam com o prefixo. O
`UsePathBase` restaura `Path` e `PathBase` ao terminar, então reler o caminho depois do pipeline devolve
o caminho cheio de novo.

A rota gravada no trace já escapa disso: o `finally` do middleware lê o template do endpoint casado
(`GetEndpoint()`), que é relativo ao `PathBase`. O que quebra são as decisões tomadas por **caminho**:

- a exclusão das rotas do próprio NetApiFlow, o que viola `.claude/rules/tracing.md` ("as próprias
  rotas do NetApiFlow nunca são rastreadas");
- `ExcludePaths` e `IncludePaths`;
- o `OpenApiDocumentPath` (Tarefa 50.4).

## Por que os remédios que parecem óbvios não servem

- **Casar pelo fim do caminho (`EndsWith`).** Conserta o documento e exclui por engano uma rota
  legítima como `/v2/openapi/v1.json`.
- **Decidir depois do pipeline.** O `UsePathBase` já restaurou o caminho, e o trace já foi aberto e
  publicado. O stream do painel é uma conexão longa: apareceria como `Running` no SSE e na persistência
  durante a conexão inteira antes de alguém descartá-lo.
- **Pedir ao usuário que repita o `PathBase` numa opção do NetApiFlow.** É API pública nova, a mesma
  informação configurada em dois lugares, e o erro continua silencioso para quem esquecer.

## O que a solução tem que cumprir

1. As rotas do NetApiFlow (painel, API REST, stream SSE, MCP) nunca são rastreadas, com ou sem
   `PathBase`, e isso é decidido pela **identidade do endpoint**, não por um padrão de texto.
2. `ExcludePaths`, `IncludePaths` e `OpenApiDocumentPath` valem contra o caminho que o usuário enxerga,
   relativo ao `PathBase`, sempre que houver um endpoint casado.
3. Uma requisição excluída não publica evento, não persiste, não aparece como `Running` no SSE e não
   devolve `X-NetApiFlow-TraceId`.
4. Sem `PathBase`, o comportamento e o custo ficam como hoje. O caminho comum não paga a mais.
5. Requisição sem endpoint casado (arquivo estático, 404) sob `PathBase`: se nenhum mecanismo resolver
   isso com exatidão, o limite é dito no README, nos dois idiomas, com o que o usuário faz (padrão com o
   prefixo). Nunca fica "resolvido pela metade" sem aviso.
6. Sem API pública nova, **exceto a que o ADR-0027 aceito decidiu**: `WithoutNetApiFlowTracking()`. Nenhum outro
   tipo ou membro público.
7. Falha da própria checagem é logada e engolida (`.claude/rules/tracing.md`).

## Mecanismos a investigar (a Planejadora não escolhe antes da 53.0)

- **A. Metadado nos endpoints do NetApiFlow, decidido no `EndpointMatched`.** É o mecanismo da ADR-0026
  e herda o R-32 (o evento não é API formal). Pede que a abertura do trace possa ser adiada ou desfeita
  sem publicar nada.
- **B. Decidir pelo template do endpoint casado**, o mesmo que o `finally` já lê. Resolve
  `ExcludePaths` para endpoint, não para requisição sem endpoint.
- **C. Adiar a publicação do início do trace** até o primeiro ponto em que o `PathBase` é conhecido.
- **D. Um middleware que o usuário chama depois do `UsePathBase`.** Quebra a promessa das duas linhas e é
  API nova. Entra na lista só para ser descartado com evidência.
- **E. Ler o `PathBase` antes do pipeline, de outra fonte.** A investigação diz se isso existe para o
  `UsePathBase` chamado no código do usuário.

## Tarefa 53.0: medir, sem corrigir

Testes de host real (`TestServer`, `net8.0` e `net10.0`) que **só descrevem o que acontece hoje**. Não
se commita teste vermelho nem pulado: o resultado vai no relatório, e os testes entram junto da correção.

Matriz, com e sem `UsePathBase("/api")`, em Development e em Production:

| Cenário | O que medir |
|---|---|
| `ExcludePaths` de uma rota de endpoint | header de trace e store |
| `IncludePaths` | idem |
| rota do painel: `config`, `traces` (REST) e o `stream` | idem, e se o trace do `stream` fica `Running` |
| MCP, `initialize` e uma chamada de ferramenta | idem |
| `OpenApiDocumentPath` | idem |
| arquivo estático excluído por `ExcludePaths` | idem |
| IIS in-process (ANCM), como aplicação virtual sob `/api` | idem, e de onde vem o `PathBase` |
| IIS out-of-process | idem |
| Proxy reverso (nginx ou YARP) com `X-Forwarded-Prefix` | idem, e se o app aplica o prefixo sozinho ou só quando o código chama `UsePathBase` |

**As três últimas linhas são medidas em host real** (decisão da auditoria, D-205), não lidas no
código. Se a máquina não tiver o IIS ou o Hosting Bundle, a Executora diz que não mediu e por quê, e a
Planejadora leva o assunto à auditoria. Nada entra no relatório como medido por leitura.

Duas perguntas que mudam o tamanho do problema, e que a Executora responde com a medição acima, não por
suposição:

- **Quem define o `PathBase` em produção, e em que ordem?** Nas três hospedagens: o `PathBase` chega por
  um `IStartupFilter` do próprio framework **antes** do middleware do NetApiFlow, ou só quando o usuário
  chama `UsePathBase`? Se o framework o define antes, boa parte das implantações reais não sofre, e o
  problema fica restrito ao `UsePathBase` explícito.
- **Cada mecanismo acima**, com um spike descartável: o que ele consegue cumprir dos sete requisitos, e o
  que ele quebra. Com a medição, não com opinião.

**Pronto quando:** a matriz preenchida com o que passa e o que falha, as duas perguntas respondidas
com evidência, e cada mecanismo avaliado contra os sete requisitos. A Executora **para e entrega o
relatório.** Não corrige.

## Resultado da 53.0, medido pela Executora (20/11/2026)

Sonda descartável fora do repositório, `ProjectReference` para o núcleo, o painel e o MCP do `develop`,
um processo por célula, Kestrel de verdade, requisições HTTP de verdade e o store lido por um endpoint de
diagnóstico. Não usou `TestServer`. Os dados brutos ficam no scratchpad dela. Os testes entram com a
correção.

**Kestrel, 32 células** (`net8.0` e `net10.0`, Development e Production, sem e com `UsePathBase("/api")`):

- Sem `UsePathBase`: 56 de 56 requisições corretas.
- Com `UsePathBase("/api")`: 36 incorretas, idênticas nos quatro TFM e ambiente. `ExcludePaths` não
  exclui, o arquivo estático vira trace `(unmatched)`, as rotas do painel (`config`, `traces`, `stream`) e
  do MCP (`initialize` e `tools/call`) são rastreadas, e em Production elas respondem 404 e **mesmo assim**
  geram trace e cabeçalho. O `OpenApiDocumentPath` é rastreado.
- **Achado novo, o mais grave:** `IncludePaths("/orders")` com `UsePathBase` deixa a rota do **usuário** fora
  do store (store vazio). O defeito também apaga dado legítimo, não só deixa passar o que devia sair.
- O stream sob `PathBase`: um segundo cliente SSE vê `TraceStarted (unmatched) Running` durante a conexão
  inteira, sem `TraceCompleted` até ela acabar. `Running` aparece no SSE, não na persistência.
- O que cada ponto enxerga: antes e depois do middleware do NetApiFlow, `path=/api/orders` e `pathBase`
  vazio. Só o código do usuário depois do `UsePathBase` vê `path=/orders` e `pathBase=/api`.

**Hospedagem real:**

| Hospedagem | Resultado |
|---|---|
| IIS in-process e out-of-process, `net8.0` e `net10.0`, aplicação virtual `/api` | 0 incorretas. O framework define o `PathBase` **antes** do NetApiFlow |
| nginx que corta o prefixo, `X-Forwarded-Prefix: /api`, app sem código | 0 incorretas, `PathBase` vazio, o cabeçalho é só informativo |
| O mesmo com `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` | 0 incorretas. A variável **não** aplica o prefixo |
| O mesmo com `UseForwardedHeaders(All)` no código | 0 incorretas. O `PathBase=/api` só aparece **depois** do NetApiFlow |
| nginx que não corta, app sem `UsePathBase` | `/api/orders` dá 404 e `/orders` direto dá 200, com e sem a variável |
| nginx que não corta, `UsePathBase("/api")` no código | 7 incorretas, igual ao Kestrel direto |

**Limites da medição de hospedagem.** O IIS foi medido no IIS Express com o ANCM v2 (o mesmo
`aspnetcorev2.dll` do IIS completo), **não** no IIS completo, que exige administrador nesta máquina. O
módulo que vem com o IIS Express não carregou (erro 8007007e). O YARP não foi medido.

**As duas perguntas:**

1. Quem define o `PathBase` antes do NetApiFlow? No IIS (os dois modelos), o framework, antes. No proxy,
   ninguém: o app não aplica o prefixo sozinho, nem com a variável de ambiente. `UseForwardedHeaders` e
   `UsePathBase` do código do usuário entram sempre **depois** do middleware do NetApiFlow.
2. Logo, o defeito fica restrito ao `UsePathBase` explícito, no Kestrel direto ou atrás de um proxy que
   não corta o prefixo. O IIS e o proxy que corta não sofrem. Como `UsePathBase` é a forma comum de rodar
   sob prefixo fora do IIS, a classe de implantação afetada continua grande.

**Mecanismos, com um spike descartável por mecanismo** (matriz de 4 grupos, com e sem `UsePathBase`,
`net8.0` e `net10.0`):

| Mecanismo | Resultado |
|---|---|
| **A** (adiar a abertura do trace até o `EndpointMatched`; decidir por metadado de identidade nos endpoints do NetApiFlow e pelo caminho relativo no resto) | 28 de 28 corretas sem `PathBase` e 28 de 28 com. Cumpre os requisitos 1 a 4. Falha o 5 (sem endpoint casado a decisão cai no caminho cheio). O 7 não foi medido |
| **B** (começar tudo e filtrar no fim pelo template do endpoint casado) | Store correto, mas **falha o 3 e o 4**: o cabeçalho sai e `TraceStarted Running` é publicado para a rota excluída e para o stream do painel, sem `TraceCompleted` (fantasma `Running` no SSE). Sem `PathBase` também publica o que hoje é excluído (20 de 28 incorretas), então o caminho comum paga |
| **C** | Não é um mecanismo à parte: o primeiro ponto em que o `PathBase` é conhecido é o `EndpointMatched`, então ele é o A |
| **D** (middleware depois do `UsePathBase`) | Descartado com evidência: só o ponto do usuário vê o caminho certo, mas pede API pública nova e o trace teria de começar nele |
| **E** (ler o `PathBase` de outra fonte antes do pipeline) | Não existe para o `UsePathBase` explícito. `ASPNETCORE_PATHBASE=/api` no Kestrel não muda nada |

O custo do A, medido grosseiramente num loop de 4000 requisições com ruído alto, foi 91 e 98 µs de
mediana contra 86 e 85 hoje. É um indício e **não** um benchmark.

**Decisão (D-211): o mecanismo A.** É o único que cumpre os requisitos 1 a 4, e herda o R-32 (o evento é
diagnóstico, não API formal), que funcionou nos dois TFMs. O ADR-0027 é escrito antes da correção.

**O que a correção ainda tem de medir, antes de qualquer código:**

- O efeito do A sobre `CapturePrePipelineSteps` e sobre o parentesco da `Activity` de entrada: com o A o
  trace passa a abrir depois de alguns middlewares.
- O requisito 7: a checagem da própria decisão logada e engolida (o spike não tem o `try/catch`).
- O custo com `tests/NetApiFlow.Benchmarks` e `[MemoryDiagnoser]`, no lugar do loop grosseiro.
- O requisito 5, o limite que vai ao README: sem endpoint casado (arquivo estático, 404) sob `PathBase`, o
  `ExcludePaths` continua valendo só contra o caminho cheio.
- O marcador de identidade é um tipo `internal`, e o MCP precisa de `InternalsVisibleTo` (o painel já tem). Substituído pelo ADR-0027 aceito: o metadado é gravado por uma extensão pública, sem `InternalsVisibleTo` novo.

## Estado depois da 53.0

A 53.0 está entregue e o mecanismo é o **A** (D-211). O **ADR-0027** está **aceito** (D-230), com uma emenda da
auditoria: o metadado é gravado por uma extensão pública. **Nenhum código da correção começa antes de a Fase 51
estar em `develop`**, porque o mecanismo A vive no mesmo listener do `EndpointMatched` que a Fase 51 cria.

## Coerência com as Fases 51, 55 e 57 (ADR-0027, ADR-0028)

A trace passa a abrir no `EndpointMatched`, o evento que a Fase 51 já usa para a etapa `Endpoint`.

- **A Fase 51 vem antes.** O listener é um só: a 51 o cria para abrir a etapa, e a 53 passa a abrir a trace nele.
- **A Fase 55 gera o id no início** (ADR-0028), e a trace abre depois. A forma de a etapa achar a trace (a cadeia
  de pais ou o `AsyncLocal`) só se fecha depois da Tarefa 53.1, que mede o destino das etapas anteriores à abertura.
- **A Fase 57 vem junto.** A rota já é conhecida na abertura, e o estado "Aguardando" deixa de ser `(unmatched)`.

## Tarefa 53.1: medir o que o ADR deixou aberto, antes de qualquer código

Host real, sem corrigir. O relatório vai à Planejadora e à auditoria. **Se alguma resposta for "não dá"**, a Executora
para e relata, e a decisão é da Planejadora e da auditoria.

1. **`CapturePrePipelineSteps`.** Com a opção ligada, uma etapa criada por um middleware que roda **antes** do
   `EndpointMatched` (um middleware do usuário com `ActivitySource` próprio, antes do `UseRouting`) aparece na trace
   hoje? Com a abertura adiada, para onde ela vai? Medir as duas formas: guardá-las no recorder pelo id e descarregar
   na abertura, ou descartá-las. A escolhida mantém o que funciona hoje e **não publica nada** para a requisição
   excluída. Se nenhuma mantém, para e relata.
2. **O pai da `Activity` de entrada.** Com a abertura adiada, a atividade do servidor continua sendo o pai do que o
   NetApiFlow cria? A hierarquia de spans (`.claude/rules/tracing.md`) muda?
3. **Hora de início e duração.** A trace aberta no `EndpointMatched` carrega a hora de início do middleware, e a
   duração inclui os middlewares anteriores, como hoje. Medir a diferença contra o `develop`.
4. **O requisito 7.** Uma política que lança é logada e engolida, a requisição responde normalmente e a trace não
   abre. Prova por teste.
5. **O custo**, com `tests/NetApiFlow.Benchmarks` e `[MemoryDiagnoser]`, `develop` contra o mecanismo A, **sem**
   `PathBase` (requisito 4: o caminho comum não paga a mais) e com. A alocação por requisição entra no número.
6. **O limite do requisito 5.** Sem endpoint casado sob `PathBase`, o que acontece com um arquivo estático e com um
   404, para a frase do README sair exata.
7. **A API pública.** O único membro público novo é `WithoutNetApiFlowTracking()`, o tipo do metadado fica
   `internal`, e o pacote do MCP **não** ganha `InternalsVisibleTo`. A Executora compara a lista de tipos e membros
   públicos antes e depois e diz como conferiu (o repositório não tem arquivo `PublicAPI.*.txt`).

**Pronto quando:** os sete pontos respondidos com número ou saída real, e a forma escolhida para as etapas anteriores
à abertura, com o motivo.

## Tarefa 53.2: o mecanismo A

- **A extensão pública `WithoutNetApiFlowTracking<TBuilder>() where TBuilder : IEndpointConventionBuilder`**, no
  núcleo, que grava o metadado, na classe `NetApiFlowEndpointConventionExtensions` (o nome do `contratos.md`: a mesma classe receberá a `WithNetApiFlowTracking<TBuilder>` genérica da Fase 67). O tipo do metadado é `internal`. O painel (REST, SSE, arquivos estáticos) e o MCP a
  chamam nos grupos deles. Nenhum `InternalsVisibleTo` novo (requisito 6).
- **O metadado de exclusão vence** `ExcludePaths`, `IncludePaths` e `WithNetApiFlowTracking()` no mesmo endpoint.
- **Middleware no início (D-245):** guarda a hora, gera o id (ADR-0028), **reserva uma trace não publicada no recorder** (o buffer da 53.1: reservar, publicar e descartar sem evento, com os eventos de span retidos e reemitidos na ordem), calcula **UMA vez** a decisão do "sem endpoint" pelo caminho **cheio**, dentro de `try/catch` com o padrão de não rastrear, e a guarda no `HttpContext` num **único objeto de estado**, não em três chaves de `Items`. Registra o `Response.OnStarting` do cabeçalho. Não publica nada.
- **Listener do `EndpointMatched`**, o da Fase 51: endpoint com o metadado do NetApiFlow não é rastreado. Nos outros,
  a política (`ExcludePaths`, `IncludePaths`, `OpenApiDocumentPath`) é avaliada contra o caminho **relativo** ao
  `PathBase`, e, se aceita, a trace abre com a hora real e a rota já conhecida. O listener passa a ser **sempre ligado** (a abertura da etapa de endpoint continua respeitando `CaptureMinimalApiEndpoints`), a mensagem de log da falha da decisão é própria, e, se o listener não se inscrever (R-32), o mecanismo degrada para decidir no fim pelo caminho cheio, que é o comportamento de hoje, sem perder trace.
- **Sem endpoint casado:** vale a decisão calculada no início, pelo caminho cheio. O `Request.Path` dentro do `OnStarting` ou no fim **não** é lido, porque ele varia por tipo de resposta sob `UsePathBase` (D-245).
- **O cabeçalho** sai só para a trace que será gravada, pelo mecanismo da Tarefa 50.13. O `OnStarting` consulta a mesma decisão guardada, então o cabeçalho e a trace não divergem.
- **As etapas anteriores à abertura** seguem o buffer escolhido na 53.1. A reserva nunca vaza: requisição excluída, sem endpoint e com exceção antes do roteamento fecham com `ActiveTraceCount` 0.
- **Falha da decisão** é logada e engolida, e o padrão é **não rastrear**.
- **Os padrões de rota são compilados uma vez**, na configuração, e não a cada requisição (D-236, varredura final). É a única otimização do caminho quente que entra na `0.6.0`.
- **Sem `PathBase`**, nenhum teste do `develop` muda de resultado e nenhum precisa de edição.

## Tarefa 53.3: os testes

A matriz da 53.0 vira teste de host real, **visto falhando antes** no `develop`, nos dois TFMs:

- `ExcludePaths`, `IncludePaths`, `config` e `traces` do painel, o `stream`, o MCP (`initialize` e `tools/call`) e o
  `OpenApiDocumentPath`, com e sem `UsePathBase("/api")`, em Development e em Production.
- **`IncludePaths("/orders")` sob `UsePathBase` mantém a rota do usuário no store.** É o achado que apagava dado.
- Requisição excluída: sem `X-NetApiFlow-TraceId`, sem linha no store e **sem evento num cliente SSE**.
- O stream do painel nunca aparece como `Running`.
- Sem endpoint casado sob `PathBase`: o comportamento que o README documenta.
- **Uma rota do usuário com `.WithoutNetApiFlowTracking()` não é rastreada sob `PathBase`**, e vence `IncludePaths` e
  `WithNetApiFlowTracking()` no mesmo endpoint. Cobre `MapGet`, grupo, `MapControllers()` e `MapHealthChecks()`.
- Política que lança (requisito 7) e etapa anterior à abertura (a forma da 53.1).
- **Teste com o pacote real** (D-176, D-186): um app novo, pacote empacotado com `-p:Version=<próxima>-verify.<n>`
  (D-153, D-174), `UsePathBase("/api")`, e os quatro casos da tabela de "O fato".

## Tarefa 53.4: README, XML doc e CHANGELOG

- `README.md` e `README.pt-BR.md`: o que `ExcludePaths`, `IncludePaths` e `OpenApiDocumentPath` valem sob
  `PathBase` (o caminho que o usuário enxerga, quando há endpoint casado), o limite do requisito 5 com o que o
  usuário faz, e a `WithoutNetApiFlowTracking()` na tabela de métodos. **Dizem o que foi medido** (Kestrel, IIS
  Express com o ANCM v2 e o nginx da 53.0) **e o que não foi** (IIS completo, YARP). Sem prometer o que não se mediu.
- XML doc de `OpenApiDocumentPath`, `ExcludePaths`, `IncludePaths` e `WithoutNetApiFlowTracking()`, em inglês, com a
  mesma frase.
- `contratos.md`: já atualizado pela Planejadora na aceitação do ADR.
- `CHANGELOG.md`, em inglês, `### Fixed`: as rotas do NetApiFlow rastreadas e o `ExcludePaths` ignorado sob
  `UsePathBase`, e o `IncludePaths` que deixava a rota do usuário fora do store. Defeito antigo, sem hotfix (D-205).
  `### Added`: `WithoutNetApiFlowTracking()`.
- `riscos.md`, R-33: a Planejadora passa a `Mitigado` na revisão, com o arquivo de teste citado.

## Relatório da correção

Para cada requisito de 1 a 7: o teste, a saída dele falhando antes e passando depois, e o número do custo da 53.1.
O que mudou de comportamento. Se algum requisito ficou como limite dito, onde o README o diz.

## Resultado da 53.1, medido pela Executora (21/11/2026)

Sonda descartável em cima da branch da #408, nada foi a PR (o patch de referência fica no scratchpad da Executora). Nenhuma das sete respostas foi "não dá".

- **Etapas anteriores à abertura:** só o **buffer** mantém o que funciona hoje (idêntico ao baseline: mesmos spans, mesmos pais, 28 eventos SSE contra 28). O descarte faz o passo do usuário sumir e achata a hierarquia.
- **Pai da Activity de entrada, início e duração:** não mudam (mediana dentro do ruído do `Task.Delay`).
- **Requisito 7:** violado hoje (`IncludePaths(new string[] { null })` dá `NullReferenceException` em toda requisição). Com o buffer, 9 de 9 testes passam nos dois TFMs (o baseline, 5 de 9).
- **Custo (`net10.0`, `MemoryDiagnoser`):** o tempo não distingue os modos. Alocação do buffer: +1,1% numa rota rastreada, +0,07 KB no `Nested`, **+0,54 KB (+8,4%) numa rota excluída**. Aceito com a condição de um objeto de estado único e uma medida nova abaixo de 0,6 KB.
- **Limite do requisito 5, com a decisão calculada uma vez no início:** cabeçalho e trace coincidem em todas as requisições (sem e sob `/api`, com e sem `MapStaticAssets`). Com endpoint casado (inclusive `MapStaticAssets`), `ExcludePaths` vale pelo caminho relativo. Sem endpoint casado (`UseStaticFiles` e 404), vale pelo cheio: `ExcludePaths("/excluded-static.css")` não exclui `/api/excluded-static.css`, e quem serve estático por middleware sob `PathBase` escreve o padrão com o prefixo.
- **API pública:** só `WithoutNetApiFlowTracking<TBuilder>` e a classe que a contém. O painel e o MCP não mudam, e nenhum `InternalsVisibleTo` muda.
- **Achado de ambiente:** com `WebApplication` e `UsePathBase`, o roteamento roda antes do middleware que o usuário declara antes do `UseRouting`, então o buffer só importa sem `PathBase`, o caminho comum.

## Veículo e ordem (D-205)

A auditoria decidiu, sem esperar a 53.0: **`0.6.0`**. O defeito é anterior, não é regressão, e não
expõe dado nem apaga nada, então pela regra nova de hotfix não é hotfix. O `0.6.0` não sai com ele
aberto: o README e o XML doc dizem "nunca rastreada", e a frase é o contrato.

Ordem da Executora: 50.11, 50.5, **53.0**, 50.10, Parte B e 50.9, Fase 51, e a correção da 53. Se a
53.0 concluir que a correção depende do `EndpointMatched` da Fase 51 (R-32), a correção entra junto
com a 51.

`PathBase` e proxy reverso entram também no inventário da auditoria de base que precede o `0.6.0`.

## Fora de escopo

- A Tarefa 50.4 e a PR da Parte A não esperam esta fase.
- Mudar a semântica de `ExcludePaths` para quem não usa `PathBase`.
- Suporte a vários `PathBase` ou a `PathBase` que muda em tempo de execução.

## Relatório final

A matriz medida, as respostas às duas perguntas com o trecho do código do framework ou o teste que as
sustenta, a avaliação de cada mecanismo contra os sete requisitos, e qual a Executora recomenda e por quê.

## Resultado da 53.2 a 53.4 (#420, 21/11/2026)

Entregue em `develop` (`1e486ae`). A auditoria **reprovou a primeira versão** (`4710caa`) por duas regressões contra a `0.5.3` e três ajustes menores. A versão mergeada é a `3bb2c94`.

- **O que entrou.** O mecanismo A com a emenda da D-245: `FlowRequestState` com a decisão única, `RequestTracingDecider`, a reserva não publicada no `FlowRecorder`. A única API pública nova é `WithoutNetApiFlowTracking<TBuilder>()`, que o painel e o MCP chamam nos grupos deles. README, XML doc e CHANGELOG dizem o que os padrões valem sob `PathBase` e o limite do requisito 5.
- **Testes.** 61 de host real em `PathBaseTracingTests` (com e sem `UsePathBase`, em Development e em Production, nos dois TFMs; 40 falham contra o código do `develop`, o que a Planejadora reproduziu), 7 em `ReExecutionTracingTests` (4 falham sem a correção do R1, também reproduzido), os da visibilidade da requisição sem endpoint e da reserva, e os unitários.
- **Custo** (`tests/NetApiFlow.Benchmarks`, `net10.0`, medido pela Executora). A rota rastreada sem `PathBase` vai de 7,64 a 7,65 KB. A rota excluída sem `PathBase` vai de 6,44 a 6,92 KB (+0,48 KB, abaixo do teto de 0,6 KB do plano). Sob `UsePathBase`, a rota excluída deixa de ser rastreada por engano.
- **As duas regressões.** R1: a reexecução abria a trace de uma requisição que o `ExcludePaths` excluía. Corrigida, e a decisão de uma requisição reexecutada segue a original. R2: uma requisição sem endpoint casado só aparece quando termina. A Planejadora decidiu manter isso como limite declarado (D-256, opção A), com CHANGELOG, README e um teste que fixa a decisão.
- **Comparação com a `0.5.3`.** A Planejadora, por referência de projeto, e a auditoria, com o pacote real em cinco modos (inclusive `PathBase`, `IncludePaths` e sem `UseRouting`), chegaram às mesmas duas diferenças, as duas intencionais e anunciadas no CHANGELOG: o cabeçalho passa a sair no 500 tratado por `UseExceptionHandler` (50.13), e a requisição sem endpoint só aparece ao terminar.
- **Critério cumprido (D-306).** A auditoria refez com o pacote real `0.6.0-audit.20`, em 25/11/2026: com `UsePathBase("/api")` e `ExcludePaths("/health")`, em memória, PostgreSQL e SQL Server, o pacote novo grava só `GET /orders/{id:int}`, e a `0.5.3` gravava health, internal e as rotas do painel. A matriz de IIS e nginx é a medição da Executora, aceita pela auditoria. O R-33 passou a `Mitigado`, com `PathBaseTracingTests` como prova.
- **Achado no caminho, para a `0.7.0`** (item 52): uma requisição tratada por `UseExceptionHandler` é gravada com a rota da página de erro. É defeito antigo, a `0.5.3` faz igual.
