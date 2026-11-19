# Fase 44: Layout mobile com paridade completa

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em `src/NetApiFlow`,
nenhum pacote opcional, nenhum `.csproj`, nenhuma mudança em `docs/plano/contratos.md`.

Depende da Fase 43 estar pronta e aceita: o mecanismo de scroll de página real, o piso de altura
do grafo, o rodapé que cede por altura, a minilinha do sparkline, a rolagem única do runner e a
orientação automática do grafo (`rankdir` com histerese) são a base estrutural compartilhada.

## Escopo: paridade completa (D-163, substitui a proposta anterior de "modo consulta")

Uma proposta anterior desta fase limitava o mobile a `/flow` (grafo, detalhes, replay), com
Runner, Arquitetura e Insights em "experiência simplificada". Rejeitada: **mobile mostra a mesma
informação e as mesmas ações que desktop, em todas as seis telas** (Flow com grafo/detalhes/
replay, History, Components, Runner, Architecture, Insights). Nada fica oculto ou inacessível só
por causa da largura da tela.

Isso não significa pixel idêntico ao desktop: uma tabela pode rolar na horizontal em vez de
encolher colunas, uma lista pode virar formulário em duas etapas em vez de lado a lado (como a
Fase 43 já fez no Runner). O critério é informação e ação sempre alcançáveis, não layout idêntico.

## O que a pesquisa encontrou antes de escrever as tarefas

Lido o SCSS/HTML real de cada tela antes de planejar, não suposto:

- **History**: já tem tratamento mobile real (filtros colapsam atrás de um botão, ordenação vira
  `<select>` abaixo de 767px, linhas da tabela reorganizam em grid). **Gap real**: a coluna de
  trace id com o botão de copiar (`.history__col-trace`) tem `display: none` abaixo de **1365px**,
  não só em mobile. Nenhuma alternativa: a ação de copiar o trace id simplesmente não existe
  abaixo desse ponto. É o único caso desta fase onde algo está hoje escondido, não só apertado.
- **Runner**: a página inteira é `<div class="runner"><app-endpoint-form /></div>`, o mesmo
  componente que a Fase 43 já corrigiu (lista → formulário, rolagem única, testado contra 248
  rotas). Provavelmente já funciona isolado em largura estreita; precisa de confirmação própria,
  não de reconstrução.
- **Components**: tabela de 8 colunas dentro de um card com `overflow-x: auto` (corrigido na Fase
  46). Nada oculto, rola para ver qualquer coluna. Precisa de confirmação, não de nova tarefa.
- **Insights**: mesma forma (tabelas em `mat-card`, `overflow-x: auto`). Nada oculto. Precisa de
  confirmação.
- **Architecture**: zero `@media` query no arquivo inteiro (achado real, nenhuma tela desta fase
  tinha isso além de Insights/Components/Runner, mas Architecture é a única com um canvas de
  grafo sem o piso de altura que a Fase 43 deu a `.flow-workspace__center`). `.architecture` é
  `height: 100%` sem `max(..., Npx)`, `.architecture__canvas` tem só `min-height: 200px` (menor
  que o piso de 360px do Flow). Mesma classe de bug que a Tarefa 43.1 fechou para `/flow` pode
  acontecer aqui, nunca testada.
- **Architecture e Insights nunca passaram pela varredura de responsividade da Fase 46** (que
  cobriu só History/Components/Runner). Dois telas sem nenhuma prova de que não vazam ou cortam
  em largura estreita.

## Tarefa 44.1: topbar compacta abaixo de ~480px (fecha o Achado 1 da Fase 46, nas seis telas)

Achado real (Fase 46): abaixo de 360px de largura, a topbar (`.app-topbar`, compartilhada por
todo o dashboard) ultrapassa a viewport em 28 a 73px, dependendo da tela (o chip "Live" é o
elemento que mais estoura). Reproduzido em `/flow`, `/history`, `/runner` (Fase 46); como é a
mesma topbar, vale para `/components`, `/architecture` e `/insights` também.

