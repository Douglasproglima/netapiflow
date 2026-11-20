# Fase 53: o NetApiFlow não conhece o `PathBase` do app (D-204, R-33)

A Executora achou isto ao provar a Tarefa 50.4 (o `OpenApiDocumentPath`) num host real com
`app.UsePathBase("/api")`: o documento OpenAPI era rastreado. A Planejadora reproduziu de novo contra o
pacote publicado e o problema é maior e anterior à 50.4.

**Esta fase começa por investigação.** Nenhuma correção entra antes da Tarefa 53.0 provar o que
falha e do ADR que a Planejadora escreve depois dela.

## O fato

App .NET 10 novo, `NetApiFlow` e `NetApiFlow.Dashboard` `0.5.2` do nuget.org,
`AddNetApiFlow(o => o.ExcludePaths("/health"))`, em Development. O mesmo código, com e sem
`app.UsePathBase("/api")`:

| Requisição | Sem `UsePathBase` | Com `UsePathBase("/api")` |
|---|---|---|
| `GET /orders` (marcada) | rastreada | rastreada |
| `GET /health` (em `ExcludePaths`) | fora | **rastreada** |
| `GET /flow/api/config` (rota do painel) | fora | **rastreada** |
| `GET /flow/api/stream` (SSE) | fora | **rastreada**, e o trace dura a conexão inteira |

O store, com `UsePathBase`, ficou com `/flow/api/config`, `/flow/api/stream`, `/health` e `/orders`.

O painel trata o `PathBase` de propósito (`DashboardConfig.HostPathBase`), então esse é um cenário que o
produto suporta e que tem um furo.

**O que não foi medido:** o MCP (`/flow/mcp`), o `IncludePaths` e o `OpenApiDocumentPath` por si só. A
leitura do código diz que sofrem do mesmo, e a Tarefa 53.0 mede.

## A causa

O `NetApiFlowMiddleware` entra no pipeline por `IStartupFilter`, antes do `UsePathBase` que o usuário
chama. A decisão `policy.ShouldTrace(context.Request.Path)` roda nesse ponto, com o caminho cheio
(`/api/flow/api/config`). O padrão do painel (`/flow/**`) e os do usuário não casam com o prefixo. O
`UsePathBase` restaura `Path` e `PathBase` ao terminar, então reler o caminho depois do pipeline devolve
o caminho cheio de novo.

A rota gravada no trace já escapa disso: o `finally` do middleware lê o template do endpoint casado
(`GetEndpoint()`), que é relativo ao `PathBase`. O que quebra são as decisões tomadas por **caminho**:

