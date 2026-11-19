# Fase 44: layout mobile com paridade completa

Prova, por geometria real contra um dashboard rodando de verdade, das sete tarefas da Fase 44
(`docs/plano/fase-44-layout-mobile-paridade-completa.md`) mais oito achados reais encontrados ao
vivo durante a rodada (dono do projeto testando o build num emulador de mobile real do DevTools,
e eu reproduzindo cada achado via Playwright contra o host).

Host usado: `samples/SampleApi.Controllers` (Postgres local via Docker), com traces reais
(`/api/orders/{id:int}`, `/api/orders/{id:int}/slow`, `/api/orders/sequential?ids=...` para o
cenário N+1). Medido via Playwright (`getBoundingClientRect`, `getBBox`, `getComputedStyle`),
nunca só inspeção visual. Toda vez que o dashboard Angular foi reconstruído (`npm run build`), o
host `.NET` precisou ser recompilado e reiniciado: a UI vai embutida como `EmbeddedResource`
(ADR-0005), então o processo antigo continua servindo o bundle antigo até ser recompilado de
verdade, não só com o Angular rebuildado.

## Tarefa 44.1: topbar compacta abaixo de ~480px

Tema e idioma colapsam num menu único (`mat-menu`, ícone de engrenagem), "Testar um endpoint"
vira só ícone. Confirmado em `360×800` e `393×852`, nas seis telas: `document.documentElement
.scrollWidth <= window.innerWidth`, tema e idioma continuam acessíveis pelo menu (radio PT/EN +
alternância de tema), clique testado de verdade.

## Tarefa 44.2: grafo antes das métricas em `/flow`

Reordenado via `order` CSS: `.flow-workspace__metrics` ganhou `order: 1` abaixo de 768px, para
renderizar depois do grafo (`.flow-workspace__body`, `order: 0` padrão). Confirmado em `360×800`:
canvas visível sem rolar a página (`top < innerHeight` sem scroll).