1. Tema e idioma entram num menu (ícone único) abaixo de ~480px.
2. "Try an endpoint" vira só ícone (sem rótulo de texto) no mesmo breakpoint.
3. Confirmar que nenhum elemento da topbar força `scrollWidth` da página a exceder `innerWidth`
   em `360×800` e `393×852`, nas seis telas.

**Pronto quando:** `document.documentElement.scrollWidth <= window.innerWidth` nos dois viewports
mobile, nas seis telas, tema e idioma continuam acessíveis pelo menu.

## Tarefa 44.2: grafo antes das métricas em `/flow`

Achado real: hoje o usuário rola por seis cartões de métrica e pelo sparkline antes de ver o
grafo, em qualquer viewport mobile. O grafo é o motivo de abrir a tela.

1. Reordenar: grafo primeiro, métricas depois, abaixo de `max-width: 767px`.
2. Métricas em faixa com rolagem horizontal, ou recolhidas por padrão com um controle para
   expandir (a Executora decide qual das duas, documentando o motivo).

**Pronto quando:** em `360×800` e `393×852`, o grafo aparece sem rolar a página, confirmado por
`getBoundingClientRect()` do canvas mostrando `top < window.innerHeight` sem scroll.

## Tarefa 44.3: ações do cabeçalho do grafo num menu "⋯" quando não couberem

Achado real: as ações do cabeçalho ("Back to live", "Replay", exportar) vazam à direita em
mobile, e o título "Request path" some.

1. Abaixo de um breakpoint mobile, ações que não couberem na linha do cabeçalho entram num menu
   "⋯" (mesmo padrão de menu compacto da Tarefa 44.1).
2. O título "Request path" nunca desaparece: se precisar de espaço, as ações cedem primeiro.

**Pronto quando:** em `360×800`/`393×852`, nenhuma ação vaza para fora do card, "Request path"
sempre visível.

## Tarefa 44.4: Histórico, trace id nunca desaparece

Achado real (pesquisa desta fase): `.history__col-trace` (trace id curto mais botão de copiar)
tem `display: none` abaixo de 1365px. Sem alternativa: a ação de copiar o trace id direto da
lista não existe abaixo desse ponto, em qualquer largura, não só em mobile.

1. No layout de card (`@media max-width: 767px`, linhas da tabela já viram grid), acrescentar o
   trace id curto e o botão de copiar como mais um item do grid, mesmo padrão visual que
   `.recent-executions__item`/`.event-feed` já usam para dados secundários numa linha compacta.
2. Entre 768px e 1365px (tela ainda em modo tabela, não card), decidir entre: encolher a coluna
   de trace id (menos caracteres visíveis, `title` com o valor completo) ou mover o botão de
   copiar para dentro da célula de rota como ação secundária. A Executora decide qual, documentando
   o motivo; qualquer uma das duas mantém a ação alcançável, que é o critério.

**Pronto quando:** em `360×800`, `393×852` e numa largura intermediária (ex. `1024×800`), o botão
de copiar o trace id está presente e funcional em toda largura testada, confirmado clicando e
checando a área de transferência (ou o estado visual de "copiado" que o componente já mostra).

## Tarefa 44.5: Arquitetura, piso de altura e scroll de página

Achado real (pesquisa desta fase): `.architecture`/`.architecture__canvas` nunca ganhou o
mecanismo da Tarefa 43.1. Mesma classe de bug (grafo cortado ou invisível em viewport curto) pode
acontecer aqui, nunca testada.

1. Aplicar o mesmo mecanismo da Fase 43 (`height: max(100%, Npx)` em `.architecture`,
   `min-height` real em `.architecture__canvas`), com valores próprios: o orçamento de altura
   fixa de `/architecture` é diferente do de `/flow` (sem rodapé de dois cartões, sem faixa de
   métricas). Medir contra o DOM real, mesmo método da Fase 43 (varredura fina via Playwright),
   não estimar de cabeça.
2. `.app-content` rola de verdade abaixo do piso medido, mesmo mecanismo já existente desde a
   Fase 43 (nenhuma mudança na cadeia do shell).

