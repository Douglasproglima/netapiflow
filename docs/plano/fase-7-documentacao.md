# Fase 7: Documentação e release

Pré-requisito: Fases 0 a 6 aceitas. Esta é a última fase antes da primeira publicação pública.

## Tarefas

### 7.0: Aplicar a identidade visual do guia de marca ao dashboard (D-27, urgente)

O dashboard (Fase 4/5, já mergeado) nunca recebeu os tokens de `docs/brand/tokens.md` (ADR-0009).
Hoje roda só com os padrões do Angular Material (`mat.$azure-palette`/`mat.$blue-palette`,
tipografia `Roboto` nunca carregada de verdade), o que o usuário avaliou como "horrível" ao abrir
o dashboard real pela primeira vez. Prioridade máxima nesta fase: nenhuma tarefa de divulgação
visual (7.3, GIF) faz sentido com o tema atual.

Escopo, confirmado por inspeção do código real:

1. **Tema Material** (`src/NetApiFlow.Dashboard/ClientApp/src/styles.scss`): trocar
   `mat.$azure-palette`/`mat.$blue-palette` pelos tokens de `docs/brand/tokens.md` (Obsidian no
   escuro, Stage Light no claro, `--naf-flow-500`/`600`/`400` como primária). `theme-type:
   color-scheme` já está certo, manter.
2. **Tipografia**: `typography: Roboto` está declarado mas nenhuma fonte é carregada (nem CDN,
   nem arquivo local). Por isso o navegador cai no serifado padrão. Embutir localmente Plus
   Jakarta Sans, Inter e JetBrains Mono (`@font-face` com arquivo `.woff2` em `public/fonts/` ou
   equivalente), **sem** depender de CDN do Google Fonts: o dashboard precisa funcionar sem
   acesso à internet, atrás de CSP restritivo ou com o navegador do cliente final.
3. **Cores do grafo** (`features/flow/graph/flow-graph.component.scss`): os seis estados já usam
   variável CSS com fallback (ex.: `var(--flow-state-completed-fill, #e8f5e9)`, cor do Material,
   não da marca). Definir essas variáveis no tema com os valores reais
   (`--naf-success`/`warning`/`danger`/`idle` de `docs/brand/tokens.md`), sem mexer na lógica do
   componente.
4. **Ícones de estado de nó** (`shared/node-state-presentation.ts`): hoje são glifos Unicode
   (`⏳`, `▶`, `✓`...). Trocar pelos SVGs reais de `docs/brand/node-states/` (mesmo mapeamento de
   `NodeVisualState` já documentado em `tokens.md`).
5. **`index.html`**: `<title>` genérico ("Dashboard") vira "NetApiFlow", e o favicon (hoje o
   padrão do Angular CLI) vira o monograma de `docs/brand/logo-mark-dark.svg` (mesma fonte do
   `docs/images/icon.png` do pacote NuGet).

**Pronto quando:** `npm run lint`/`test:ci`/`build` continuam verdes, screenshot visual (claro e
escuro) mostra as cores e tipografia da marca, sem nenhum glifo Unicode nem cor padrão do Material
restando nas telas `/flow`, `/history`, `/components`.

