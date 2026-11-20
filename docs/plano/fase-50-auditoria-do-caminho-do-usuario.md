# Fase 50: os achados da auditoria pelo caminho do usuário (D-185, D-186)

Um app novo de `dotnet new webapi` (.NET 10), os pacotes publicados da `0.5.0`, o README ao pé da
letra, cada tela em Development e em Production. Sete achados (a a g) que nenhuma rodada anterior
pegou, porque todas conferiam diff, CI e status HTTP, e nenhuma seguia o caminho de quem chega ao
produto. O (b) já teve a parte de documentação resolvida na PR #325. Aqui fica a parte que exige
código.

A fase tem duas partes, separadas pelo impacto de SemVer:

- **Parte A, sem API pública nova**: (a), (b) aviso no grafo, (c), (d) e (e), mais a 50.9 (aviso do 401
  mudo) e a 50.10 (os portões e o `AllowAnonymous`).
- **Parte B, com opção pública nova** (MINOR): (f) e (g).

**Veículo (D-188, decisão do dono).** O achado (a) sai antes, sozinho, como hotfix `0.5.2`: é a
Fase 52. Todo o resto desta fase vai no `0.6.0`, junto com a Fase 51, e a Executora o implementa em
`develop`.

Cada achado com código pede **um teste por host real, visto falhando antes da correção**
(`.claude/rules/testing.md`, D-176 e D-180). Teste que confere só o valor de uma opção ou o retorno de
uma função isolada não prova o achado.

## Como cada achado foi conferido pela Planejadora

Não escrever na PR que algo foi reproduzido se a Executora não reproduziu de novo antes.

| # | Conferido por | Como |
|---|---|---|
| a | Execução | App novo com os pacotes `0.5.0` do nuget.org e `AddOpenApi`/`MapOpenApi`: 16 caminhos no documento, 14 do NetApiFlow, incluindo `DELETE /flow/api/traces` |
| b | Execução | O mesmo app: `/plain` grava o trace com 0 etapas, `/tracked` (com `.WithNetApiFlowTracking()`) com 1 etapa `Endpoint` |
| c | Leitura de código | Causa achada, não executada no navegador (ver abaixo) |
| d | Leitura de código | `TracingRoutePolicy`, não reproduzido no navegador |
| e | Leitura de código e do fixture | 14 de 20 caminhos do documento gravado são internos, e o interceptor da demo responde 404 sem cabeçalho. O "Send request" não foi aberto no navegador |
| f | Leitura de código | `NetApiFlowMiddleware` escreve o cabeçalho sempre que o trace começa |
| g | Leitura de código | Os dois `SchemaInitializer` e os dois `schema.sql` |

## Ponto de parada: regra de recuo

Se a Executora não conseguir reproduzir o (c), o (d) ou o (e) do jeito descrito, ela **para e relata**
em vez de corrigir o que supôs. Idem se a correção do (a) fizer o Runner perder alguma rota do
usuário, ou se a do (d) tirar do trace uma rota que não é o documento OpenAPI.

## Ordem de execução (D-199, D-200)

1. Os ajustes da PR da Parte A (50.2, 50.3 e 50.4) que a Planejadora devolveu na revisão. São baratos e
   a PR está parada por eles.
2. Tarefa 50.11, o CI do Angular. Pequena, só passos de workflow, e a 50.5 é trabalho de Angular com
   teste novo, então precisa de um CI que o prove (D-202).
3. Tarefa 50.5, a demo funcional. A auditoria a pôs antes de continuar a Parte B (D-200). Quem estiver
   no meio de um passo termina o passo e vem para ela.
4. Fase 53, Tarefa 53.0: medir o `PathBase`, sem corrigir (D-205).
5. Tarefa 50.10, a correção de segurança dos portões, em PR própria.
6. Parte B (50.6 e 50.7) e a tarefa 50.9.
7. Fase 51, e depois a correção da Fase 53.

Cada item é uma PR separada, de `develop` atualizado. PR pequena é mais fácil de a auditoria ler, e a
50.10 precisa poder ser mergeada sozinha.