- a exclusão das rotas do próprio NetApiFlow, o que viola `.claude/rules/tracing.md` ("as próprias
  rotas do NetApiFlow nunca são rastreadas");
- `ExcludePaths` e `IncludePaths`;
- o `OpenApiDocumentPath` (Tarefa 50.4).

## Por que os remédios que parecem óbvios não servem

- **Casar pelo fim do caminho (`EndsWith`).** Conserta o documento e exclui por engano uma rota
  legítima como `/v2/openapi/v1.json`.
- **Decidir depois do pipeline.** O `UsePathBase` já restaurou o caminho, e o trace já foi aberto e
  publicado. O stream do painel é uma conexão longa: apareceria como `Running` no SSE e na persistência
  durante a conexão inteira antes de alguém descartá-lo.
- **Pedir ao usuário que repita o `PathBase` numa opção do NetApiFlow.** É API pública nova, a mesma
  informação configurada em dois lugares, e o erro continua silencioso para quem esquecer.

## O que a solução tem que cumprir

1. As rotas do NetApiFlow (painel, API REST, stream SSE, MCP) nunca são rastreadas, com ou sem
   `PathBase`, e isso é decidido pela **identidade do endpoint**, não por um padrão de texto.
2. `ExcludePaths`, `IncludePaths` e `OpenApiDocumentPath` valem contra o caminho que o usuário enxerga,
   relativo ao `PathBase`, sempre que houver um endpoint casado.
3. Uma requisição excluída não publica evento, não persiste, não aparece como `Running` no SSE e não
   devolve `X-NetApiFlow-TraceId`.
4. Sem `PathBase`, o comportamento e o custo ficam como hoje. O caminho comum não paga a mais.
5. Requisição sem endpoint casado (arquivo estático, 404) sob `PathBase`: se nenhum mecanismo resolver
   isso com exatidão, o limite é dito no README, nos dois idiomas, com o que o usuário faz (padrão com o
   prefixo). Nunca fica "resolvido pela metade" sem aviso.
6. Sem API pública nova. Se a investigação provar que não há outro jeito, volta à Planejadora antes de
   qualquer código.
7. Falha da própria checagem é logada e engolida (`.claude/rules/tracing.md`).

## Mecanismos a investigar (a Planejadora não escolhe antes da 53.0)

- **A. Metadado nos endpoints do NetApiFlow, decidido no `EndpointMatched`.** É o mecanismo da ADR-0026
  e herda o R-32 (o evento não é API formal). Pede que a abertura do trace possa ser adiada ou desfeita
  sem publicar nada.
- **B. Decidir pelo template do endpoint casado**, o mesmo que o `finally` já lê. Resolve
  `ExcludePaths` para endpoint, não para requisição sem endpoint.
- **C. Adiar a publicação do início do trace** até o primeiro ponto em que o `PathBase` é conhecido.
- **D. Um middleware que o usuário chama depois do `UsePathBase`.** Quebra a promessa das duas linhas e é
  API nova. Entra na lista só para ser descartado com evidência.
- **E. Ler o `PathBase` antes do pipeline, de outra fonte.** A investigação diz se isso existe para o
  `UsePathBase` chamado no código do usuário.

## Tarefa 53.0: medir, sem corrigir

Testes de host real (`TestServer`, `net8.0` e `net10.0`) que **só descrevem o que acontece hoje**. Não
se commita teste vermelho nem pulado: o resultado vai no relatório, e os testes entram junto da correção.

Matriz, com e sem `UsePathBase("/api")`, em Development e em Production:

| Cenário | O que medir |
|---|---|
| `ExcludePaths` de uma rota de endpoint | header de trace e store |
| `IncludePaths` | idem |
| rota do painel: `config`, `traces` (REST) e o `stream` | idem, e se o trace do `stream` fica `Running` |
| MCP, `initialize` e uma chamada de ferramenta | idem |
| `OpenApiDocumentPath` | idem |
| arquivo estático excluído por `ExcludePaths` | idem |

Duas perguntas que mudam o tamanho do problema, e que a Executora responde com o código do framework e
com um teste, não por suposição:

- **Quem define o `PathBase` em produção, e em que ordem?** Aplicativo em IIS (aplicação virtual) e atrás
  de proxy reverso: o `PathBase` chega por um `IStartupFilter` do próprio framework **antes** do
  middleware do NetApiFlow, ou só quando o usuário chama `UsePathBase`? Se o framework o define antes, boa
  parte das implantações reais não sofre, e o problema fica restrito ao `UsePathBase` explícito.
- **Cada mecanismo acima**, com um spike descartável: o que ele consegue cumprir dos sete requisitos, e o
  que ele quebra. Com a medição, não com opinião.

**Pronto quando:** a matriz preenchida com o que passa e o que falha, as duas perguntas respondidas
com evidência, e cada mecanismo avaliado contra os sete requisitos. A Executora **para e entrega o
relatório.** Não corrige.

## Depois da 53.0

A Planejadora escreve a ADR e as tarefas de correção (53.1 em diante), com os testes de host real da
matriz agora provando a correção e vistos falhando antes. O README e o XML doc do `OpenApiDocumentPath`,
`ExcludePaths` e `IncludePaths` passam a dizer o que vale sob `PathBase`, nos dois idiomas.

## Veículo

A auditoria decide. A Planejadora recomenda decidir **depois da 53.0**:

- Se a correção couber numa mudança interna pequena, sem API pública, a falha reproduzida contra o pacote
  publicado cabe em hotfix pelo critério de `.claude/rules/planning.md` ("Quando um achado de auditoria
  vira hotfix, não roadmap").
- Se depender do `EndpointMatched` da Fase 51 (R-32), vai no `0.6.0` junto com ela.
- O `0.6.0` não sai com o README e o XML doc dizendo "nunca rastreada" e este defeito aberto. A frase é o
  contrato.

## Fora de escopo

- A Tarefa 50.4 e a PR da Parte A não esperam esta fase.
- Mudar a semântica de `ExcludePaths` para quem não usa `PathBase`.
- Suporte a vários `PathBase` ou a `PathBase` que muda em tempo de execução.

## Relatório final

A matriz medida, as respostas às duas perguntas com o trecho do código do framework ou o teste que as
sustenta, a avaliação de cada mecanismo contra os sete requisitos, e qual a Executora recomenda e por quê.
