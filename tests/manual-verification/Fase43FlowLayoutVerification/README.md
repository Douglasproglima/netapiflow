# Fase 43: layout do workspace `/flow`, base estrutural e desktop

Prova, por geometria real contra um dashboard rodando de verdade, das sete tarefas da Fase 43
(`docs/plano/fase-43-layout-flow-base-e-desktop.md`). Hosts usados:

- `samples/SampleApi.Controllers` (Postgres local via `docker run postgres:16-alpine`), para
  traces reais com spans de verdade (`OrderRepository`/`OrderService` rastreados).
- Um host descartável de 248 rotas (`/api/resource{0..247}/{id:int}`, núcleo + dashboard
  referenciados por `ProjectReference`, sem Postgres) só para a Tarefa 43.6, mesma régua de
  volume real do R-27/R-28 (D-116).

Medido via Playwright (`getBoundingClientRect`, `elementFromPoint`, `getComputedStyle`), nunca só
inspeção visual.

## Tarefa 43.1: piso real de altura

**Correção à especificação original, confirmada antes de implementar**: não existe nenhum
`height: 100vh` no `ClientApp` (a cadeia real é `html`/`body`/`:host` do shell/`.app-content`,
todos `height: 100%`, resolvendo contra a viewport sem nenhum `vh` explícito). Trocar essa cadeia
por `min-height: 100%` reabriria o R-27 (os descendentes do painel de ação precisam de uma altura
*definida* para resolver `max-height: %` contra ela).

Direção aplicada: `.flow-workspace` (o elemento certo, não a cadeia acima dela) ganhou
`height: max(100%, <piso>px)`. Quando `100%` já é maior que o piso (caso comum), o valor
computado continua exatamente `100%`, sem tocar no contrato do R-27. Quando não é, o próprio
`.app-content` (`mat-sidenav-content`, já `overflow-y: auto` por padrão do Angular Material) passa
a rolar de verdade, sem precisar que `html`/`body` participem.

`.flow-workspace__center` ganhou `min-height: 360px` de verdade.

### Os dois limiares de altura, medidos contra o DOM real, não só calculados

| Limiar | Valor do guia (estimativa) | Valor medido (Playwright, varredura fina) | Mecanismo |
|---|---|---|---|
| Rodapé vira aba única | `window.innerHeight` < 800px | **exatamente 800px** (800 → dois cartões, 799 → aba única) | `@media (max-height: 799px)` |
| Página rola de verdade | `window.innerHeight` < 720px | **entre 706 e 707px** (707 → não rola, 706 → rola) | floor de `.flow-workspace` vs altura real de `.app-content` |

O segundo número diverge do estimado (720) por 13px: a conta do guia somou o piso do rodapé em
dois modos (804/724) incluindo a topbar (56px), mas a topbar é irmã de `.flow-workspace`, não
filha. O piso certo de `.flow-workspace` é só a própria soma (732px modo dois-cartões, 652px modo
aba única), e o limiar real de scroll é onde `window.innerHeight - 56 (topbar)` cruza 652px, ou
seja, ~707px de `window.innerHeight`, não 720px. Valores finais no código:
`height: max(100%, 732px)` (padrão) e `height: max(100%, 652px)` (`@media (max-height: 799px)`).

Varredura (1728 de largura, trace de 28 spans real, `/api/orders/sequential?ids=1..25`):

| `window.innerHeight` | `.flow-workspace__center` | `.flow-workspace__footer` | `.app-content` rola? |
|---|---|---|---|
| 1080 | 772px | 220px | não |
| 800 | 372px | 220px (dois cartões) | não |
| 798 | 421px | 170px (aba única) | não |
| 730–710 | 360px (piso) | 170px | não |
| 707 | 360px (piso) | 170px | não |
| 706 | 360px (piso) | 170px | **sim** (8px de overflow real em `.app-content`, `scrollHeight` 652 vs `clientHeight` 651) |
| 700 | 360px (piso) | 170px | sim |

