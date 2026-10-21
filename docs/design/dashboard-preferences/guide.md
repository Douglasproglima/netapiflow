# Guia de UX/UI do dashboard: idioma e tema

Destino: IA Executora, quando o usuário abrir a fase que implementa D-35 e D-36. Roadmap pós
`0.1.0`, não bloqueia a Fase 7 nem nenhuma tarefa atual.

Complementa `docs/design/flow-workspace/guide.md` (D-33): não redesenha a tela `/flow`, só
acrescenta dois controles novos ao shell já especificado lá (rail + topbar, seção 6.1) e troca o
conteúdo de texto de cada componente por uma entrada de dicionário.

Fontes de verdade, nesta ordem: [ADR-0011](../../adr/0011-i18n-em-tempo-de-execucao-e-tema-manual.md)
(mecanismo técnico), `docs/plano/decisoes.md` D-35 e D-36 (escopo), `docs/design/flow-workspace/guide.md`
(layout e tokens existentes), `docs/brand/tokens.md` (cores). Em conflito, essa ordem prevalece
sobre este guia.

Arquivos deste pacote:

| Arquivo | Para que serve |
|---|---|
| `guide.md` | Este documento |
| `mockup.html` | Fork navegável de `docs/design/flow-workspace/mockup.html`, com o alternador de tema promovido de "mockup only" para o controle real e o seletor de idioma novo ao lado. Abre em quatro combinações: claro+inglês, escuro+português, e as duas outras, trocáveis ao vivo pelos próprios controles |

---

## 1. O que já está decidido (não é deste guia para decidir de novo)

- **i18n em tempo de execução**, um dicionário por idioma dentro do mesmo bundle, trocado por um
  `signal`, sem recarregar a página (ADR-0011). Não é `@angular/localize`, não gera `dist/` por
  idioma.
- **PT-BR é o padrão sem preferência salva** (D-35). Inglês é secundário. Não é o idioma do
  navegador.
- **Tema**: `prefers-color-scheme` continua o padrão sem preferência salva. O alternador manual
  escreve `data-theme="dark"` ou `data-theme="light"` na raiz do documento e grava em
  `localStorage`, sobrepondo a detecção automática quando presente (D-36, já assim no mockup de
  D-33: `toggleTheme()` em `docs/design/flow-workspace/mockup.html` já faz exatamente isso, só que
  rotulado "mockup only"; este guia promove esse mesmo mecanismo a controle real).
- Código, comentário XML, log, mensagem de exceção, API pública do núcleo: inglês, sempre. D-02
  não muda (ADR-0011).

## 2. Decisão deste guia: onde os dois controles entram

ADR-0011 deixou em aberto "topbar ou diálogo About, a decidir na tarefa de implementação". Decisão:
**os dois ficam na topbar**, não no diálogo "About".

Motivo: são preferências de uso constante (alguém que prefere inglês ou tema claro quer isso
sempre visível, não atrás de um clique em "About"), e a topbar já reserva esse espaço. O mockup de
D-33 (`docs/design/flow-workspace/mockup.html`) já colocou um botão de tema ali, ao lado do chip de
conexão, como rascunho ("mockup only"). Este guia confirma esse lugar como definitivo e acrescenta
o seletor de idioma do lado dele.

Ordem final em `.app-topbar__right` (da esquerda para a direita, guide `flow-workspace` seção 6.1):

```text
[Try an endpoint, < 1600px] [N in progress] [slow threshold] [seletor de idioma] [alternador de tema] [chip de conexão]
```

O chip de conexão continua por último (é o item mais "vivo", olho treinado já procura ele no canto
direito). Os dois controles novos ficam entre as métricas e o chip, nessa ordem (idioma antes de
tema): não há um motivo forte para a ordem inversa, e agrupar os dois lado a lado antes do chip
mantém a leitura "dados → preferências → conexão".

