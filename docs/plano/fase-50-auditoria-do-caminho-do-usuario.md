# Fase 50: os achados da auditoria pelo caminho do usuário (D-185, D-186)

Um app novo de `dotnet new webapi` (.NET 10), os pacotes publicados da `0.5.0`, o README ao pé da
letra, cada tela em Development e em Production. Sete achados (a a g) que nenhuma rodada anterior
pegou, porque todas conferiam diff, CI e status HTTP, e nenhuma seguia o caminho de quem chega ao
produto. O (b) já teve a parte de documentação resolvida na PR #325. Aqui fica a parte que exige
código.

A fase tem duas partes, separadas pelo impacto de SemVer, para o dono poder escolher o veículo de
cada uma (`docs/plano/decisoes-pendentes.md`):

- **Parte A, sem API pública nova** (candidata a PATCH): (a), (b) aviso no grafo, (c), (d) e (e).
- **Parte B, com opção pública nova** (MINOR): (f) e (g).

Cada achado com código pede **um teste por host real, visto falhando antes da correção**
(`.claude/rules/testing.md`, D-176 e D-180). Teste que confere só o valor de uma opção ou o retorno de
uma função isolada não prova o achado.

## Como cada achado foi conferido pela Planejadora

Não escrever na PR que algo foi reproduzido se a Executora não reproduziu de novo antes.

| # | Conferido por | Como |
|---|---|---|
| a | Execução | App novo com os pacotes `0.5.0` do nuget.org e `AddOpenApi`/`MapOpenApi`: 16 caminhos no documento, 14 do NetApiFlow, incluindo `DELETE /flow/api/traces` |
| b | Execução | O mesmo app: `/plain` grava o trace com 0 etapas, `/tracked` (com `.WithNetApiFlowTracking()`) com 1 etapa `Endpoint` |
| c | Leitura de código | Causa achada, não executada no navegador (ver abaixo) |
| d | Leitura de código | `TracingRoutePolicy`, não reproduzido no navegador |
| e | Leitura de código e do fixture | 14 de 20 caminhos do documento gravado são internos, e o interceptor da demo responde 404 sem cabeçalho. O "Send request" não foi aberto no navegador |
| f | Leitura de código | `NetApiFlowMiddleware` escreve o cabeçalho sempre que o trace começa |
| g | Leitura de código | Os dois `SchemaInitializer` e os dois `schema.sql` |

## Ponto de parada: regra de recuo

Se a Executora não conseguir reproduzir o (c), o (d) ou o (e) do jeito descrito, ela **para e relata**
em vez de corrigir o que supôs. Idem se a correção do (a) fizer o Runner perder alguma rota do
usuário, ou se a do (d) tirar do trace uma rota que não é o documento OpenAPI.

## Parte A

### Tarefa 50.1 (a): rotas internas fora do documento OpenAPI

Os grupos `endpoints.MapGroup(basePath + "/api")` (`DashboardApiEndpoints.cs`) e
`endpoints.MapGroup(basePath)` (`NetApiFlowDashboardEndpointRouteBuilderExtensions.cs`) não chamam
`ExcludeFromDescription()`. As 14 rotas internas, `DELETE /flow/api/traces` e `POST /flow/api/deploys`
entre elas, vão para o Swagger e para os SDKs gerados a partir do documento.

- `ExcludeFromDescription()` nos dois grupos. Conferir também o endpoint do stream SSE e o do
  `NetApiFlow.Mcp`: qualquer rota mapeada pelo próprio NetApiFlow.
- **Teste (host real):** host com `AddOpenApi()`, `AddNetApiFlowDashboard()`, `AddNetApiFlowMcp()` e
  `MapOpenApi()`, uma rota do usuário, `GET /openapi/v1.json`. Esperado: a rota do usuário presente e
  nenhum caminho sob o caminho base do dashboard nem do MCP. Visto falhando antes. Rodar também com um
  caminho base diferente de `/flow`.