**Até a 50.11 entrar**, toda PR que mexe em `ClientApp/` roda `npm run test:ci`, `npm run lint` e
`npm run format:check` localmente e cola a saída no resumo, porque o CI ainda não os roda.

## Parte A

### Tarefa 50.1 (a): rotas internas fora do documento OpenAPI

**Movida para a Fase 52 (hotfix `0.5.2`, D-188).** O texto completo, os testes e o ponto de parada
estão lá. Quando o hotfix chega a `develop` pelo merge de `main` de volta, esta tarefa está cumprida,
e a Tarefa 50.5 pode regravar o documento da demo.

### Tarefa 50.2 (b): o grafo vazio ensina a marcar

Um trace com 0 etapas mostra o canvas em branco, e a única mensagem de vazio da tela é "nenhuma
requisição em andamento". A tela precisa explicar.

- Estado vazio do grafo para um trace carregado com 0 etapas, nos dois idiomas: a requisição foi
  gravada, mas nenhuma etapa foi marcada. Controllers MVC são rastreados sozinhos. Um endpoint de
  Minimal API precisa de `.WithNetApiFlowTracking()`. Outro código, de `NetApiFlow.Step(...)` ou de
  `AddFlowTracked`. Texto em frases curtas, com o nome da chamada em código. Estado de vazio com cor,
  ícone e texto (`.claude/rules/frontend.md`).
- **Teste:** de componente para o estado vazio, mais um teste em navegador de verdade (Playwright)
  contra um host real com um endpoint sem a marcação, nos dois idiomas e nos dois temas, visto
  falhando antes.

O endpoint de Minimal API virar etapa sem marcação é a Fase 51, por decisão do dono (D-185). O aviso
desta tarefa continua valendo depois dela: com a opção da Fase 51 desligada, ou em qualquer trace sem
etapa, é ele que ensina.

### Tarefa 50.3 (c): o painel se contradiz com etapa de microssegundos

Com uma etapa `Endpoint` de 120 µs visível no grafo, o painel diz "100% do tempo (441 µs de 441 µs) não
está coberto por nenhuma etapa registrada".

**Causa, achada lendo o código.** O erro está na conta, não no texto.
`computeUnexplainedTime` (`features/flow/details/waterfall.ts`) monta a união dos intervalos
`[startedAt, endedAt)` com `Date.parse`, que tem precisão de milissegundo. Uma etapa de 120 µs que
começa e termina no mesmo milissegundo vira um intervalo de largura 0, a cobertura dá 0, e o
tempo sem cobertura dá o total: 441 de 441. O texto, ao contrário, mostra microssegundos exatos, o que
faz a contradição aparecer. O mesmo `Date.parse` calcula o deslocamento de cada barra em
`buildWaterfall`, então uma trace de microssegundos tem as barras mal posicionadas pela mesma razão.

- A união de intervalos continua (D-78: etapa aninhada e ramos paralelos não contam duas vezes), mas
  passa a usar microssegundos. Confirmar quantos dígitos fracionários o JSON de `startedAt` e `endedAt`
  traz. Se ele chegar até 7 dígitos, ler todos. Se truncar em milissegundo, a conta usa
  `durationMicros` de cada etapa, que é a fonte da duração exibida (seção 7.3 do prompt).
- Corrigir o deslocamento das barras pelo mesmo caminho.
- **Não corrigir o que está certo (D-196).** A auditoria num app real mostrou que, com etapa de
  milissegundos, a conta bate (40% sem cobertura, controller com 77 de 131 ms). E um "96% (1,01 s de
  1,05 s)" com uma etapa de 41 ms na primeira requisição também está certo: o tempo da primeira
  requisição fora das etapas é real. O defeito é só o de etapa de microssegundos.
- **Teste:** uma trace de 441 µs com uma etapa de 120 µs esperando 321 µs sem cobertura, 73%, e não
  100%. Visto falhando antes. Casos que já existiam (etapa aninhada, `Task.WhenAll`) continuam
  passando. Mais um teste em navegador de verdade que abre o painel de uma trace gravada com etapa de
  microssegundos.

