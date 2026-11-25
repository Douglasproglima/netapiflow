# Fase 71: as tools do NetApiFlow só existem no endpoint do NetApiFlow (D-278, S9, R-36)

**Estado (24/11/2026):** entregue em `develop` (#487, squash `0793930`), registrada na D-282 e **fechada pela auditoria** (D-283), que reproduziu com o pacote `0.6.0-audit.10` num app novo. Falta só publicar o advisory, que é da auditoria, junto do Release (o rascunho, já corrigido por ela, está na Tarefa 71.3).

Um app que hospeda o próprio servidor MCP (`AddMcpServer().WithHttpTransport()` e `MapMcp("/mcp")`) e usa o `NetApiFlow.Mcp`
serve as tools do NetApiFlow no endpoint dele. A auditoria reproduziu com o pacote real, na `0.5.3` publicada e na head da #481:
em `Production`, sem nenhuma autorização configurada, o `/flow/mcp` responde `404` ao `initialize`, como o README promete, mas o `/mcp`
do app lista `get_component_stats`, `get_trace` e `list_recent_traces`, e o `list_recent_traces` devolve as traces (id, método, rota,
status e duração).

Vai no `0.6.0` pela exceção de segurança do D-232 (D-278). **Não é hotfix** (D-205): não é regressão, e a exposição exige que o app
hospede um MCP próprio. O dono decidiu em 24/11/2026 corrigir na `0.6.0` e publicar um quarto GitHub Security Advisory junto do release, como os outros três (D-279).

## O achado e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| S9 | O `AddNetApiFlowMcp` registra as tools no servidor MCP único do processo, e qualquer endpoint `MapMcp` do app serve essas tools. A regra "Same security rule as the dashboard" do README não vale para o endpoint do app | **Reproduzido pela auditoria**, com o pacote real, na `0.5.3` e na PR. A Planejadora não reproduziu e não leu o código |

O que vaza é o que as tools devolvem. O `list_recent_traces` devolveu id, método, rota, status e duração, e o `get_trace` devolve a
trace com as etapas, que é mais do que a lista mostrou. Não há corpo, argumento, header, query string nem SQL com parâmetro, porque o
NetApiFlow nunca os captura (ADR-0007). A auditoria põe a severidade na faixa dos outros advisories.

## Quando entra

**Depois da #481 (Fase 67) mergeada, e antes da Fase 58.** A 67 muda o mesmo código do MCP (as tools viram `internal`, o
`serverInfo.name` passa a `NetApiFlow` e, na correção da auditoria, só quando o app não deu o dele). Uma correção de segurança não
espera as outras fases, mas não pode ser escrita sobre código que está mudando.

## Ponto de parada

- Se nenhum desenho mantiver o `/flow/mcp` com **as mesmas regras de acesso** (`404` em `Production` sem autorização, `403` para
  `Origin` e `Host` alheios) e **as mesmas três tools**, **para e relata**. **A exceção decidida (D-281):** num app com `PerSessionExecutionContext = true` sem `Stateless`, o filtro não tem `HttpContext`, e as tools do NetApiFlow ficam indisponíveis nos dois endpoints. É falhar fechado, e é a única configuração em que o `/flow/mcp` não tem as três tools.
- Se o SDK (`ModelContextProtocol.AspNetCore` 2.3.1) não tiver como separar as tools por endpoint sem um servidor à parte, **para e
  relata**: a escolha entre um servidor separado e outra saída é da Planejadora e da auditoria.
- Se a correção mudar o que um app **sem** MCP próprio vê (o `tools/list` do `/flow/mcp`, o `serverInfo`), **para**.
- Se as tools do próprio app deixarem de funcionar no `/mcp` dele, **para**.

## Tarefa 71.0: reproduzir e medir, antes de qualquer código