### Tarefa 50.2 (b): o grafo vazio ensina a marcar

Um trace com 0 etapas mostra o canvas em branco, e a única mensagem de vazio da tela é "nenhuma
requisição em andamento". A tela precisa explicar.

- Estado vazio do grafo para um trace carregado com 0 etapas, nos dois idiomas: a requisição foi
  gravada, mas nenhuma etapa foi marcada. Controllers MVC são rastreados sozinhos. Um endpoint de
  Minimal API precisa de `.WithNetApiFlowTracking()`. Outro código, de `NetApiFlow.Step(...)` ou de
  `AddFlowTracked`. Texto em frases curtas, com o nome da chamada em código. Estado de vazio com cor,
  ícone e texto (`.claude/rules/frontend.md`).
- **Teste:** de componente para o estado vazio, mais um teste em navegador de verdade (Playwright)
  contra um host real com um endpoint sem a marcação, nos dois idiomas e nos dois temas, visto
  falhando antes.

O endpoint de Minimal API virar etapa sem marcação é a Fase 51, por decisão do dono (D-185). O aviso
desta tarefa continua valendo depois dela: com a opção da Fase 51 desligada, ou em qualquer trace sem
etapa, é ele que ensina.

### Tarefa 50.3 (c): o painel se contradiz com etapa de microssegundos

Com uma etapa `Endpoint` de 120 µs visível no grafo, o painel diz "100% do tempo (441 µs de 441 µs) não
está coberto por nenhuma etapa registrada".

**Causa, achada lendo o código.** O erro está na conta, não no texto.
`computeUnexplainedTime` (`features/flow/details/waterfall.ts`) monta a união dos intervalos
`[startedAt, endedAt)` com `Date.parse`, que tem precisão de milissegundo. Uma etapa de 120 µs que
começa e termina no mesmo milissegundo vira um intervalo de largura 0, a cobertura dá 0, e o
tempo sem cobertura dá o total: 441 de 441. O texto, ao contrário, mostra microssegundos exatos, o que
faz a contradição aparecer. O mesmo `Date.parse` calcula o deslocamento de cada barra em
`buildWaterfall`, então uma trace de microssegundos tem as barras mal posicionadas pela mesma razão.

- A união de intervalos continua (D-78: etapa aninhada e ramos paralelos não contam duas vezes), mas
  passa a usar microssegundos. Confirmar quantos dígitos fracionários o JSON de `startedAt` e `endedAt`
  traz. Se ele chegar até 7 dígitos, ler todos. Se truncar em milissegundo, a conta usa
  `durationMicros` de cada etapa, que é a fonte da duração exibida (seção 7.3 do prompt).
- Corrigir o deslocamento das barras pelo mesmo caminho.
- **Teste:** uma trace de 441 µs com uma etapa de 120 µs esperando 321 µs sem cobertura, 73%, e não
  100%. Visto falhando antes. Casos que já existiam (etapa aninhada, `Task.WhenAll`) continuam
  passando. Mais um teste em navegador de verdade que abre o painel de uma trace gravada com etapa de
  microssegundos.

### Tarefa 50.4 (d): a leitura do OpenAPI pelo Runner vira trace do usuário

`TracingRoutePolicy.ShouldTrace` só exclui `{DashboardBasePath}/**` e os `ExcludePaths` do usuário.
Quando o Runner busca o documento OpenAPI do app, a requisição `GET /openapi/{documentName}.json`
entra nas execuções recentes e nas métricas. No teste do auditor, 2 requisições viraram 5 traces.

**Decisão (D-185):** a política passa a excluir também `OpenApiDocumentPath`, comparando o caminho
sem diferenciar maiúsculas. O documento OpenAPI é infraestrutura, como o próprio dashboard, e não fluxo
de negócio. Descartado: um cabeçalho que o Runner mandaria para a requisição não ser rastreada, porque
exige mudança no frontend e confiar num cabeçalho que qualquer cliente pode mandar. Quem serve o
documento por outro caminho (Swagger UI, por exemplo) já pode usar `ExcludePaths`.

