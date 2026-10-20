# ADR-0010: Dashboard em workspace único, não mais uma rota por feature

## Status
Aceito

## Contexto

A Fase 4 (ADR implícito em D-23) decidiu uma rota por feature (`/flow`, `/history`,
`/components`, `/runner`), espelhando a estrutura de pastas `features/` da seção 7.4 do prompt.
Isso já está implementado e mergeado.

Ao ver o dashboard real, o usuário comparou com o
[apiflow](https://github.com/joshuamaia/apiflow) (seção 1.1 do `netapiflow-prompt.md`, projeto
que inspira a ideia do NetApiFlow, sem copiar código, arquivo ou asset dele) e pediu algo "elegante
e funcional" como `docs/images/dashboard-error.png` daquele projeto. Analisando a captura (só a
imagem, nenhum código ou arquivo foi acessado), o que a torna mais rica não é cor ou tipografia
(isso já é a tarefa 7.0 em andamento). É a arquitetura de informação: tudo numa tela só, com
cartões de métrica sempre visíveis no topo, painel de ação à esquerda, grafo com replay ao centro,
painel de detalhes com lista de execução à direita, feed de eventos ao vivo embaixo.

O usuário decidiu segurar a publicação da `0.1.0` para fazer esse redesenho agora, em vez de
tratá-lo como iniciativa de uma versão futura.

## Decisão

O dashboard muda de "uma rota por feature" para um **workspace único** na rota `/flow` (rota
default). `/history`, `/components` e `/runner` deixam de ser telas cheias isoladas e viram
painéis dentro do mesmo workspace:

- **Sidebar fixa** à esquerda (ícone + rótulo), substituindo a barra de navegação horizontal
  atual.
- **Faixa de cartões de métrica** no topo: total de traces, sucesso, erros, latência média,
  latência máxima, passos executados. Dado já existe em `GET /flow/api/metrics`
  (`TraceStats`, D-18), sem mudança de contrato.
- **Painel de ação** à esquerda: cenários rápidos de teste e o formulário do Runner (hoje em
  `/runner`), movido para dentro do workspace.
- **Centro**: o grafo (`graph-builder`, já pronto) com os controles de replay, sem mudança de
  lógica, só de posição no layout.
- **Painel de detalhes** à direita: o que já existe em `FlowDetailsPanelComponent` e
  `buildWaterfall` (lista de execução com barra proporcional à duração), sem mudança de lógica,
  só de estilo visual da lista.
- **Feed de eventos ao vivo**: painel novo, consumindo o `GET /stream` (SSE) já existente,
  mostrando o log cru de eventos (`SpanStarted`, `SpanCompleted`, etc.), sem endpoint novo.
- `/history` e `/components` continuam existindo como rotas próprias (telas de consulta histórica
  não cabem bem dentro do workspace ao vivo), só ganham os tokens visuais da tarefa 7.0.

Nenhum contrato de API muda. É redesenho de camada de apresentação, consumindo dados que o
backend já expõe desde as Fases 1 a 3.

## Consequências

- Atraso na publicação da `0.1.0`: aceito explicitamente pelo usuário, registrado aqui para não
  ser lido como atraso não planejado.
- Tarefa 7.0 (D-27) é reescrita e ampliada para cobrir este layout, não só tokens de cor e fonte.
- `/runner` como rota isolada é removida depois que o painel de ação a incorporar; a rota em si
  pode continuar existindo como atalho direto (deep link), mas deixa de ser a forma principal de
  uso.
- Boa parte da lógica (grafo, waterfall, replay, SSE) é reaproveitada sem mudança: o esforço novo
  é majoritariamente de composição de layout e dos componentes que ainda não existem (cartões de
  métrica, sidebar, feed de eventos).