Host real, `net8.0` e `net10.0`, com o pacote `NetApiFlow.Mcp` e um app que tem o MCP dele: `AddMcpServer().WithHttpTransport()`,
uma tool própria (`app-tool`) e `MapMcp("/mcp")`, mais `AddNetApiFlowMcp` e `MapNetApiFlowMcp`.

1. **A reprodução.** Em `Production` sem autorização, e em `Development`: o `tools/list` do `/mcp` lista as tools do NetApiFlow, e o
   `tools/call list_recent_traces` pelo `/mcp` devolve traces. Visto falhando antes, com a saída.
2. **O espelho.** O `tools/list` do `/flow/mcp`, com a tool própria do app registrada, lista só as três tools do NetApiFlow, ou
   lista também a `app-tool`? Se listar, o `/flow/mcp` (que o NetApiFlow protege) serve uma tool do app, e isso entra no escopo.
3. **O que o SDK 2.3.1 oferece** para separar as tools por endpoint: a configuração por sessão
   (`HttpServerTransportOptions.ConfigureSessionOptions`, que recebe o `HttpContext` da requisição de `initialize`), os filtros de
   `tools/list` e de `tools/call`, e um servidor separado. Para cada um: funciona nos dois TFMs e nos dois modos do servidor (`Stateless = true` e o padrão)? Preserva as tools e o
   `ConfigureSessionOptions` do app? Qual o custo? A Executora escolhe o menor que passa nos critérios da 71.1 e diz qual.

4. **Desde quando,** para o intervalo do advisory: desde qual versão do `NetApiFlow.Mcp` o registro das tools no servidor MCP do processo é o mesmo (`git log -S` e a leitura do código do `v0.2.0` em diante), e a reprodução em ao menos uma versão publicada anterior à `0.5.3`.

**Pronto quando:** os quatro pontos respondidos com saída real, e o desenho escolhido dito antes do código.

## Resultado da 71.0 e o desenho (D-281, 24/11/2026)

A Executora mediu sem código, e a Planejadora e a auditoria leram o relatório (a Planejadora não repetiu as medições).

- **Reproduzido** na `0.2.0` e na `0.5.3` publicadas e no `develop`, nos dois TFMs e nos dois modos do servidor. **O intervalo afetado é de `0.2.0` a `0.5.3`, todas as versões publicadas do `NetApiFlow.Mcp`:** o registro das tools é idêntico desde o `b1a933d`, e o SDK é o 2.3.1 em todas.
- **O espelho existe:** o `/flow/mcp` lista e chama a `app-tool` do próprio app.
- **O SDK não tem servidor separado por endpoint.** O `ConfigureSessionOptions` não serve no modo padrão: roda só no `initialize`, e uma sessão aberta no `/flow/mcp` é aceita no `/mcp`. Os filtros de `tools/list` e `tools/call`, com `IHttpContextAccessor`, veem o endpoint da requisição atual nos dois modos e nos dois TFMs.
- **O desenho:** os filtros, com uma marca interna no grupo do `MapNetApiFlowMcp`, e nenhuma API pública. A decisão vale a cada requisição. **Trocado na segunda rodada de medição (D-282): veja a seção seguinte.**
- **A exceção:** com `PerSessionExecutionContext = true` sem `Stateless`, o filtro recebe `HttpContext` nulo, e falhando fechado as três tools somem também do `/flow/mcp` desse app. Decidir por sessão, no `initialize`, reabriria o furo da sessão trocada de endpoint. A opção é opcional no SDK (o padrão é `false`) e só existe em app que já tem MCP próprio.

## O que foi entregue (D-282, 24/11/2026)

A segunda rodada de medição, feita antes do código final, trocou o desenho da D-281. **Os critérios da 71.1 continuam valendo.** Onde dizem "sem `HttpContext`", leia "sem a marca do gate". O relato abaixo é da Executora, no corpo da #487, e a Planejadora não repetiu as medições.