- **Teste (host real):** uma requisição ao `OpenApiDocumentPath` não gera trace nem entra em
  `GET /metrics`, e uma rota qualquer do usuário continua gerando. Com `OpenApiDocumentPath`
  personalizado também. Visto falhando antes.

### Tarefa 50.5 (e): a demo

Duas coisas, e a segunda depende da 50.1.

- **O documento gravado herdou o (a).** `assets/demo-fixtures/openapi-document.json` tem 20 caminhos,
  14 deles internos, por isso o Runner da demo lista rota interna. Depois da 50.1, regravar o documento
  contra o sample, sem as rotas internas.
- **O "Send request" não dá retorno.** O `demoFixtureInterceptor` trata qualquer URL com `/api/` como
  chamada do dashboard e, sem fixture, responde 404 sem cabeçalho. O Runner lê o `X-NetApiFlow-TraceId`
  da resposta (`endpoint-form.component.ts`), então sem ele não segue para trace nenhum, e a tela não
  explica o que houve. A Executora reproduz isso no navegador antes de mexer.
  - Respostas gravadas para as rotas do documento, cada uma com `X-NetApiFlow-TraceId` apontando para
    uma trace que o fixture já tem, de modo que o "Send request" navega para um trace de verdade.
  - Rota sem resposta gravada: mensagem explícita de que a demo não envia requisição de verdade.
- **Teste:** em navegador de verdade (Playwright), no desktop e em `390x844`: abrir o Runner da demo,
  confirmar que nenhuma rota interna aparece, enviar uma requisição e chegar ao trace.
- **Publicação:** a demo vive em `netapiflow-site`, repositório separado (D-157). Reconstruir e empurrar
  é a confirmação do dono, a Executora pede antes de empurrar.

## Parte B

### Tarefa 50.6 (f): o cabeçalho `X-NetApiFlow-TraceId`

O `NetApiFlowMiddleware` escreve o cabeçalho em toda requisição rastreada, em qualquer ambiente, sem
opção para desligar. O README só o cita no contexto dos testes, e o `NetApiFlow.Testing` depende dele.

**Decisão (D-185):** opção nova `NetApiFlowOptions.EmitTraceIdHeader`, padrão **`true`**. Não muda o
comportamento de ninguém, e quem não quer o cabeçalho em Production o desliga. O texto completo do
contrato está em `contratos.md`.

- Com `false`, o cabeçalho não sai em nenhuma resposta.
- `NetApiFlow.Testing`: a exceção de `GetFlowTrace` que hoje diz que a resposta não tem o cabeçalho
  passa a nomear também a opção como causa possível.
- README, nos dois idiomas, na seção de segurança em produção: o cabeçalho existe, o que ele expõe (o
  trace id, o mesmo que o W3C `traceparent` já carrega) e como desligar.
- **Teste (host real):** em `Production`, o cabeçalho sai por padrão e some com `false`. Configurar só
  pela seção de configuração `NetApiFlow` também funciona (D-175, D-176).

### Tarefa 50.7 (g): os storages mexem no banco da aplicação sem pedir

`SqlServerSchemaInitializer` e `PostgreSqlSchemaInitializer`, chamados na primeira vez que o store é
usado, rodam `CREATE TABLE`, `ALTER TABLE ... ADD route_key` e o `UPDATE` de `NormalizeRoutesAsync`
no banco da aplicação, sem opção de desligar. Quem não dá permissão de DDL ao app não tem saída.

E o script de referência não ajuda: os dois `schema.sql` **não têm a coluna `route_key`** (0
ocorrências), nem o índice dela, e não vão no pacote. Quem criar o banco à mão com o script recebe um
schema que o código não aceita.

