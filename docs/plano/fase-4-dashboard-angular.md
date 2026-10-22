# Fase 4: Dashboard Angular

Projeto: `src/NetApiFlow.Dashboard/ClientApp`. Testes: Vitest, ao lado de cada arquivo (`*.spec.ts`).

Pré-requisito: Fase 3 aceita, com `docs/plano/contratos.md` seção 3 congelada (tarefa 3.1). Regras
desta fase: `.claude/rules/frontend.md`.

## Tarefas

### 4.1: Workspace, shell e roteamento (D-23)

Angular 22, standalone, **zoneless** (`provideZonelessChangeDetection()`), Signals. Vitest como
test runner (não Karma/Jasmine). ESLint + Prettier. `.nvmrc` fixando a versão do Node usada no CI
(a confirmada localmente é compatível com Node 26, mas fixar a versão exata escolhida pela
Executora).

Shell e rotas fazem parte desta tarefa (achado na revisão: faltava no plano original, mas as
tarefas 4.12 e 4.13 pressupõem que já existe). Uma rota por feature, espelhando a estrutura de
pastas da seção 7.4 do prompt: `/flow` (padrão, tela principal, aceita `?trace=<traceId>` como
**query string**, não segmento de rota: é assim que a seção 7.3 do prompt descreve, e é o que a
tarefa 4.13 testa), `/history`, `/components`, `/runner`. Navegação lateral ou superior ligando
as quatro. `PathLocationStrategy` padrão do Angular Router. O fallback de SPA que serve
`index.html` para qualquer rota não resolvida é responsabilidade do middleware da Fase 5 (tarefa
5.3), não desta tarefa.

**Pronto quando:** `npm run lint`, `npm run test:ci` e `npm run build` verdes, com as quatro
rotas navegáveis e um componente placeholder em cada uma, só para provar que o roteamento e o
build (`dist/`) funcionam.

### 4.2: `core/`

Modelos TypeScript espelhando os DTOs de `docs/plano/contratos.md` (seção 3), `ApiService` (HTTP
sobre `/flow/api`), `SseService` (`EventSource` sobre `/flow/api/stream`, com parsing do formato
da seção 2 de `contratos.md`), resolução do base path só por **URL relativa** (sem hardcode de
`/flow`, porque o dashboard pode estar montado em qualquer caminho, ver risco R-03).

**Pronto quando:** spec de cada serviço, usando `HttpTestingController` para `ApiService` e um
`EventSource` fake para `SseService`.

### 4.3: `graph-builder` (peça mais crítica do frontend)

Função pura em `features/flow/graph/`: `(spans: FlowSpan[]) => { nodes: GraphNode[], edges:
GraphEdge[] }`, usando `@dagrejs/dagre` só para o cálculo de layout (posição x/y), nunca para
renderizar. Sem dependência de Angular dentro da função (testável sem `TestBed`).

**Pronto quando:** cobertura alta (D-10, meta 80% de linha só para este módulo) cobrindo: layout
com hierarquia de 3+ níveis, os seis estados visuais da seção 7.2 do prompt (Aguardando, Em
execução, Concluído, Lento, Erro, Não executado), e o caso de etapa "não executada" inferida do
blueprint.

### 4.4: Grafo SVG

Componente que renderiza a saída do `graph-builder` em SVG próprio (sem biblioteca de grafo
pronta: a biblioteca é só para layout, não para render, seção 4.2 do prompt). Zoom e pan com
`d3-zoom`.

**Pronto quando:** spec de componente renderiza um trace de exemplo e confere que cada nó e aresta
aparece com o número de ordem de início correto.

### 4.5: Estados visuais acessíveis

Cada um dos seis estados tem cor **e** ícone **e** texto. Cor nunca é o único sinal (seção 7.2 do
prompt).

**Pronto quando:** teste prova que, com as cores removidas (ex.: checar só os atributos de texto
e `aria-label`/ícone), ainda é possível diferenciar os seis estados.

### 4.6: Painel de detalhes e waterfall

Sem nó selecionado: resumo da requisição + waterfall (barras proporcionais à duração de cada
span, ordenadas por início). Com nó selecionado: componente, operação, ids, horários, duração,
status, erro, ancestrais, filhos, diferença para a mediana normal daquela operação
(`/flow/api/traces/{traceId}/spans/{spanId}`, seção 8 do prompt).