Abaixo de 1024 px (rail vira overlay, guide `flow-workspace` seção 5.2), os dois controles
continuam na topbar, não migram para dentro do rail: o rail é navegação, não preferência.

Abaixo de 768 px, a topbar já esconde subtítulo e metadados de texto (seção 5.2 do guia
`flow-workspace`); os dois controles continuam visíveis, só os botões "Try an endpoint"/metadados
de texto somem. São ícones compactos (36 px), cabem.

## 3. Alternador de tema

Reaproveita o botão já desenhado no mockup de D-33 (`.icon-btn`, 36 × 36, `--naf-surface`,
`--naf-border`), só troca o rótulo e o ícone conforme o tema ativo:

- Tema escuro ativo: ícone de sol (`u-sun`, já existe no sprite do mockup), `aria-label="Switch to
  light theme"` (en) / `aria-label="Mudar para o tema claro"` (pt-BR). Clicar aplica
  `data-theme="light"`.
- Tema claro ativo: ícone de lua (novo, `u-moon`, mesmo estilo dos outros ícones do sprite: traço
  1.75, 24 × 24, ver SVG abaixo), `aria-label="Switch to dark theme"` / `"Mudar para o tema
  escuro"`. Clicar aplica `data-theme="dark"`.

```xml
<symbol id="u-moon" viewBox="0 0 24 24">
  <path d="M20 14.2A8.5 8.5 0 1 1 9.8 4a7 7 0 0 0 10.2 10.2Z"/>
</symbol>
```

Sem texto ao lado do ícone (o rótulo vive só no `aria-label`/`title`), igual aos outros ícones
isolados da topbar. `title` espelha o `aria-label` (tooltip nativo do navegador, mesmo padrão já
usado em `app-rail__item` para `title="Flow"` etc., ver `app.html` atual).

## 4. Seletor de idioma

**Decidido pela Planejadora**: manter o padrão abaixo (grupo segmentado, mesmo visual do seletor
de velocidade do replay), não copiar o estilo do site de marketing (`netapiflow-site`), que usa
pílula com fundo neutro, sem destaque de cor. São telas diferentes propositalmente: o site é
página de marketing, o dashboard é ferramenta densa com seu próprio vocabulário visual já
estabelecido (o mesmo grupo segmentado do seletor de velocidade, seção 6.5). Consistência interna
do dashboard pesa mais que igualar as duas superfícies do produto:

- Grupo segmentado de dois botões, altura 36 (mesma altura do `icon-btn` ao lado), raio 8, borda
  `--naf-border`, divisória vertical `--naf-border` entre os dois.
- Texto do botão: código do idioma em letras maiúsculas, mono 12 px, `letter-spacing: .02em`:
  `PT` e `EN`. Não usa bandeira (bandeira associa idioma a país, PT-BR é usado fora do Brasil
  também; o mesmo raciocínio do guia de marca para não usar emoji como ícone de estado).
- Ativo: fundo `--naf-flow-tint`, texto `--naf-flow-text`. Inativo: texto `--naf-text-muted`,
  fundo nenhum. Hover no inativo: `--naf-surface-2`.
- `role="radiogroup"` no grupo, `aria-label="Language"` (en) / `"Idioma"` (pt-BR, o próprio
  `aria-label` deste controle também segue o idioma ativo, não fica travado em inglês). Cada botão
  `role="radio"`, `aria-checked`, `lang="pt-BR"`/`lang="en"` no botão correspondente (marca o
  idioma do próprio rótulo do botão para leitor de tela, inclusive quando o idioma ativo é o
  outro).

```text
┌──────┬──────┐
│  PT  │  EN  │   ← PT ativo: fundo flow-tint, texto flow-text em "PT"
└──────┴──────┘
```

## 5. Mecânica (visão de implementação, sem prescrever código)