**Decisão alterada ao vivo**: a opção original foi uma faixa com rolagem horizontal. O dono do
projeto viu ao vivo e não gostou ("não gostei dos kpis com scroll horizontal, acho que eles
deveriam ficar no top da tela um kpi de cada vez, um debaixo do outro"). Trocado para
empilhamento vertical, um cartão por linha, largura cheia (`display: flex; flex-direction:
column`). Confirmado: 6 cartões empilhados verticalmente em `360×800`, sem rolagem horizontal, sem
transbordo de página.

**Achado real ao vivo, corrigido na hora**: o `order: 1` nas métricas empurrava-as para depois do
**rodapé** também, não só depois do corpo: o rodapé (`.flow-workspace__footer`) não tinha `order`
próprio, então ficava no mesmo grupo de prioridade 0 que o corpo, e a ordem final virava corpo →
rodapé → métricas (a pior ordem possível, abaixo até das duas execuções recentes e eventos ao
vivo). Reportado com captura de tela real pelo dono do projeto. Corrigido dando `order: 2` ao
rodapé. Confirmado depois: `bodyTop (72) < metricsTop (1264) < footerTop (1888)`.

## Tarefa 44.3: ações do cabeçalho do grafo num menu "⋯"

Abaixo de 767px, as três ações (exportar, replay, pausar/retomar ao vivo) somem para um menu "⋯"
(`mat-menu`), o título "Caminho da requisição" nunca desaparece. Confirmado em `360×800`: menu
abre com as ações certas (testado clicando de verdade), título sempre visível.

## Tarefa 44.4: Histórico, trace id nunca desaparece

`.history__col-trace` parou de ter `display: none` abaixo de 1365px. Entre 768px e 1365px, só o
texto do id encolhe (`.history__trace-id-text { display: none }`), o botão de copiar continua.
Confirmado nas três faixas (`360`, `1024`-ish, `1706`-ish): botão presente, **clicado de
verdade**, id real copiado para a área de transferência (`navigator.clipboard.readText()` depois
do clique).

## Tarefa 44.5: Arquitetura, piso de altura

`.architecture` ganhou `height: max(100%, 500px)`, `.architecture__canvas` subiu de
`min-height: 200px` para `360px` (mesmo piso do grafo de `/flow`). Medido contra o DOM real, não
estimado: em `1366×728` até `1366×240`, o canvas sempre resolve para ~363-364px, logo acima do
próprio piso de 360px, sem folga desperdiçada. Confirmado também que `.app-content` rola de
verdade abaixo do piso (`contentScrollable: true` nas alturas mais curtas).

## Tarefa 44.6: varredura das seis telas

- **Architecture**: nunca varrida antes. Em `360×800`/`393×852`, canvas visível, sem transbordo,
  legenda presente.
- **Insights**: nunca varrida antes. Sem transbordo de página; as 3 tabelas rolam dentro do
  próprio `mat-card` (confirmado chegando ao fim real do scroll, última linha visível).
- **Runner (standalone)**: confirmado isolado em `360×800`, sem transbordo, reaproveitando o
  `EndpointFormComponent` já corrigido na Fase 43.
- **Components**: scroll horizontal confirmado chegando à última coluna de verdade
  (`scrollLeft` no máximo, última linha visível).

## Tarefa 44.7: regressão da base da Fase 43

- Scroll de página real e sparkline: sem regressão nas duas larguras mobile.
- `formatDurationMicrosCompact`: só `metric-cards` usa. Insights usa `formatDurationMicros`
  (precisão completa, por design, desde a Fase 43). Architecture nunca mostra duração por nó
  (`durationMicros: null` sempre, é uma visão agregada sem uma execução única). Não há nada
  para truncar.
- **Achado real, não assumido**: a histerese de `rankdir` (`flow-page.component.ts`) é uma
  implementação própria do `/flow`, não um mecanismo do `app-flow-graph` compartilhado. Medido em
  Architecture (`1366×900` até `1366×400`, proporção do canvas de 0,55 a 1,67, bem além do limiar
  de 1,2): o `viewBox` nunca muda de direção, sempre `LR`. **Registrado para a Planejadora, não
  corrigido nesta fase**: estender a histerese para Architecture exigiria o mesmo mecanismo
  (`ResizeObserver` + `effect`) duplicado num segundo componente, e o KISS/YAGNI deste projeto só
  libera abstração nova na terceira repetição real. Como a tela de Architecture tende a canvases
  largos com poucos nós (visão agregada, não uma árvore funda), o impacto prático é baixo; ainda
  assim é uma divergência real de comportamento entre as duas telas que a Planejadora precisa
  decidir se vale a pena fechar.

## Achados ao vivo, fora das sete tarefas originais

Reportados pelo dono do projeto durante a rodada (capturas de tela reais e, depois, uma
verificação externa contra o build de demonstração de uma `develop` anterior a esta fase).
Tratados como achados reais desta mesma rodada, mesmo padrão das Fases 43/46 (D-141): todos
reproduzidos por geometria real antes de corrigir, nenhum corrigido só por suposição.

### Legenda do grafo sobre os controles de zoom

Abaixo de 767px, a legenda (`.flow-graph__legend`) vira uma linha que quebra, podendo ocupar quase
toda a largura do canvas (`max-width: calc(100% - 24px)`), e os controles de zoom
(`.flow-graph__zoom-controls`) ficavam no mesmo canto inferior. Medido: sobreposição real de até
140px horizontais na mesma faixa vertical. Corrigido movendo os controles de zoom para o canto
superior direito abaixo de 767px (`top: 12px` em vez de `bottom: 12px`), canto que nenhum outro
elemento absoluto do canvas ocupa. Confirmado depois: `overlapY: 0` em qualquer largura/altura
testada.

### Nome do método encostando na duração no cabeçalho do nó

`nodeOperationLabel` já reservava espaço pela duração (mecanismo existente, não desta fase), mas a
margem medida era de só 2,8 a 7,6px em casos reais (trace de N+1 com `GetManySequentiallyAsync`).
Risco real de encostar com um nome ou duração um pouco mais longos. `DURATION_GAP_PX` subiu de 8
para 12, `MONO_CHAR_WIDTH_PX` de 6,3 para 6,6 (fonte monoespaçada real um pouco mais larga que a
estimativa original). Medido depois: margem de 11 a 16px no mesmo trace real.

### Painel de detalhes com altura fixa de 640px

Abaixo de 1024px, `.flow-workspace__details` tinha `height: 640px` fixo, deixando um vão vazio
grande em traces curtos. Virou teto (`max-height: 640px`), com a altura seguindo o conteúdo
abaixo dele. Como `.flow-details` (dentro do painel) dependia de `height: 100%` para rolar
internamente contra uma altura definida do ancestral (mecanismo que só existe no desktop, onde a
altura da linha flex é definida), esse componente ganhou sua própria exceção abaixo de 1024px:
`height: auto`, deixando o ancestral ser o scroll container. Medido: trace de 4 spans cai de 640px
para 361px de altura real; trace maior (N+1) continua dentro do teto (483px), com `overflow-y:
auto` pronto para quando precisar.

### Inconsistência de status: mesmo 404 aparece verde num lugar, vermelho noutro

Achado de correção, não de layout. `history.component.ts` tinha sua própria regra
(`trace.httpStatus >= 400 ? 'error' : 'completed'`), divergente da regra que
`recent-executions`/`statusTint` já usavam (`trace.status`, o status real da execução, não o
código HTTP cru). Um controller que responde 404 de propósito (busca que não achou nada) é uma
execução bem-sucedida, não um erro, pela própria semântica do SDK. Corrigido: Histórico agora lê
`trace.status === 'Error'`, mesma regra do resto do dashboard. Dois testes novos em
`history.component.spec.ts` travam os dois lados: 404-mas-`Success` tinta verde,
200-mas-`Error` (exceção lançada) continua vermelho.

### Execuções recentes: rota some, tempo relativo nunca passa de minutos, rolagem presa

Três problemas reais na mesma grade (`.recent-executions__item`, `grid-template-columns: 56px
minmax(4ch,...) 1fr minmax(6ch,...) minmax(4ch,...)`):

1. A rota (a informação principal) dividia a linha com quatro outros campos de largura fixa;
   sobrava pouco ou nada para ela, virando `/…` ou `,`. Corrigido com uma grade de áreas nomeadas
   abaixo de 767px: a rota ganha a linha de cima inteira, status/método/duração/tempo dividem a
   linha de baixo. Confirmado com dado real: as quatro rotas aparecem inteiras, nenhuma
   truncada.
2. `relativeTime()` só tinha `now`/segundos/minutos; passava de 60 minutos e virava "973 min" em
   vez de horas. Adicionados os níveis de hora e dia (`recent.relativeTime.hours`/`.days`, EN e
   PT-BR). Testado: 3h → "3 h", 50h → "2 d".
3. O card tinha `height: 100%; overflow-y: auto` incondicional, rolagem própria dentro de uma
   página que já rola (dedo preso no celular). Abaixo de 767px a lista corta para 4 itens
   (`visibleTraces()`, sinal derivado de `BreakpointObserver`) e o card vira `height: auto;
   overflow-y: visible`: sem conteúdo sobrando, não precisa de scroll próprio. "Ver todas" continua
   sendo o caminho para o resto. Dois testes novos confirmam a contagem (8 acima do breakpoint, 4
   abaixo, os 4 primeiros).

### Histórico: card mobile se desmontava em 4-5 linhas desalinhadas

A grade mobile original (`auto auto 1fr`, com rota/início/trace cada um em `grid-column: 1 / -1`
próprio) empilhava cada campo em sua própria linha cheia: seleção+status+método dividiam a
primeira, e rota, início e trace id ganhavam uma linha inteira cada, sozinhos. Redesenhado com
áreas nomeadas: `select route route trace` em cima, `status method duration started` embaixo.
São duas linhas organizadas, em vez de até cinco linhas avulsas. Achado colateral do redesenho,
corrigido antes de dar por encerrado: `.history__col-started` carregava um `width: 150px` fixo do
layout de tabela desktop, que vazava para dentro da célula de grade e inflava a coluna
compartilhada com `trace` além da largura do card (496px de linha contra 360px de tela real,
medido). Toda célula reaplicada ganhou `width: auto; min-width: 0` no breakpoint mobile; `started`
também ganhou `white-space: nowrap` com elipse própria, para encolher sem quebrar o texto no meio
da data. Confirmado depois: linha de 334px dentro de uma tela de 360px, sem transbordo.

### Componentes: tabela de ~800px alargando a página

Já corrigido antes desta fase (Fase 46, `components__card` como contêiner de rolagem). Reconfirmado
nesta rodada: nunca alarga a página em nenhuma das cinco larguras testadas. **Melhoria nova**:
primeira coluna (nome do componente) fixada com `position: sticky; left: 0` abaixo de 767px, para
continuar legível enquanto rola até as colunas de métrica. Confirmado: a célula fixa não se move
nem 1px ao rolar o card até o fim.

Mesmo tratamento aplicado às três tabelas de Insights (método + rota fixos, já que nenhum dos dois
sozinho identifica a linha). Coluna do método com largura fixa de 64px para a rota saber onde
ancorar. Confirmado funcionalmente (ambas ficam visíveis durante o scroll); pequena imprecisão de
alinhamento (~7px de deslocamento na segunda coluna) por causa do `table-layout: auto` do
navegador não respeitar exatamente o `width` declarado. Não há quebra funcional: registrado aqui
em vez de perseguir um ajuste de sub-pixel.

### Cartões de métrica na faixa 768-1199px: valor e rótulo truncados

Com 3 colunas iguais nessa faixa, cada cartão ficava estreito demais: "Latência média" e "Latência
máxima" truncavam para o mesmo "Latência…", indistinguíveis, e o valor também cortava. Virou 2
colunas. Medido nas duas pontas da faixa (`768px` e `900px`): `valueOverflow`/`labelOverflow`
`false` nos sete cartões, nos dois extremos.

## Varredura de aceite completa

320, 360, 393, 480 e 767px de largura, claro e escuro, nas seis telas, depois de todas as
correções acima: `document.documentElement.scrollWidth <= window.innerWidth` em 60 de 60
combinações. Dois achados reais de transbordo apareceram **durante** esta varredura (não antes) e
foram corrigidos antes do fechamento:

- `/flow` em 480px exato: tema/idioma cheios mais "Testar um endpoint" com rótulo não cabiam. O
  limiar dos quatro breakpoints de colapso da topbar subiu de `479px` para `480px` (a palavra
  "abaixo de ~480px" da tarefa original não cobria o próprio 480).
- `/components` em 320px exato: título "Componentes" (mais longo que "Fluxo") somado ao chip de
  conexão não cabiam com a topbar já compacta. `.app-topbar__title` ganhou `flex: 1 1 auto;
  min-width: 0` com elipse, cedendo espaço só depois que `.app-topbar__sub` (que já cedia) não
  bastasse mais.

## Impeccable

Mesmo contorno das Fases 43/44/46 (modo URL trava pela conexão SSE persistente). Modo arquivo
estático, nos catorze arquivos `.html`/`.scss` tocados nesta rodada:

| Momento | Achados |
|---|---|
| Antes desta fase | 0 |
| Depois desta fase | 0 |

0 antes, 0 depois. Nenhum achado de `low-contrast`/`undersized-ui-text` (as duas categorias que
bloqueariam a fase).

## Build, testes, lint e formato

```bash
cd src/NetApiFlow.Dashboard/ClientApp
npm run build        # OK (budget de estilo por componente subiu de 9kB para 10kB: history.component.scss)
npm run test:ci      # 398 testes, 0 falhas (393 antes desta fase + 5 novos: status por trace.status
                      # no Histórico, slice/sem-scroll das execuções recentes, hora/dia no tempo relativo)
npm run lint          # OK
npm run format:check  # OK
```

## Reproduzindo

```bash
docker run -d --name netapiflow-verify-pg2 -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=netapiflow_sample -p 5432:5432 postgres:16-alpine

cd samples/SampleApi.Controllers
ASPNETCORE_ENVIRONMENT=Development dotnet run

curl "http://localhost:<port>/api/orders/1"
curl "http://localhost:<port>/api/orders/2/slow"
curl "http://localhost:<port>/api/orders/sequential?ids=1&ids=2&ids=3&ids=4"
# abrir /flow/?trace=<id>, /flow/history, /flow/components, /flow/runner,
# /flow/architecture, /flow/insights; redimensionar a janela pelos limiares acima
```

Qualquer rebuild do Angular (`npm run build`) exige recompilar e reiniciar o host `.NET`
(`dotnet build samples/SampleApi.Controllers -f net10.0`, matar o processo antigo, rodar de novo):
a UI vai embutida no assembly como `EmbeddedResource` (ADR-0005), não é servida de uma pasta que
atualiza sozinha.
