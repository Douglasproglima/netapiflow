# Guia de UX/UI do dashboard: workspace `/flow`

Destino: IA Executora. Complementa a tarefa 7.0c, item 4 (D-31), de `docs/plano/fase-7-documentacao.md`.
Não substitui nenhum ADR, contrato ou regra de `.claude/rules/`. Em conflito, eles prevalecem.

Arquivos deste pacote:

| Arquivo | Para que serve |
|---|---|
| `guia-ux-ui-flow-workspace.md` | Este documento. Especificação do que mudar e como |
| `mockup-flow-workspace.html` | Referência visual navegável da tela `/flow` em 1440 px. Abrir no navegador e comparar lado a lado com o dashboard rodando |

Fontes de verdade, nesta ordem: `docs/brand/tokens.md` (cores, tipografia, raios, espaçamento), ADR-0009 (guia de marca), ADR-0010 (layout do workspace), `docs/plano/contratos.md` (dados disponíveis). O mockup segue essas fontes. Os dados que aparecem nele são ilustrativos.

---

## 1. Regra de ouro: arquitetura de informação sim, identidade não

O dashboard de referência mostrado pelo usuário é o apiflow (Spring Boot). Do apiflow vale copiar só a **arquitetura de informação**, que já está decidida no ADR-0010: tudo numa tela, métricas no topo, ação à esquerda, grafo ao centro, detalhes à direita, execuções e eventos embaixo.

Nunca copiar dele:

- Cor (o azul de destaque, os blocos de ícone amarelo e verde, o verde-limão das arestas).
- Ícone, logo, desenho de nó ou qualquer asset.
- Texto de interface literal.

Os rótulos do dashboard ficam em inglês, como já estão no código (pacote NuGet internacional, ver `CLAUDE.md`). Este guia cita cada rótulo em inglês, entre aspas, exatamente como deve aparecer na tela. Alguns foram escolhidos de propósito para não repetir o texto do apiflow ("Try an endpoint", "Request path", "Error origin").

Toda cor sai de `docs/brand/tokens.md`. Todo ícone sai de `docs/brand/` ou é desenhado no mesmo estilo (traço 2 px, terminação arredondada, grid 16 ou 24). Isso é exigência do `CLAUDE.md` ("nenhum código, arquivo ou asset dele pode ser copiado").

---

## 2. Diagnóstico do que foi entregue

Captura analisada: `/flow` em 1366 × 768, tema escuro, trace `GET /api/orders/{id:int}`.

| # | Área | Problema observado | Correção (seção) |
|---|---|---|---|
| D1 | Painel de ação | Badges de verbo e rotas se sobrepõem. Rotas longas quebram linha por baixo do badge do item seguinte (`/flow/api/traces/{traceId}/spans/{spanId}` colide com `GET /flow/api/blueprint`) | 6.3 |
| D2 | Painel de ação | Lista mostra as rotas internas do próprio dashboard (`/flow/api/*`), incluindo `DELETE /flow/api/traces`, que apaga o histórico com um clique | 6.3 |
| D3 | Painel de ação | Sem título, sem formulário visível, sem estado vazio. Não comunica "execute uma requisição aqui" | 6.3 |
| D4 | Grafo | Coluna vertical única, nós de ~120 px com texto de ~8 px, ilegíveis. O último nó corta no fim do painel. Dois terços da área ficam vazios | 6.4 |
| D5 | Grafo | Não está dentro de um card. Não tem cabeçalho (método, rota, trace id) nem destaque de falha | 6.4 |
| D6 | Replay | Controles flutuam no topo do grafo, desalinhados. Velocidade usa `mat-select` padrão do Material, fora do visual da marca | 6.5 |
| D7 | Cartões de métrica | Ícone pequeno e sempre violeta. Os seis cartões são indistinguíveis. "Errors" não muda de cor quando é maior que zero | 6.2 |
| D8 | Sidebar | "About" é um botão com borda pill, estilo diferente dos outros itens. "History" e "About" usam um círculo genérico como ícone. Versão aparece como `V0.1.0`, em caixa alta | 6.1 |
| D9 | Cabeçalho | Não existe barra de topo: nada indica conexão SSE, limiar de lentidão ou requisições em andamento | 6.1 |
| D10 | Detalhes | Waterfall pinta todas as barras de violeta. O estado da etapa (lento, erro) se perde. `Started at` em ISO cru. Trace id em fonte proporcional | 6.6 |
| D11 | Execuções recentes | Sem código HTTP, sem ícone de status, badge de verbo cinza para todos. Sem link para `/history` | 6.7 |
| D12 | Eventos ao vivo | Sem indicador de conexão. Estado vazio sem orientação | 6.8 |
| D13 | Layout geral | Altura dos painéis não fecha com a viewport. Grafo cortado embaixo, área morta à direita do grafo | 5 |