- Um `LanguageService` (nome ilustrativo) expõe um `signal<'pt-BR' | 'en'>`, inicializado de
  `localStorage` e, sem preferência salva, `'pt-BR'` (D-35, nunca `navigator.language`).
- Um `ThemeService` (idem) expõe um `signal<'dark' | 'light' | null>` (`null` significa seguir
  `prefers-color-scheme`), grava no mesmo `localStorage`, e escreve `data-theme` na raiz via
  `effect()`. Mesmo padrão já usado em `app.ts` para o `sidenavOpened`: `effect()` reagindo a um
  signal e aplicando efeito colateral de DOM.
- O dicionário (seção 6) é um objeto por idioma; cada componente lê a chave correspondente do
  idioma ativo. Implementação exata (pipe, função, token de injeção) é decisão da tarefa de
  implementação, não deste guia. Este guia fixa o **conteúdo** do dicionário, não o mecanismo de
  leitura dele.
- Nenhuma rota, nenhum parâmetro de URL, nenhum cookie: a preferência é só client-side, só
  `localStorage` (ADR-0011 já deixa isso explícito, reforçado aqui porque afeta o design: trocar
  idioma não muda a URL, `?trace=<id>` continua intacto).

## 6. Dicionário completo: `en` (entrada) → `pt-BR` (tradução)

Toda string citada entre aspas no guia `flow-workspace` (D-33) e toda string literal encontrada na
implementação atual do dashboard (`src/NetApiFlow.Dashboard/ClientApp/src/app/**`) até a data
deste guia. Tradução completa, não resumida (mesmo critério de `README.pt-BR.md`, D-02). `{chave}`
marca um trecho interpolado (número, nome de componente, código HTTP etc.), que não se traduz.

Dado vindo direto do backend sem rótulo de UI ao redor (rota, trace id, nome de componente,
operação, status HTTP, timestamp, duração formatada) **não entra neste dicionário**: é dado, não
texto de interface, continua idêntico nos dois idiomas. Nome de evento SSE (`TraceStarted`,
`SpanCompleted` etc.) também não entra: é vocabulário do protocolo (`docs/plano/contratos.md`),
não texto de tela.

### 6.1 Shell (rail, topbar)

| Chave | en | pt-BR |
|---|---|---|
| nav.flow | Flow | Fluxo |
| nav.history | History | Histórico |
| nav.components | Components | Componentes |
| nav.runner | Runner | Executor |
| nav.about | About | Sobre |
| topbar.subtitle.flow | Live request paths | Caminhos de requisição ao vivo |
| topbar.tryEndpoint | Try an endpoint | Testar um endpoint |
| topbar.inProgress | {count} in progress | {count} em andamento |
| topbar.slowThreshold | slow threshold {value} | limiar de lentidão {value} |
| topbar.hamburger.open | Open navigation | Abrir navegação |
| topbar.hamburger.close | Close navigation | Fechar navegação |
| rail.close | Close navigation | Fechar navegação |
| rail.logoAria | NetApiFlow, version {version} | NetApiFlow, versão {version} |
| chip.connection.live | Live | Ao vivo |
| chip.connection.reconnecting | Reconnecting | Reconectando |
| chip.connection.offline | Offline | Offline |
| theme.toggle.toLight | Switch to light theme | Mudar para o tema claro |
| theme.toggle.toDark | Switch to dark theme | Mudar para o tema escuro |
| language.selectorAria | Language | Idioma |

`Offline` fica igual nos dois idiomas (empréstimo já consagrado em pt-BR técnico, mesmo critério
usado para não forçar tradução de termos técnicos sem uso real em português, ver D-02 sobre texto
técnico vs. texto de produto). Se a Planejadora revisar e discordar, troca por "Desconectado" na
implementação.

### 6.2 Cartões de métrica

