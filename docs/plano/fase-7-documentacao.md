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

### 7.7: `.github/workflows/release.yml`

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