---

## 3. Tokens: como consumir

Criar `src/styles/_tokens.scss` (ou equivalente) com as variáveis abaixo em `:root`, trocando os valores em `@media (prefers-color-scheme: light)`. **Nenhum componente usa hex direto.** Componente lê `var(--naf-*)`.

### 3.1 Cores base (de `docs/brand/tokens.md`)

| Token | Escuro (Obsidian) | Claro (Stage Light) |
|---|---|---|
| `--naf-bg` | `#090B10` | `#FFFFFF` |
| `--naf-surface` | `#131722` | `#F1F5F9` |
| `--naf-border` | `#232B3E` | `#E2E8F0` (divisória) |
| `--naf-border-strong` | `#2E3850` | `#64748B` (borda de controle, 4,8:1) |
| `--naf-text` | `#F8FAFC` | `#0F172A` |
| `--naf-text-muted` | `#94A3B8` | `#475569` |
| `--naf-flow-fill` | `#8B5CF6` (flow-500) | `#6D28D9` (flow-700) |
| `--naf-flow-text` | `#A78BFA` (flow-400) | `#6D28D9` |
| `--naf-flow-button` | `#7C3AED` (flow-600) | `#6D28D9` |
| `--naf-success` | `#10B981` | `#047857` |
| `--naf-warning` | `#F59E0B` | `#B45309` |
| `--naf-danger` | `#EF4444` | `#B91C1C` |
| `--naf-idle` | `#64748B` | `#64748B` |

### 3.2 Tokens derivados (novos, só para o dashboard)

Não são cores novas. São as mesmas cores da marca com transparência, para fundo de badge, nó e linha selecionada.

| Token | Valor (escuro) | Uso |
|---|---|---|
| `--naf-surface-2` | `#1A2030` | Hover de linha, fundo de input, trilho de barra |
| `--naf-flow-tint` | `rgba(139, 92, 246, .14)` | Item ativo da sidebar, linha selecionada, badge GET |
| `--naf-success-tint` | `rgba(16, 185, 129, .12)` | Nó concluído, badge POST, chip 2xx |
| `--naf-warning-tint` | `rgba(245, 158, 11, .12)` | Nó lento, badge PUT/PATCH |
| `--naf-danger-tint` | `rgba(239, 68, 68, .12)` | Nó com erro, badge DELETE, caixa de erro, chip 4xx/5xx |
| `--naf-idle-tint` | `rgba(100, 116, 139, .14)` | Nó aguardando, badge de verbo neutro |
| `--naf-focus` | `#8B5CF6` | Anel de foco, 2 px, `outline-offset: 2px` |

No tema claro, os tints usam a cor 700 correspondente com alpha `.10`.

### 3.3 Tipografia aplicada ao dashboard

Fontes embutidas localmente (tarefa 7.0, sem CDN). Escala por papel (tamanho, altura de linha,
peso) em `docs/brand/tokens.md`, seção "Escala aplicada ao dashboard": única fonte, não duplicada
aqui, para não divergir em silêncio (achado da Fase 11, `generate-design-md.js` lia as duas
fontes antes dessa correção).

### 3.4 Espaçamento, raio, elevação

- Espaçamento base 4: `4, 8, 12, 16, 24, 32, 48`. Gap entre painéis: 16. Padding interno de card: 16 (denso) ou 20 (cabeçalho).
- Raio: painel grande 18 (`radius-lg`), cartão de métrica e item de lista 12 (`radius-md`), controle e badge de verbo 8 (`radius-sm`) ou 6 em badge pequeno, chip de status `999`.
- Elevação: nenhuma sombra. A separação vem de `--naf-surface` sobre `--naf-bg` e borda `1px solid var(--naf-border)`. É o mesmo princípio do guia (cards translúcidos sem sombra pesada).