| Chave | en | pt-BR |
|---|---|---|
| metrics.traces | Traces | Rastreamentos |
| metrics.successful | Successful | Bem-sucedidas |
| metrics.errors | Errors | Erros |
| metrics.avgLatency | Avg latency | Latência média |
| metrics.maxLatency | Max latency | Latência máxima |
| metrics.stepsExecuted | Steps executed | Etapas executadas |
| metrics.loading | Loading… | Carregando… |
| metrics.error | Failed to load metrics. | Falha ao carregar as métricas. |

### 6.3 Painel de ação ("Try an endpoint")

| Chave | en | pt-BR |
|---|---|---|
| action.title | Try an endpoint | Testar um endpoint |
| action.intro | Sends a real request to your API. The graph follows it live. | Envia uma requisição real para a sua API. O grafo acompanha em tempo real. |
| action.tab.custom | Custom | Personalizado |
| action.tab.scenarios | Scenarios | Cenários |
| action.filterPlaceholder | Filter by route | Filtrar por rota |
| action.filterAria | Filter endpoints | Filtrar endpoints |
| action.hiddenRoutesNote.one | {count} dashboard route ({prefix}) hidden | {count} rota do dashboard ({prefix}) oculta |
| action.hiddenRoutesNote.many | {count} dashboard routes ({prefix}) hidden | {count} rotas do dashboard ({prefix}) ocultas |
| action.field.kind.path | path | caminho |
| action.field.kind.query | query | consulta |
| action.field.kind.body | body | corpo |
| action.field.typeSeparator | {kind} · {type} | {kind} · {type} |
| action.send | Send request | Enviar requisição |
| action.loading | Loading… | Carregando… |
| action.openApiError.title | Could not load the OpenAPI document. | Não foi possível carregar o documento OpenAPI. |
| action.openApiError.hint | Check that the host calls MapOpenApi(). | Verifique se o host chama MapOpenApi(). |
| action.noEndpoints | No endpoints found. | Nenhum endpoint encontrado. |
| action.invalidJson | Body is not valid JSON. | O corpo não é um JSON válido. |
| action.requestFailed | Request failed. | A requisição falhou. |
| action.traceConfirmation | Trace: {traceId} | Trace: {traceId} |

`action.hiddenRoutesNote.*`: o inglês de hoje já lida mal com plural (`"{count} dashboard routes
hidden"` fica gramaticalmente estranho quando `{count}` é 1, "1 dashboard routes hidden"). Esta é
a oportunidade de corrigir os dois idiomas ao mesmo tempo: `.one` quando `{count} === 1`, `.many`
nos demais casos (inclusive zero, que na prática não ocorre porque a nota só aparece quando há
pelo menos uma rota oculta). `action.field.typeSeparator` troca o `kind` cru (`path`/`query`/`body`)
pela chave traduzida de `action.field.kind.*`; `{type}` (`int`, `application/json`) continua sem
tradução, é tipo/media type técnico, não rótulo de produto. `MapOpenApi()` dentro de
`action.openApiError.hint` não traduz (é nome de método real do código do host, citação técnica,
mesmo critério do D-02 para trechos de código dentro de texto de produto).

### 6.4 Card central: fluxo da requisição

| Chave | en | pt-BR |
|---|---|---|
| graph.title | Request path | Caminho da requisição |
| graph.backToLive | Back to live | Voltar para o ao vivo |
| graph.copyTraceId | Copy trace id | Copiar id do trace |
| graph.errorOrigin | Error origin: {component} · HTTP {status} · {duration} | Origem do erro: {component} · HTTP {status} · {duration} |
| graph.slowestStep | Slowest step: {component} · {duration} | Etapa mais lenta: {component} · {duration} |
| graph.empty.title | No request is currently in progress. | Nenhuma requisição em andamento no momento. |
| graph.empty.hint | Send a request from "Try an endpoint" or call your API directly. | Envie uma requisição por "Testar um endpoint" ou chame sua API diretamente. |
| graph.loading | Loading… | Carregando… |
| node.state.waiting | Waiting | Aguardando |
| node.state.running | Running | Em execução |
| node.state.completed | Completed | Concluído |
| node.state.slow | Slow | Lento |
| node.state.error | Error | Erro |
| node.state.notExecuted | Not executed | Não executado |
| node.ariaLabel | {component}, {operation}, {state}{, duration} | {component}, {operation}, {state}{, duration} |