**Pronto quando:** spec cobre as duas situações (nada selecionado / nó selecionado) com dado de
exemplo.

### 4.7: Player live

Consome `SseService`, aplica só eventos do trace atualmente acompanhado, deduplicação por
`eventId`. Reconexão com `Last-Event-ID` e backoff exponencial; a cada reconexão, recarrega o
estado consolidado do trace via `GET /traces/{traceId}` antes de voltar a aplicar eventos ao vivo.

**Pronto quando:** spec simula eventos fora de ordem e duplicados, confere que o estado final é
idempotente; spec simula desconexão e reconexão, confere o reload do estado consolidado.

### 4.8: Player de replay

Timeline reconstruída dos timestamps gravados: play, pause, reiniciar, passo a passo,
velocidades de 0,25× a 4×, intervalo mínimo visual entre passos para etapas de microssegundos
serem perceptíveis. **Só a animação é cadenciada**: a duração exibida em qualquer painel é
sempre a medida de verdade, nunca ajustada pela velocidade de reprodução.

**Pronto quando:** spec prova que a duração exibida não muda conforme a velocidade de reprodução
selecionada; spec prova que passo a passo preserva a ordem de início dos spans.

### 4.9: Histórico

Lista paginada de `GET /traces` com os filtros da seção 7.1 item 3: status, método, rota, tenant,
período, duração mínima.

**Pronto quando:** spec cobre paginação e cada filtro isoladamente e combinado.

### 4.10: Componentes

Tela com execuções, erros, mediana, média, p95 e máximo por operação, de `/flow/api/metrics/components`.

**Pronto quando:** spec confere que os números exibidos batem com o fixture de resposta da API.

### 4.11: Runner via OpenAPI

Lê `/openapi/v1.json` (ou caminho configurável) da própria API hospedeira, lista endpoints,
monta formulário de path params, query e corpo JSON, dispara a requisição via `ApiService` e
acompanha o fluxo resultante ao vivo (reaproveitando o player da tarefa 4.7).

**Pronto quando:** spec com um documento OpenAPI de exemplo gera o formulário esperado para um
endpoint com path param e corpo.

### 4.12: Tema

Angular Material, tema claro e escuro respeitando `prefers-color-scheme`, responsivo em tela
estreita.

**Pronto quando:** spec ou teste visual (conforme ferramenta disponível no projeto) confere que
os dois temas renderizam sem quebra de layout, e que o grafo e as tabelas não vazam horizontalmente
em largura de tela estreita.

### 4.13: `?trace=<traceId>` na URL

Query string da rota `/flow` (não segmento de rota, ver tarefa 4.1). Abre diretamente a
execução referenciada, sem precisar navegar pelo histórico.

**Pronto quando:** teste de rota confere que a URL com o parâmetro carrega o trace certo ao
montar o componente.

### 4.14: `GET /flow/api/config` e consumo real do `SlowThreshold` (D-24)

Nova rota no backend (`src/NetApiFlow.Dashboard`), mesma regra de autorização das outras
(ADR-0006). Frontend (`flow-page.component.ts`) troca a constante `SLOW_THRESHOLD_MICROS`
hardcoded pelo valor real, buscado uma vez via `ApiService`.

**Pronto quando:** teste de integração do backend confere o valor devolvido bate com
`NetApiFlowOptions.SlowThreshold` configurado no host; spec do frontend confere que o estado
"lento" usa o valor vindo da API, não mais a constante.

## Aceite da fase

Critério da seção 13, Fase 4: `graph-builder` com testes cobrindo layout, os seis estados e etapa
não executada; sample (Fase 2) mostra o grafo acendendo ao vivo; replay respeita ordem e
velocidade; dashboard funciona em tema claro e escuro e em tela estreita.

## Ao terminar

Resumo para a Planejadora, com prints ou descrição do que foi visualmente verificado (tema claro
e escuro, tela estreita), já que parte do critério de aceite não é só automatizável por `npm run
test:ci`. `/revisar-fase 4` roda o sample da Fase 2 com o dashboard montado e observa o grafo ao
vivo antes de liberar a Fase 5.