### 3.5 Badges de verbo HTTP

| Verbo | Texto | Fundo |
|---|---|---|
| GET | `--naf-flow-text` | `--naf-flow-tint` |
| POST | `--naf-success` | `--naf-success-tint` |
| PUT, PATCH | `--naf-warning` | `--naf-warning-tint` |
| DELETE | `--naf-danger` | `--naf-danger-tint` |
| Outros | `--naf-text-muted` | `--naf-idle-tint` |

Largura fixa de 56 px, texto centralizado, mono 11 px. A largura fixa é o que alinha a coluna de rotas (corrige D1). Como o verbo está escrito, a cor é reforço, não o único sinal.

### 3.6 Estados de nó (cor + ícone + texto)

Ícones de `docs/brand/node-states/` (16 × 16). Substituem os glifos Unicode de `shared/node-state-presentation.ts`.

| Estado | Cor do traço e ícone | Fundo do nó | Borda |
|---|---|---|---|
| Aguardando | `--naf-idle` | `--naf-surface` | `--naf-border` |
| Em execução | `--naf-flow-text` | `--naf-flow-tint` | flow-500 a 55%, com pulso de opacidade 1,2 s (desligado em `prefers-reduced-motion`) |
| Concluído | `--naf-success` | `--naf-success-tint` | success a 45% |
| Lento | `--naf-warning` | `--naf-warning-tint` | warning a 55% |
| Erro | `--naf-danger` | `--naf-danger-tint` | danger a 65%. Se `isErrorOrigin`, borda de 2 px |
| Não executado | `--naf-idle` | transparente | tracejada `4 3`, conteúdo com opacidade .6 |

---

## 4. Ícones de tipo de componente

O nó do grafo ganha um ícone de tipo (7.0c, item 4). Mapeamento a partir de `ComponentType` em `core/models.ts`, sem contrato novo. Desenhar em SVG inline, 16 × 16, traço 1,75 px, `stroke="currentColor"`, no estilo dos ícones de `docs/brand/icons/`:

| `ComponentType` | Metáfora do ícone |
|---|---|
| `Endpoint` | Seta entrando num nó (versão reduzida de `inbound.svg`) |
| `Controller` | Losango com ponto central (ponto de roteamento) |
| `UseCase` | Dois nós ligados por seta curva |
| `Service` | Engrenagem simples de 6 dentes |
| `Repository` | Caixa com três linhas (lista de registros) |
| `Database` | Cilindro |
| `Http` | Globo com meridiano |
| `Custom` | Quadrado com cantos HUD (marcas de canto) |

O ícone fica num bloco de 28 × 28, raio 8, com fundo no tint do estado e traço na cor do estado. O mockup tem os SVGs prontos para copiar.

---

## 5. Layout da tela `/flow`

### 5.1 Grade (≥ 1600 px)

```text
┌──────┬──────────────────────────────────────────────────────────────────────────┐
│      │ TOPBAR 56px: Flow · Live request paths       1 in progress · slow · Live │
│      ├──────────────────────────────────────────────────────────────────────────┤
│ RAIL │ MÉTRICAS: 6 cartões, altura 88px  (+ sparkline 0.2.0)                    │
│ 76px ├────────────┬───────────────────────────────────────┬─────────────────────┤
│      │ AÇÃO       │ REQUEST PATH                          │ DETALHES            │
│ logo │ 300px      │ 1fr                                   │ 360px               │
│ ver. │ endpoints  │ cabeçalho · faixa de erro             │ resumo · erro       │
│ nav  │ formulário │ grafo                                 │ metadados · passos  │
│      │            │ barra de replay                       │                     │
│ About├────────────┴─────────────────────┬─────────────────┴─────────────────────┤
│      │ RECENT EXECUTIONS  (1fr)         │ LIVE EVENTS  (1fr)                    │
└──────┴──────────────────────────────────┴───────────────────────────────────────┘
```

- Linha do meio: `grid-template-columns: 300px minmax(0, 1fr) 360px`, altura `clamp(560px, calc(100vh - 56px - 88px - 300px - 64px), 760px)`. Os três painéis têm a mesma altura e rolam por dentro. A página nunca empurra o grafo para fora.
- Linha de baixo: altura fixa de 300 px, cada painel com rolagem interna.
- Padding da área de conteúdo: 16 px.