`node.ariaLabel`: só o `{state}` interpolado muda (vem de `node.state.*` acima); `{component}` e
`{operation}` são dado, ficam como estão nos dois idiomas. A vírgula e o formato da frase também
não mudam de posição entre os dois idiomas neste caso (ambos leem bem como lista separada por
vírgula); se a Executora achar a ordem estranha em pt-BR ao implementar, reportar à Planejadora
antes de reordenar (afeta teste de acessibilidade existente).

Correção de pequena inconsistência encontrada na implementação atual: o aviso do estado vazio do
grafo cita `"Try an endpoint"` entre aspas, mas o aviso equivalente de eventos ao vivo (seção 6.8)
cita o mesmo nome sem aspas. Este guia padroniza **com aspas nos dois idiomas** (`graph.empty.hint`
e `events.empty` abaixo), porque é o nome literal de um botão da tela, não uma referência solta.
É um ajuste pontual de cópia, não precisa de uma task própria.

### 6.5 Barra de replay

| Chave | en | pt-BR |
|---|---|---|
| replay.chip.replay | REPLAY | REPLAY |
| replay.restart | Restart | Reiniciar |
| replay.previousStep | Previous step | Etapa anterior |
| replay.play | Play | Reproduzir |
| replay.pause | Pause | Pausar |
| replay.nextStep | Next step | Próxima etapa |
| replay.positionAria | Replay position | Posição da reprodução |
| replay.speedAria | Playback speed | Velocidade de reprodução |
| replay.status | Step {step}/{total} · finished with HTTP {status} · replaying recorded timestamps at {speed}× | Etapa {step}/{total} · concluída com HTTP {status} · reproduzindo marcações de tempo originais a {speed}× |

**Decidido pela Planejadora**: `REPLAY` não traduz, mesmo critério de `Offline` em 6.1 (termo
técnico já consagrado em português, tradução soaria estranha).

### 6.6 Painel de detalhes

Sem nó selecionado (resumo do trace):

| Chave | en | pt-BR |
|---|---|---|
| details.kpi.total | Total | Total |
| details.kpi.executed | Executed | Executadas |
| details.kpi.notExecuted | Not executed | Não executadas |
| details.error.failedAt | Failed at {component}.{operation} | Falhou em {component}.{operation} |
| details.summary.traceId | Trace ID | ID do trace |
| details.summary.started | Started | Iniciado |
| details.summary.finished | Finished | Concluído |
| details.httpStatus | HTTP {status} | HTTP {status} |
| details.steps.sectionLabel | Steps | Etapas |
| details.steps.sectionHint | select one to inspect | selecione uma para inspecionar |
| details.steps.skipped | skipped | ignorada |

Com nó selecionado (detalhe do span, substitui o bloco acima no mesmo card):

| Chave | en | pt-BR |
|---|---|---|
| details.span.id | Span ID | ID do span |
| details.span.parentId | Parent span ID | ID do span pai |
| details.span.started | Started | Iniciado |
| details.span.ended | Ended | Encerrado |
| details.span.duration | Duration | Duração |
| details.span.status | Status | Status |
| details.span.medianDuration | Median duration (same operation) | Duração mediana (mesma operação) |
| details.span.deltaFromMedian | Delta from median | Diferença em relação à mediana |
| details.span.ancestors | Ancestors | Ancestrais |
| details.span.children | Children | Filhos |
| details.span.failedLabel | Failed | Falhou |
| details.backToSummary | Back to trace summary | Voltar ao resumo do trace |