**Fora de escopo desta tarefa:** o bug funcional observado em `/runner` ("Failed to load the
OpenAPI document") é separado, não é de estilo. Reportar à parte: `openApiUrl` do
`runner.component.ts` aponta para `/openapi/v1.json` na raiz do host (absoluto, correto por
design), então o 404/falha provável é o host de teste não ter `MapOpenApi()` configurado, não um
bug do dashboard. Confirmar e, se for mesmo isso, não é algo para o NetApiFlow corrigir.

### 7.0b: Redesenhar `/flow` como workspace denso (ADR-0010)

Depois de ver o dashboard real com a tarefa 7.0 aplicada, o usuário pediu uma arquitetura de
informação mais densa, descrita e decidida em ADR-0010. Segura a publicação da `0.1.0` até esta
tarefa fechar.

Escopo, por peça (nenhum contrato de API muda, nenhuma rota é removida):

1. **Sidebar fixa** à esquerda, substituindo a barra de navegação horizontal atual, com um item
   por rota existente (`/flow`, `/history`, `/components`, `/runner`).
2. **Faixa de cartões de métrica** no topo de `/flow`: total de traces, sucesso, erros, latência
   média, latência máxima, passos executados. Consome `GET /flow/api/metrics` (`TraceStats`,
   já implementado desde a Fase 3, D-18).
3. **Painel de ação** à esquerda de `/flow`: o mesmo formulário do Runner (extrair o formulário
   de `runner.component.ts` para um componente compartilhado, usado nos dois lugares, para não
   duplicar a lógica de montagem via OpenAPI). `/runner` continua existindo como rota própria.
4. **Painel de detalhes** à direita: reaproveita `FlowDetailsPanelComponent` e `buildWaterfall`
   (já implementados), só com novo estilo visual de lista com barra proporcional.
5. **Feed de eventos ao vivo**: painel novo, consumindo `GET /stream` (SSE, já implementado),
   mostrando o log cru de eventos conforme chegam.
6. Grafo e replay (centro da tela) continuam a mesma lógica (`graph-builder`, já implementado),
   só mudam de posição dentro do novo layout.

**Pronto quando:** `/flow` mostra sidebar, cartões de métrica, painel de ação, grafo, painel de
detalhes e feed de eventos juntos na mesma tela, sem quebrar nenhuma rota existente (`/history`,
`/components`, `/runner` continuam acessíveis e funcionais por link direto). `npm run
lint`/`test:ci`/`build` verdes.

### 7.0c: Correções achadas em verificação manual real (D-31)

A tarefa 7.0b foi verificada pessoalmente num navegador real (Playwright, e depois pelo usuário
direto no Chrome), contra o sample com dado real. Três problemas, nenhum pego pelos testes
automatizados existentes:

1. **Grafo sempre vazio com dado real** (bloqueante, achado pela Planejadora): `buildMergedTree`
   (`graph-builder.ts`) procura a raiz da árvore com `span.parentSpanId === null`. Nenhum span
   real tem esse valor: o span raiz (`Endpoint`/`Controller`) tem `parentSpanId` apontando para a
   Activity do próprio ASP.NET Core, que não é `null` nem corresponde a nenhum `spanId` do
   conjunto do NetApiFlow. `buildGraph` sempre devolve `{ nodes: [], edges: [] }`.
   Confirmado com `GET /traces/{traceId}` real (5 spans, hierarquia correta) e inspeção do DOM
   (`<svg class="flow-graph">` renderiza, mas vazio). Existe desde a implementação original da
   Fase 4, não é regressão da 7.0b: nenhuma verificação anterior rodou o dashboard contra dado
   real num navegador. Fix sugerido: detectar raiz como "span cujo `parentSpanId` não corresponde
   a nenhum `spanId` do próprio conjunto" (`!spans.some(s => s.spanId === span.parentSpanId)`),
   em vez de `=== null`.
2. **SSE lança exceção não tratada ao cancelar** (bloqueante, achado pelo usuário): fechar a
   conexão (navegar para outra rota, fechar a aba, EventSource reconectando) cancela o
   `CancellationToken` da requisição, e `DashboardSseEndpoint.StreamEventsAsync` (linha 84) deixa
   o `TaskCanceledException` subir sem capturar. Aparece como `fail` no log e na tela de erro de
   desenvolvimento. Cancelamento é o fim normal de uma conexão SSE, não uma falha: a regra de
   `.claude/rules/tracing.md` ("nunca propagar exceção da instrumentação") vale aqui também, por
   mais que seja código de hospedagem do dashboard, não do núcleo de rastreamento.
3. **Transbordo horizontal real** (achado pelo usuário): uma caixa do painel de ação fica com
   scroll horizontal cortando o texto (`GET /flow/api/traces` cortado), overflow visual, não só
   questão de gosto.
4. **Distância visual grande do protótipo do Guia de Marca**: espaçamento, tipografia e
   hierarquia hoje mais perto do padrão genérico do Angular Material do que do HTML do guia
   (`G:\Meu Drive\Controle\2026\00 - Personal\00 - NetApiFlow\Designer System and UI`). Não é um
   bug pontual, é um pedido de polimento. **Especificação completa e normativa deste item**:
   `docs/design/flow-workspace/guide.md` (tokens, layout, breakpoints, componente a componente,
   critérios de aceite em checklist) e `docs/design/flow-workspace/mockup.html` (referência
   visual navegável, abrir no navegador e comparar lado a lado com o dashboard rodando). As
   duas decisões que o guia deixava em aberto já foram resolvidas pela Planejadora (D-33, seção 9
   do próprio guia). O que o guia marca como "roadmap D-32" (zoom, legenda, sparkline, aba de
   cenários) não entra nesta tarefa. Dentro deste item, confirmado pelo usuário como parte do
   pacote de produção antes de considerar o dashboard pronto para `v1`:
   - **Logo real na sidebar**: hoje só o texto "NetApiFlow", sem o monograma de
     `docs/brand/logo-mark-dark.svg` (o mesmo usado no favicon e no `icon.png` do pacote NuGet).
   - **Paleta de cores**: conferir cada cor usada hoje contra `docs/brand/tokens.md`
     componente a componente, não só a troca inicial de tema feita na tarefa 7.0 (achado real:
     telas como o painel de ação ainda usam roxo de tom diferente do `--naf-flow-500` oficial em
     alguns estados, sinal de que a troca não foi 100% nos componentes novos da 7.0b).
   - **Responsividade**: a sidebar fixa (`mat-sidenav mode="side"`) não tem comportamento para
     tela estreita (tablet, celular). Precisa de um breakpoint que recolha a sidebar.
   - **Menu hambúrguer**: para abrir/fechar a sidebar em tela estreita, consequência direta do
     item de responsividade acima (não faz sentido ter um sem o outro).
   - **Cartões de métrica sem vida**: hoje só número e rótulo em cinza/branco, sem ícone nem cor,
     indistinguíveis entre si. Cada cartão ganha ícone próprio (traces, sucesso, erros, latência
     média, latência máxima, passos executados) e cor de destaque quando o valor importa (ex.:
     "Errors" em `--naf-danger` quando maior que zero), seguindo a mesma regra de cor + ícone +
     texto já usada nos estados de nó, não só texto solto.
   - **Versão do pacote**: nenhum lugar do dashboard mostra a versão do NetApiFlow rodando.
     Exibir perto do logo na sidebar (ex.: "v0.1.0"), lida de algum metadado já disponível em
     build (ou de uma nova rota, se não houver outra forma, a confirmar com a Planejadora antes
     de criar contrato novo).
   - **Seção "Sobre"**: link ou item na sidebar abrindo informação do projeto (versão, licença
     MIT, link do repositório, crédito à inspiração no apiflow conforme já exigido no README,
     tarefa 7.3). Forma exata (modal, página própria, rodapé expansível) fica a critério da
     Executora.
   - **Ícone de tipo dentro de cada nó do grafo**: hoje o nó só tem texto e um ícone pequeno de
     estado. Falta um ícone indicando o tipo de componente (Controller, Service, Repository,
     Database etc.), reaproveitando o mapeamento de `ComponentType` já existente em
     `core/models.ts`, não um contrato novo.
   - **Lista de execuções recentes dentro de `/flow`**: hoje só existe o feed de eventos crus.
     Falta uma lista curta (últimas N requisições, método, rota, status, duração), igual ao que
     `/history` já mostra em detalhe, só que condensada direto na tela principal, para não
     precisar trocar de rota pra ver o que acabou de rodar. Consome `GET /traces` já existente
     (paginado, pegar só a primeira página), sem contrato novo.
   - **Badge de verbo HTTP com cor própria por verbo** (GET, POST, PUT, DELETE cada um com uma
     cor de fundo distinta, não todos iguais), tanto na lista de execuções recentes quanto no
     painel de ação.

**Pronto quando:** os itens 1 e 2 corrigidos com teste automatizado cobrindo o cenário
(`graph-builder.spec.ts` com spans cujo root não tem `parentSpanId` nulo; teste de integração que
cancela a conexão SSE e confirma que não vira `fail` no log). Item 3 corrigido. Item 4 (incluindo
logo, paleta, responsividade, menu hambúrguer, cartões de métrica com ícone e cor, versão, "Sobre",
ícone de tipo no nó do grafo, lista de execuções recentes e badge de verbo HTTP colorido por
verbo) com captura de tela comparando antes/depois em pelo menos duas larguras de tela (desktop
e tela estreita) ao lado do HTML do guia, revisada pela Planejadora antes de prosseguir para a
tarefa 7.2 (GIF).

**Nota à parte, fora do escopo de 7.0c:** a Executora já implementou e a Planejadora já verificou o
roadmap pós `0.1.0` de i18n e tema manual (ADR-0011, D-35, D-36), mais um pedido de compactação do
bundle Angular, feito à parte. Achado e fechado nesse meio tempo: D-37 (alternador de tema não
funcionava) e D-38 (fechamento de D-37, mais o resultado da compactação). Nenhum dos dois é
critério de aceite desta tarefa nem da Fase 7: continuam roadmap pós `0.1.0` por classificação
explícita do usuário (D-35, D-36). Registrado aqui só para não parecer trabalho esquecido numa
leitura futura deste arquivo.

### 7.1: Benchmark de overhead (risco R-05)

Medir o custo de ter o NetApiFlow ligado vs. desligado (`Enabled = false`) vs. ausente, num
endpoint simples e num endpoint com vários spans aninhados. BenchmarkDotNet, método descrito por
escrito (hardware, carga, o que foi medido) para o número ser reprodutível por quem ler o README.

**Pronto quando:** números reais, com o projeto de benchmark versionado (ex.:
`tests/NetApiFlow.Benchmarks/`, fora da solução principal ou como projeto adicional no `.slnx`).

### 7.2: GIF do grafo animado

Gravado a partir do sample da Fase 2 com o dashboard da Fase 4/5, mostrando o grafo acendendo ao
vivo numa requisição real. Salvo em `docs/images/`.

**Pronto quando:** arquivo existe e é referenciado pelo README (tarefa 7.3). É o primeiro
elemento visual do README, por pedido explícito do prompt (seção 11, item 2: "é o que vende o
projeto").

### 7.3: `README.md`

Em inglês, nesta ordem exata (seção 11 do prompt):

1. Nome, frase de uma linha, badges (NuGet, build, licença, .NET 8/10).
2. GIF do grafo animado (tarefa 7.2).
3. O que é e o que não é (não objetivos, seção 2.1).
4. Início rápido: `dotnet add package`, as duas linhas de código, endereço do dashboard.
5. Formas de marcar etapas (tabela da seção 6.3), com exemplos de código reais do sample.
6. Pacotes opcionais (EF Core, MediatR, storages).
7. Como ler o grafo (estados e cores da seção 7.2).
8. Configuração completa (seção 9).
9. **Segurança do dashboard em produção**, em destaque visual (não só mais um parágrafo).
10. Dados sensíveis: o que é e o que não é capturado (seção 6.8, ADR-0007).
11. Desempenho e overhead, com os números da tarefa 7.1 e o método de medição.
12. Limitações conhecidas (seção 14 do prompt, incluindo os riscos R-11 e o que ficou
    documentado em vez de resolvido).
13. Contribuindo, licença MIT, seção **Inspiration** com crédito ao apiflow
    (`https://github.com/joshuamaia/apiflow`).

**Pronto quando:** os 13 itens presentes, nesta ordem, revisado pela Planejadora.

### 7.4: `README.pt-BR.md`

Mesmo conteúdo da tarefa 7.3, traduzido, não resumido.

**Pronto quando:** paridade de seções com o README em inglês.

### 7.5: `docs/architecture.md`

Diagrama da seção 6.1 do prompt (o mesmo `mermaid flowchart` reproduzido em `CLAUDE.md`, mais
detalhado aqui) e as decisões de arquitetura, linkando os ADRs relevantes em vez de repetir o
conteúdo deles.

**Pronto quando:** diagrama renderiza no GitHub (sintaxe `mermaid` válida).

### 7.6: `CHANGELOG.md`

Seção `[0.1.0]` substituindo `[Unreleased]`, formato Keep a Changelog, listando por categoria
(`Added`, `Changed`, `Fixed`, etc.) o que foi entregue nas Fases 0 a 6.

**Pronto quando:** seção da versão presente, com data de release.

### 7.7: Pequenos ajustes de arquitetura do frontend (D-26)

Achados de uma revisão de qualidade fora do ciclo normal de fase, sem gravidade suficiente para
bloquear a Fase 4 (já aceita), mas que não devem acumular até a Fase 7:

- Mover `NodeVisualState` (hoje em `features/flow/graph/graph-builder.ts`) para `core/models.ts`
  ou outro módulo de `shared/`, e ajustar `graph-builder.ts` e
  `shared/node-state-presentation.ts` para importar de lá. `shared/` não deve depender de um
  tipo de dentro de `features/`.
- Exportar a constante `GHOST_ID_PREFIX` de `graph-builder.ts` (ou expor um helper
  `isGhostNodeId(id)`) e usá-la em `flow-page.component.ts`, que hoje duplica o mesmo literal
  `'ghost-'` sem importar de lá.

**Pronto quando:** os dois pontos corrigidos, `npm run lint` e `npm run test:ci` continuam
verdes, sem mudança de comportamento visível (refactor puro).

### 7.8: `.github/workflows/release.yml`

Dispara em tag `v*.*.*`: empacota os 6 pacotes com a versão da tag
(`/p:Version=${{ ... }}` extraído da tag), publica no nuget.org via **Trusted Publishing** (OIDC
do GitHub Actions, sem API key guardada em secret).

**Pré-requisito manual, fora do repositório:** configurar a política de Trusted Publishing no
nuget.org para este repositório **antes** de criar a primeira tag. Isso não é automatizável
pela Executora, é um passo manual do Douglas no site do nuget.org. Registrar no resumo da fase
que esse passo foi (ou não) confirmado como feito antes de criar `v0.1.0`.

**Pronto quando:** a tag `v0.1.0` dispara o workflow e os 6 pacotes aparecem no nuget.org.

## Aceite da fase

`0.1.0` publicada no nuget.org pelo workflow de tag (critério da seção 13, Fase 7, do prompt).

## Ao terminar

Resumo final do projeto inteiro para a Planejadora: link dos 6 pacotes no nuget.org, link do
GIF/README publicado, e qualquer limitação conhecida descoberta durante a implementação que não
estava nas Fases anteriores. `/revisar-fase 7` é a revisão final antes de considerar a `0.1.0`
entregue.
