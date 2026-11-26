# Fase 73: a verdade do fluxo (D-320, grupo A da `0.7.0`)

**Estado (26/11/2026):** planejada, não iniciada. Abre **depois** de a auditoria fechar a Fase 72 (D-320).

O NetApiFlow existe para mostrar o caminho certo de cada requisição. Oito defeitos antigos, todos de antes da `0.6.0`
(nenhum é regressão dela), fazem a tela mostrar algo errado: a rota do `/boom` aparece como `GET /error 500`, o 500 de
`Production` aparece com status 200, e uma requisição sem endpoint só aparece quando termina. Pela regra do D-205, defeito
antigo vai para a minor seguinte, e esta é ela.

A `0.7.0` **não quebra a API pública** (D-320). Nenhuma tarefa daqui acrescenta ou remove uma linha do `PublicAPI`. Se uma
precisar (o item 46 é o candidato), a Executora **para** e relata.

## Os achados e o que a Planejadora conferiu

| # | Item do roadmap | Conferi |
|---|---|---|
| 52 | A requisição tratada por `UseExceptionHandler` ou por `UseStatusCodePagesWithReExecute` é gravada com a rota da página de erro: o `/boom` vira `GET /error 500`, e um 404 vira `GET /error/{code}`. O template sai do `GetEndpoint()` do fim, que depois da reexecução é o endpoint de erro. A `0.5.3` faz igual | Li a D-258, que registra a causa e que a decisão de rastrear já segue a requisição original. Não li o código |
| 45 | No 500 que o Kestrel monta em `Production` sem tratador de exceção, o trace sai com `Status = Error` e `HttpStatus = 200`, porque o middleware lê `Response.StatusCode` antes de o servidor atribuir o 500. O Runner e o grafo mostram esse valor | Li a D-241. A Executora mediu em Kestrel real na 50.13 |
| 51 | A visão ao vivo de uma requisição sem endpoint casado (middleware terminal, WebSocket, e os spans de EF e de `HttpClient` dela) só aparece quando termina. Na `0.5.3` aparecia `Running` | Li a D-256: a opção A foi escolhida, e a D (publicar depois de uma janela curta por uma varredura única em segundo plano) ficou para esta versão, com medição, limite no README e o caso do WebSocket à mão |
| 46 | Um status `Cancelled` de verdade para o span. Hoje todo cancelamento num step fecha o span como `Error` com o `ErrorType` da exceção, e só o trace tem `FlowStatus.Cancelled` | Li a D-242. Um status novo mexe no listener, no painel, no MCP, no `ErrorCount`, na propagação de erro para os ancestrais e no tail sampling |
| 47 | `SqlClientCommandListener` e `FlowAuthorizationFilterListener` assinam por `DiagnosticListener.AllListeners` e não conferem de quem é a requisição. Com dois hosts no mesmo processo (o caso do `WebApplicationFactory`), o listener de um age na requisição do outro. O `EndpointMatchedListener` já confere pelo `RequestServices` | Li a D-243. Num app de produção há um host por processo, então o efeito é de teste |
| 53 | O `Detach` do `FlowTraceResolver` solta o estado no holder, mas `RequestHolder.ServerActivity` é só de leitura: um `ExecutionContext` capturado (um `Timer`, um `Task.Run` longo) segue segurando o objeto `Activity` do servidor | Vi a linha em `src/NetApiFlow/Instrumentation/FlowTraceResolver.cs` (`ServerActivity { get; }`). Achado da auditoria na #464 (D-271) |
| 48 | A consulta do blueprint (`GetLastSuccessfulAsync`) compara o método com `ILIKE` no PostgreSQL e com `UPPER` no SQL Server, e isso impede o índice `(method, route_key)`. Medido com 110 050 traces: a rota rara leva 2,4 a 6,2 ms no PostgreSQL e 6766 leituras lógicas no SQL Server | Li a D-246, com os números da Executora |
| 44 | Em `Production`, `DELETE` e `PUT` em `/flow/mcp` respondem 405 e o resto do produto responde 404 | Li o item. A auditoria mediu em 25/11/2026 (no modo padrão sem sessão: `POST` 404, `GET`, `DELETE` e `PUT` 405). O README já descreve isso (D-298) |

## Ponto de parada

