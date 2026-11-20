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
| IIS in-process (ANCM), como aplicação virtual sob `/api` | idem, e de onde vem o `PathBase` |
| IIS out-of-process | idem |
| Proxy reverso (nginx ou YARP) com `X-Forwarded-Prefix` | idem, e se o app aplica o prefixo sozinho ou só quando o código chama `UsePathBase` |

**As três últimas linhas são medidas em host real** (decisão da auditoria, D-205), não lidas no
código. Se a máquina não tiver o IIS ou o Hosting Bundle, a Executora diz que não mediu e por quê, e a
Planejadora leva o assunto à auditoria. Nada entra no relatório como medido por leitura.

Duas perguntas que mudam o tamanho do problema, e que a Executora responde com a medição acima, não por
suposição:

- **Quem define o `PathBase` em produção, e em que ordem?** Nas três hospedagens: o `PathBase` chega por
  um `IStartupFilter` do próprio framework **antes** do middleware do NetApiFlow, ou só quando o usuário
  chama `UsePathBase`? Se o framework o define antes, boa parte das implantações reais não sofre, e o
  problema fica restrito ao `UsePathBase` explícito.
- **Cada mecanismo acima**, com um spike descartável: o que ele consegue cumprir dos sete requisitos, e o
  que ele quebra. Com a medição, não com opinião.

**Pronto quando:** a matriz preenchida com o que passa e o que falha, as duas perguntas respondidas
com evidência, e cada mecanismo avaliado contra os sete requisitos. A Executora **para e entrega o
relatório.** Não corrige.

## Resultado da 53.0, medido pela Executora (20/11/2026)

Sonda descartável fora do repositório, `ProjectReference` para o núcleo, o painel e o MCP do `develop`,
um processo por célula, Kestrel de verdade, requisições HTTP de verdade e o store lido por um endpoint de
diagnóstico. Não usou `TestServer`. Os dados brutos ficam no scratchpad dela. Os testes entram com a
correção.

**Kestrel, 32 células** (`net8.0` e `net10.0`, Development e Production, sem e com `UsePathBase("/api")`):

- Sem `UsePathBase`: 56 de 56 requisições corretas.
- Com `UsePathBase("/api")`: 36 incorretas, idênticas nos quatro TFM e ambiente. `ExcludePaths` não
  exclui, o arquivo estático vira trace `(unmatched)`, as rotas do painel (`config`, `traces`, `stream`) e
  do MCP (`initialize` e `tools/call`) são rastreadas, e em Production elas respondem 404 e **mesmo assim**
  geram trace e cabeçalho. O `OpenApiDocumentPath` é rastreado.
- **Achado novo, o mais grave:** `IncludePaths("/orders")` com `UsePathBase` deixa a rota do **usuário** fora
  do store (store vazio). O defeito também apaga dado legítimo, não só deixa passar o que devia sair.
- O stream sob `PathBase`: um segundo cliente SSE vê `TraceStarted (unmatched) Running` durante a conexão
  inteira, sem `TraceCompleted` até ela acabar. `Running` aparece no SSE, não na persistência.
- O que cada ponto enxerga: antes e depois do middleware do NetApiFlow, `path=/api/orders` e `pathBase`
  vazio. Só o código do usuário depois do `UsePathBase` vê `path=/orders` e `pathBase=/api`.

**Hospedagem real:**

| Hospedagem | Resultado |
|---|---|
| IIS in-process e out-of-process, `net8.0` e `net10.0`, aplicação virtual `/api` | 0 incorretas. O framework define o `PathBase` **antes** do NetApiFlow |
| nginx que corta o prefixo, `X-Forwarded-Prefix: /api`, app sem código | 0 incorretas, `PathBase` vazio, o cabeçalho é só informativo |
| O mesmo com `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` | 0 incorretas. A variável **não** aplica o prefixo |
| O mesmo com `UseForwardedHeaders(All)` no código | 0 incorretas. O `PathBase=/api` só aparece **depois** do NetApiFlow |
| nginx que não corta, app sem `UsePathBase` | `/api/orders` dá 404 e `/orders` direto dá 200, com e sem a variável |
| nginx que não corta, `UsePathBase("/api")` no código | 7 incorretas, igual ao Kestrel direto |

**Limites da medição de hospedagem.** O IIS foi medido no IIS Express com o ANCM v2 (o mesmo
`aspnetcorev2.dll` do IIS completo), **não** no IIS completo, que exige administrador nesta máquina. O
módulo que vem com o IIS Express não carregou (erro 8007007e). O YARP não foi medido.

**As duas perguntas:**

1. Quem define o `PathBase` antes do NetApiFlow? No IIS (os dois modelos), o framework, antes. No proxy,
   ninguém: o app não aplica o prefixo sozinho, nem com a variável de ambiente. `UseForwardedHeaders` e
   `UsePathBase` do código do usuário entram sempre **depois** do middleware do NetApiFlow.
