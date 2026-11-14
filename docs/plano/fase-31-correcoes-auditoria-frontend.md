# Fase 31: Correções da auditoria de frontend (achados Alta e Média)

Auditoria do frontend do dashboard, feita a partir do commit `2e900be` de `main`, encontrou 24
achados reais (4 Alta, 10 Média, 10 Baixa). Todos os 24 foram reverificados pela Planejadora
contra o código atual antes deste plano, com arquivo e linha. A verificação completa está em
D-121, corrigida em D-122 (o sub-item do `<image href>` relativo no M7 procede: está em
`flow-graph.component.html`, componente filho que `architecture.component.html` embute via
`<app-flow-graph>`, não no próprio template do mapa de arquitetura).

Os achados de Baixa severidade viram a Fase 32, separada desta. Justificativa: nenhum dos dez é
bug de correção visível pro usuário (são acessibilidade, i18n, validação de formulário e
orçamento de build), enquanto todo achado desta fase é um comportamento que já quebra ou confunde
alguém usando o dashboard hoje. Juntar os 24 numa fase só passaria de 20 tarefas, difícil de
revisar e de reverter em partes se algo sair errado.

## Regra geral desta fase

Toda tarefa de correção de bug (não as de contrato/decisão de forma) escreve primeiro o teste que
reproduz o problema, confirma que ele falha, só depois corrige. Isso vale tanto pro backend quanto
pro Angular.

## Tarefa 31.1 (A1, bloqueante): remover a aba "Scenarios" hardcoded

Hoje `endpoint-form.component.ts` tem uma constante `SCENARIOS` hardcoded com rotas do
`samples/SampleApi.Controllers` (`/api/orders/1`, `/api/orders/2/confirm`, etc.), exibida pra todo
usuário do pacote publicado, numa API real que nunca tem essas rotas.

Escopo travado pela Planejadora pra esta tarefa ser PATCH de verdade (0.3.1, D-122): sem nenhuma
API pública nova. Remover a constante `SCENARIOS`, a aba "Scenarios" inteira (volta a sobrar só a
aba "Custom") e o seletor de delay artificial (`X-NetApiFlow-Demo-Delay-Ms`) que só fazia sentido
junto dela. "Cenário configurável pelo host" (o host declarar os próprios cenários, cada sample
configurando os seus, seletor de delay voltando condicionado a isso) vira ideia separada, registrada
em `roadmap-valor.md`, fora desta fase e fora da 0.3.1: exige API pública nova
(`NetApiFlowOptions`, `GET /config`), que força MINOR.

**Pronto quando:** `endpoint-form.component.ts` não tem mais `SCENARIOS` nem nada do seletor de
delay. O painel "Testar um endpoint" só mostra "Custom", sem aba fixa com rota de exemplo
nenhuma. Teste Angular confirma que a aba antiga não existe mais no DOM.

## Tarefa 31.2 (A2): coalescer as chamadas disparadas por cada trace

Cada trace completada hoje dispara ~6 chamadas HTTP sem debounce: `app.ts` (`getLiveTraces` em
`TraceStarted`/`TraceCompleted`), `metric-cards` (3 chamadas: `loadMetrics`,
`loadComponentMetrics`, `loadSparkline`), `recent-executions` (1 chamada). Numa sequência rápida
de traces (replay rápido, ou um N+1 real), isso vira uma rajada de requisições.

Centralizar a atualização reagindo ao evento com `auditTime` de 1 a 2 segundos, e `switchMap`
pra nunca deixar duas chamadas da mesma origem em voo ao mesmo tempo. Forma exata (um serviço
compartilhado, ou cada componente com seu próprio `audit`) fica a critério da implementação, desde
que o resultado observável seja o mesmo.

**Pronto quando:** um teste dispara 10 eventos `TraceCompleted` num intervalo menor que o
`auditTime` configurado e confirma que o número de chamadas HTTP de cada origem (live traces,
métricas, recentes) não passa de 1 ou 2, não 10.

## Tarefa 31.3 (A3): trace inexistente ou expirado em `?trace=`

