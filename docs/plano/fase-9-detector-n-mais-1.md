# Fase 9: Detector de N+1

Projeto: `src/NetApiFlow.Dashboard/ClientApp` (dashboard Angular). Nenhuma mudança no núcleo .NET,
nenhum contrato novo: todo dado necessário (`component`, `operation`, `type`, `parentSpanId`) já
trafega na API REST e no SSE, documentado em `docs/plano/contratos.md`.

Pré-requisito: Fase 4 aceita (`graph-builder`, `flow-graph.component`). Segundo item do roadmap
D-58 a entrar em implementação (D-71), liberado por D-70 sem esperar a Fase 8 estar "em produção"
de verdade, já que o produto ainda não foi divulgado.

## Contexto da detecção

N+1 clássico: um laço chama um serviço N vezes, e cada chamada dispara 1 consulta SQL do mesmo
formato (`"SELECT Orders"`, por exemplo). As N consultas **não são filhas umas das outras nem
necessariamente do mesmo pai direto**: cada uma é filha de uma instância diferente do passo que
está em loop. Por isso o agrupamento certo é por `(Component, Operation)` em todo o trace, não
por irmãos do mesmo pai. `FlowDbCommandInterceptor.DescribeCommand` já produz o mesmo
`Operation` (`"{CommandType} {MainTable}"`) para comandos com o mesmo formato e parâmetros
diferentes, então o sinal já está lá, sem precisar mudar nada no núcleo.

## Tarefas

### 9.1: Agrupamento em `graph-builder.ts`

Nova função pura (ou extensão de `buildGraph`): entre os nós reais (não-ghost, `status !== null`)
com `type === 'Database'`, agrupar por `` `${component}::${operation}` ``. Grupo com tamanho maior
ou igual a um limiar fixo (`N_PLUS_ONE_THRESHOLD = 3`, constante no próprio arquivo, sem config
nova em `NetApiFlowOptions` nesta fase) marca cada nó do grupo com a contagem total do grupo.

`GraphNode` ganha o campo `nPlusOneCount: number | null` (`null` = não marcado).

**Pronto quando:** teste unitário (sem `TestBed`, função pura) com um trace de 3+ spans
`Database` do mesmo `Component`/`Operation`, espalhados sob pais diferentes, confirma que todos
os membros do grupo recebem o mesmo `nPlusOneCount`. Teste negativo: 2 spans iguais (abaixo do
limiar) não são marcados. Teste negativo: spans `Database` com `Operation` diferente nunca
entram no mesmo grupo. Nó `ghost` (`status === null`) nunca conta para o agrupamento nem é
marcado.

### 9.2: Selo visual no grafo

`flow-graph.component.ts`/`.html` ganham um selo no card do nó quando `nPlusOneCount` não é
`null` (ex.: ícone de aviso mais texto `"×12"`, nunca só cor, igual à regra de estado visual já
usada para `slow`/`error`). Posição e estilo seguem o guia de workspace existente
(`docs/design/flow-workspace/guide.md`), não um desenho novo.

**Pronto quando:** teste do componente confirma que o selo aparece só quando `nPlusOneCount` não
é `null`, e que o texto do selo muda com a contagem real.

### 9.3: Painel de detalhes

Com um nó marcado selecionado, o painel de detalhes (`details/`, tarefa 4.6 original) mostra uma
linha curta citando quantos irmãos de mesmo formato existem no trace, com um jeito de pular para
o primeiro deles (não precisa listar os N, só apontar que existem e quantos).

**Pronto quando:** teste do componente confirma o texto e a navegação para o primeiro membro do
grupo.

### 9.4: Documentação

README e README.pt-BR.md ganham uma linha breve sobre o detector de N+1, na lista de
funcionalidades (não na seção de configuração, porque não há configuração nova nesta fase).

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente.

## Aceite da fase

Todos os critérios de pronto acima. `npm run lint`, `format:check`, `test:ci` e `build` limpos.
Nenhuma mudança em `src/NetApiFlow` (núcleo), `docs/plano/contratos.md` ou qualquer pacote .NET.

## Pendência explícita, fora de escopo

Limiar fixo (`3`), não configurável nesta fase. Se o usuário real achar o limiar errado demais
(muito ruído ou pouco sensível), vira uma tarefa própria de configuração
(`NetApiFlowOptions`/`GET /config`), não um retrabalho desta fase.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (print ou descrição do selo aparecendo
num cenário de teste manual com N+1 de verdade), o que ficou pendente. A Planejadora roda
`/revisar-fase 9` antes de decidir a próxima fatia do roadmap.