- **Por que o `HttpContext` caiu.** A `notifications/initialized` chegou ao filtro com `HttpContext` nulo em 1 ou 2 de cada 20 sessões nos modos com sessão, e no SSE legado as mensagens carregam o `HttpContext` do `GET /sse`. Uma `AsyncLocal` ligada pelo gate chegou em 100% das mensagens do `/flow/mcp` e em nenhuma do `/mcp`.
- **A marca.** O `McpAccessGate` liga a `AsyncLocal` interna `NetApiFlowMcpRequest` só depois de liberar o acesso, nunca num caminho de `403` ou `404`. O `IHttpContextAccessor` deixou de ser registrado.
- **A lista de permitidos.** O `/flow/mcp` responde só `initialize`, `server/discover`, `ping`, `tools/list`, `tools/call` e `logging/setLevel`, mais as notificações `initialized`, `cancelled` e `progress`. O resto recebe `-32601`, e a notificação fora da lista é descartada. Isso fecha o espelho de prompts, resources e completions, o `subscriptions/listen` da revisão `2026-07-28` e os métodos que uma revisão futura criar.
- **A tool pelo método, não pelo nome.** O filtro confere todo `tools/call`, em todo endpoint, pelo método que o executaria. No `/flow/mcp` só roda uma tool declarada pelo `NetApiFlow.Mcp`, e fora dele essa tool recebe `-32602`. O filtro de lista segue a mesma regra. O `AddCallToolFilter` saiu, porque derrubava o MCP inteiro de um app com `CallToolWithAlternateHandler` (A1). Uma tool do app com o nome de uma nossa (A2) fica no endpoint do app, e a partida avisa a colisão.
- **O nome do servidor.** Um filtro de saída dá `NetApiFlow` só às respostas do `/flow/mcp`. O `ServerInfo ??=` global saiu: o endpoint de um app mantém o nome dele ou o padrão do SDK, como na `0.5.3`.
- **`PerSessionExecutionContext = true` com sessões:** o `/flow/mcp` responde `404`, as tools ficam fora de todo endpoint e a partida avisa, com as duas saídas (D-281).
- **Sem API pública nova.** Nenhuma linha do `PublicAPI.Unshipped.txt` mudou.
- **Resíduos aceitos pela auditoria (R-36):** o `initialize` do `/flow/mcp` ainda anuncia as capabilities do app, e toda chamada a elas responde `-32601`; um filtro de mensagem do app registrado antes do `AddNetApiFlowMcp` roda por fora do nosso; no SSE legado (`EnableLegacySse`, obsoleto e desligado por padrão) a sessão leva o contexto do `GET /sse`, e uma sessão aberta no `GET /flow/mcp/sse` já passou pelo gate.
- **Testes e guardas.** Teste de host real visto falhando antes (47 por TFM no `13dfe0e`), nos dois TFMs e nos três modos de sessão, com o cliente oficial na `2025-06-18` e na revisão que ele negocia por padrão. Os guardas que nasceram verdes foram provados por mutação (A, B e C). O cenário `j` do pacote real, dezessete linhas novas no `guards.json` na segunda rodada (sobre as dez da primeira) e o CI com 136 de 136.
- **Medido pela auditoria:** o teste do prefixo existe (`/flow/mcp-app` e `/flow/mcpx`, nos três modos), e com o pacote real um `MapMcp("/flow/mcp-app")` serve só o app, mantém os resources e os prompts dele e não tem as nossas tools.
- **CHANGELOG.** `### Security` conta o espelho, a lista de permitidos e a tool identificada pelo método. Não há linha em `### Fixed` para o A1 nem para o nome do servidor, porque nenhum dos dois chegou a uma versão publicada.

## Tarefa 71.1: as tools só no endpoint do NetApiFlow

O desenho é o que a 71.0 escolher. Os critérios, todos testados:

- O `tools/list` do `/mcp` do app **não** traz as tools do NetApiFlow, e o `tools/call` delas pelo `/mcp` falha (a tool não existe).
- A `app-tool` continua funcionando no `/mcp`, e o `ConfigureSessionOptions` que o app já tenha continua valendo.
- O `/flow/mcp` fica **igual**: as mesmas três tools, as mesmas respostas, as mesmas regras de acesso, o mesmo `serverInfo`.
- Se a 71.0 mostrou o espelho, o `/flow/mcp` também **não** lista a tool do app.
- Um app **sem** MCP próprio não vê diferença nenhuma.
- **`PerSessionExecutionContext = true` sem `Stateless`.** O filtro não tem `HttpContext`, e as tools do NetApiFlow ficam indisponíveis nos dois endpoints, o `/flow/mcp` incluído: é falhar fechado (D-281). Na partida sai um aviso único que diz o porquê e as duas saídas (`Stateless = true`, ou `PerSessionExecutionContext = false`). O aviso nunca lança e nunca impede o app de subir. Teste de host para ele e uma frase no README, nos dois idiomas.
- **Base path e `PathBase`.** Com `MapNetApiFlowMcp("/interno/mcp")` e com `app.UsePathBase("/api")`, as três tools funcionam no endpoint do NetApiFlow e seguem fora do `/mcp` do app. A decisão não compara com o `/flow/mcp` fixo.
- **Prefixo.** Um endpoint MCP do app cujo caminho começa com o do NetApiFlow (`MapMcp("/flow/mcp-app")`, `"/flow/mcpx"`) não recebe as tools. A comparação é pelo endpoint, e não por `StartsWith`.
- **Sessão trocada de endpoint.** Um cliente que faz o `initialize` no `/flow/mcp`, onde tem acesso, e reusa o `Mcp-Session-Id` no `/mcp` do app não alcança as tools pelo `/mcp`, nem no `tools/list` nem no `tools/call`. A decisão vale a cada requisição e não fica guardada na sessão. Mede-se também o contrário: a sessão aberta no `/mcp`, usada no `/flow/mcp`, segue as regras de acesso do `/flow/mcp`.
- **Falhar fechado.** Sem `HttpContext`, ou quando não dá para saber de qual endpoint veio a requisição (por exemplo um transporte `stdio` no mesmo processo, se o SDK permitir junto do HTTP), as tools do NetApiFlow não aparecem. Se a 71.0 mostrar que esse caso não existe na 2.3.1, o relatório diz isso.
- **Os dois modos do servidor.** Todos os critérios acima valem com `WithHttpTransport(o => o.Stateless = true)` e no modo padrão, porque o caminho da sessão muda entre os dois.
- Sem API pública nova. Se o desenho pedir uma, **para** e avisa.

## Tarefa 71.2: testes, o pacote real, README e CHANGELOG

- **Teste de host real, visto falhando antes,** nos dois TFMs, para cada critério da 71.1, os cinco últimos nos dois modos do servidor. Um critério que nasce verde (o de falhar fechado, por exemplo) é provado por mutação, como na Fase 66 (D-276).
- **Um cenário permanente no job do pacote real** (`scripts/package-scenarios.mjs`): o app do job ganha um MCP próprio, e em `Production`
  sem autorização o `/mcp` não lista as tools do NetApiFlow, enquanto o `/flow/mcp` segue em `404`. Todo achado da auditoria que dá para
  reproduzir de fora vira cenário permanente (`.claude/rules/contratos-e-consumidores.md`). A vermelha vista no CI.
- **A linha do `guards.json`:** `S9-mcp-tools-not-in-the-app-endpoint`.
- **README, nos dois idiomas:** a frase "Same security rule as the dashboard" passa a dizer onde as tools existem (só no `/flow/mcp`) e que
  um app com MCP próprio não as serve no endpoint dele.
- **`CHANGELOG.md`, em inglês, `### Security`:** o que vazava, para quem (um app que hospeda o próprio MCP), desde quando (até a `0.5.3`) e o
  que mudou. **O espelho também conta, em `### Security`:** quem tem acesso ao `/flow/mcp` alcançava as tools do app, sob a regra do painel e não a do app. O advisory é a Tarefa 71.3.