Por que um rail de 76 px e não uma sidebar de 232 px: a 1366 px (largura da captura do usuário), uma sidebar larga mais os painéis laterais deixam menos de 500 px para o grafo. O rail mantém ícone e rótulo (ADR-0010 pede "ícone + rótulo"), só empilhados, e devolve 156 px ao grafo.

### 5.2 Breakpoints

| Largura | Comportamento |
|---|---|
| ≥ 1600 | Grade completa da 5.1 |
| 1200 a 1599 | Painel de ação sai da grade e vira drawer à esquerda, aberto pelo botão "Try an endpoint" da topbar. Grafo e detalhes (360) lado a lado. Sparkline some |
| 1024 a 1199 | Igual, com detalhes em 320. Métricas em 3 colunas. Topbar esconde os metadados de texto e mantém o chip de conexão |
| 768 a 1023 | Rail vira drawer com hambúrguer na topbar (`mat-sidenav mode="over"`). Detalhes passam para baixo do grafo. Execuções e eventos empilhados |
| < 768 | Uma coluna. Métricas em 2 colunas. Grafo com altura 520 e pan por toque. Linhas de lista quebram em duas linhas. Nada transborda na horizontal |

`BreakpointObserver` do CDK decide o `mode` do `mat-sidenav` e se o painel de ação é grade ou drawer. O estado aberto ou fechado vive num `signal`. O mockup implementa todos esses breakpoints: redimensione a janela para conferir.

---

## 6. Componentes, um por um

### 6.1 Shell: rail e topbar (D8, D9)

Rail, 76 px, fundo `--naf-bg`, borda direita `--naf-border`, fixo na altura da viewport:

- Topo: monograma de `docs/brand/logo-mark-dark.svg` (claro: `logo-mark-light.svg`) em 36 px. Abaixo, versão em mono 10 px `--naf-text-muted`, minúscula: `v0.1.0`. O wordmark completo aparece no diálogo "About", não no rail.
- Navegação: quatro itens ("Flow", "History", "Components", "Runner"). Cada item tem 60 × 56, raio 12, ícone 20 px em cima e rótulo Inter 500 11 px embaixo. Ativo: fundo `--naf-flow-tint`, texto `--naf-text`, ícone `--naf-flow-text`. Sem barra colorida na lateral. Hover: `--naf-surface-2`. Usar `routerLinkActive` com `aria-current="page"`.
- Rodapé do rail: item "About" com o **mesmo estilo** dos itens de navegação, separado por divisória. Abre um `mat-dialog` com wordmark, versão, licença MIT, link do repositório e crédito ao apiflow. Nunca botão com borda pill.
- Ícones: Flow (três nós ligados), History (relógio com seta anti-horária), Components (grade 2 × 2), Runner (triângulo de play), About (círculo com "i"). SVGs prontos no sprite do mockup.

Topbar, 56 px, borda inferior `--naf-border`:

- Esquerda: hambúrguer (só abaixo de 1024), título "Flow" (Plus Jakarta 700 18), subtítulo muted 13 "Live request paths".
- Direita, em ordem: botão "Try an endpoint" (só abaixo de 1600, abre o drawer de ação), "N in progress" (de `GET /traces/live`, só a contagem), "slow threshold 250 ms" (de `GET /config`, `slowThresholdMicros` formatado) e chip de conexão SSE.
- Chip de conexão: "Live" (success), "Reconnecting" (warning, pulso), "Offline" (danger). Sempre bolinha mais texto. Sai do estado do `SseService`, que já existe.

### 6.2 Cartões de métrica (D7)

Seis cartões, `grid-template-columns: repeat(6, minmax(0, 1fr))`, gap 16, altura 88, raio 12, padding 16, fundo `--naf-surface`.

Estrutura: bloco de ícone 40 × 40 (raio 10, fundo tint, ícone 20 px) à esquerda. À direita, valor (Plus Jakarta 700 24 tabular) e rótulo (Inter 500 12 muted).