`live-trace-player.service.ts`, `loadBaseline()`, não trata erro da chamada. Se o `traceId` da URL
não existir (expirou do buffer, ou é inválido), `loadingBaseline` fica `true` pra sempre e
`pendingEvents` cresce sem teto. A tela não mostra nada disso, só fica vazia.

Tratar o erro: parar `loadingBaseline`, descartar `pendingEvents` acumulados, e `flow-page` mostra
um estado explícito de "trace não encontrada" (ícone, texto, nos dois idiomas), distinto do estado
vazio padrão.

**Pronto quando:** teste de integração confirma que uma URL com `trace` inexistente chega num
estado terminal (não fica em loading pra sempre) e a `flow-page` mostra a mensagem de trace não
encontrada, não a lista de execuções em andamento.

## Tarefa 31.4 (A4): replay não pode marcar etapa futura como "Não executado"

`graph-builder.ts`, a etapa ainda não revelada durante o replay vira `'not-executed'` porque a
função só olha `trace.status` (já final, replay só roda em trace concluída), nunca se o replay
ainda está em andamento. Alguém assistindo o replay vê o resto do grafo marcado como "nunca
rodou" antes mesmo do replay chegar lá.

Precisa de um sinal novo, independente de `trace.status`: se o replay ainda não revelou aquele
spanId, o nó é `'waiting'`; só depois que o replay termina (ou numa trace ao vivo de verdade,
`Running`), um nó nunca revelado vira `'not-executed'`. `notExecutedCount` usa a mesma regra.

**Pronto quando:** teste de `graph-builder` prova que, com um sinal de "replay em andamento, span
ainda não revelado", o nó correspondente é `'waiting'`, não `'not-executed'`; e que, com o replay
já terminado, o mesmo span (se nunca apareceu de verdade) volta a ser `'not-executed'`.

## Tarefa 31.5 (M1): `GET /blueprint` nunca é chamado

A função já existe em `api.service.ts`, mas nenhum componente chama. Sem isso, "Não executado"
contra a execução anterior bem-sucedida não funciona no modo ao vivo (só funciona em replay, que
tem a trace inteira na mão).

Conectar `getBlueprint` na `flow-page` pra uma trace ao vivo: carregar o blueprint da rota assim
que o primeiro evento da trace chega, usar como referência pra marcar "not-executed" do mesmo jeito
que o replay já faz. Três regras:

- o blueprint é buscado uma vez por trace acompanhada (por método e rota), nunca de novo a cada
  evento SSE daquela mesma trace;
- resposta `204` do `GET /blueprint` significa "sem fantasmas": a trace ao vivo não tem nenhuma
  etapa prevista além das que já chegaram, nenhum nó vira "not-executed" por causa dela;
- precedência com a correção da tarefa 31.4 (A4): enquanto o replay (ou a trace ao vivo) ainda
  está em andamento, span do próprio trace ainda não revelado é `'waiting'`, nunca `'not-executed'`.
  Só depois que a trace termina, um span que nunca apareceu (nem no replay, nem ao vivo) e que o
  blueprint previa vira `'not-executed'`. Fantasma do blueprint nunca aparece como `'waiting'`
  permanente.

**Pronto quando:** teste de integração com uma trace ao vivo real, onde um branch condicional não
executa uma etapa que o blueprint previa, confirma que essa etapa aparece como "not-executed"
antes da trace terminar, não só depois. Teste de `graph-builder` cobrindo um replay de trace com
erro que também tem blueprint: span do próprio trace ainda não revelado fica `'waiting'`, fantasma
do blueprint não revelado fica `'not-executed'` desde o início, nenhum dos dois troca de estado
incorretamente até o replay terminar.

## Tarefa 31.6 (M2): legenda do grafo ao vivo mostra dois estados impossíveis

`NODE_STATE_PRESENTATION` tem 8 chaves (inclui `cancelled`/`timeout`, usados por `/history` e
outras telas de nível de trace). A legenda do `flow-graph.component.ts` itera o mapa inteiro, então
mostra os dois na legenda do grafo, mesmo que nenhum nó individual possa ficar assim (ADR-0020:
cancelamento/timeout são status de trace inteira, nunca de span). Não é pra remover do mapa
(quebraria `/history`/diff/recent-executions), só filtrar o que a legenda do grafo mostra pros
estados que um nó realmente pode ter.