`details.steps.sectionLabel`/`details.steps.sectionHint` formam uma frase só na tela
(`STEPS · select one to inspect`, maiúsculas por CSS/`text-transform`, não por estarem maiúsculas
na string-fonte, por isso aqui aparecem em caixa normal). A transformação visual continua sendo
responsabilidade do CSS, igual já é hoje. `details.span.status` é o rótulo "Status" acima do
valor; o **valor** em si (`Running`/`Success`/`Error`) é o mesmo caso de `trace.status` discutido
na seção 6.11: o rótulo traduz, o valor segue a decisão tomada lá (nota final da seção 6.11).

### 6.7 Execuções recentes

| Chave | en | pt-BR |
|---|---|---|
| recent.title | Recent executions | Execuções recentes |
| recent.viewAll | View all | Ver todas |
| recent.loading | Loading… | Carregando… |
| recent.error | Failed to load recent executions. | Falha ao carregar as execuções recentes. |
| recent.empty | No traces recorded yet. | Nenhum trace registrado ainda. |
| recent.expandAria | Expand recent executions | Expandir execuções recentes |
| recent.collapseAria | Collapse recent executions | Recolher execuções recentes |
| recent.relativeTime.now | now | agora |
| recent.relativeTime.seconds | {n} s | {n} s |
| recent.relativeTime.minutes | {n} min | {n} min |

`recent.relativeTime.*` não muda de forma entre os dois idiomas (`"12 s"`/`"3 min"` já são curtos e
compreensíveis em português técnico, mesmo critério de não forçar tradução de unidade já
consagrada, como `ms` em `duration-format.ts`).

### 6.8 Eventos ao vivo

| Chave | en | pt-BR |
|---|---|---|
| events.title | Live events | Eventos ao vivo |
| events.chip.connected | Connected | Conectado |
| events.chip.reconnecting | Reconnecting | Reconectando |
| events.chip.offline | Offline | Offline |
| events.expandAria | Expand live events | Expandir eventos ao vivo |
| events.collapseAria | Collapse live events | Recolher eventos ao vivo |
| events.empty | Waiting for events. Send a request from "Try an endpoint" or call your API directly. | Aguardando eventos. Envie uma requisição por "Testar um endpoint" ou chame sua API diretamente. |

`events.chip.*` usa um vocabulário diferente do chip de conexão da topbar (seção 6.1:
`"Live"`/`"Reconnecting"`/`"Offline"`) para o mesmo estado de SSE. Isso já existe assim na
implementação atual (não é bug introduzido por este guia) e continua intencional: a topbar fala do
produto como um todo ("a experiência está ao vivo"), o card de eventos fala da própria conexão SSE
("esta lista está conectada ao servidor"). Os dois dicionários ficam separados de propósito, cada
um traduzido por conta própria.

### 6.9 Diálogo "About"

| Chave | en | pt-BR |
|---|---|---|
| about.title | About NetApiFlow | Sobre o NetApiFlow |
| about.version | Version | Versão |
| about.license | License | Licença |
| about.repository | Repository | Repositório |
| about.credit | Inspired by the idea behind apiflow (Spring Boot): visualizing a request's path through an API in real time. No code, file or asset from that project was copied or ported. | Inspirado na ideia por trás do apiflow (Spring Boot): visualizar em tempo real o caminho de uma requisição dentro de uma API. Nenhum código, arquivo ou recurso daquele projeto foi copiado ou portado. |
| about.close | Close | Fechar |

`apiflow` e `Spring Boot` não traduzem (nome próprio de projeto e de framework). O link de
`about.repository` continua mostrando a URL crua nos dois idiomas (é um dado, a própria URL, não
um rótulo).

### 6.10 Componentes (`/components`)