| Cartão (rótulo na tela) | Ícone | Cor do bloco | Regra de destaque |
|---|---|---|---|
| "Traces" | camadas | flow | Nenhuma |
| "Successful" | check em círculo | success | Nenhuma |
| "Errors" | x em círculo | danger se > 0, idle se 0 | Com valor > 0, número e borda do cartão em danger |
| "Avg latency" | cronômetro | flow | Bloco em warning se ≥ limiar de lentidão |
| "Max latency" | cronômetro com seta para cima | flow | Bloco em warning se ≥ limiar de lentidão |
| "Steps executed" | nós em cascata | flow | Nenhuma |

Valores de tempo usam o formatador existente (`shared/duration-format.ts`): `98.1 ms`, `3.03 s`, `299 µs`. Unidade no mesmo tamanho do número, cor muted.

Fonte dos dados: `GET /metrics` (`totalTraces`, `errorCount` de D-30, `averageDurationMicros`, `maxDurationMicros`). Sucesso = `totalTraces - errorCount`. Passos executados: manter a fonte usada hoje pela 7.0b.

### 6.3 Painel de ação: "Try an endpoint" (D1, D2, D3)

Reaproveita o formulário extraído do Runner (ADR-0010). Só a apresentação muda.

- Cabeçalho do card: título "Try an endpoint" (Inter 600 14) e, abaixo, texto muted 12: "Sends a real request to your API. The graph follows it live."
- Abas "Custom" e "Scenarios": só "Custom" entra na 0.1.0. A aba "Scenarios" é roadmap D-32 (seção 8). Enquanto não existir, não renderizar as abas.
- Busca: input de filtro por rota, placeholder "Filter by route" (altura 36, raio 8, ícone de lupa), filtra a lista localmente.
- Lista de endpoints: cada item é um `<button>` de altura mínima 36, `display: grid; grid-template-columns: 56px minmax(0, 1fr); gap: 8px; align-items: center`. Badge de verbo (3.5) na primeira coluna. Rota em mono 12 na segunda, **uma linha com `text-overflow: ellipsis`**, rota completa no `title`. Selecionado: fundo `--naf-flow-tint`, borda `--naf-flow-fill` a 45%.
- **Filtrar as rotas do próprio dashboard**: esconder toda rota cujo caminho começa no caminho base do dashboard (ex.: `/flow/api/...`). Elas são infraestrutura do NetApiFlow, não endpoints do usuário, e `DELETE /flow/api/traces` apaga o histórico. Abaixo da lista, nota muted 11 com ícone: "3 dashboard routes (/flow/api/*) hidden". Decisão de comportamento: **confirmar com a Planejadora antes** (ver seção 9).
- Formulário do endpoint selecionado, abaixo da lista, separado por divisória: cabeçalho com badge + rota, campos com `<label>` visível (Inter 500 12) seguido do tipo em mono 11 muted (`path · int`, `application/json`), input altura 36, corpo JSON em `<textarea>` mono 12 com altura 96. Botão "Send request" largura total, altura 40, fundo `--naf-flow-button`, texto branco, raio 8, ícone de play.
- Estados: carregando (skeleton de 6 linhas), erro de OpenAPI (caixa danger-tint com "Could not load the OpenAPI document." e a dica "Check that the host calls MapOpenApi()."), lista vazia ("No endpoints found").
- Proporção: a lista ocupa até 55% da altura do card e rola. O formulário ocupa o resto e rola por dentro.

### 6.4 Card central: fluxo da requisição (D4, D5)

Cabeçalho do card (altura 52, padding `0 16px`, borda inferior):

- Esquerda: "Request path" (Inter 600 14), badge de verbo, rota em mono 13, trace id curto em mono 12 muted (`7f3ac21e…`), com botão de copiar (`aria-label="Copy trace id"`).
- Direita: botão "Back to live" (só visível em replay ou trace fixado).

Faixa de erro, logo abaixo do cabeçalho, só quando o trace tem erro: `<button>` com fundo `--naf-danger-tint`, borda danger a 55%, raio 10, ícone de alerta e texto Inter 600 13: `Error origin: InventoryService · HTTP 409 · 1.28 s`. Clicar seleciona o nó de origem do erro (`isErrorOrigin`). Equivalente para trace lento sem erro: faixa warning com `Slowest step: PaymentService · 842 ms`.

Grafo:

