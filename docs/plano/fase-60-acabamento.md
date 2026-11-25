# Fase 60: o acabamento que um revisor vê no primeiro dia (D-210 nível 3, D-236 B12 e B14)

**Estado (25/11/2026):** **as Partes A e B estão entregues em `develop` (#502, squash `0c839d3`, D-294; #504, squash `7934db6`, D-296) e conferidas pela auditoria com o pacote real. A fase está fechada, menos a Arquitetura (60.7), que entra na segunda passada.**

O nível 3 da auditoria de base, mais dois itens da varredura final (B12, o filtro de rota do MCP, e B14, os samples). Nada
aqui derruba o produto, mas é o que um purista abre primeiro: o celular, textos que se contradizem, o Runner pela metade,
um README de sample que não roda.

Vai no `0.6.0` (D-236). Vem **perto do fim**, depois das Fases 57 e 68 (as três mexem no painel) e **antes da Fase 59**.

## Duas PRs, como a Fase 50

- **Parte A, o painel (60.1 a 60.8 e 60.12):** só `ClientApp`.
- **Parte B, a API, o MCP e os samples (60.9 a 60.11).**

Cada tarefa **reproduz antes de corrigir** e tem o teste que a mostra falhando. Tarefa de painel roda o detector
(`design-quality.md`) nas telas afetadas, nos dois temas, no desktop e no `390x844`, com a contagem de avisos antes e depois.
**No celular, a escolha aberta é empilhar na vertical, não rolar na horizontal.**

## Ponto de parada

- Se um item exigir **desenho novo** (um componente que não existe, uma decisão de produto), a Executora para e pergunta.
- Se não achar a causa de uma corrida do formulário (60.3) depois de a medir, para e relata com o que mediu. Não inventa causa.
- O que a Executora **não** conseguir fechar no prazo vai à auditoria, que decide se vai para a `0.7.0` (D-232).

# Parte A: o painel

## 60.1 O celular

| Tela | Defeito |
|---|---|
| Comparar | As colunas B e Delta ficam escondidas |
| Insights | O p95 fica fora da tela |
| Histórico | O botão Comparar é cortado, e a rota é truncada |

## 60.2 Textos do painel

"Etapa mais lenta" aponta a **raiz** e não a folha. "Origem do erro" mostra a duração da **trace**, não a da etapa. "0% do
tempo (812 µs de 402,3 ms)" lê contraditório. "Status Success" sem tradução.

**O critério de cada texto (D-290).** "Etapa mais lenta" é a de maior **tempo próprio**: a duração do span menos a soma da duração dos filhos diretos, nunca abaixo de 0. Numa folha, o tempo próprio é a duração. Com filhos em paralelo a soma passa da duração e o valor fica em 0, que está certo, porque o pai só esperou. A folha mais lenta enganaria no caso comum: um controller que gasta 900 ms no próprio código e faz uma consulta de 5 ms apareceria como "Etapa mais lenta: SELECT ...". O limiar de lento vale para o tempo próprio, o valor mostrado é o tempo próprio, e a tela diz "tempo próprio" nos dois idiomas. O teste tem pelo menos quatro casos: o pai com tempo próprio maior que o do filho, a folha mais lenta, filhos em paralelo e o span único (decisão da auditoria, D-291). "Origem do erro" mostra a duração do span que é a origem. "0% do tempo" passa a "<1%" quando a porcentagem arredonda para 0 e o tempo é maior que zero. "Status Success" é traduzido pelo dicionário do painel, para todos os status que a tela exibir. Se a reprodução mostrar que o defeito é outro, a Executora relata o que mediu antes de mudar.

## 60.3 O Runner

Sem o status HTTP e sem o corpo da resposta. "Trace: <id>" não é link, e o `/runner` sozinho, depois de enviar, **navega para o
grafo ou tem o link** (vale para o produto, não só para a demo). Rótulos colados ("idcaminho", "bodycorpo"). POST sem exemplo de
corpo. Mensagem genérica no 500 (liga com a Tarefa 58.4). **A corrida do formulário:** o campo de rota preenchido poucas dezenas de
milissegundos depois de escolher o endpoint volta vazio, e o envio diz "Preencha o parâmetro de rota" (1 em 25 no `develop`, 3 em 25 num
branch, causa não determinada): medir, achar a causa e corrigir.

**Decisão (D-293): a corrida sai da `0.6.0`.** A Executora mediu 100 tentativas no `3ed8b98` e não reproduziu nenhuma, e a auditoria não tem mais o roteiro que deu 1 em 25. Sem reprodução, o item vira o item 62 do roadmap, da `0.7.0`, como "não reproduzido". As outras partes da 60.3 seguem. A auditoria tenta de novo com backend real na segunda passada, e se reproduzir traz o roteiro.

**O desenho do que faltava (D-290).** A Executora propôs, e a Planejadora aprovou com ajustes, o menor bloco possível, no rodapé do formulário e compacto, para o botão de enviar continuar à vista (60.12). Uma linha `HTTP <status> · Trace: <id>`, com o id como link do app (`routerLink`, nunca um `/flow` escrito à mão, por causa do `PathBase`) para o grafo. Sem navegação automática depois de enviar: o status e o corpo têm de ficar à vista. O status aparece também nas falhas 4xx e 5xx, e a falha de rede mantém o texto da 58.4, sem "HTTP 0". Abaixo, um `<details>` fechado, "Corpo da resposta", com o texto em mono 12, altura máxima de 10rem com rolagem, cortado em 4000 caracteres com um aviso, e "(corpo vazio)" quando não há corpo. Sem formatar JSON e sem botão de copiar. Resposta que não é texto (content-type fora de `text/*`, JSON e XML): se o content-type já estiver à mão, `(conteúdo <tipo>, não exibido)`. O corpo fica só na memória do componente: some no próximo envio e ao sair da tela, nunca em `localStorage`, na URL ou em log, e um teste prova isso. Textos nos dois idiomas, pelo dicionário. **O exemplo de corpo do POST** vem do `example` do `requestBody` do OpenAPI quando ele existir, e sem `example` o campo fica vazio como hoje: esqueleto gerado do schema é desenho novo e fica para a `0.7.0`. **A mensagem genérica no 500** já foi trocada pela 58.4: a Executora confere que o painel mostra a causa e não mexe mais.

**Quatro amarras que a auditoria confere (D-291).** (1) O corpo entra só por interpolação de texto do Angular, sem `[innerHTML]` e sem `bypassSecurityTrust`, com um teste para `<img src=x onerror=alert(1)>` e para `text/html`. (2) O corpo nunca vai para `localStorage`, `sessionStorage`, URL ou servidor, e um teste afirma que o storage continua sem ele. (3) O corte de 4000 caracteres vale antes de renderizar, o componente só guarda os 4000 primeiros, e uma resposta de 5 MB não pode travar a aba: a Executora mede e diz o número. (4) Os cabeçalhos da resposta não aparecem, só o status e o corpo, porque um cabeçalho pode carregar `Set-Cookie` ou token. O `X-NetApiFlow-TraceId` segue sendo lido, para o link.

## 60.4 Rótulos

`aria-label="Request summary"` fixo em inglês em `flow-details-panel.component.html`, mesmo em português.

## 60.5 Rotas cortadas sem `title`

A rota do cabeçalho do Fluxo (`flow-workspace__route`) e a do painel de detalhes (`flow-details__route`) são cortadas pela
elipse sem tooltip. A Tarefa 51.10 cobre só o nó do grafo e o rótulo da cascata.

## 60.6 Pequenos de acessibilidade e de estado

No celular, o botão ▷ do cabeçalho do Fluxo, que abre o Runner, tem `aria-label` **vazio**. A lista "ETAPAS" não é clicável. O chip
"Ao vivo" fica aceso numa trace importada.

## 60.7 Arquitetura

Legenda com estados que não existem num mapa estático. N+1 sem contagem. O SVG exportado quebra fora do painel (`_ngcontent` sem CSS).

## 60.8 Histórico

A 3ª caixa marcada desmarca outra em silêncio. "Só lentas" aparece duplicado.

## 60.12 O botão de enviar do Runner fica à vista no celular (D-289)

**O achado, da auditoria (25/11/2026).** Pacote `0.6.0-audit.15` e a `0.5.3` publicada, o mesmo app nos dois. **É idêntico na `0.5.3`: defeito antigo,** que entra por decisão do dono (D-289), exceção única ao congelamento, como a 58.11 (D-284). Em `390x844`, a gaveta "Testar um endpoint" começa em `y=56` e tem 844 de altura, então os últimos 56 px ficam abaixo da tela. Num `POST /orders`, o "Enviar requisição" fica entre `y=839` e `y=883` numa janela de 844: só 5 px visíveis. Receber o foco por `Tab` não o rola para a vista, e o scroll do documento fica em 0. Quem usa o teclado chega ao botão sem enxergá-lo, e no toque ele quase não aparece. É o critério 2.4.11 da WCAG 2.2 (foco não encoberto) e, mais que isso, o Runner no celular sem o botão de enviar à vista. A captura da auditoria é dela (`send390-audit.png`, fora do repositório). **A Planejadora não mediu.**

- **Reproduzir primeiro,** em navegador de verdade, nos dois temas: em `390x844`, e em 768 e 1023, onde a gaveta também aparece. Num endpoint `POST` com corpo e num `GET` sem corpo. O número que vale é o retângulo do "Enviar requisição" contra a janela, antes e depois, e se ele chega à vista por `Tab` e por rolagem da gaveta.
- **A correção é pequena, e o mecanismo é da Executora.** A auditoria sugeriu a altura da gaveta descontando o topo em que ela começa, ou o rodapé do formulário com o botão fixo no fundo da gaveta. O que não pode acontecer é o botão ficar fora da janela em qualquer largura em que a gaveta apareça. Ponto de parada: se a correção pedir um desenho novo, a Executora para e pergunta.
- **O teste:** o jsdom não calcula layout, então um teste de unidade não vê este defeito. A prova é o roteiro de navegador, que mede o retângulo do botão em 390, 768 e 1023, nos dois temas, na pasta `tests/manual-verification/`, no molde da Fase 68, com a captura de antes e de depois. Se algum teste de CI conseguir guardar o defeito (por exemplo, uma checagem do CSS da gaveta), a Executora o propõe, e a linha do `guards.json` só entra se uma mutação a derrubar (D-276).
- **Sem regressão na Fase 68:** o foco preso, o `Esc` e a devolução do foco da gaveta seguem como a #497 deixou, e o detector não acusa `low-contrast` nem `undersized-ui-text`.

## Resultado da Parte A (D-294, 25/11/2026)

Entregue em `develop` na #502 (squash `0c839d3`), com a 60.12. O relato é da Executora, no corpo da PR, e da auditoria, que conferiu com o pacote real `0.6.0-audit.16`, sem e com `UsePathBase`. A Planejadora não repetiu as medições.

- **60.12:** o botão "Enviar requisição" passou de 5 de 44 px visíveis para 44 de 44, em `390x844`, `768x1024` e `1023x768`, nos dois temas. A causa era o `height: 100%` do host da gaveta, que vence o `bottom`.
- **60.1, 60.4 a 60.8:** feitos como no plano, com as linhas que empilham no celular, o `title` das rotas, a lista "Etapas" que seleciona, o chip "Trace importado", a legenda fora do mapa, o `(×5)` no N+1, o SVG exportado limpo e o aviso do Histórico.
- **60.2:** a "Etapa mais lenta" é a de maior tempo próprio (a auditoria mediu Service, 413,2 ms), a origem do erro mostra a duração do próprio passo, `<1%` e o status traduzido.
- **60.3:** o bloco da resposta (status, link por `routerLink`, corpo em texto cortado em 4000 caracteres, sem cabeçalhos), o `example` do OpenAPI no POST. **A corrida do campo de rota saiu da `0.6.0`** (D-293, item 62 do roadmap): 100 tentativas, nenhuma reprodução.
- **Visto vermelho:** 50 testes em 8 arquivos no `9294ec9`, e a mutação do `e024ed8` derrubou 4 dos testes de proteção do corpo. 22 linhas `60A-*`, 206 guardas.
- **O detector:** zero `low-contrast` e zero `undersized-ui-text`, antes e depois, em 24 varreduras cada.
- **Aberto:** a Parte B (60.9 a 60.11) e a tela de Arquitetura, que entra na segunda passada com o pacote real.

# Parte B: a API, o MCP e os samples

## 60.9 Sobras da API

"Expected at most 1 Database calls" (plural). **`Enabled = false` não desliga com outro listener:** reproduzir primeiro, e corrigir só se
for defeito do NetApiFlow.

## 60.10 O filtro de rota (B12)

O filtro de rota do MCP exige o **template exato**, e o exemplo da própria tool (`/api/orders/{id}`) devolve vazio para
`{id:int}`. O filtro passa a casar **ignorando a restrição**, pelo `RouteTemplateNormalizer`, **no REST, no MCP e no Histórico**
(o OpenAPI lista `/api/orders/{id}` e o histórico mostra `/api/orders/{id:int}`). Teste de host real nos três.

## 60.11 Os samples (B14)

O README do `SampleApi.Controllers`: o `dotnet run` sem `-f` falha por multi-target, cita `GET /api/orders` (que dá 404) e não diz como
subir o PostgreSQL. O `SampleApi.MinimalMediatR` **não tem README**. Os dois funcionam pelo caminho do usuário, com os nomes novos da
Fase 67, e cada comando do README foi **executado**.

## Resultado da Parte B (D-296, 25/11/2026)

Entregue em `develop` na #504 (squash `7934db6`). O relato é da Executora, no corpo da PR, e da auditoria, que mediu com o pacote real `0.6.0-audit.17` contra a `0.5.3`. A Planejadora não repetiu as medições.

- **60.9a:** a mensagem usa o singular para o limite 1, nos dois READMEs.
- **60.9b:** com `Enabled = false`, um listener próprio na fonte `NetApiFlow` recebe 0 etapas (3 na `0.5.3`), e com `Enabled = true` recebe como antes. Entra em `### Fixed` (D-295). O custo do `Flow.Step` com `Enabled = true` e com `Enabled = false`, contra a `0.5.3`, entra na medição da 59.4.
- **60.10:** `?route=/orders/{id}` sobre traces `/orders/{id:int}` acha 3 (0 na `0.5.3`), no REST, no MCP e no Histórico, em memória e nos dois bancos. A auditoria conferiu no código da `v0.5.3` que o `route_key` já era gravado normalizado, e refaz com um banco gravado pela `0.5.3` na segunda passada.
- **60.11:** os READMEs dos dois samples, com cada comando executado, e a correção do id do Aspire.
- **Visto vermelho:** em etapas, 4 por TFM em `NetApiFlow.Tests`, 1 em `IntegrationTests` e 4 em `Storage.Tests`, e 213 guardas no verde. O vermelho do MCP foi visto local, e a causa está coberta pela linha `60B-route-filter-in-memory`, que tem o vermelho no CI.
- **Aberto:** a tela de Arquitetura (60.7), com o pacote real, na segunda passada.

## O que **não** está aqui, e onde está

| Item | Onde |
|---|---|
| `dbo.Users` vira "SELECT dbo" | Tarefa 58.10 (B11) |
| `Tag` só aceita `string`, `UseNetApiFlowDashboard` virar `Map*` | Fase 67 (B8) |
| O OTLP sai com spans órfãos | Fase 59 (documentar) |
| `DELETE` e `POST /deploys` sem papel só leitura | Fase 59 (documentar), roadmap `0.7.0` |
| O atributo `sql` do OTLP no lugar de `db.query.text` | Roadmap `0.7.0` |
| `NetApiFlow.Storage.SqlServer` traz o `SqlClient` 5.2.2, que não grava comandos, e a licença do MediatR 13 | Fase 59 (README) |

## Critério que vale para a fase inteira

- Parte A: `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` limpos. Parte B: build, testes e formato limpos nos
  dois TFMs.
- Cada item com teste **visto falhando antes**. Os do painel também no navegador, com captura de tela antes e depois.
- Zero achado de `low-contrast` e de `undersized-ui-text` no detector.
- **Pendência pequena dos scripts, sem PR própria:** o guarda `import.meta.url === pathToFileURL(process.argv[1]).href` quebra com `ERR_INVALID_ARG_TYPE` quando o módulo é importado por `node -e`, porque `process.argv[1]` não existe (reproduzido em `verify-built-demo.mjs` e em `verify-live-panel.mjs`). A mesma linha está em oito scripts de `scripts/`. Não afeta o CI. Se a fase tocar um deles, corrige nos oito de uma vez, com um teste do módulo importado sem `argv[1]`.

## Versionamento e fluxo

- `0.6.0`. Parte A: `bugfix/60a-acabamento-painel`. Parte B: `bugfix/60b-acabamento-api`. Ambas a partir de `develop` atualizado.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Por tarefa: o teste, a saída dele falhando antes e passando depois, e a captura de tela dos itens do painel. A contagem do detector. O que
ficou de fora, com o motivo.
