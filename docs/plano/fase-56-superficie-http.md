# Fase 56: a superfície HTTP do painel e do MCP (D-210, S6 e S7)

**Estado (21/11/2026):** S6, o guarda de `Host` e `Origin` nos dois portões (#398), e S7, os cabeçalhos de segurança e a CSP do painel (#401); e a matriz de autorização em teste de host real, com a correção do `POST /flow/api/deploys`, que lia o corpo antes do portão (#404). S6 e S7 estão em `develop`, e a matriz está aprovada na #404, aguardando o merge. O advisory 3 (DNS rebinding e iframe) é publicado pela auditoria no release.

O painel tem um botão que apaga todos os traces, e o MCP entrega os dados de quem está desenvolvendo. Em
Development os dois ficam abertos de propósito (ADR-0006). A auditoria de base achou que, aberto, **qualquer página da
web** consegue falar com eles, e que o painel pode ser carregado num iframe. Esta fase fecha os dois furos, e
automatiza a matriz de autorização que hoje só existe como auditoria manual.

Vai no `0.6.0` (D-210, D-212). É defeito antigo e não regressão, então não é hotfix (D-205).

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| S6 | O MCP e a API REST não validam `Origin` nem `Host` em Development: `POST /flow/mcp` com `Origin: http://evil.example` deu 200, e o `tools/list` listou as três ferramentas; `GET /flow/api/traces` deu 200 com `Origin` e `Host` falsos; `DELETE /flow/api/traces` deu 204 com `Origin` falso | **Reproduzido pela auditoria no `0.5.2` publicado** (os comandos estão em `docs/plano/auditoria-de-base.md`). Eu não reproduzi de novo. **A especificação** do MCP (versão `2025-06-18`, seção de transporte Streamable HTTP, lida por mim) diz: "Servers **MUST** validate the `Origin` header on all incoming connections to prevent DNS rebinding attacks", "servers **SHOULD** bind only to localhost (127.0.0.1) rather than all network interfaces (0.0.0.0)" e "servers **SHOULD** implement proper authentication for all connections". A página **não** diz que status devolver para uma `Origin` inválida: o `403` deste plano é escolha nossa |
| S7 | Sem cabeçalhos de segurança em `/flow`: nem `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options` ou `Referrer-Policy`. O painel, com o botão de apagar traces, carrega num iframe | **Reproduzido pela auditoria no `0.5.2`**: `GET /flow/` só traz `Content-Type: text/html`. **Código:** li o `ServeStaticOrFallback`, que devolve `Results.File(...)` e `Results.Content(...)` sem nenhum cabeçalho de segurança. Não reproduzi de novo |

## O que está em jogo, mesmo em Development

- **DNS rebinding.** Uma ferramenta de desenvolvimento escutando em `localhost` só é segura se o navegador do
  desenvolvedor não deixar uma página qualquer falar com ela. Numa página que o atacante controla, o nome dele passa a
  resolver para `127.0.0.1`, e o navegador acha que é a mesma origem. A requisição chega com `Host: evil.example` e
  `Origin: http://evil.example`, e hoje responde 200. É o que a `MUST` da especificação do MCP quer impedir.
- **Clickjacking.** O painel num iframe de outra página, com o botão de apagar por baixo de um clique.
- O que muda não é o painel deixar de ser aberto em Development. É ele deixar de ser aberto para **toda página da
  web**, ficando aberto só para quem está na máquina e fala por `localhost`.

## Ponto de parada

Se a Executora não conseguir reproduzir o S6 ou o S7 pelos comandos da auditoria, **para e relata**. Idem se a CSP
exigir `'unsafe-inline'` em `script-src` para o painel funcionar, ou se a validação de `Origin` ou de `Host` quebrar
um uso legítimo que o plano não previu (o servidor de desenvolvimento do painel, o MCP Inspector, um proxy reverso): é decisão
da Planejadora e da auditoria, não da Executora.

## Tarefa 56.0: reproduzir antes de corrigir

Teste de **host real**, visto falhando antes de qualquer mudança, partindo dos comandos da auditoria: as requisições
com `Origin` e `Host` de um domínio qualquer ao painel (`GET` e `DELETE`) e ao MCP (`initialize` e `tools/list`), e a
leitura dos cabeçalhos de `GET /flow/`. **Mais um caso, o que o rebinding produz de verdade (auditoria):** um `GET` com `Host: evil.example` e **sem
`Origin`**. Hoje passa. Depois da 56.1 tem de dar 403. O relatório diz o que foi reproduzido e o que não.

## Tarefa 56.1: validar `Origin` e `Host` (S6)

**Onde roda a checagem.** No portão (`DashboardAccessGate` e `McpAccessGate`), que é um filtro de endpoint e portanto
roda **depois** do pipeline do usuário, quando o `Request.Host` já é o que o app enxerga (depois do
`UseForwardedHeaders`, se o usuário o configurou). Uma checagem no middleware inicial do NetApiFlow veria o `Host` de
antes do proxy. A checagem tem **duas cópias**, uma em cada portão, e **uma tabela única de casos** rodada contra o painel e contra o MCP (um arquivo de teste compartilhado, incluído por link nos dois projetos de teste), de modo que uma cópia que divirja derruba um teste (D-234). Ela roda antes da decisão de autorização
(é barata e não depende de estado).

**O `Host` é a defesa principal, e a `Origin` a secundária (a auditoria mudou a regra).** Depois do rebinding, a
página do atacante e o painel são a **mesma origem** para o navegador, e num `GET` de mesma origem o navegador
pode não mandar `Origin` nenhuma. Então "`Origin` ausente passa" sozinho deixaria o `GET /flow/api/traces` aberto
ao rebinding. Só o `Host` barra, porque ele chega como `evil.example`.

**A regra, a confirmar medindo:**

| Situação | Esperado |
|---|---|
| Em Development, `Host` de loopback (`localhost`, `127.0.0.1`, `[::1]`) ou **declarado explicitamente** | Segue para a checagem de `Origin` |
| Em Development, qualquer outro `Host` | **403**, com uma mensagem que diz como declarar o host. `"AllowedHosts": "*"`, o padrão do `appsettings.json` do `dotnet new`, **não** conta como declarado |
| `Origin` ausente (um cliente que não é navegador, ou um `GET` de mesma origem) | Passa, desde que o `Host` já tenha passado |
| `Origin` presente com o host e a porta do `Host` que o app enxerga, **sem comparar o esquema** (D-240) | Passa |
| ~~Em Development, `Origin` de loopback em qualquer porta~~ | **Removida (D-240).** A Executora mediu: o `npm start` do painel não fala com a API (as chamadas relativas voltam para a porta 4200, sem `Origin`), e o MCP Inspector e o cliente C# não mandam `Origin`. Mantê-la deixaria qualquer app web local, em outra porta, apagar os traces. Vira caso de teste recusado |
| `Origin` presente de qualquer outro host | **403**, com a causa na mensagem |
| Fora de Development | A mesma regra de `Origin`. O `Host` segue o que o `AllowedHosts` e o `UseForwardedHeaders` do usuário já decidem |

"Declarado explicitamente" é uma entrada que nomeia o host no `AllowedHosts` da configuração, e o curinga `*` não
conta. Isso é configuração que o app já tem, não API nova.

O **mesmo `403`** vale para o painel (API REST, stream SSE e arquivos estáticos) e para o MCP, e para todo método. A
especificação do MCP diz "all incoming connections".

**O que precisa ser medido antes de fixar a regra**, porque quebra uso legítimo se errar:

- O `npm start` do painel (`ng serve`, porta padrão `4200`). O repositório **não tem** `proxy.conf` nem
  `proxyConfig` no `angular.json` (conferido pela Planejadora), e o `CONTRIBUTING.md` só manda rodar `npm start`.
  A Executora mede **como** o painel nesse modo fala com a API, se fala, e a `Origin` e o `Host` que chegam.
- O MCP Inspector e um cliente MCP real por HTTP: mandam `Origin`?
- Um proxy reverso, com os contêineres de `nginx` que a medição da Fase 53 já montou: `Host` preservado, `Host`
  reescrito, e `X-Forwarded-Host`. Um `403` falso atrás de um proxy é o risco maior do lado de Production.
- **Os usos legítimos de Development em que o `Host` fica fora do loopback** (auditoria): acessar o painel pelo
  IP da rede local, para testar no celular; `host.docker.internal`; e o WSL. Cada um tem o seu caminho
  documentado, que é declarar o host, e **nenhum fica mudo**: o `403` diz o que fazer.

**API pública:** nenhuma nova, se for possível. Se a investigação provar que é preciso uma lista de origens
permitidas, **volta à Planejadora antes de qualquer código**, porque é opção pública nova (`contratos.md`).

## Tarefa 56.2: cabeçalhos de segurança (S7)

Em **toda** resposta das rotas do painel e do MCP, inclusive as de `403` e `404` dos portões:

- `X-Content-Type-Options: nosniff`.
- `Referrer-Policy: no-referrer`.
- Contra o iframe: `Content-Security-Policy` com `frame-ancestors 'none'`, e `X-Frame-Options: DENY` para o
  navegador antigo.
- Uma `Content-Security-Policy` para o HTML do painel. **A Planejadora não fixa a política: a Executora a mede.** O
  `index.html` de origem não tem script embutido nem recurso externo, mas o **build** do Angular costuma embutir CSS
  crítico e um `onload` no `link`, e isso muda o que a política pode proibir. A Executora lê o `index.html` de
  `dist/`, monta a política mais estrita com que o painel funciona (`script-src 'self'` **sem** `'unsafe-inline'` é o
  que não se negocia), abre o painel num navegador de verdade com os relatos de violação ligados, e registra o que
  precisou ceder e por quê. Se o `'unsafe-inline'` em `style-src` for necessário, ela o mede e o registra. Se só dá
  para o `script-src` ficar estrito desligando uma otimização do build (`inlineCritical`), ela relata.

**Medido e decidido (D-240).** O `index.html` de produção embutia um `<style>` e um `<script>` inline (o CSS crítico e o `data-beasties-media`). Com `script-src 'self'` e o build de então, o painel saía sem estilo. **Saída escolhida: desligar `optimization.styles.inlineCritical` no `angular.json`**, e o `script-src 'self'` literal, sem hash. A política é `default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'` (o `object-src 'none'` foi acrescentado pela auditoria). O `'unsafe-inline'` em `style-src` fica **de propósito**: o Angular injeta o CSS de cada componente como `<style>` em tempo de execução, e hashes que mudam a cada build não se mantêm. Condições: o `DashboardIndexHtmlCache` reescreve o `<base>` de forma robusta (o build passou a serializar `<base href="/"/>`, e a reescrita por texto exato deixava o painel sem renderizar, sem nenhuma violação no log), um teste afirma que o `index.html` embutido não tem script nem style inline, e o painel é medido no navegador com 0 violações nos dois temas.

**Onde:** um ponto só, que cobre as respostas dos dois, incluindo as que o servidor monta. Teste de host real: cada
classe de rota (API, SSE, estático, `index.html`, MCP, e a resposta de `403` e de `404`) traz os cabeçalhos. E uma
medição no navegador, em duas larguras e nos dois temas: o painel carrega **sem nenhuma violação** de CSP no
console, e uma página que o embute num iframe **não o renderiza**.

## Tarefa 56.3: a matriz de autorização, em teste de host real

Hoje a matriz do painel e do MCP é auditoria manual. Passa a ser teste, e a lista de rotas **vem do
`EndpointDataSource`**, não de uma lista escrita à mão, para uma rota nova não escapar dela. Duas coisas:

- **Uma invariante:** todo endpoint sob o caminho base do painel e do MCP tem o portão. Se alguém registrar uma rota
  sem ele, o teste falha.
- **A matriz:** toda rota e todo método, em cada configuração: Development aberto; Production sem configuração;
  `RequireAuthorization` sozinho com e sem a claim; `AllowAnonymous` sozinho; os dois, nas **duas ordens**;
  `AllowAnonymous` por convenção, por atributo no handler e por `MapGroup(...)`; `IFlowAccessFilter` liberando e
  negando. No painel: a API REST (`GET`, `POST` e `DELETE`), o `/flow/api/stream`, os arquivos estáticos e o
  `index.html`. No MCP: `POST`, `GET` e `DELETE` em `/flow/mcp`.

Isto fecha as exigências que a auditoria deixou na #360 (a ordem inversa, o atributo, o grupo, o stream, o `GET` e o
`DELETE` do MCP). O fixture de `DashboardAuthorizationTests.cs` com `RouteTemplate = "/api/orders/1"` passa para
template (`{id}`). Nomear as condições do portão (`HasAuthorizationRequirement`, `AllowsAnonymousAccess`) é opcional,
para quem tocar o portão de novo.