`1366×728` (achado real original): `.flow-workspace__center` = 360px exato (no piso), grafo visível
por inteiro, sem scroll (728 > 707). `.flow-workspace__center.getBoundingClientRect().height >=
360` confirmado em toda a varredura acima.

### Regressão do R-27

Não testado contra as 248 rotas reais nesta rodada especificamente para o R-27 (esse dataset foi
gerado só para a Tarefa 43.6); o mecanismo que o R-27 protege (`.flow-workspace { height: ... }`
continua resolvendo para um valor definido, nunca `none`) não foi alterado: `max(100%, Npx)` é tão
definido quanto `100%` sozinho. Como evidência adicional, o painel de ação com 248 rotas (Tarefa
43.6) nunca estica a página: a lista rola dentro do próprio card, confirmado abaixo.

## Tarefa 43.2: investigação do grafo cortado em `2560×1080`

As duas hipóteses da auditoria estavam abertas; medição confirmou qual: **mesma causa da Tarefa
43.1** (o card do grafo recebia pouca altura, não "sobra de espaço com cálculo de auto-fit
quebrado"). Não há bug separado no `computeGraphViewBox`/`flow-graph-viewbox.ts`.

Prova: em `2560×1080` real (`window.innerWidth/innerHeight` confirmados), com o trace de 4 spans
(`/api/orders/1/confirm`), `.flow-workspace__center` mede 529,86px (bem acima do piso de 360px,
porque sobra altura real nessa resolução), e nenhum nó do grafo (`getBoundingClientRect()` dos
quatro `.flow-graph__node`) ultrapassa os limites do canvas, ao vivo e em replay:

```json
{ "innerWidth": 2560, "innerHeight": 1080, "nodeCount": 4, "anyClipped": false }
```

Corrigido como efeito colateral da Tarefa 43.1: antes dela, `.flow-workspace__center` não tinha
piso nenhum, então mesmo com folga na viewport inteira, a distribuição de flex podia entregar
menos altura do que o grafo precisava. Com o piso de 360px e o floor de `.flow-workspace`, isso
não acontece mais.

## Tarefa 43.3: rodapé cede por altura

Mecanismo automático (Tarefa 43.1, tabela acima) confirmado. Mecanismo manual (seta de recolher,
agora efetivo): `collapsed` de `RecentExecutionsComponent`/`EventFeedComponent` virou `model()`
(two-way bind) em vez de signal interno, para o `FlowPageComponent` saber quando um cartão colapsa
e reduzir o `flex-basis` do próprio `.flow-workspace__footer` (220px → 140px com um colapsado →
56px com os dois).

Medido (`1728×972`, trace de 28 spans): clicar na seta de "Execuções recentes" leva
`.flow-workspace__center` de **652,07px para 732,07px** (+80px, `.flow-workspace__footer` de
220px para 140px). Aumento mensurável confirmado, em qualquer altura de viewport (mecanismo não
depende do limiar de 800px: funciona tanto acima quanto abaixo dele).

Memória de sessão (aba ativa do rodapé, cartão recolhido) não implementada nesta fase: decisão
consciente, não esquecimento (permitido pela própria tarefa 43.3, item 3).

## Tarefa 43.4: sparkline dentro do cartão

Grade do `.metric-cards` passou de `repeat(6, ...)` para `repeat(7, ...)` (≥1600px); o cartão do
sparkline deixou de ter `grid-column: 1 / -1` e virou um 7º item normal na mesma linha de 88px.
Abaixo de 1600px o cartão do sparkline simplesmente não renderiza (`display: none`), igual ao que
a seção 5.2 do guia já pedia e que a Fase 25 nunca implementou à parte. Sem essa remoção, trocar
o grid para 7 colunas teria recriado a mesma falha (um 8º item isolado numa segunda linha) em
qualquer largura entre 1200 e 1599px.

Piso de amostras escolhido: **3**. Com 2 pontos, uma sparkline é sempre um segmento reto (dois
números, uma linha), não uma forma; o mínimo para uma curva comunicar qualquer coisa é 3 pontos.

Prova com dado real, não sintético: o host de `SampleApi.Controllers`, logo após reiniciar (store
em memória zerado), tinha **exatamente 2 traces com duração**, o mesmo cenário real do achado
original. Nessa condição, `.metric-cards__sparkline` não existe no DOM (`sparklineRect: null`),
`.metric-cards` tem 7 filhos (os 6 originais + o cartão de p95 sem a linha), card ainda 88px.
Gerada uma 3ª execução (`GET /api/orders/2/slow`), a sparkline aparece e fica inteira dentro do
cartão (`sparklineRect` contido em `cardRect`, topo e base, confirmado por
`getBoundingClientRect()`).

Também coberto por teste unitário (`metric-cards.component.spec.ts`): abaixo de 3 amostras,
`sparklineVisible()` é `false`; a partir de 3, `true`.

## Tarefa 43.5: orientação automática do grafo com histerese

`graph-builder.ts`: `rankdir` virou parâmetro de `GraphBuilderInput` (default `'LR'`), com teste
cobrindo `'TB'` (`graph-builder.spec.ts`). `flow-page.component.ts`: `ResizeObserver` próprio no
elemento `.flow-workspace__canvas` (distinto do que já existia dentro de `FlowGraphComponent`,
que só mede o canvas para o overlay de legenda/zoom) alimenta um `effect()` de histerese: troca
para `'TB'` quando a proporção altura/largura do canvas cruza **1,2** para cima, volta para
`'LR'` quando cruza **0,9** para baixo, sem mudar nada entre os dois limiares.

Confirmado ao vivo (trace de 4 spans, redimensionando só a largura da janela até o canvas ficar
mais alto que largo):

| Proporção do canvas (altura/largura) | `rankdir` resultante | `viewBox` do SVG |
|---|---|---|
| 0,23 (largo) | `LR` | `1018,8 × 221,8` |
| 0,89 | `LR` | `1018,8 × 283` |
| 1,12 (na zona morta, chegando por cima) | **continua `TB`** (não desceu do estado anterior) | `331,6 × 529,3` |
| 1,31 | `TB` | `331,6 × 529,3` |

A linha da zona morta prova a histerese de verdade: em 1,12 (entre 0,9 e 1,2) o grafo continuou em
`TB` porque chegou ali vindo de cima (1,31), não trocou de volta para `LR` só por estar abaixo de
1,2. Nenhuma oscilação observada ao arrastar a largura da janela continuamente através da faixa.

**Achado incidental, não é bug desta tarefa**: testado também com o trace real de 28 spans
(cenário N+1, 25 ramos irmãos sob a raiz). Mesmo com o canvas largo (`LR` corretamente escolhido,
proporção ~0,23–0,26), o `viewBox` resultante ainda sai alto (`1052 × 3896`), porque a MINILINHA de
25 irmãos empilha no eixo que `LR` usa para ordenar irmãos (eixo Y), não no eixo de rank. O
mecanismo está correto conforme a especificação (reage só à proporção do *canvas*, nunca à forma
da árvore, decisão explícita do D-160/guia seção 6.4): a limitação é inerente a um trace muito
"largo" (muitos irmãos, pouca profundidade) combinado com uma heurística que não olha a forma dos
dados. Registrado para a Planejadora avaliar se vale a pena virar item de roadmap (heurística
também considerar a forma da árvore), não corrigido aqui, já que não há reprodução de erro na
implementação em si (D-141).

## Tarefa 43.6: "Try an endpoint" com uma única área de rolagem

Correção aplicada: lista e formulário do endpoint selecionado nunca aparecem ao mesmo tempo. Lista
→ seleciona → formulário substitui a lista inteira (botão "←" volta). O botão "Send request"
(com feedback de erro/trace) saiu do `<form>` e virou rodapé fixo do card
(`form="endpoint-form-send"`, associação HTML nativa, fora da área que rola), sempre visível.

**Testado explicitamente contra volume real (248 rotas, mesma régua do R-27/R-28/D-116)**, não só
contra a lista pequena do achado original:

- Lista com 248 linhas renderizada (`customScrollHeight: 9065`, bem maior que o card).
- Selecionar a primeira rota: lista some do DOM (`listPresent: false`), formulário mostra, botão
  "Send request" **inteiro dentro do card** (`sendVisibleWithoutScroll: true`) e
  `elementFromPoint` no centro do botão resolve pro próprio botão
  (`elAtSendCenterIsSend: true`), card inteiro sem precisar rolar
  (`customScrollHeight === customClientHeight`, 622 === 622).
- Botão "←" volta: lista reaparece inteira (248 linhas), formulário some.

Sem a mudança para duas etapas, uma rolagem única teria empurrado o botão "Send request" ~9000px
abaixo do topo com 248 rotas, exatamente o risco que a correção adicional já antecipava.

## Tarefa 43.7: valores nunca truncados

`formatDurationMicrosCompact` nova (`duration-format.ts`), só para `metric-cards` (avg/max/p95).
`formatDurationMicros` original intacto para Histórico, Insights, painel de detalhes e banners de
erro/lentidão, onde a precisão de verdade importa.

Prova com dado real (trace de 28 spans, duração 407782µs = 407,8ms): cartão "Latência máxima"
mostra `408 ms` (sem reticência), "Latência média" `318 ms`. Nenhum dos sete cartões reporta
`scrollWidth > clientWidth` no elemento de valor, em nenhuma das capturas acima.

## Impeccable

`npx impeccable detect` em modo URL trava por causa da conexão SSE persistente de "Eventos ao
vivo" (mesmo achado já registrado no hotfix 0.4.4, PR #285: `impeccable detect` nunca atinge
`network-idle`). Tentativa adicional nesta fase: servir o build estático de demonstração
(`npm run build:demo`, que nunca abre SSE) via `http-server` para rodar o Impeccable em modo URL
contra ele. Não concluída por limitação do ambiente (servidor estático não respondeu a tempo),
não investigada a fundo por não ser o bloqueio real da fase.