**Pronto quando:** em `1366×728` e numa altura ainda mais curta, com uma rota real selecionada, o
grafo de arquitetura aparece inteiro ou a página rola até ele aparecer, nunca os dois ao mesmo
tempo, confirmado por `getBoundingClientRect()`.

## Tarefa 44.6: varredura e verificação de paridade nas seis telas

Diferente das tarefas acima, estas três telas não têm achado prévio de algo escondido. A
pesquisa desta fase não encontrou `display: none` nem conteúdo inalcançável. Confirmar com
geometria real, mesma régua da Fase 46 (varredura por faixa, largura e altura):

1. **Architecture e Insights**: nunca passaram por nenhuma varredura de responsividade (a Fase
   46 cobriu só History/Components/Runner). Rodar a mesma matriz de larguras/alturas da Fase 46,
   documentando achado real (se houver) ou a ausência confirmada dele.
2. **Runner (página standalone, `/runner`, distinta do painel embutido em `/flow`)**: confirmar
   isoladamente em `360×800`/`393×852` que o `EndpointFormComponent` (já corrigido na Fase 43)
   funciona igual fora do contexto de `/flow`.
3. **Components e Insights**: confirmar que a rolagem horizontal das tabelas alcança de fato toda
   coluna em `360×800`/`393×852`, nenhuma informação impossível de ver mesmo rolando até o fim.

**Pronto quando:** as três telas documentadas com o resultado da varredura (achado real ou
nenhum) em `tests/manual-verification/`, mesmo padrão dos READMEs já existentes. Qualquer achado
real vira tarefa própria desta fase antes de aceitar, mesmo padrão de "achado real durante a
rodada" já usado nas Fases 43/46 (não teórico, D-141).

## Tarefa 44.7: regressão da base estrutural da Fase 43, nas seis telas

Confirmar que os mecanismos entregues na Fase 43 continuam corretos fora de `/flow`, não só
dentro dela:

- Scroll de página real (Tarefa 43.1) e sparkline dentro do cartão (Tarefa 43.4): só se aplicam a
  `/flow`, confirmar que nada regrediu ali nas duas larguras mobile.
- Valores nunca truncados (Tarefa 43.7, `formatDurationMicrosCompact`): usado hoje só em
  `metric-cards`; confirmar se Insights/Architecture também formatam duração e, se sim, se sofrem
  do mesmo truncamento que motivou a correção original.
- Orientação automática do grafo com histerese (Tarefa 43.5): como `app-flow-graph` é
  compartilhado, Architecture já herda o mecanismo de graça. Confirmar isso de fato acontece
  (canvas de Architecture também troca `rankdir` ao ficar mais alto que largo), não assumir
  herança automática sem testar.

**Pronto quando:** os três pontos acima confirmados por geometria real em `360×800` e `393×852`,
tema claro e escuro.

## Fora de escopo

- Qualquer mecanismo novo de layout que não exista já na Fase 43: esta fase estende a base, não
  cria um segundo conjunto de mecanismos paralelo.
- Redesenho de Components/Runner/Insights além do necessário para os achados reais da Tarefa
  44.6. É correção de paridade, não oportunidade de redesenho.

## Aceite da fase

Critérios de pronto das sete tarefas acima, medidos por geometria real, tema claro e escuro, nos
modos ao vivo e replay onde se aplica, com o trace de 4 spans do sample e com um trace de pelo
menos 20 spans. Varredura de largura na faixa `< 768` do guia (320, 360, 393, 480, 767) nas seis
telas, mais o redimensionamento contínuo já exigido pela Fase 43/D-161.

`npx impeccable detect` nas seis telas, tema claro e escuro, nos pontos de largura acima.
Contagem de avisos antes e depois no resumo final; qualquer achado novo de
`low-contrast`/`undersized-ui-text` bloqueia a fase.

Fecha o Achado 1 da Fase 46 (topbar) de vez, nas seis telas de uma vez, não só nas três que a
Fase 46 cobriu.

## Ao terminar

Resumo para a Planejadora: o que foi feito por tarefa, como verificar, o que ficou pendente.
Atualizar `docs/plano/riscos.md` é tarefa da Planejadora depois de revisar esta fase, não da
Executora.