## Tarefa 56.4: README e CHANGELOG

- `README.md` e `README.pt-BR.md`: as regras de `Origin` e de `Host` (o que passa, o que dá `403`, como corrigir
  atrás de um proxy), e os cabeçalhos. O parágrafo "Same security rule as the dashboard" do README do MCP, que só aponta
  para o painel, passa a ser autocontido.
- `CHANGELOG.md`, `### Security`, em inglês: o painel e o MCP recusam `Origin` e `Host` que não são da própria
  aplicação, e respondem com os cabeçalhos de segurança.

## O advisory (D-226)

O dono decidiu publicar **um** GitHub Security Advisory cobrindo o DNS rebinding e o iframe (CWE-346 e CWE-1021),
junto do `0.6.0`, o terceiro, com os dois da Fase 54 e da 50.10. Quem publica é a auditoria. Esta seção é só o
rascunho, em inglês e sem dado de app real.

**O que foi verificado para escrever o texto, e o que não foi:**

- Procurei nos fontes de `NetApiFlow.Dashboard` e `NetApiFlow.Mcp`, em `v0.1.0`, `v0.2.0`, `v0.3.0`, `v0.4.0`,
  `v0.5.0` e `v0.5.2`, qualquer código de `Origin`, `Content-Security-Policy`, `X-Frame-Options`,
  `X-Content-Type-Options` ou `frame-ancestors`: **zero arquivos em todas**. O nuget.org tem o
  `NetApiFlow.Dashboard` de `0.1.0` a `0.5.2` e o `NetApiFlow.Mcp` de `0.2.0` a `0.5.2`.