Modo arquivo estático (sem navegação de browser), mesmo contorno documentado no hotfix 0.4.4:

| Arquivo | Achados |
|---|---|
| `flow-page.component.html`/`.scss` | 0 |
| `metric-cards.component.html`/`.scss` | 0 |
| `endpoint-form.component.html`/`.scss` | 0 |
| `recent-executions.component.html` | 0 |
| `event-feed.component.html` | 0 |

0 achados antes (não houve achado, pois o mecanismo do detector em modo arquivo já é limitado,
seção "Impeccable" do `.claude/rules/design-quality.md`), 0 depois. Nenhum achado novo de
`low-contrast`/`undersized-ui-text` (as duas categorias que bloqueariam a fase).

## Build, testes, lint e formato

```bash
cd src/NetApiFlow.Dashboard/ClientApp
npm run build        # OK
npm run test:ci      # 389 testes, 0 falhas (385 antes desta fase + 4 novos: rankdir TB no graph-builder, backToList do endpoint-form, piso e teto do sparkline)
npm run lint         # OK
npm run format:check # OK
```

## Reproduzindo

```bash
docker run -d --name netapiflow-verify-pg -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=netapiflow_sample -p 5432:5432 postgres:16-alpine

cd samples/SampleApi.Controllers
ASPNETCORE_ENVIRONMENT=Development dotnet run

# trace com spans reais (N+1 de propósito, Tarefa 43.2/43.5):
curl "http://localhost:<port>/api/orders/sequential?$(python3 -c "print('&'.join(f'ids={i}' for i in range(1,26)))")"
# abrir /flow/?trace=<id>, redimensionar a janela, testar os limiares acima
```