### Tarefa 50.4 (d): a leitura do OpenAPI pelo Runner vira trace do usuário

`TracingRoutePolicy.ShouldTrace` só exclui `{DashboardBasePath}/**` e os `ExcludePaths` do usuário.
Quando o Runner busca o documento OpenAPI do app, a requisição `GET /openapi/{documentName}.json`
entra nas execuções recentes e nas métricas. No teste do auditor, 2 requisições viraram 5 traces.

**Decisão (D-185):** a política passa a excluir também `OpenApiDocumentPath`, comparando o caminho
sem diferenciar maiúsculas. O documento OpenAPI é infraestrutura, como o próprio dashboard, e não fluxo
de negócio. Descartado: um cabeçalho que o Runner mandaria para a requisição não ser rastreada, porque
exige mudança no frontend e confiar num cabeçalho que qualquer cliente pode mandar. Quem serve o
documento por outro caminho (Swagger UI, por exemplo) já pode usar `ExcludePaths`.

- **Teste (host real):** uma requisição ao `OpenApiDocumentPath` não gera trace nem entra em
  `GET /metrics`, e uma rota qualquer do usuário continua gerando. Com `OpenApiDocumentPath`
  personalizado também. Visto falhando antes.

### Tarefa 50.5 (e): a demo funcional (D-200)