- **Reproduzido de verdade só no `0.5.2`**, pela auditoria. Eu não reproduzi.
- **Raciocínio meu, não testado:** um app que protege o painel e o MCP com autorização fica de fora da parte do
  rebinding, porque uma requisição ao nome do atacante não leva a credencial emitida para o nome do app. A parte
  do iframe vale em qualquer modo.

**A conta do CVSS 3.1, feita por script, e ela não dá Baixa com o texto verdadeiro.** O dono escolheu a opção
"severidade baixa", e a regra que a auditoria deu é "se o texto verdadeiro não der Baixa, vale o texto
verdadeiro":

| Vetor | Nota | O que conta |
|---|---|---|
| `AV:N/AC:H/PR:N/UI:R/S:U/C:L/I:L/A:N` | **4,2, Média** | Ler os traces (`C:L`) e **apagar todos** (`I:L`) |
| `AV:N/AC:H/PR:N/UI:R/S:U/C:L/I:N/A:N` | 3,1, Baixa | Só a leitura, ignorando o `DELETE` |

O `DELETE /flow/api/traces` conta como integridade, a mesma lógica que deixou o primeiro advisory em Média. O
rascunho segue o texto verdadeiro, **Média 4,2**, e a auditoria leva ao dono que o número saiu diferente da
opção escolhida. `AC:H`, porque o desenvolvedor precisa estar com o app aberto e visitar uma página maliciosa
montada para o rebinding; `UI:R`, porque ele precisa visitar a página; `PR:N`, porque o painel aberto não pede
credencial.