## Tarefa 71.3: o advisory (D-279)

O dono decidiu publicar um quarto GitHub Security Advisory junto do release da `0.6.0`, como os outros três (D-203, D-206 e o da Fase 56). **A Planejadora escreve o rascunho nesta fase, depois da correção,** no molde da Tarefa 50.10 (título, pacote e versões afetadas, CWE, descrição, impacto, o que não é afetado, a correção e o contorno, em inglês e sem dado de app real). **A severidade e o vetor CVSS a auditoria propõe depois da correção, com o pacote real,** como fez nos outros, e o dono confirma. As versões afetadas são de `0.2.0` a `0.5.3`, todas as publicadas do `NetApiFlow.Mcp` (o registro é idêntico desde o `b1a933d`, reproduzido na `0.2.0` publicada, nos dois TFMs). **O rascunho conta os dois lados:** as traces vazam pelo `/mcp` do app, sem a regra de autorização do painel; e quem tem acesso ao `/flow/mcp` alcança as tools do app, que podem ser de escrita, com a regra do painel em vez da regra do app. Quem publica é a auditoria, junto do Release (D-198), e a Executora não publica nada.

### O rascunho (D-282)

Esta seção é só o rascunho, em inglês e sem dado de app real. Quem publica é a auditoria, junto do Release (D-198).

**O que foi verificado para escrever o texto, e o que não foi:**

- O nuget.org lista o `NetApiFlow.Mcp` de `0.2.0` a `0.5.3`, e o `AddNetApiFlowMcp` da tag `v0.2.0` é o mesmo registro sem filtro (`git show v0.2.0`). Isso é da auditoria.
- **Reproduzido de verdade** na `0.5.3` pela auditoria, e na `0.2.0` e na `0.5.3` publicadas pela Executora (a 71.0). Nas versões entre as duas, o intervalo vale por construção. A Planejadora não reproduziu nada.
- **A conta do CVSS 3.1, feita por script pela Planejadora:** 5,3 para o vetor abaixo, e 4,3 se o `/mcp` do app exigir autenticação (`PR:L`). O mesmo script reproduz os 4,2 do vetor do advisory da Fase 56.
- **O vetor conta só o lado das traces,** e a auditoria decidiu (D-283) que o espelho fica fora da nota: ele exige passar pela regra do painel (Development ou autorização configurada), um acesso que em geral já é de quem opera o app, e o `I` dependeria de o app ter tools que escrevem, o que o NetApiFlow não controla. O espelho fica no texto de Description e Impact.
- **A CWE e a mitigação para quem fica na `0.5.3`:** a CWE é proposta da Planejadora, e a auditoria aceitou as duas (a 862 é o lado das traces, a 863 é o espelho). A mitigação (`RequireAuthorization()` no `MapMcp` do app) é da auditoria, e ninguém a testou com o pacote: é o comportamento padrão do ASP.NET para um endpoint com autorização.
- **O que a auditoria corrigiu no texto (D-283):** saiu a regra de `Host` e `Origin`, porque o `RequestOriginGuard` não existe na `0.5.3` (a Planejadora repetiu o `git grep`: zero arquivos em `src` na `v0.5.3`); a colisão de nome só foi medida com o app registrando a tool primeiro, por isso o "first"; as saídas do `PerSessionExecutionContext` são as do aviso do pacote; a decisão é por mensagem JSON-RPC; e o `RequireAuthorization()` só restringe a quem autentica.

**Texto (campos do formulário de advisory do GitHub):**