**Decisão (D-185):** opção `AutoCreateSchema`, padrão **`true`**, em `PostgreSqlStorageOptions` e em
`SqlServerStorageOptions` (texto em `contratos.md`), mais scripts corretos.

- Com `false`, o store não roda nenhum `CREATE`, `ALTER` nem `UPDATE`. Na primeira vez que for usado,
  confere se as tabelas e a coluna `route_key` existem. Se faltar algo, registra uma mensagem acionável
  (diz o que falta e onde está o script) e não derruba a requisição, como `.claude/rules/tracing.md`
  exige de toda exceção da instrumentação.
- Os dois `schema.sql` passam a ser o schema inteiro: com `route_key`, os quatro índices que o
  inicializador cria, a tabela de anotações de deploy, e uma seção de atualização idempotente com o
  `ALTER` e o preenchimento de `route_key` para quem vem de uma versão anterior.
- **Os scripts não vão dentro do pacote.** Ficam no repositório e os READMEs apontam para eles pela
  tag da versão, num trecho "rodando sem permissão de DDL", nos dois idiomas. Dentro do `.nupkg` eles
  entrariam como `contentFiles` no projeto de quem instala.
- **Teste (banco real, `NetApiFlow.Storage.Tests`):** banco criado só pelo `schema.sql` e usado pelo
  store (salvar, consultar, mapa de arquitetura) passa. E o catálogo do banco criado pelo script,
  colunas e índices, é igual ao do criado pelo inicializador. Com `AutoCreateSchema = false` num banco
  vazio, nenhuma tabela é criada. Visto falhando antes, no `route_key` do script.

## Critério que vale para a fase inteira

- Build, testes, formato e lint limpos nos dois TFMs e no Angular. Os projetos de interop e o
  `Storage.Tests` rodados de verdade, com Docker.
- `tests/manual-verification/AotCoreVerification` continua com zero aviso.
- Teste com o pacote real (D-186) no caminho do usuário: um app novo de `dotnet new webapi` seguindo o
  README, o documento OpenAPI conferido, e as telas usadas em navegador de verdade.
- Os cenários da fase novos em `ci.yml` e `release.yml`, na mesma lista (D-183).

## Fora de escopo

- Endpoint de Minimal API virar etapa sem marcação, e o atalho de uma linha para as camadas: Fase 51.
- Os itens 20 a 22 do roadmap (seção "Why not OpenTelemetry?", orçamento de performance no CI com o
  `NetApiFlow.Testing`, caminho para a 1.0).
- O que a auditoria **não** cobriu: os pacotes de storage, o MCP e o OpenTelemetry num app novo, o
  layout mobile num app real. Não chamar de auditado o que não foi testado.

## Versionamento e fluxo

- Parte A: PATCH, nenhuma mudança de API pública. Parte B: MINOR, duas opções públicas novas. O veículo
  de cada parte é decisão do dono (`docs/plano/decisoes-pendentes.md`). Se a Parte A sair como `0.5.2`,
  a branch é `hotfix/0.5.2-user-path-fixes` a partir de `main`, como o `docs/gitflow.md` descreve. Se
  esperar o `0.6.0`, `bugfix/50-caminho-do-usuario` a partir de `develop`. A Parte B é
  `feature/50b-opcoes-cabecalho-e-schema` a partir de `develop`.
- Commits pequenos, em inglês, uma linha, sem linha de coautor.
- Ao terminar cada parte: abrir a PR e parar. Tem código, então o merge exige confirmação do dono.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois, e, nos que passam por
navegador, a captura de tela. A confirmação de que o (c), o (d) e o (e) foram reproduzidos de novo antes
da correção. A saída do `AotCoreVerification`. Qual arquivo de teste prova o R-31. A lista do que o
teste com o pacote real do caminho do usuário encontrou, mesmo que não estivesse nesta fase.