O dono pediu por escrito uma "demo funcional (mesmo mockado)". O critério de pronto abaixo é da
`AUDITORIA-16-11` (D-200) e vale acima do texto anterior desta tarefa. A dependência do (a) acabou: o
hotfix `0.5.2` está em `develop` (#340). Esta tarefa passa à frente da Parte B, e vem **depois da 50.11**:
o teste Vitest novo dela precisa de um CI que o rode (D-202).

**O que o dono vê hoje na demo publicada** (relato da auditoria, a Executora reproduz no navegador antes
de mexer): a versão `v0.5.0`; o Runner lista `GET /api/orders/{id}` e, com `id` 1, o "Enviar requisição"
responde só "A requisição falhou.".

**Causas, por leitura de código, a confirmar:**

- O documento gravado herdou o (a). `assets/demo-fixtures/openapi-document.json` tem 20 caminhos, 14
  deles internos, por isso o Runner lista rota interna.
- O `demoFixtureInterceptor` trata qualquer URL com `/api/` como chamada do dashboard. As rotas do próprio
  sample (`/api/orders/...`) caem nessa regra e, sem fixture, recebem 404 sem cabeçalho. O Runner lê o
  `X-NetApiFlow-TraceId` da resposta (`endpoint-form.component.ts`), então sem ele não segue para trace
  nenhum, e a tela não explica o que houve. Separar chamada do dashboard (sob o caminho base) de rota do
  sample é parte da correção.
- O `config.json` do fixture já diz `0.5.2` em `develop`. O `v0.5.0` que o dono vê é o build publicado,
  que não foi refeito.

**Critério de pronto (D-200):**

- O documento é regravado contra o sample, sem rota interna. O Runner da demo lista só rotas do sample.
- **Toda** rota que o Runner da demo lista tem resposta gravada, com `X-NetApiFlow-TraceId` apontando para
  um trace que existe no fixture. "Enviar requisição" sempre chega a um grafo. Não basta cobrir algumas
  rotas. A resposta é achada por método e template da rota, não pelo valor do parâmetro, e não depende do
  corpo enviado: mudar o `id` ou editar o corpo não leva a "falhou".
- "A requisição falhou." sem explicação não aparece em nenhum caminho da demo. A mensagem "a demo não
  envia requisição de verdade" existe só como rede de segurança, para rota sem resposta gravada, e um
  usuário que clica pelo caminho normal nunca a vê. Se o mesmo texto genérico existir fora do modo demo, a
  Executora relata à Planejadora em vez de mexer nele.
- A versão mostrada na demo publicada é a do `config.json`, e esse arquivo é o do pacote atual.

**Testes:**

- Vitest: um teste percorre cada operação do `openapi-document.json` e prova que há resposta gravada com
  o cabeçalho e que o trace apontado existe no fixture. É ele que falha quando alguém regrava o documento
  e esquece uma rota. Visto falhando antes.
- Navegador de verdade (Playwright), no desktop e em `390x844`, nos dois temas: abrir o Runner, enviar
  **cada** rota listada e chegar ao trace, conferindo que o grafo renderizou, não só que a URL mudou.
- O detector de qualidade visual (`.claude/rules/design-quality.md`), com a contagem de avisos antes e
  depois no relatório. As capturas de tela ficam locais e vão no relatório, sem commit de PNG.

**Publicação:** a demo vive em `netapiflow-site`, repositório separado (D-157). A Executora reconstrói,
prepara e para. A auditoria revisa o resultado e faz o push (D-198).

### Tarefa 50.8: todo projeto de teste roda no CI e no `release.yml`

**Movida para a Fase 52 (Tarefa 52.6, D-194), por decisão do dono.** Os três projetos entram nos dois
workflows dentro do hotfix `0.5.2`. Resta aqui só a regra: um projeto de teste novo entra nos dois
workflows na mesma PR (`.claude/rules/testing.md`). A decisão de rodar ou aposentar o
`PrePipelineInteropSpike` fica adiada.

### Tarefa 50.9: aviso quando o dashboard cai num 401 mudo (D-197)

Num app com `FallbackPolicy` que exige usuário autenticado, as rotas do dashboard respondem 401 se
ninguém encadeou `AllowAnonymous()` nem `RequireAuthorization(...)` (D-196). Quem recebe esse 401 não
tem como saber por quê.

**Desenho.** Depois que os endpoints existem (`IHostApplicationLifetime.ApplicationStarted`), um aviso
em log, uma vez, nível `Warning`, quando as duas coisas são verdadeiras: o
`AuthorizationOptions.FallbackPolicy` está definido, e as rotas do dashboard (as sob o caminho base)
não têm `IAllowAnonymous` nem `IAuthorizeData` nos metadados. A mensagem diz o que acontece (401 no
lugar da UI) e como resolver: em Development, `AllowAnonymous()` encadeado em
`UseNetApiFlowDashboard()`. Fora de Development, `RequireAuthorization(...)` sozinho, ou
`AllowAnonymous()` junto de um `IFlowAccessFilter`. E nunca `AllowAnonymous()` junto de
`RequireAuthorization()`. Sem API pública nova, e a exceção da própria checagem é logada e engolida,
como `.claude/rules/tracing.md` exige: o aviso nunca atrasa nem derruba o início do app.

**Testes de host real**, o aviso visto faltando antes:

| Cenário | Esperado |
|---|---|
| `FallbackPolicy` e nada encadeado no dashboard, em Development e em Production | Um aviso, com o texto-chave |
| O mesmo com `.AllowAnonymous()` encadeado | Nenhum aviso |
| O mesmo com `.RequireAuthorization("x")` encadeado | Nenhum aviso |
| Sem `FallbackPolicy` | Nenhum aviso |

**Pronto quando:** os quatro cenários passam e o primeiro foi visto falhando antes.

### Tarefa 50.10: `AllowAnonymous` não pode abrir o dashboard nem o MCP (D-199)

Achado de segurança da `AUDITORIA-16-11`, reproduzido de novo pela Planejadora em 20/11/2026 contra os
pacotes publicados `NetApiFlow`, `NetApiFlow.Dashboard` e `NetApiFlow.Mcp` `0.5.2`, num app .NET 10 em
`Production`, com uma policy `Admins` que nega todo mundo:

| Configuração (fora de Development) | Dashboard | MCP |
|---|---|---|
| `RequireAuthorization("Admins")` e `AllowAnonymous()` encadeados, sem filtro | `/flow/` 200, `/flow/api/config` 200, `GET /flow/api/traces` 200, **`DELETE /flow/api/traces` 204** | `initialize` 200 |
| `AllowAnonymous()` sozinho, sem filtro | 404 | 404 |
| Os dois encadeados, com `IFlowAccessFilter` negando | 403 | 403 |
| Os dois encadeados, com `IFlowAccessFilter` liberando | 200 e `DELETE` 204 | 200 |

**Causa.** `DashboardAccessGate` e `McpAccessGate` decidem com uma linha só: o endpoint tem
`IAuthorizeData`? O middleware de autorização ignora a policy quando o endpoint também tem
`IAllowAnonymous`, então a presença de `IAuthorizeData` não prova que alguém vai ser barrado. Quem
encadeou os dois, por engano ou por copiar um trecho do próprio app, abre o painel e a rota que apaga
todos os traces.

**Desenho.** Fora de Development e sem `IFlowAccessFilter`, a rota conta como autorizada só quando tem
`IAuthorizeData` **e não tem** `IAllowAnonymous`. Qualquer outra coisa é 404, como já é hoje. Com
`IFlowAccessFilter` registrado, o filtro continua decidindo, e em Development nada muda. Sem API pública
nova. Os dois portões são duas cópias da mesma regra, em dois pacotes: corrige-se as duas igual. Não
criar helper público para isso.

**Testes de host real**, o primeiro de cada bloco visto falhando antes (hoje responde 200 e 204),
junto dos testes de portão que já existem (`DashboardAuthorizationTests`, `NetApiFlowMcpEndpointTests`):

| Cenário, fora de Development | Esperado |
|---|---|
| `RequireAuthorization` e `AllowAnonymous` encadeados, sem filtro | Toda rota 404 (no dashboard incluindo `DELETE /flow/api/traces`; no MCP o `initialize`). Um trace gravado antes continua no store depois da tentativa de `DELETE` |
| `RequireAuthorization` sozinho, usuário com a claim | 200 (trava contra corrigir demais) |
| `RequireAuthorization` sozinho, usuário sem a claim | 403, como já é |
| `AllowAnonymous` sozinho, sem filtro | 404, como já é |
| Os dois encadeados mais `IFlowAccessFilter` liberando, e negando | 200 e 403, o filtro decide |
| Development, os dois encadeados | Aberto, como já é |

**Também nesta tarefa:**

- O README, nos dois idiomas, diz hoje (seção de segurança, perto de `dashboard.AllowAnonymous()`):
  "Nunca encadeie `.AllowAnonymous()` e `.RequireAuthorization()` juntos. O `AllowAnonymous` vence, a
  autorização é ignorada, e o dashboard fica aberto para todo mundo." (`README.md:540`,
  `README.pt-BR.md:545`.) Essa frase passa a ser falsa. Troca por: os dois juntos não autorizam nada, e
  fora de Development, sem `IFlowAccessFilter`, toda rota responde 404. O XML doc de
  `UseNetApiFlowDashboard` e de `UseNetApiFlowMcp` e o resumo dos dois portões dizem a mesma regra.
