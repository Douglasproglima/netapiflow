# Fase 2: Formas de marcação

Projetos: `src/NetApiFlow` (DispatchProxy e filtros), `src/NetApiFlow.MediatR`,
`src/NetApiFlow.EntityFrameworkCore`, `samples/SampleApi.Controllers`,
`samples/SampleApi.MinimalMediatR`.

Pré-requisito: Fase 1 aceita.

## Tarefas

### 2.1: `FlowInterceptorProxy` (`DispatchProxy`) (risco R-01)

Intercepta chamada de método de uma interface registrada no DI, abre um `Step` antes e fecha
depois, cobrindo as cinco formas de retorno: síncrono (`void`/`T`), `Task`, `Task<T>`,
`ValueTask`, `ValueTask<T>`. Fechar o span **quando a tarefa termina**, não quando o método
retorna. Ver mitigação do risco R-01 em `docs/plano/riscos.md` antes de implementar: `ValueTask`
só pode ser consumida uma vez, então converter com `.AsTask()` antes de anexar continuação, ou
consumir o `ValueTaskAwaiter` uma única vez.

**Pronto quando:** teste com cada uma das cinco formas de retorno, incluindo `Task<T>` e
`ValueTask<T>` que completam depois de um delay artificial (`Task.Delay`), prova que a duração do
span reflete o delay real, não a duração do método síncrono até o primeiro `await`.

### 2.2: `AddFlowTracked<TInterface, TImpl>(ComponentType)` (risco R-02)

Registra `TImpl` no DI com o lifetime pedido, e a interface `TInterface` resolvendo para o proxy
decorado, preservando o mesmo lifetime (via `ImplementationFactory`, não um novo `services.Add*`
que crie um segundo lifetime).

**Pronto quando:** teste resolve `TInterface` duas vezes no mesmo escopo (`Scoped`) e confere que
as duas resoluções devolvem a mesma instância por trás do proxy (comparar o que o proxy decora,
não o proxy em si, que é uma instância nova por `DispatchProxy.Create`: decidir e documentar no
código qual identidade é comparada).

### 2.3: `DecorateFlowTracked(predicate)`

Decora em massa toda interface já registrada no DI cujo tipo de implementação casa com o
predicado (ex.: `type => type.Namespace!.StartsWith("MyApp.Application")`).

**Pronto quando:** teste com predicado por namespace prova que só os tipos que casam viram proxy;
os que não casam continuam resolvendo para a implementação original, sem overhead de proxy.

### 2.4 e 2.5: Filtros de Controller e Minimal API

Filtro de ação (`IAsyncActionFilter` ou `IActionFilter` para Controllers;
`IEndpointFilter` para Minimal API) que abre um `Step` com `ComponentType.Controller` ou
`ComponentType.Endpoint` ao redor da execução.

**Pronto quando:** sample com controller (tarefa 2.8) produz o span `Controller` sem nenhum
`NetApiFlow.Step(...)` manual no código do controller; idem para Minimal API (tarefa 2.9) com
`ComponentType.Endpoint`.

### 2.6: `NetApiFlow.MediatR`

`IPipelineBehavior<TRequest, TResponse>` que abre um `Step` com `ComponentType.UseCase` ao redor
de `Handle`. Projeto referencia `NetApiFlow` e `MediatR` (ver sugestão sobre licença do MediatR em
`docs/plano/README.md` → a decisão de qual versão fixar é da Planejadora; a Executora usa a
versão disponível em `Directory.Packages.props` sem mudar sozinha).

**Pronto quando:** handler registrado no DI vira span `UseCase` automaticamente, sem código extra
no handler.

### 2.7: `NetApiFlow.EntityFrameworkCore`

`DbCommandInterceptor` que abre um `Step` com `ComponentType.Database` ao redor de cada comando
SQL. Operação gravada como `"{Tipo} {TabelaPrincipal}"` (ex.: `"SELECT Orders"`), nunca o SQL com
parâmetros, a menos que `NetApiFlowOptions.CaptureSqlText == true`, e mesmo assim, só o SQL
**sem** os valores de parâmetro.

**Pronto quando:** teste com `CaptureSqlText = false` (padrão) prova que nenhum parâmetro de
comando aparece em nenhum campo do span, nem na tag, nem na operação, nem em mensagem de erro.

### 2.8: `samples/SampleApi.Controllers`

Controllers + EF Core + PostgreSQL. Quatro cenários de demonstração (para o GIF da Fase 7
também): sucesso, erro de negócio no meio do fluxo, operação lenta (acima do `SlowThreshold`), e
chamadas paralelas com `Task.WhenAll`.

**Pronto quando:** os quatro cenários produzem o fluxo completo Controller → Service →
Repository → Database, sem nenhum `NetApiFlow.Step(...)` manual no código de negócio do sample
(só as formas automáticas das tarefas 2.1-2.7). Sem `UseCase`: este sample não usa MediatR
(corrigido na revisão da Fase 2 — a versão anterior deste texto incluía `UseCase` na cadeia por
engano, o que só faria sentido se este sample usasse MediatR, que é o caso da tarefa 2.9, não
desta).

### 2.9: `samples/SampleApi.MinimalMediatR`

Minimal API + MediatR + SQLite (ou InMemory do EF Core). Mesmos quatro cenários da tarefa 2.8,
pelo caminho do MediatR em vez de Controllers.

**Pronto quando:** idem à 2.8, com o span `UseCase` do MediatR no lugar do fluxo de Controllers.

## Aceite da fase

Critério da seção 13, Fase 2, do prompt: os dois samples produzem o fluxo completo sem
`Step(...)` manual; `Task`, `ValueTask` e métodos síncronos têm durações corretas (provado pela
tarefa 2.1).

## Ao terminar

Resumo para a Planejadora. `/revisar-fase 2` roda os dois samples e inspeciona o trace gerado
diretamente via `ITraceStore`/`FlowRecorder` (a API REST só existe a partir da Fase 3; até lá,
inspecionar com um teste de integração temporário ou um endpoint de diagnóstico descartável do
próprio sample, nunca com um `Step(...)` manual no código de negócio).