- Fundo do canvas: `--naf-bg` com grade de pontos `radial-gradient(rgba(148,163,184,.12) 1px, transparent 1px)` de 20 px. É o mesmo padrão do hero do guia de marca.
- Layout dagre com `rankdir: 'LR'` (esquerda para direita), `nodesep: 16`, `ranksep: 44`, `marginx/marginy: 24`. Trocar `TB` por `LR` é o que tira o grafo da coluna única (D4). Só muda parâmetro do layout, não a lógica do `graph-builder`.
- Ao montar ou trocar de trace: ajustar à tela (fit) com zoom entre 0,7 e 1. Nunca ampliar acima de 100% (trace de duas etapas não vira nó gigante) e nunca reduzir abaixo de 70% (texto deixa de ser legível). Se o grafo não couber a 70%, ele começa alinhado à esquerda, com a raiz visível, e o resto fica acessível por pan.

Nó (SVG, 184 × 60, raio 10):

```text
┌──────────────────────────────────────┐
│ ┌────┐  InventoryService             │  Inter 600 12, --naf-text
│ │icon│  ● Error · Service            │  Inter 500 11, cor do estado + muted
│ └────┘  ReserveAsync()       427 ms  │  mono 10,5 muted · mono 10,5 --naf-text
└──────────────────────────────────────┘
```

- Bloco de ícone de tipo 28 × 28 à esquerda (seção 4).
- Linha 1: nome do componente, truncado com reticências.
- Linha 2: ícone de estado 12 px + rótulo do estado + `· tipo` em muted.
- Linha 3: operação em mono à esquerda, duração em mono à direita (`text-anchor: end`).
- Número de sequência (`#3`): pill pequena (mono 10, fundo `--naf-surface-2`) no meio da aresta de entrada, não dentro do nó.
- Selecionado: anel externo de 2 px em `--naf-focus`, 3 px fora da borda.
- Foco por teclado: cada nó é focável (`tabindex="0"`, `role="button"`, `aria-label="InventoryService, ReserveAsync(), Error, 427 ms"`). Enter seleciona.
- Nó não executado: sem duração na linha 3.

Aresta: curva suave (`d3.curveBasis` ou bezier própria), 1,5 px, cor do estado do nó **de destino**, ponta de seta 6 px. Aresta para nó não executado: tracejada `4 3` em `--naf-idle`.

### 6.5 Barra de replay (D6)

Fixa no rodapé do card central, altura 56, borda superior, padding `0 16px`, flex com gap 12:

1. Chip "REPLAY" (mono 11, flow-tint) ou "LIVE" (success-tint), conforme o modo.
2. Botões ícone 36 × 36, raio 8: reiniciar, passo anterior.
3. Play/pausa: círculo 40 px, fundo `--naf-flow-button`, ícone branco.
4. Botão passo seguinte.
5. Trilho de progresso: `flex: 1`, altura 4, trilho `--naf-surface-2`, preenchimento `--naf-flow-fill`, thumb 14 px. É um `<input type="range">` estilizado, com `aria-label`.
6. Seletor de velocidade como **grupo segmentado** (`0.25×`, `0.5×`, `1×`, `2×`, `4×`): botões de 36 de altura, mono 12, ativo com fundo `--naf-flow-tint` e texto `--naf-flow-text`. Substitui o `mat-select` atual. Usar `role="radiogroup"`.

Abaixo da barra, linha de status mono 12 muted: `Step 10/10 · finished with HTTP 409 · replaying recorded timestamps at 1×`. A duração exibida nunca muda com a velocidade (tarefa 4.8).

### 6.6 Painel de detalhes (D10)

Card de 360 px com rolagem interna. Sem nó selecionado:

1. Cabeçalho: badge de verbo + rota em mono 14.
2. Linha de chips: status HTTP com ícone de estado em SVG ("HTTP 409" em danger-tint com o ícone de erro, "HTTP 200" em success-tint com o ícone de concluído), tenant quando existir (chip neutro, mono).
3. Três números lado a lado, separados por divisória vertical: `1.28 s` "Total", `7` "Executed", `3` "Not executed". Plus Jakarta 700 20 + rótulo muted 12.
4. Caixa de erro (só com erro): danger-tint, raio 10, título Inter 600 13 `Failed at InventoryService.ReserveAsync()`, mensagem Inter 13 (já sanitizada pelo backend, ADR-0007).
5. Metadados em `<dl>` de duas colunas: "Trace ID" (mono 12, ellipsis, botão copiar), "Started" e "Finished" em hora local `HH:mm:ss.SSS` com data completa no `title`.
6. Lista de passos: rótulo mono 11 muted em caixa alta `STEPS · select one to inspect`. Cada linha: índice (mono 11 muted, 20 px), ícone de estado 12 px + nome do componente (Inter 13, ellipsis), trilho de waterfall (92 px, altura 6, raio 3), duração (mono 12, alinhada à direita, 60 px). A barra começa no deslocamento de início da etapa e usa **a cor do estado da etapa**. Etapa não executada: trilho tracejado vazio e o texto "skipped" no lugar da duração. Linha clicável, seleciona o nó no grafo e vice-versa.