- `CHANGELOG.md`, em `[Unreleased]`, seção `### Security`, em inglês: o que mudou, e que quem dependia dos
  dois encadeados para abrir o painel agora recebe 404 e deve registrar um `IFlowAccessFilter`.
- **Fora desta tarefa:** um aviso novo na partida para o caso dos dois encadeados. O 404 sem configuração
  já é mudo hoje e o README o explica, então o aviso da 50.9 segue só para o 401 sob `FallbackPolicy`.
  Se alguém topar com isso num app real, vira item do roadmap.

**Pronto quando:** os testes da tabela passam nos dois TFMs, o primeiro de cada bloco foi visto falhando
antes, o README nos dois idiomas, o XML doc e o `CHANGELOG.md` dizem a regra nova, e a PR é própria,
separada da 50.5, para a auditoria conseguir ler e mergear a correção de segurança sozinha.

#### Rascunho do GitHub Security Advisory (D-203)

O dono decidiu publicar um advisory de severidade baixa para as versões afetadas, junto do `0.6.0`. Quem
publica é a auditoria, com o Release. Esta seção é só o texto, em inglês, sem dado de app real. A
Executora não publica nada.

**O que foi verificado para escrever o texto, e o que não foi:**

- A linha com defeito é idêntica no `v0.1.0` do `NetApiFlow.Dashboard` e no `v0.2.0` do
  `NetApiFlow.Mcp`, e `git log -S"IAuthorizeData"` acha um único commit em cada arquivo, o que o
  introduziu. Nenhum commit posterior a tocou.
