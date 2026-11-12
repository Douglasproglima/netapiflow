# Fase 25: Controles e sinais visuais do grafo ao vivo

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em `src/NetApiFlow`,
nenhum pacote opcional, nenhum `.csproj`. Nenhuma mudança em `docs/plano/contratos.md`: os quatro
itens reaproveitam dado e mecanismo que já existem.

Quatro dos seis itens de D-32, escolhidos para esta fase por serem 100% frontend e por já terem
posição definida desde a Fase 7: `docs/design/flow-workspace/guide.md`, seção 8, reserva espaço
pros seis desde o mockup original, com a etiqueta `0.2.0`. Esta fase libera quatro deles pra
implementação (D-106). Os outros dois (cenários de demonstração, atraso artificial) ficam na
Fase 26, por terem uma decisão de arquitetura própria que não se aplica aqui.

Posição normativa (guia, seção 8, não decidir de novo):

| Item | Onde entra |
|---|---|
| Botões de zoom (+, −, ajustar) e porcentagem | Canto inferior direito do canvas do grafo |
| Legenda fixa dos seis estados | Canto inferior esquerdo do canvas do grafo |
| Sparkline de latência com p95 | Sétimo cartão da faixa de métricas, ocupando 2 colunas |
| Alternador "seguir ao vivo" | Cabeçalho do card central, à direita |

## Tarefa 25.1: zoom e pan com controle visível

`FlowGraphComponent` (`features/flow/graph/flow-graph.component.ts`) já tem zoom e pan
funcionando de verdade: `d3-zoom` (`scaleExtent([0.25, 4])`) aplicado ao próprio `<svg>`, responde
a wheel/drag/pinch nativos. O que falta é só o controle visível: hoje `svgSelection` e
`zoomBehavior` são campos privados, sem nenhum método público pra acionar zoom de fora.

Acrescentar três botões (`+`, `−`, "ajustar à tela") mais uma leitura de porcentagem do zoom
atual, no canto inferior direito do canvas, chamando métodos novos do próprio
`FlowGraphComponent` que usam a `zoomBehavior` já existente (`behavior.scaleBy`/`behavior.transform`
do `d3-zoom`, não reinventar a física do zoom). "Ajustar à tela" segue a mesma regra que a seção 6
do guia já define pro zoom automático ao trocar de trace (zoom entre 0,7 e 1, nunca ampliar acima
de 100%, nunca reduzir abaixo de 70%, alinhado à esquerda com pan se não couber): não é uma regra
nova, é o mesmo comportamento hoje só disparado pelo `effect()` do construtor, agora também
acionável por clique.

**Pronto quando:** os três botões funcionam (zoom limitado pelo `scaleExtent` já existente,
porcentagem atualiza em tempo real), "ajustar à tela" reproduz a mesma regra da seção 6 do guia,
scroll/pinch continuam funcionando sem mudança de comportamento.

## Tarefa 25.2: legenda fixa dos seis estados

Os seis estados (`NodeVisualState` em `core/models.ts`: `waiting`, `running`, `completed`,
`slow`, `error`, `not-executed`) já têm ícone e texto centralizados em
`NODE_STATE_PRESENTATION` (`shared/node-state-presentation.ts`). Não existe nenhuma legenda hoje,
nem fixa nem solta.

Novo elemento no canto inferior esquerdo do canvas, iterando sobre `NODE_STATE_PRESENTATION`
(`Object.entries` ou equivalente), **não uma lista hardcoded de seis itens**: se um estado novo
for acrescentado a esse mapa no futuro (a Fase 27 deste mesmo roadmap é candidata real), a legenda
cresce sozinha, sem precisar ser editada de novo.

**Pronto quando:** legenda visível, mostra ícone + label de cada entrada do mapa de apresentação,
continua passando o teste de "os seis estados são distinguíveis sem cor" que a tarefa 4.5 já
prova.

## Tarefa 25.3: sparkline de latência com p95

Nenhuma rota nova. `GET /metrics` já devolve `p95DurationMicros` (contrato congelado,
`docs/plano/contratos.md`), e `MetricCardsComponent` (`features/flow/metric-cards/`) já busca
esse payload inteiro (`api.getMetrics({})`), só ignora esse campo hoje. `GET /traces` (já usado
por `RecentExecutionsComponent`, mesma página) já devolve `FlowTraceSummary[]` com
`durationMicros` por execução, ordenável por `startedAt`.