- **Rota sempre como template.** Se a única forma de achar a rota original (item 52) for gravar o caminho cru, a Executora
  **para**: o princípio do projeto é nunca capturar o caminho com valores. Uma requisição sem endpoint casado grava
  `(unmatched)`, como já grava no ao vivo (D-246).
- **Item 51: nenhuma publicação no começo.** Se a medição mostrar que a varredura custa mais que o ruído no caminho quente,
  ou se um endpoint excluído (`.WithoutNetApiFlowTracking()`, `ExcludePaths`) puder aparecer por um instante, para e relata.
  O requisito 3 da Fase 53 (nada excluído aparece como `Running`) vale mais que o ao vivo.
- **Item 46: desenho primeiro, e para.** A 73.5 entrega um relatório e **não implementa** antes de a Planejadora responder.
  Se o desenho mexer em tipo público (um `enum`, um membro), o item vai para a versão da API.
- **Item 48: sem mudança de schema.** Se a solução exigir índice novo, coluna nova ou migração, para: quem roda com
  `AutoCreateSchema = false` não receberia a mudança sozinho.
- **Item 44: sem mexer no roteamento por dentro.** Se a correção exigir uma política de matcher ou tipo interno do ASP.NET
  Core, o item é fechado como "aceito e documentado" com a medida, e o README fica como está.

## Tarefa 73.0: reproduzir e medir, antes de corrigir

Em host real (Kestrel), `net8.0` e `net10.0`, contra o pacote `0.6.0` do nuget.org e contra o `develop`:

1. **52.** Um app com `UseExceptionHandler("/error")` e um `GET /boom` que lança: a rota gravada. Um app com
   `UseStatusCodePagesWithReExecute("/error/{0}")` e uma URL que não existe: a rota gravada.
2. **45.** `Production` sem tratador e um endpoint que lança: o que o cliente recebe, e o `Status` e o `HttpStatus` da trace.
3. **51.** Uma requisição de middleware terminal e uma de WebSocket: quando cada uma aparece em `/flow/api/traces/live` e o
   que a `0.5.3` mostrava. A linha de base do custo do caminho quente: `EnabledOverheadBenchmarks` contra a `0.6.0`.
4. **44.** A matriz completa de métodos em `/flow/mcp` em `Production`, no modo padrão e com sessões.
5. **47.** Dois hosts no mesmo processo, o listener de um agindo na requisição do outro.
6. **53.** Um `ExecutionContext` capturado depois do fim da requisição e se ele ainda alcança a `Activity` do servidor (com
   um `WeakReference` e uma coleta forçada).
7. **48.** Os planos de consulta nos dois bancos, com o método em caixa alta e em caixa mista.

Se um item **não** reproduzir, a Executora para nele e relata.

## Tarefa 73.1: a rota da requisição original (item 52)

A rota gravada segue a requisição original, como a decisão de rastrear já segue (D-258): a primeira rota casada vale, e a
reexecução para uma página de erro não a troca. O `/boom` grava `GET /boom`, com o 500. Um 404 sem endpoint grava
`(unmatched)`. As traces já gravadas ficam como estão, sem migração.

Testes de host real, **vistos falhando antes**: `UseExceptionHandler` e `UseStatusCodePagesWithReExecute`, nos dois TFMs.

## Tarefa 73.2: o status do 500 sem tratador (item 45)

Quando uma exceção escapa do pipeline antes de a resposta começar, a trace grava o `HttpStatus` que o servidor vai enviar
(500), e não o que a resposta tinha até ali. Quando a resposta já começou, o status gravado continua o que foi enviado.

O teste mora ao lado do teste do limite conhecido da 50.13, em Kestrel real. Não muda a resposta que o usuário recebe
(D-241): só o que o NetApiFlow grava sobre ela.

## Tarefa 73.3: a requisição sem endpoint aparece ao vivo (item 51)

A opção D da D-256, com as três condições de lá: medição, limite no README e o caso do WebSocket feito à mão.

1. **O desenho:** uma varredura única em segundo plano (um timer só, sem trabalho por requisição) publica a trace
   reservada que passou da janela e ainda não casou endpoint. A janela sai da medição, não de um número escolhido.
2. **A medição:** o custo por requisição no caminho quente, contra a `0.6.0`, dentro do ruído nos dois TFMs. O custo da
   varredura com 0, 100 e 1000 traces reservadas.