- O nuget.org tem o Dashboard de `0.1.0` a `0.5.2` e o `NetApiFlow.Mcp` de `0.2.0` a `0.5.2`, então o
  intervalo afetado é toda a vida publicada de cada pacote.
- **Reproduzido de verdade só no `0.5.2`.** As versões anteriores são afirmadas pela leitura do código
  idêntico. Se a auditoria quiser dizer "reproduzido em todas", precisa rodar o `0.1.0` e o `0.2.0`.
- No MCP, `tools/list` devolveu `get_component_stats`, `get_trace` e `list_recent_traces`, e
  `get_component_stats` devolveu dados reais do store. As três ferramentas são de leitura, e o MCP não
  tem nenhuma que apague. A rota que apaga é a do Dashboard, `DELETE /flow/api/traces`.

**Severidade, para a auditoria decidir ao publicar.** O dono escolheu "baixa". Pelo CVSS 3.1, com o
disparo exigindo configuração do dono do app (`AC:H`), o vetor `AV:N/AC:H/PR:N/UI:N/S:U/C:L/I:L/A:N`
dá 4,8, que é **Média**, porque o `DELETE` conta como integridade. Para o resultado ser Baixa (3,7) o
vetor precisa de `I:N`, o que esconde o `DELETE`. Publicar "baixa" e ao mesmo tempo dizer no texto que
qualquer um apaga todos os traces é uma inconsistência que um leitor atento nota, então a escolha entre
as duas coisas é consciente: ou "Média" com o `DELETE` no texto, ou "Baixa" com o texto dizendo que a
exposição é de leitura. A Planejadora recomenda a primeira, porque o texto verdadeiro é o que sustenta a
transparência que o dono quis.

**Texto (campos do formulário de advisory do GitHub):**

> **Title:** Dashboard and MCP routes are served to anonymous callers when `RequireAuthorization()` and
> `AllowAnonymous()` are chained
>
> **Ecosystem and packages:** NuGet. `NetApiFlow.Dashboard`, affected `>= 0.1.0, <= 0.5.2`.
> `NetApiFlow.Mcp`, affected `>= 0.2.0, <= 0.5.2`. Patched version: `0.6.0` for both.
>
> **CWE:** CWE-863, Incorrect Authorization.
>
> **Description:** Outside the Development environment, the access gate of the NetApiFlow dashboard and
> of the NetApiFlow MCP endpoint is meant to answer 404 unless the application protected the routes. It
> decided that by checking only that the endpoint carries authorization metadata. ASP.NET Core ignores
> that authorization when the endpoint also carries `IAllowAnonymous`. An application that chained
> `RequireAuthorization(...)` and `AllowAnonymous()` on the NetApiFlow endpoints, outside Development
> and without registering an `IFlowAccessFilter`, therefore served every dashboard and MCP route to
> anonymous callers, while the configuration looked protected.
>
> **Impact:** An anonymous caller can read trace metadata: route templates, step names, durations,
> statuses, truncated and sanitized error messages, SQL command text when `CaptureSqlText` is enabled,
> and any step tags the application set. Through the dashboard, the caller can also delete every stored
> trace (`DELETE /flow/api/traces`). NetApiFlow never records request or response bodies, headers, query
> strings, method arguments or SQL parameter values, so none of those are exposed.
>
> **Not affected:** applications that chain only `RequireAuthorization(...)` (the policy is enforced),
> only `AllowAnonymous()` (the routes answer 404), or that register an `IFlowAccessFilter` (the filter
> decides every request). The Development environment is open by design.
>
> **Patches:** From `0.6.0`, outside Development and without an `IFlowAccessFilter`, a route is served
> only when it carries authorization metadata and no `AllowAnonymous` metadata. Otherwise it answers 404.
> An application that relied on chaining both to open the dashboard now gets 404 and should register an
> `IFlowAccessFilter`.
>
> **Workarounds:** Do not chain `AllowAnonymous()` and `RequireAuthorization()` on the NetApiFlow
> endpoints. Chain `RequireAuthorization("YourPolicy")` alone, or chain `AllowAnonymous()` and register
> an `IFlowAccessFilter`. Upgrade to `0.6.0`.