Sétimo cartão na faixa de métricas (`MetricCardsComponent`), ocupando 2 colunas, com:

- O valor de p95 da janela, formatado com `formatDurationMicros` (mesmo helper já usado nos
  outros cartões), vindo direto de `GET /metrics`. Número exato, calculado no backend sobre toda a
  janela, nunca recomputado no cliente.
- Um sparkline (SVG próprio, sem biblioteca de gráfico, `.claude/rules/frontend.md`) desenhando a
  sequência de `durationMicros` das últimas N execuções, buscadas via `GET /traces` (mesmo
  endpoint de `RecentExecutionsComponent`, instância de busca separada, sem acoplar os dois
  componentes). Escolher N grande o bastante pra um p95 fazer sentido estatístico. Os 8 que
  `RecentExecutionsComponent` usa pra sua lista são poucos pra isso, é só o tamanho certo pra uma
  lista curta de "últimas execuções", não pra uma amostra de série. Documentar o N escolhido no
  resumo de fim de fase.
- Atualiza quando chega `TraceCompleted` via SSE, mesmo padrão que `MetricCardsComponent` e
  `RecentExecutionsComponent` já usam.

**Pronto quando:** card mostra p95 formatado e a forma da série recente, atualiza ao vivo sem
recarregar a página.

## Tarefa 25.4: alternador "seguir ao vivo" / "parar de seguir"

Hoje, abrir `/flow?trace=<id>` sempre conecta `LiveTracePlayerService.track(traceId)`
(`features/flow/playback/live-trace-player.service.ts`), que aplica todo evento recebido
(`mergeEvent`) imediatamente ao `trace` signal. Não existe pausa voluntária dessa ingestão.

O serviço já tem o mecanismo certo pra isso, só usado hoje num momento diferente: enquanto
`loadingBaseline` é `true` (recarregando o trace depois de uma reconexão SSE), eventos chegando
entram em `pendingEvents` em vez de aplicar na hora, e são reaplicados em ordem quando a baseline
termina de carregar. Um novo estado `paused` (signal) reaproveita exatamente essa fila: enquanto
pausado, `onEvent` empilha em `pendingEvents` em vez de chamar `mergeEvent`; ao despausar, a fila
inteira é reaplicada em ordem, sem perder nenhum evento que tenha chegado durante a pausa.

Controle no cabeçalho do card central (`flow-page.component.html`), à direita, visível só quando
há um trace `Running` sendo seguido ao vivo (não faz sentido pausar um trace já terminado nem
substituir o Replay existente). **Este alternador é diferente do Replay** (`toggleReplay` em
`flow-page.component.ts`): Replay troca pra reproduzir os spans já carregados de um trace, com
velocidade controlável, modo próprio. Este alternador só pausa/retoma a ingestão ao vivo do trace
ainda em andamento, sem trocar de modo. Os dois coexistem; não reaproveitar o rótulo nem o botão
de um pro outro.

**Pronto quando:** com um trace `Running` sendo seguido, pausar impede a UI de mudar mesmo com
eventos continuando a chegar (o `trace` signal não muda enquanto pausado, verificável por teste);
retomar aplica todo o backlog de uma vez, na ordem certa, sem perder nenhum span que tenha
terminado durante a pausa.

## Fora de escopo, de propósito

- Cenários de demonstração e atraso artificial configurável (D-32): Fase 26, decisão de
  arquitetura própria (ficam fora do núcleo).
- Qualquer mudança em `ITraceStore`, `contratos.md` ou núcleo .NET.
- Zoom/pan em touch além do que `d3-zoom` já suporta nativamente (pinch já funciona hoje, não é
  retrabalho desta fase).

## Aceite da fase

Critérios de pronto das quatro tarefas acima. `npm run lint`, `npm run format:check`,
`npm run test:ci`, `npm run build` limpos. Detector Impeccable
(`.claude/rules/design-quality.md`) rodado em `/flow` (tema claro e escuro, desktop e `390x844`):
contagem de avisos antes/depois no resumo de fim de fase.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (passo a passo manual: zoom pelos botões,
legenda visível, sparkline atualizando com tráfego real, pausar/retomar o live sem perder
evento), o que ficou pendente, qual N foi escolhido pro sparkline. A Planejadora roda
`/revisar-fase 25` antes de liberar a Fase 26.