> **Title:** The MCP tools of NetApiFlow are served on the MCP endpoint of the application, outside the access rule of the
> dashboard
>
> **Ecosystem and packages:** NuGet. `NetApiFlow.Mcp`, affected `>= 0.2.0, <= 0.5.3`. Patched version: `0.6.0`.
>
> **CWE:** CWE-862, Missing Authorization. CWE-863, Incorrect Authorization.
>
> **Severity:** Moderate. CVSS 3.1 base score 5.3, `CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:N/A:N`. The score applies when the
> MCP endpoint of the application is anonymous. When that endpoint requires authentication, the privileges required are low
> and the score is 4.3.
>
> **Description:** `NetApiFlow.Mcp` adds three read-only tools to the MCP server of the process: `list_recent_traces`,
> `get_trace` and `get_component_stats`. The MCP SDK it uses, `ModelContextProtocol.AspNetCore`, has one MCP server per
> process, so every endpoint mapped with `MapMcp` serves everything that server has. In an application that hosts its own MCP
> server in the same process, the three tools were served on the endpoint of the application, outside the access rule that
> protects the NetApiFlow endpoint (`/flow/mcp`): not found outside the Development environment unless authorization or an
> `IFlowAccessFilter` is configured. `list_recent_traces` returns the id, method, route
> template, status and duration of recent requests, and `get_trace` returns a trace with its steps (component names and SQL
> text without parameter values). The reverse also held: the NetApiFlow endpoint listed and ran the tools of the application,
> and served its prompts, resources and completions, under the access rule of the dashboard instead of the rule of the
> application.
>
> **Trigger:** The application hosts its own MCP server in the same process (`AddMcpServer` and `MapMcp`) and also calls
> `AddNetApiFlowMcp`.
>
> **Impact:** Anyone who can reach the MCP endpoint of the application can read the metadata of recent requests and their
> traces, even where the NetApiFlow endpoint is closed. NetApiFlow never records request bodies, headers, query strings,
> method arguments or SQL parameter values, so none of those are exposed. The three tools only read. Through the reverse path,
> a client admitted to the NetApiFlow endpoint could call the tools of the application, which may write, under the rule of the
> dashboard.
>
> **Not affected:** Applications that do not host their own MCP server in the same process. For them the NetApiFlow endpoint
> was, and is, the only place where the tools exist.
>
> **Patches:** From `0.6.0`, the three tools exist only on the NetApiFlow endpoint, and the decision is made for every JSON-RPC
> message.
> The endpoint of the application does not serve them. The NetApiFlow endpoint answers only the handshake, `ping`,
> `logging/setLevel`, `tools/list` and `tools/call` for its three tools. Any other method gets `-32601`, so the prompts,
> resources and completions of the application are not reachable through it. When the application registers a tool with the
> same name as a NetApiFlow tool first, that name serves the tool of the application on its own endpoint, the NetApiFlow tool
> with that name is not served on any endpoint, and a warning at startup names the tool to rename. When the application sets
> `PerSessionExecutionContext = true` with sessions, the tools are not served on any endpoint, the NetApiFlow endpoint answers
> 404, and a warning at startup names the two ways out (`PerSessionExecutionContext = false`, or
> `SessionMode = HttpServerSessionMode.Stateless`).
>
> **Workarounds:** Require authorization on the MCP endpoint of the application (`MapMcp(...).RequireAuthorization()`). Its
> authenticated users can still read the traces. Or do not host the MCP server of the application in the same process as
> `NetApiFlow.Mcp`. Upgrade to `0.6.0`.

## Fora de escopo

- A publicação do advisory: é da auditoria, junto do Release (D-198).
- Mudar o que as tools devolvem ou o nome delas.
- O `/flow/mcp` de um app que não usa o MCP do SDK de outra forma.

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Cada critério da 71.1 com teste de host real **visto falhando antes**, e o cenário do pacote real visto vermelho no CI.
- A auditoria refaz a reprodução com o pacote real, num app novo com MCP próprio, e reconfere a frase do README.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/71-tools-do-mcp` a partir de `develop` atualizado, **depois** da #481.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

A reprodução e o espelho (71.0). O desenho escolhido e por quê. Para cada critério: o teste, a saída dele falhando antes e passando
depois. O cenário do pacote real, visto vermelho.