| Chave | en | pt-BR |
|---|---|---|
| components.loading | Loading… | Carregando… |
| components.error | Failed to load component metrics. | Falha ao carregar as métricas de componentes. |
| components.empty | No component metrics recorded yet. | Nenhuma métrica de componente registrada ainda. |
| components.table.component | Component | Componente |
| components.table.operation | Operation | Operação |
| components.table.executions | Executions | Execuções |
| components.table.errors | Errors | Erros |
| components.table.median | Median | Mediana |
| components.table.average | Average | Média |
| components.table.p95 | p95 | p95 |
| components.table.max | Max | Máx. |

`p95` não traduz (termo estatístico padrão, usado como está em português técnico também).

### 6.11 Histórico (`/history`)

| Chave | en | pt-BR |
|---|---|---|
| history.filtersAria | Filters | Filtros |
| history.filter.status | Status | Status |
| history.filter.method | Method | Método |
| history.filter.route | Route | Rota |
| history.filter.tenant | Tenant | Tenant |
| history.filter.from | From | De |
| history.filter.to | To | Até |
| history.filter.minDuration | Min duration (µs) | Duração mínima (µs) |
| history.filter.status.any | Any | Qualquer |
| history.filter.status.running | Running | Em execução |
| history.filter.status.success | Success | Sucesso |
| history.filter.status.error | Error | Erro |
| history.loading | Loading… | Carregando… |
| history.error | Failed to load traces. | Falha ao carregar os traces. |
| history.empty | No traces match these filters. | Nenhum trace corresponde a estes filtros. |
| history.table.traceId | Trace id | Id do trace |
| history.table.method | Method | Método |
| history.table.route | Route | Rota |
| history.table.status | Status | Status |
| history.table.httpStatus | HTTP status | Status HTTP |
| history.table.startedAt | Started at | Iniciado em |
| history.table.duration | Duration | Duração |
| history.paginationAria | Pagination | Paginação |
| history.pagination.total | {n} total | {n} no total |
| history.pagination.previous | Previous | Anterior |
| history.pagination.next | Next | Próxima |
| history.pagination.page | Page {n} | Página {n} |

`history.filter.status`, o rótulo acima do select, é diferente de `history.filter.status.*`, os
quatro valores dentro dele (`Any`/`Running`/`Success`/`Error`). **Esses mesmos três últimos
valores (`Running`, `Success`, `Error`) são também o texto literal do contrato `FlowStatus`**
(`docs/plano/contratos.md`), exibido cru em pelo menos três lugares: a coluna "Status" desta
tabela, o rótulo de estado dentro do nó do grafo (seção 6.4, que já usa seu próprio vocabulário
visual, "Waiting"/"Running"/"Completed"/"Slow"/"Error"/"Not executed", não o enum cru) e o campo
`details.span.status` (seção 6.6).

**Decisão deste guia**: o enum `FlowStatus` em si (`Running`/`Success`/`Error`, o valor que viaja
no contrato) nunca muda de grafia, nos dois idiomas. É dado, D-02 cobre isso. Mas **o texto que a
tela mostra para representar esse valor pode e deve traduzir**, porque é rótulo de UI, não o
contrato em si. Por isso `history.filter.status.*` existe como chave de tradução separada do valor
cru: a tabela de histórico (`history.table.status`, coluna) continua mostrando o `trace.status` cru
hoje, é assim que já funciona. Se a Executora decidir, nesta mesma tarefa, passar a exibir
`history.filter.status.*` também na célula da tabela em vez do enum cru, isso é uma melhoria
válida, mas **fora do escopo de D-35/D-36**: seria uma mudança de comportamento da tabela de
histórico, não só de idioma. Reportar à Planejadora antes de fazer, não decidir sozinha dentro
desta tarefa de i18n.

### 6.12 Runner (`/runner`)

Reaproveita o mesmo `endpoint-form` do painel de ação (seção 6.3): mesmas chaves, sem chave nova.

---

## 7. Acessibilidade