### Tarefa 50.11: o CI roda os testes, o lint e o formato do Angular

Achado do `merge-auditor` na revisão da Parte A, **conferido pela Planejadora nos workflows**: `ci.yml`
e `release.yml` só rodam `npm ci` e `npm run build` no dashboard, e o `ci.yml` também o detector
`npx impeccable detect`. O script `build` é só `ng build`. Nenhum passo roda `npm run test:ci`,
`npm run lint` ou `npm run format:check`. Os 422 testes do Vitest, o lint e o formato do dashboard só
existem na palavra de quem implementa, e o resumo de cada fase, até hoje, os cita como prova.

**Desenho.** Os três comandos entram em `ci.yml` com o mesmo critério do `visual-quality`
(`needs.detect-changes.outputs.clientapp_changed == 'true'`), e em `release.yml` sempre, antes do `pack`
(D-183). Rodam depois do `npm ci`. A primeira execução pode mostrar violação que se acumulou fora do CI: corrigir o código na mesma PR, nunca relaxar
a regra nem tirar o passo.

1. **Antes de mudar qualquer workflow** (mesma regra da Tarefa 48.4), rodar os três comandos
   localmente e colar a saída no resumo. Se algum falhar sem relação com esta tarefa, parar e relatar.
2. Acrescentar os passos a `ci.yml` e `release.yml`.
3. **Visto falhando no CI de verdade:** numa execução de prova na própria PR, um teste quebrado de
   propósito (e, em outra, um arquivo mal formatado) deixa o CI vermelho no passo certo. O commit de prova
   é desfeito antes do merge, e o relatório traz o link das execuções vermelhas. Sem isso o passo não está
   provado: um passo que nunca viu vermelho pode estar apontando para o lugar errado.

**Pronto quando:** a saída local colada, os três passos rodando em `ci.yml` e `release.yml`, os dois tipos
de falha vistos vermelhos no CI, e a PR de volta ao verde com o `ClientApp` limpo. Branch
`chore/ci-angular-testes-lint-formato`. Esta tarefa vem antes da 50.5 (D-202).

## Parte B

### Tarefa 50.6 (f): o cabeçalho `X-NetApiFlow-TraceId`

O `NetApiFlowMiddleware` escreve o cabeçalho em toda requisição rastreada, em qualquer ambiente, sem
opção para desligar. O README só o cita no contexto dos testes, e o `NetApiFlow.Testing` depende dele.

**Decisão (D-185):** opção nova `NetApiFlowOptions.EmitTraceIdHeader`, padrão **`true`**. Não muda o
comportamento de ninguém, e quem não quer o cabeçalho em Production o desliga. O texto completo do
contrato está em `contratos.md`.

- Com `false`, o cabeçalho não sai em nenhuma resposta.
- `NetApiFlow.Testing`: a exceção de `GetFlowTrace` que hoje diz que a resposta não tem o cabeçalho
  passa a nomear também a opção como causa possível.
- README, nos dois idiomas, na seção de segurança em produção: o cabeçalho existe, o que ele expõe (o
  trace id, o mesmo que o W3C `traceparent` já carrega) e como desligar.
- **Teste (host real):** em `Production`, o cabeçalho sai por padrão e some com `false`. Configurar só
  pela seção de configuração `NetApiFlow` também funciona (D-175, D-176).

### Tarefa 50.7 (g): os storages mexem no banco da aplicação sem pedir

`SqlServerSchemaInitializer` e `PostgreSqlSchemaInitializer`, chamados na primeira vez que o store é
usado, rodam `CREATE TABLE`, `ALTER TABLE ... ADD route_key` e o `UPDATE` de `NormalizeRoutesAsync`
no banco da aplicação, sem opção de desligar. Quem não dá permissão de DDL ao app não tem saída.