**Pronto quando:** teste do componente de legenda confirma que ela renderiza exatamente os estados
possíveis de nó (sem `cancelled`/`timeout`), e um teste separado confirma que `NODE_STATE_PRESENTATION`
continua intacto (as outras telas não quebram).

## Tarefa 31.7 (M3): zoom/pan do usuário é desfeito a cada atualização ao vivo

`flow-graph.component.ts`, o `effect` chama `zoomToFit` toda vez que o viewBox muda, sem checar se
o usuário já deu zoom/pan manual. Numa trace ao vivo adicionando nó atrás de nó, o grafo volta pro
zoom automático a cada novo nó, brigando com quem está tentando olhar uma parte específica.

Rastrear se o usuário já interagiu manualmente (zoom, pan, ou o próprio botão "fit to screen"
contra); só auto-ajustar no primeiro render do grafo, nunca depois de uma interação manual, até o
usuário clicar "fit to screen" de novo.

**Pronto quando:** teste confirma que, depois de uma interação manual simulada, uma mudança
subsequente no viewBox (novo nó) não chama `zoomToFit` de novo.

## Tarefa 31.8 (M4): resposta antiga sobrescrevendo resposta nova

Quatro fluxos sem cancelamento de requisição anterior: seleção de nó/span na `flow-page`, filtros
do `history.component`, `insights.component`, `architecture.component`. Clicar rápido em dois
filtros/nós diferentes pode deixar a resposta do primeiro clique sobrescrever a do segundo, se a
rede entregar fora de ordem.

Aplicar `switchMap` (ou equivalente, ex. `rxResource`) nos quatro, cancelando a chamada anterior
assim que uma nova começa.

**Pronto quando:** teste em cada um dos quatro, forçando a resposta da primeira chamada a chegar
depois da segunda (`HttpTestingController`, ordem de flush controlada), confirma que a tela reflete
sempre a última seleção/filtro pedido, nunca a resposta atrasada da anterior.

## Tarefa 31.9 (M5): replay disponível durante trace `Running`, e reseta sozinho

Botão de replay clicável mesmo com a trace ainda rodando ao vivo. O `effect` que carrega o replay
recarrega a cada evento SSE da mesma trace, voltando sempre pro passo 0 enquanto a trace ainda
está em andamento.

Desabilitar o botão de replay enquanto `trace().status === 'Running'`. O `effect` de carregar o
replay só dispara quando a trace muda de identidade (novo `traceId`) ou termina, nunca a cada
evento de uma trace que já estava carregada.

**Pronto quando:** teste confirma o botão desabilitado com status `Running`, e que eventos SSE
subsequentes da mesma trace não reiniciam um replay já em andamento.

## Tarefa 31.10 (M6): lista "em andamento" não acompanha o SSE

Carregada uma vez só no load da `flow-page`, nunca atualiza com os eventos SSE que já chegam pra
outras partes da tela.

Reagir a `TraceStarted`/`TraceCompleted` do mesmo stream SSE que o resto da página já usa, com o
mesmo `auditTime` da tarefa 31.2 (reaproveitar o mecanismo, não duplicar).

**Pronto quando:** teste confirma que um `TraceStarted` simulado atualiza a lista "em andamento"
sem precisar recarregar a página.

## Tarefa 31.11 (M7): SVG exportado do mapa de arquitetura não é autocontido

`exportSvg` clona o SVG da página (`architecture.component.ts`, `exportSvg()`) sem resolver dois
tipos de referência externa: os valores computados de `var(--naf-*)`, e o `href` relativo de cada
`<image>` de ícone de estado (`flow-graph.component.html`, linhas 98 a 105 e 161, renderizado pelo
`<app-flow-graph>` que `architecture.component.html` embute). Abrir o arquivo fora do navegador
(outro programa, ou a mesma página sem o stylesheet e sem o servidor do dashboard) perde a cor e
mostra um ícone quebrado em todo nó.