- Atributo `lang` no `<html>` troca junto com o idioma ativo (`lang="pt-BR"` ou `lang="en"`), para
  leitor de tela aplicar a pronúncia certa. Hoje o `index.html` gerado pelo Angular fixa
  `lang="en"`; a tarefa de implementação precisa tornar isso dinâmico.
- `aria-label`/`title` de todo controle (inclusive os dois novos, seção 3 e 4) seguem o idioma
  ativo, nunca ficam presos em inglês depois de trocar para português.
- Troca de tema respeita `prefers-reduced-motion` (nenhuma transição de cor deve ser animação
  chamativa; um `transition: background-color .15s, color .15s` discreto é aceitável, igual ao que
  o guia `flow-workspace` já permite para hover, seção 7).
- Contraste: os pares de cor não mudam entre os dois temas além do que `docs/brand/tokens.md` já
  define (claro e escuro já passam AA ou AAA, seção 3 do guia `flow-workspace`). Trocar de tema não
  é trocar para uma paleta nova, é a mesma paleta Stage Light que já existe.

---

## 8. Escopo, já com as três pendências decididas

**Entra nesta fase (quando o usuário abrir):** os dois controles (seção 2-4), o dicionário
completo (seção 6), `lang` dinâmico (seção 7).

As três pendências de confirmação visual foram decididas pela Planejadora, depois de rodar o
mockup de verdade (Playwright, interação real com os dois controles):

1. **Seletor de idioma** (seção 4): mantém o grupo segmentado já especificado, não copia o estilo
   do site de marketing. Resolvido depois de comparar os dois de verdade.
2. **Rótulo do replay** (seção 6.5): `REPLAY` não traduz.
3. **Ordem idioma-antes-de-tema na topbar** (seção 2): confirmada como está.

Achado durante a verificação, corrigir na implementação real: no mockup, `recent.relativeTime.now`
("now"/"agora") não troca de idioma na tabela de execuções recentes, apesar de o dicionário
(seção 6.7) já prever a tradução. Falha só do mockup de referência, não da especificação; a
implementação real precisa aplicar o dicionário nesse campo também.

**Não é deste guia, seria roadmap futuro se pedido:** detectar idioma por `Accept-Language`,
quarto idioma além de pt-BR/en, tradução de conteúdo gerado dinamicamente pelo usuário (corpo de
request, mensagem de exceção do host), persistência de preferência no backend (hoje é só
`localStorage`, por usuário/navegador).

---

## 9. Critérios de aceite visual

Abrir `mockup.html` e o dashboard real lado a lado, nas quatro combinações (claro+inglês,
escuro+inglês, claro+português, escuro+português):

- [ ] Nenhuma string em inglês sobra na tela com português ativo, fora do que a seção 6 marca
  como dado (rota, nome de componente, código HTTP, nome de evento SSE).
- [ ] Nenhuma string maior em português quebra layout (ex.: "Caminho da requisição" vs. "Request
  path"): widths com `min-width:0`/ellipsis já existentes no guia `flow-workspace` absorvem a
  diferença; se algum rótulo estourar, a Executora reporta à Planejadora, não encurta a tradução
  por conta própria.
- [ ] Trocar o tema não recarrega a página, não perde o trace aberto (`?trace=<id>` intacto),
  não perde o idioma ativo.
- [ ] Trocar o idioma não recarrega a página, não perde o trace aberto, não perde o tema ativo.
- [ ] Preferência de tema e de idioma sobrevivem a um recarregamento de página
  (`localStorage`).
- [ ] Sem preferência salva (storage limpo): tema segue `prefers-color-scheme`, idioma é pt-BR
  (nunca o idioma do navegador).
- [ ] `lang` do documento muda junto com o idioma ativo.
- [ ] Os dois controles novos têm alvo de toque ≥ 36 px (desktop) / ≥ 44 px (abaixo de 768 px,
  mesma regra do guia `flow-workspace` seção 7).
- [ ] `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` verdes.