E o script de referência não ajuda: os dois `schema.sql` **não têm a coluna `route_key`** (0
ocorrências), nem o índice dela, e não vão no pacote. Quem criar o banco à mão com o script recebe um
schema que o código não aceita.

**Decisão (D-185):** opção `AutoCreateSchema`, padrão **`true`**, em `PostgreSqlStorageOptions` e em
`SqlServerStorageOptions` (texto em `contratos.md`), mais scripts corretos.

- Com `false`, o store não roda nenhum `CREATE`, `ALTER` nem `UPDATE`. Na primeira vez que for usado,
  confere se as tabelas e a coluna `route_key` existem. Se faltar algo, registra uma mensagem acionável
  (diz o que falta e onde está o script) e não derruba a requisição, como `.claude/rules/tracing.md`
  exige de toda exceção da instrumentação.
- Os dois `schema.sql` passam a ser o schema inteiro: com `route_key`, os quatro índices que o
  inicializador cria, a tabela de anotações de deploy, e uma seção de atualização idempotente com o
  `ALTER` e o preenchimento de `route_key` para quem vem de uma versão anterior.
- **Os scripts não vão dentro do pacote.** Ficam no repositório e os READMEs apontam para eles pela
  tag da versão, num trecho "rodando sem permissão de DDL", nos dois idiomas. Dentro do `.nupkg` eles
  entrariam como `contentFiles` no projeto de quem instala.
- **Teste (banco real, `NetApiFlow.Storage.Tests`):** banco criado só pelo `schema.sql` e usado pelo
  store (salvar, consultar, mapa de arquitetura) passa. E o catálogo do banco criado pelo script,
  colunas e índices, é igual ao do criado pelo inicializador. Com `AutoCreateSchema = false` num banco
  vazio, nenhuma tabela é criada. Visto falhando antes, no `route_key` do script.

## Critério que vale para a fase inteira

- Build, testes, formato e lint limpos nos dois TFMs e no Angular. Os projetos de interop e o
  `Storage.Tests` rodados de verdade, com Docker.
- `tests/manual-verification/AotCoreVerification` continua com zero aviso.
- Teste com o pacote real (D-186) no caminho do usuário: um app novo de `dotnet new webapi` seguindo o
  README, o documento OpenAPI conferido, e as telas usadas em navegador de verdade.
- Os cenários da fase novos em `ci.yml` e `release.yml`, na mesma lista (D-183).

## Fora de escopo

- Endpoint de Minimal API virar etapa sem marcação, e o atalho de uma linha para as camadas: Fase 51.
- Os itens 20 a 22 do roadmap (seção "Why not OpenTelemetry?", orçamento de performance no CI com o
  `NetApiFlow.Testing`, caminho para a 1.0).
- O que a auditoria **não** cobriu: os pacotes de storage, o MCP e o OpenTelemetry num app novo, o
  layout mobile num app real. Não chamar de auditado o que não foi testado.

## Versionamento e fluxo

- Tudo desta fase, menos a Tarefa 50.1, vai no `0.6.0` (D-188). A Parte A não muda API pública, e a
  Parte B traz duas opções públicas novas. Branch `bugfix/50-caminho-do-usuario` a partir de
  `develop` para as tarefas 50.2 a 50.4, `bugfix/50e-demo-funcional` para a 50.5,
  `bugfix/50-portoes-allow-anonymous` para a 50.10, `chore/ci-angular-testes-lint-formato` para a 50.11
  e `feature/50b-opcoes-cabecalho-e-schema` para a Parte B. O achado (a) é a Fase 52, em hotfix a partir de `main`.
- Commits pequenos, em inglês, uma linha, sem linha de coautor.
- Ao terminar cada parte: abrir a PR e parar. Tem código, então o merge exige confirmação do dono.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois, e, nos que passam por
navegador, a captura de tela. A confirmação de que o (c), o (d) e o (e) foram reproduzidos de novo antes
da correção. A saída do `AotCoreVerification`. Qual arquivo de teste prova o R-31. A lista do que o
teste com o pacote real do caminho do usuário encontrou, mesmo que não estivesse nesta fase.
