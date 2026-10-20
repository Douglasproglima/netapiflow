# Fase 7: Documentação e release

Pré-requisito: Fases 0 a 6 aceitas. Esta é a última fase antes da primeira publicação pública.

## Tarefas

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