3. **O requisito 3:** nenhum endpoint excluído aparece, nem por um instante, com `ExcludePaths`,
   `.WithoutNetApiFlowTracking()` e `PathBase`. Teste de host real para cada um.
4. **O README, nos dois idiomas:** a frase do limite diz depois de quanto tempo a requisição sem endpoint aparece.
5. **O WebSocket, à mão:** a Executora abre um e mostra a captura de tela do painel.

O comportamento muda contra a `0.6.0` publicada (hoje só aparece quando termina): entra em `### Changed` do `CHANGELOG.md`
e em `tests/real-package/expected-differences.json`, com a frase que o `comparison` exige (D-310 e D-315).

## Tarefa 73.4: os quatro pequenos (itens 47, 53, 48, 44)

- **47.** Os dois listeners conferem de quem é a requisição pelo `RequestServices`, como o `EndpointMatchedListener`. Teste:
  dois hosts no mesmo processo, e cada listener só age na requisição do próprio host.
- **53.** O `Detach` solta também a referência à `Activity` do servidor. Teste com `WeakReference`: um `ExecutionContext`
  capturado depois do fim da requisição não segura o objeto.
- **48.** O método se compara sem `ILIKE` nem `UPPER`, de um jeito que usa o índice, sem mudar o schema. A Executora mede o
  plano antes e depois nos dois bancos e mostra a leitura lógica e o tempo. Se existir linha gravada com o método em caixa
  mista, o relatório diz o que acontece com ela.
- **44.** `DELETE` e `PUT` em `/flow/mcp` respondem 404 em `Production`, em todos os modos. Se não couber no ponto de parada,
  o item fecha como "aceito e documentado".

## Tarefa 73.5: o status `Cancelled` do span (item 46), só o desenho

Um relatório, sem código de produto:

1. A lista exata dos pontos que um status de span novo mexe (o listener, o painel, o MCP, o `ErrorCount`, a propagação para
   os ancestrais, o tail sampling), cada um com o arquivo e a linha.
2. Se existe um jeito de representar o cancelamento **sem** tipo público novo (por exemplo, uma tag no `Activity` que o
   painel lê), e o que ele custa. Se só existe com tipo público novo, o relatório diz qual.
3. A recomendação, e o que o painel mostraria.

A Executora **para** aqui. A Planejadora decide se a implementação entra na `0.7.0` (sem tipo público novo) ou vai para a
versão da API.

## Fora de escopo

- O grupo B (a API pública) e o `Cancelled` público, se o desenho pedir tipo novo.
- Mudar o schema dos storages.
- Mudar a resposta que o usuário recebe em qualquer falha dele (D-241).

## Critério que vale para a fase inteira

- `dotnet build`, `dotnet test`, `dotnet format` limpos nos dois TFMs, e o AOT (`AotCoreVerification` e
  `AotCoreVerificationNet8`) sem aviso `IL`.
- Cada defeito **visto falhando** antes e passando depois, nos dois TFMs, e cada um com a linha no
  `tests/regression-guards/guards.json`.
- O caminho quente (itens 52, 45 e 51): o roteiro de `EnabledOverheadBenchmarks` contra o pacote publicado `0.6.0`, e a
  comparação do `comparison` do pacote real, com cada diferença nova anunciada.
- Nenhuma linha nova ou removida no `PublicAPI`.
- `CHANGELOG.md`, em inglês: `### Fixed` para os itens 52, 45, 47, 53, 48 e 44, e `### Changed` para o 51.

## Versionamento e fluxo

- `0.7.0`. Branch `bugfix/73-verdade-do-fluxo` a partir de `develop` atualizado, depois da Fase 72 fechada. A Fase 73 pode
  sair em PRs separadas (a 73.1 e a 73.2 primeiro, que mudam o que se vê). Commits pequenos, em inglês, uma linha, sem linha
  de coautor. Abrir a PR e parar.
- A auditoria fecha a fase com o pacote real e o núcleo fixo do D-318 antes de a Fase 74 abrir.

## Relatório final

Para cada item: o comando e a saída antes e depois, nos dois TFMs. O custo do caminho quente antes e depois (item 51 em
especial). O relatório do item 46. Os itens que não reproduziram ou que fecharam como "aceito e documentado".