2. Logo, o defeito fica restrito ao `UsePathBase` explícito, no Kestrel direto ou atrás de um proxy que
   não corta o prefixo. O IIS e o proxy que corta não sofrem. Como `UsePathBase` é a forma comum de rodar
   sob prefixo fora do IIS, a classe de implantação afetada continua grande.

**Mecanismos, com um spike descartável por mecanismo** (matriz de 4 grupos, com e sem `UsePathBase`,
`net8.0` e `net10.0`):

| Mecanismo | Resultado |
|---|---|
| **A** (adiar a abertura do trace até o `EndpointMatched`; decidir por metadado de identidade nos endpoints do NetApiFlow e pelo caminho relativo no resto) | 28 de 28 corretas sem `PathBase` e 28 de 28 com. Cumpre os requisitos 1 a 4. Falha o 5 (sem endpoint casado a decisão cai no caminho cheio). O 7 não foi medido |
| **B** (começar tudo e filtrar no fim pelo template do endpoint casado) | Store correto, mas **falha o 3 e o 4**: o cabeçalho sai e `TraceStarted Running` é publicado para a rota excluída e para o stream do painel, sem `TraceCompleted` (fantasma `Running` no SSE). Sem `PathBase` também publica o que hoje é excluído (20 de 28 incorretas), então o caminho comum paga |
| **C** | Não é um mecanismo à parte: o primeiro ponto em que o `PathBase` é conhecido é o `EndpointMatched`, então ele é o A |
| **D** (middleware depois do `UsePathBase`) | Descartado com evidência: só o ponto do usuário vê o caminho certo, mas pede API pública nova e o trace teria de começar nele |
| **E** (ler o `PathBase` de outra fonte antes do pipeline) | Não existe para o `UsePathBase` explícito. `ASPNETCORE_PATHBASE=/api` no Kestrel não muda nada |

O custo do A, medido grosseiramente num loop de 4000 requisições com ruído alto, foi 91 e 98 µs de
mediana contra 86 e 85 hoje. É um indício e **não** um benchmark.

**Decisão (D-211): o mecanismo A.** É o único que cumpre os requisitos 1 a 4, e herda o R-32 (o evento é
diagnóstico, não API formal), que funcionou nos dois TFMs. O ADR-0027 é escrito antes da correção.

**O que a correção ainda tem de medir, antes de qualquer código:**

- O efeito do A sobre `CapturePrePipelineSteps` e sobre o parentesco da `Activity` de entrada: com o A o
  trace passa a abrir depois de alguns middlewares.
- O requisito 7: a checagem da própria decisão logada e engolida (o spike não tem o `try/catch`).
- O custo com `tests/NetApiFlow.Benchmarks` e `[MemoryDiagnoser]`, no lugar do loop grosseiro.
- O requisito 5, o limite que vai ao README: sem endpoint casado (arquivo estático, 404) sob `PathBase`, o
  `ExcludePaths` continua valendo só contra o caminho cheio.
- O marcador de identidade é um tipo `internal`, e o MCP precisa de `InternalsVisibleTo` (o painel já tem).

## Depois da 53.0

A Planejadora escreve o ADR-0027 e as tarefas de correção (53.1 em diante) **depois da Fase 51**, porque o
mecanismo A vive no mesmo listener do `EndpointMatched` que a Fase 51 cria, e a Fase 57 (a rota da trace
ao vivo) depende do mesmo ponto. Os testes de host real da matriz passam a provar a correção e são vistos
falhando antes. O README e o XML doc do `OpenApiDocumentPath`,
`ExcludePaths` e `IncludePaths` passam a dizer o que vale sob `PathBase`, nos dois idiomas.

## Veículo e ordem (D-205)

A auditoria decidiu, sem esperar a 53.0: **`0.6.0`**. O defeito é anterior, não é regressão, e não
expõe dado nem apaga nada, então pela regra nova de hotfix não é hotfix. O `0.6.0` não sai com ele
aberto: o README e o XML doc dizem "nunca rastreada", e a frase é o contrato.

Ordem da Executora: 50.11, 50.5, **53.0**, 50.10, Parte B e 50.9, Fase 51, e a correção da 53. Se a
53.0 concluir que a correção depende do `EndpointMatched` da Fase 51 (R-32), a correção entra junto
com a 51.

`PathBase` e proxy reverso entram também no inventário da auditoria de base que precede o `0.6.0`.

## Fora de escopo

- A Tarefa 50.4 e a PR da Parte A não esperam esta fase.
- Mudar a semântica de `ExcludePaths` para quem não usa `PathBase`.
- Suporte a vários `PathBase` ou a `PathBase` que muda em tempo de execução.

## Relatório final

A matriz medida, as respostas às duas perguntas com o trecho do código do framework ou o teste que as
sustenta, a avaliação de cada mecanismo contra os sete requisitos, e qual a Executora recomenda e por quê.