Resolver cada `var(--naf-*)` usado pro valor computado real antes de serializar, inline no próprio
elemento ou num `<style>` embutido no SVG exportado. Embutir cada ícone de estado como data URI ou
SVG inline no lugar do `href` relativo, pros mesmos oito estados que `node-state-presentation.ts`
já lista.

**Pronto quando:** teste abre o arquivo `architecture.svg` exportado, isoladamente (sem o
stylesheet da página e sem o servidor do dashboard no ar), e confirma que nem a cor nem nenhum
ícone de nó dependem de recurso externo.

## Tarefa 31.12 (M8): item "Fluxo" do menu fica ativo em toda rota

`app.html`, `routerLink="/"` com `exact: false` faz o item ficar marcado como ativo em `/history`,
`/insights`, etc.

Ligar `exact: true` (ou o equivalente atual de `routerLinkActiveOptions`) só pro item cuja rota é
`/`.

**Pronto quando:** teste confirma que navegar pra `/history` não marca o item "Fluxo" como ativo.

## Tarefa 31.13 (M9): `GET /config` sem cache e sem tratamento de erro

Nove chamadas independentes pra `getConfig()`, nenhuma compartilhando resultado. Se a chamada
falhar, a `flow-page` nunca renderiza o grafo (trava em `slowThresholdMicros === null`), e
`loadLiveTraces` sem tratamento de erro deixa "Carregando" pra sempre.

Cachear/compartilhar o resultado de `getConfig()` entre os nove pontos de consumo (um serviço ou
signal compartilhado, carregado uma vez). Tratar erro nos dois casos: `slowThresholdMicros` cai pra
um valor padrão razoável em vez de travar o grafo inteiro, e `loadLiveTraces` sai do estado de
loading com uma mensagem de erro em caso de falha.

**Pronto quando:** teste confirma que uma falha simulada em `getConfig()` não impede o grafo de
renderizar com um padrão razoável, e que uma falha em `loadLiveTraces` sai do estado de loading.

## Tarefa 31.14 (M10): falso positivo de "rota morta" com tail sampling ou janela maior que a retenção

`insights.component.ts`, `deadRoutes` compara contra `routeStats()` sem considerar se tail sampling
está ligado ou se a janela pedida passa da retenção real do `ITraceStore`. Uma rota pode parecer
morta só porque foi amostrada pra fora ou porque os dados daquela janela já foram descartados.

Quando `tailSamplingEnabled` (já exposto por `GET /config`) estiver ligado, o aviso de rota morta
ganha uma ressalva explícita mencionando que a amostragem pode ser a causa, não a ausência real de
tráfego. Não precisa resolver o caso de retenção menor que a janela nesta tarefa (não há sinal de
retenção configurada exposto hoje); registrar como item de roadmap se a Executora confirmar que
não há forma simples de resolver com o contrato atual.

**Pronto quando:** teste confirma que, com `tailSamplingEnabled: true`, a tela mostra a ressalva
junto de qualquer rota morta.

## Fora de escopo

- Os 10 achados de Baixa severidade (Fase 32).
- Resolver o falso positivo de rota morta causado por janela de tempo maior que a retenção real do
  `ITraceStore` (sem contrato que exponha a retenção configurada hoje; decisão de expor isso, se
  fizer sentido, é da Planejadora, fora desta fase).
- Qualquer redesenho maior do mecanismo de replay além do que a tarefa 31.4/31.9 pede.

## Aceite da fase

Critérios de pronto das 14 tarefas acima. Build/test/lint/format dos dois lados (backend só muda
na tarefa 31.1). Impeccable rodado em `/flow`, `/history` e `/insights` (telas tocadas por mais de
uma tarefa), tema claro e escuro, desktop e `390x844`.

## Ao terminar

Resumo pra Planejadora: o que foi feito por tarefa, como verificar, o que ficou pendente. Se a
tarefa 31.14 encontrar que o falso positivo de retenção não tem solução simples com o contrato
atual, reportar o achado específico pra decisão de uma fase futura, não decidir sozinha.