**Texto (campos do formulário de advisory do GitHub):**

> **Title:** The dashboard and the MCP endpoint accept requests from any web page when they are open, and the
> dashboard can be framed
>
> **Ecosystem and packages:** NuGet. `NetApiFlow.Dashboard`, affected `>= 0.1.0, <= 0.5.2`. `NetApiFlow.Mcp`,
> affected `>= 0.2.0, <= 0.5.2`. Patched version: `0.6.0` for both.
>
> **CWE:** CWE-346, Origin Validation Error. CWE-1021, Improper Restriction of Rendered UI Layers or Frames.
>
> **Severity:** Moderate. CVSS 3.1 base score 4.2, `CVSS:3.1/AV:N/AC:H/PR:N/UI:R/S:U/C:L/I:L/A:N`.
>
> **Description:** When the dashboard or the MCP endpoint is open, which is the default in the Development
> environment, it answered requests regardless of their `Host` and `Origin` headers. It also sent no
> `X-Frame-Options`, `Content-Security-Policy` or `X-Content-Type-Options` header. A web page controlled by an
> attacker can use DNS rebinding so that the browser treats the page and the local dashboard as the same origin,
> and then read the traces, delete all of them, and call the MCP tools. The MCP transport specification requires
> servers to validate the `Origin` header on all incoming connections to prevent exactly this. Separately, a
> page can embed the dashboard in an iframe and lead the developer to click its delete button (clickjacking).
>
> **Trigger:** The dashboard or the MCP endpoint is reachable without access protection, and the developer
> visits a malicious web page while the application is running.
>
> **Impact:** Reading and deleting the stored traces, calling the read-only MCP tools, and clickjacking of the
> delete button. NetApiFlow never records request bodies, headers, query strings, method arguments or SQL
> parameter values, so none of those are exposed.
>
> **Not affected:** Applications that protect the dashboard and the MCP endpoint with authorization are not
> exposed to the rebinding part, because a request to the attacker's host name carries no credential issued for
> the application's host name. The framing part applies in every mode.
>
> **Patches:** From `0.6.0`, the dashboard and the MCP endpoint reject requests whose `Host` is not a loopback
> name or explicitly declared, and requests whose `Origin` is not their own origin or a loopback origin, and
> they send security headers that forbid framing.
>
> **Workarounds:** Do not leave a development application running while browsing untrusted sites, or protect the
> dashboard and the MCP endpoint with an `IFlowAccessFilter`, which is enforced in Development too. Upgrade to
> `0.6.0`.

## Fora de escopo

- Autenticação do painel em API só com JWT (item 23 do roadmap).
- TLS.
- Qualquer mudança na regra de quando o painel fica aberto (ADR-0006).

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs e no Angular. `AotCoreVerification` com zero aviso.
- Cada achado com código tem teste de host real **visto falhando antes**.
- A auditoria reconfere a frase do README e refaz as reproduções do S6 e do S7 com o pacote real.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/56-superficie-http` a partir de `develop` atualizado. O S6 e o S7 podem ir em PRs
  separadas, se a Executora preferir. A 56.3 entra numa PR própria, porque é teste e não muda comportamento.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. O que foi reproduzido e o que não. As
medições do `npm start`, do MCP Inspector e do proxy. A política de CSP escolhida e o que precisou ceder. A matriz de
autorização e a invariante.
