# ADR-0010: `/flow` vira um workspace denso, as demais rotas continuam existindo

## Status
Aceito

## Contexto

A Fase 4 (D-23) decidiu uma rota por feature (`/flow`, `/history`, `/components`, `/runner`),
espelhando a estrutura de pastas `features/` da seção 7.4 do prompt. Isso já está implementado e
mergeado.

Ao ver o dashboard real, o usuário pediu algo mais "elegante e funcional", comparando com a
densidade de informação de outros dashboards de observabilidade de mercado. O que torna esse tipo
de tela mais rica não é cor ou tipografia (isso já é a tarefa 7.0 em andamento). É a arquitetura de
informação: tudo numa tela só, com cartões de métrica sempre visíveis no topo, painel de ação à
esquerda, grafo com replay ao centro, painel de detalhes com lista de execução à direita, feed de
eventos ao vivo embaixo.

O usuário decidiu segurar a publicação da `0.1.0` para fazer esse redesenho agora, em vez de
tratá-lo como iniciativa de uma versão futura. Confirmou também que **todas as rotas continuam
existindo e acessíveis**, cada uma com seu link direto. O que muda é só a tela `/flow`, que fica
mais densa, não a quantidade de rotas do dashboard.

## Decisão

A rota `/flow` (default do dashboard) passa a mostrar um workspace mais denso em vez de só o
grafo:

- **Sidebar fixa** à esquerda (ícone + rótulo) com um item por rota existente (`/flow`,
  `/history`, `/components`, `/runner`), substituindo a barra de navegação horizontal atual.
  Continua sendo navegação real entre rotas, só muda a posição e o formato visual.
- **Faixa de cartões de métrica** no topo de `/flow`: total de traces, sucesso, erros, latência
  média, latência máxima, passos executados. Dado já existe em `GET /flow/api/metrics`
  (`TraceStats`, D-18), sem mudança de contrato.
- **Painel de ação** à esquerda de `/flow`: cenários rápidos de teste, o mesmo formulário que já
  existe em `/runner`. `/runner` continua existindo como rota própria e com link direto; o painel
  dentro de `/flow` é um atalho para quem não quer trocar de tela, não uma substituição.
- **Centro**: o grafo (`graph-builder`, já pronto) com os controles de replay, sem mudança de
  lógica, só de posição no layout.
- **Painel de detalhes** à direita: o que já existe em `FlowDetailsPanelComponent` e
  `buildWaterfall` (lista de execução com barra proporcional à duração), sem mudança de lógica,
  só de estilo visual da lista.
- **Feed de eventos ao vivo**: painel novo dentro de `/flow`, consumindo o `GET /stream` (SSE) já
  existente, mostrando o log cru de eventos (`SpanStarted`, `SpanCompleted`, etc.), sem endpoint
  novo.
- `/history` e `/components` continuam como hoje, telas próprias de consulta histórica, só com os
  tokens visuais da tarefa 7.0 aplicados.

Nenhum contrato de API muda, nenhuma rota é removida. É redesenho de camada de apresentação,
consumindo dado que o backend já expõe desde as Fases 1 a 3.

## Consequências

- Atraso na publicação da `0.1.0`: aceito explicitamente pelo usuário, registrado aqui para não
  ser lido como atraso não planejado.
- Tarefa 7.0 (D-27) é reescrita e ampliada para cobrir este layout, não só tokens de cor e fonte.
- O formulário do Runner passa a existir em dois lugares (painel dentro de `/flow` e a rota
  `/runner` inteira): extrair o formulário para um componente compartilhado evita duplicar a
  lógica de montagem via OpenAPI.
- Boa parte da lógica (grafo, waterfall, replay, SSE) é reaproveitada sem mudança: o esforço novo
  é majoritariamente de composição de layout e dos componentes que ainda não existem (cartões de
  métrica, sidebar, feed de eventos).
