# Fase 56: a superfície HTTP do painel e do MCP (D-210, S6 e S7)

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
leitura dos cabeçalhos de `GET /flow/`. O relatório diz o que foi reproduzido e o que não.

## Tarefa 56.1: validar `Origin` e `Host` (S6)

**Onde roda a checagem.** No portão (`DashboardAccessGate` e `McpAccessGate`), que é um filtro de endpoint e portanto
roda **depois** do pipeline do usuário, quando o `Request.Host` já é o que o app enxerga (depois do
`UseForwardedHeaders`, se o usuário o configurou). Uma checagem no middleware inicial do NetApiFlow veria o `Host` de
antes do proxy. A checagem fica **num lugar só**, usada pelos dois portões, e roda antes da decisão de autorização
(é barata e não depende de estado).

**A regra, a confirmar medindo:**

| Situação | Esperado |
|---|---|
| `Origin` ausente (um cliente que não é navegador: `curl`, o `McpClient` do SDK) | Passa, e segue para a decisão de autorização |
| `Origin` presente e igual à origem da própria requisição (esquema mais `Host`) | Passa |
| Em Development, `Origin` presente cujo host é de loopback (`localhost`, `127.0.0.1`, `[::1]`), em qualquer porta | Passa. É o caso de uma ferramenta local em outra porta, por exemplo o servidor de desenvolvimento do painel, **se** ele fala com a API desse jeito (a confirmar, ver abaixo) |
| `Origin` presente de qualquer outro host | **403**, com uma mensagem que diz a causa e como corrigir |
| Em Development, `Host` que não é de loopback, sem a aplicação ter declarado o host em `AllowedHosts` | **403** |
| Fora de Development | A mesma regra de `Origin`. O `Host` segue o que o `AllowedHosts` e o `UseForwardedHeaders` do usuário já decidem |

O **mesmo `403`** vale para o painel (API REST, stream SSE e arquivos estáticos) e para o MCP, e para todo método. A
especificação do MCP diz "all incoming connections".

**O que precisa ser medido antes de fixar a regra**, porque quebra uso legítimo se errar:

- O `npm start` do painel (`ng serve`, porta padrão `4200`). O repositório **não tem** `proxy.conf` nem
  `proxyConfig` no `angular.json` (conferido pela Planejadora), e o `CONTRIBUTING.md` só manda rodar `npm start`.
  A Executora mede **como** o painel nesse modo fala com a API, se fala, e a `Origin` e o `Host` que chegam.
- O MCP Inspector e um cliente MCP real por HTTP: mandam `Origin`?
- Um proxy reverso, com os contêineres de `nginx` que a medição da Fase 53 já montou: `Host` preservado, `Host`
  reescrito, e `X-Forwarded-Host`. Um `403` falso atrás de um proxy é o risco maior do lado de Production.

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

## Pergunta em aberto, para a auditoria e o dono

O S6 (qualquer página da web fala com o painel e com o MCP abertos) e o S7 (o painel num iframe) merecem um **GitHub
Security Advisory**, como os dois que já existem? Pela classe, seriam CWE-346 (validação de origem) e CWE-1021
(restrição imprópria da camada visual). É decisão de comunicação pública, do dono, e a Planejadora não a toma. A
diferença para os outros dois: aqui só afeta quem roda com o painel aberto, em Development, e o dado exposto são traces
de quem está desenvolvendo.

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