Com nó selecionado: mesmo card, conteúdo de `SpanDetail` (já existe). Adicionar botão "Back to trace summary" no topo para voltar.

### 6.7 Execuções recentes (D11)

Consome `GET /traces` (primeira página, `pageSize` 10). Cabeçalho: "Recent executions" e link "View all" com seta, para `/history`.

Linha (altura 40, raio 10, padding `0 10px`, `grid-template-columns: 64px 56px minmax(0,1fr) 72px 56px`):

1. Status: ícone 14 px + código HTTP em mono 12, na cor do status (2xx success, 4xx/5xx danger, em andamento flow com ícone de execução).
2. Badge de verbo.
3. Rota em mono 12, ellipsis. Tag de cenário ou tenant opcional como chip neutro à direita da rota.
4. Duração em mono 12, alinhada à direita. Warning se acima do limiar.
5. Tempo relativo Inter 12 muted (`now`, `56 s`, `1 min`), absoluto no `title`.

Linha do trace aberto: fundo `--naf-flow-tint`, borda `--naf-flow-fill` a 40%. Clicar navega para `?trace=<id>`.

### 6.8 Eventos ao vivo (D12)

Consome o `SseService` existente. Cabeçalho: "Live events" e chip de conexão ("Connected", "Reconnecting", "Offline").

- Contêiner com `role="log"` e `aria-live="off"` (o fluxo é rápido demais para leitor de tela), rolagem interna, mais recente no topo, limite de 200 linhas em memória.
- Linha mono 12, `grid-template-columns: 44px 136px minmax(0,1fr) 72px`: `#117` muted, tipo do evento colorido, descrição (`POST /api/orders/{id:int}/confirm → 409 · 1.28 s`), trace id curto muted.
- Cores do tipo: `TraceStarted` e `SpanStarted` em `--naf-flow-text`. `SpanCompleted`, `TraceCompleted` em `--naf-success`. `SpanFailed` em `--naf-danger`. `TracePersisted` em `--naf-text-muted`. Eventos `ping` nunca aparecem (D-20).
- Estado vazio: ícone de antena + "Waiting for events. Send a request from Try an endpoint or call your API."

---

## 7. Acessibilidade e movimento

- Contraste: todos os pares de texto da seção 3 passam 4,5:1. Não inventar cinza novo.
- Foco visível em todo elemento interativo: `outline: 2px solid var(--naf-focus); outline-offset: 2px`. Nunca `outline: none` sem substituto.
- Alvo de toque mínimo: 36 px no desktop, 44 px abaixo de 768.
- `prefers-reduced-motion: reduce`: desligar pulso do nó em execução e do chip "Reconectando". A animação do replay continua, porque é conteúdo, não decoração.
- Elementos clicáveis são `<button>` ou `<a>` de verdade, nunca `div` com `(click)`.
- Os seis estados continuam distinguíveis sem cor (teste da tarefa 4.5 tem que continuar passando).

---

## 8. Escopo: o que entra agora e o que é roadmap

**Entra na 7.0c (0.1.0):** tudo das seções 3 a 7, exceto os itens abaixo.

**Roadmap D-32 (0.2.0), não implementar sem liberação explícita do usuário.** O mockup mostra estes itens com a etiqueta `0.2.0` para o layout já reservar espaço:

| Item D-32 | Onde entraria |
|---|---|
| Botões de zoom (+, −, ajustar) e porcentagem | Canto inferior direito do canvas do grafo |
| Legenda fixa dos seis estados | Canto inferior esquerdo do canvas do grafo |
| Sparkline de latência com p95 | Sétimo cartão da faixa de métricas, ocupando 2 colunas |
| Cenários de demonstração com um clique | Aba "Cenários" no painel de ação, ao lado de "Personalizado" |
| Atraso artificial configurável | Dentro da aba "Cenários" |
| Alternador "seguir ao vivo" | Cabeçalho do card central, à direita |

Se o usuário promover algum desses itens para a 0.1.0, a Planejadora registra a mudança em `decisoes.md` antes da implementação.

Liberados para implementação em D-106: os seis itens, agrupados em `docs/plano/fase-25-grafo-controles-ao-vivo.md` (zoom, legenda, sparkline, seguir ao vivo) e `docs/plano/fase-26-cenarios-demo-e-delay.md` (cenários, atraso artificial). As posições da tabela acima continuam valendo como especificação normativa.

**Achado real da Fase 25, corrige a frase acima:** a legenda e os controles de zoom são overlay
sobre o canvas (painel opaco, `position: absolute` nos cantos inferiores), mas o ajuste
automático do grafo (zoom/pan pra caber o conteúdo no canvas) centraliza sem saber que esses dois
cantos estão ocupados. Um trace com poucos nós reais pode posicionar um nó exatamente atrás de um
dos dois painéis, texto sobre texto. A posição normativa continua valendo (canto inferior
esquerdo pra legenda, direito pros controles de zoom), mas o cálculo de ajuste automático precisa
reservar uma margem de segurança nos dois cantos inferiores, do tamanho real de cada painel (a
Executora mede o tamanho real renderizado, não assume um valor fixo), pra nenhum nó real
renderizar atrás deles. Achado via Impeccable (`text-occlusion`) na própria Fase 25, confirmado
por medição real de geometria, não suposição.

---

## 9. Decisões já tomadas pela Planejadora (D-33)

1. **Esconder rotas `/flow/api/*` da lista do Runner** (6.3). **Decidido: esconder por padrão**,
   como o guia recomendava. Previne o acidente real descrito em D2 (`DELETE /flow/api/traces`
   visível e clicável na lista).
2. **Origem da versão exibida na sidebar**. **Decidido: `GET /flow/api/config` ganha um campo
   `version`**, lido de `AssemblyInformationalVersionAttribute` do assembly `NetApiFlow` (o mesmo
   valor de `<Version>` em `Directory.Build.props`, sem rota nova, mesmo padrão aditivo de D-24 e
   D-30).
3. Qualquer dado que o mockup mostre e que não exista em `docs/plano/contratos.md`: o dado sai da
   tela, não entra um contrato novo improvisado. Continua valendo, sem exceção.

---

## 10. Critérios de aceite visual

Captura de tela em 1440 × 900 e em 390 × 844, tema escuro e claro, ao lado do `mockup-flow-workspace.html`. Conferir:

- [ ] Nenhum hex direto em `.scss` de componente: `grep -rnE "#[0-9a-fA-F]{3,8}\b" src/app --include=*.scss` só retorna o arquivo de tokens.
- [ ] Nenhum glifo Unicode como ícone (`⏳ ▶ ✓ ⚠ ✕ ∅`).
- [ ] Nenhum componente Material com a aparência padrão (select, botão, toolbar) visível em `/flow`.
- [ ] Lista de endpoints sem sobreposição nem quebra de linha. Rotas longas com reticências.
- [ ] Grafo da esquerda para a direita, texto do nó legível a 100%, nenhum nó cortado ao abrir o trace.
- [ ] Barras do waterfall com a cor do estado de cada etapa.
- [ ] Cartão "Erros" muda para danger quando o valor é maior que zero.
- [ ] Verbos GET, POST, PUT e DELETE com cores distintas, na lista de ação e nas execuções recentes.
- [ ] Rail com logo, versão em minúsculas (`v0.1.0`), "About" no mesmo estilo dos outros itens.
- [ ] Abaixo de 1600 px, painel de ação abre como drawer pelo botão "Try an endpoint".
- [ ] Chip de conexão SSE visível na topbar e no feed de eventos.
- [ ] A 390 px: hambúrguer abre a sidebar, nada transborda na horizontal (`document.documentElement.scrollWidth === innerWidth`).
- [ ] `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` verdes.
- [ ] Nenhuma cor, ícone ou texto copiado do apiflow.
