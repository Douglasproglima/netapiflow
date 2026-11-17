# Fase 36: lote de dívida técnica pequena, sem mudança de API pública

Dez achados pequenos do `roadmap-valor.md`, triados pela Planejadora (D-142) por não exigirem
nenhuma decisão de arquitetura ou de produto: cada um já tem forma decidida, precedente
estabelecido no próprio código, ou decisão de forma fechada nesta entrada. PATCH, sem mudança em
`contratos.md`, sem ADR. Cada tarefa é um commit.

## Tarefa 36.1: contador "em andamento" do topbar sobrevive a falha de rede (item 3)

`app.ts:124`, `.pipe(switchMap(() => this.api.getLiveTraces()))` sem `catchError`: um erro na
chamada mata a assinatura inteira (RxJS propaga o erro pro `subscribe` sem handler de erro e
encerra o `Observable`), travando o contador até recarregar a página. Mesma classe de bug que
`flow-page.component.ts:166-172` já corrigiu, mesmo padrão de correção: `catchError` dentro do
`pipe` do `getLiveTraces()`, devolvendo `of([])` pra manter a assinatura viva na próxima rodada do
`merge(...)` externo, sem crescer escopo com um sinal de erro visível (o contador do topbar é só um
badge, não uma tela com estado de erro).

**Pronto quando:** spec novo em `app.spec.ts` (ou arquivo equivalente) que simula
`getLiveTraces()` lançando erro numa emissão e confirma que o `inProgressCount` não trava: emissão
seguinte do `merge(...)` externo volta a atualizar o valor normalmente.

## Tarefa 36.2: ícone de cadeado na lista de rotas do Runner (item 6)

A lista de endpoints do painel "Testar um endpoint" (`endpoint-form.component.html`, grid
`56px minmax(0, 1fr)` por item, seção 6.1 de `docs/design/flow-workspace/guide.md`) não indica
quais rotas exigem autenticação antes de selecionar. `hasSecurityDeclaration(doc, method, path)`
(`openapi-form.ts:160`) já calcula exatamente esse booleano, hoje só usado depois de selecionar a
rota. Reaproveitar a mesma função no `@for` da lista: quando verdadeiro, ícone de cadeado inline
(SVG 16×16, traço 1,75px, `stroke="currentColor"`, mesmo estilo de `docs/brand/icons/`, seção 4 do
guia) ao lado do badge de verbo, com `aria-label` descrevendo "requer autenticação" (ou chave de
tradução equivalente). Nunca o único sinal: o cadeado some a navegação por texto, acompanha o
padrão de cor+ícone+texto que `.claude/rules/frontend.md` já exige em todo lugar do produto.

**Pronto quando:** `endpoint-form.component.spec.ts` cobre uma rota com `security` declarado e uma
sem, confirmando a presença/ausência do ícone (por `aria-label` ou atributo de teste, não por
classe CSS). `npx impeccable detect` na tela do Runner, claro e escuro, sem achado novo de
`low-contrast`/`undersized-ui-text`.

## Tarefa 36.3: `SqlServerTraceStore` compara `method` sem normalização explícita (item 7)

`PostgreSqlTraceStore.cs` usa `ILIKE`/`ToUpperInvariant()` nos quatro pontos onde filtra por
`method` (`GetArchitectureMapAsync`, duas consultas; `GetLastSuccessfulAsync`; `QueryAsync`).
`SqlServerTraceStore.cs` compara com `=` simples nos mesmos quatro pontos, dependendo
implicitamente da collation do banco (normalmente case-insensitive por padrão, mas não garantido,
e inconsistente com o outro store independente de collation). Trocar os quatro `method = @method`
por `UPPER(method) = UPPER(@method)`, mesmo tratamento explícito que já existe pro agrupamento de
`GetRouteStatsAsync` (R-17, já mitigado com `ToUpperInvariant()` no lado C#).

**Pronto quando:** teste em `NetApiFlow.Storage.Tests` (Testcontainers, SQL Server real) grava uma
trace com `method` em minúsculo e confirma que as quatro consultas acham o registro passando
`method` em outro caso (maiúsculo ou misto), espelhando o teste equivalente que já existe pro lado
PostgreSQL.

## Tarefa 36.4: piso em pixels na lista do Runner (item 9)

`endpoint-form.component.scss:104-108`, `.endpoint-form__list-wrapper` só tem `max-height: 55%`,
sem piso: numa janela baixa, a lista encolhe até sobrar espaço pra menos de um item inteiro.
Decisão de forma (D-142, contra a seção 6.1 do guia, que já define altura mínima de 36px por item
de rota): piso de `112px` (`36px × 3` arredondado pro múltiplo de 8 mais próximo, mesma grade de
8px que o guia já usa em `gap`/padding), o suficiente pra mostrar ao menos três rotas inteiras
antes de rolar, nunca menos.

**Pronto quando:** `min-height: 112px` adicionado ao lado do `max-height: 55%` existente.
`npx impeccable detect` no Runner, viewport `390x844` (o caso que estoura o problema), claro e
escuro, confirma a lista não encolhendo abaixo do piso.

## Tarefa 36.5: teste de cancelamento sensível a timing sob carga de CI (item 10)

Quarta ocorrência do mesmo padrão (D-54, D-125, D-127, D-137):
`CancellationClassificationIntegrationTests.WaitForCompletionAsync` (linha 70) faz polling numa
`ConcurrentDictionary` com `Task.Delay(20)` contra um orçamento fixo de
`new CancellationTokenSource(TimeSpan.FromSeconds(10))`, que estoura sob disputa real de CPU
quando a suíte inteira roda com Testcontainers subindo ao mesmo tempo, mesmo quando a condição
real (a trace completar) eventualmente seria satisfeita. `FlowRecorder.TraceCompleted` (assinado
em `CreateHostAsync`, linha 63) já dispara exatamente no momento em que a trace completa: trocar o
polling por um `TaskCompletionSource<FlowTrace>` por `routeTemplate`, resolvido direto dentro do
handler de `TraceCompleted` (sem tocar a `ConcurrentDictionary`, que pode sair), esperado com um
timeout de segurança bem mais largo (`TimeSpan.FromSeconds(30)`, mesma ordem de grandeza do
`Task.Delay(TimeSpan.FromSeconds(30))` que o próprio endpoint `/internal-timeout` já usa), que só
dispara se o evento nunca chegar a acontecer de verdade, não por lentidão incidental de agendamento
de CPU.

**Pronto quando:** os dois testes existentes (`ClientDisconnectingMidRequest_...`,
`InternalCancellationWithClientStillConnected_...`) continuam verdes, sem a `ConcurrentDictionary`
nem o `Task.Delay(20)` de polling. Rodados 15× em sequência pela Planejadora antes do merge, sem
falha, mesma régua de confiança que D-89 já exigiu pra fechar D-54.

## Tarefa 36.6: tempo até expirar de um JWT colado, no painel "Autorizar" (item 11)

Decodificar o `exp` (claim padrão JWT, segunda parte do token separada por `.`, base64url, sem
verificar assinatura, só leitura) de um valor colado num campo de esquema `http`/`bearer` ou
`oauth2`/`openIdConnect` (os mesmos que `isSupportedAuthScheme` em `openapi-form.ts:169` já
reconhece), mostrando tempo até expirar (`"expira em 14 min"`) ou aviso quando já expirado
(`"expirado há 3 min"`), ao lado do campo. Função pura nova (`decodeJwtExpiry(token: string): Date
| null`, `null` quando não é um JWT decodificável ou não tem `exp`), sem alterar
`buildAuthApplication`/`previewAuthApplication`: é só apresentação, nunca bloqueia o envio da
requisição mesmo com o token expirado (o servidor é quem decide, o painel só avisa).

**Pronto quando:** `[Theory]`/spec cobrindo um JWT válido com `exp` futuro, um com `exp` passado,
e um valor que não é JWT (token opaco, chave de API), confirmando `null` no terceiro caso sem
lançar exceção.

## Tarefa 36.7: busca do documento OpenAPI retenta uma vez com token guardado em 401 (item 12)

`endpoint-form.component.ts:159-169`, `loadOpenApiDocument` trata qualquer erro só com
`this.loadError.set(...)`, sem distinguir `401`. Quando a API tranca o próprio
`/openapi/v1.json` fora de ambiente de desenvolvimento (cenário real, mesmo tipo de proteção que
`.claude/rules/tracing.md` já espera de rotas sensíveis), o Runner morre sem carregar nada, mesmo
que o usuário já tenha usado o painel antes nesta aba e tenha um valor de `Authorization: Bearer`
guardado em `RunnerCredentialsService` de uma sessão anterior com outro endpoint. Decisão de forma
(D-142): `RunnerCredentialsService` ganha `lastBearerValue(): string | null`, atualizado (via um
novo `recordBearerUsage(value: string)`) toda vez que `buildAuthApplication` produz um header
`Authorization: Bearer ...` pra qualquer esquema, em qualquer requisição enviada pelo painel. No
`error` de `loadOpenApiDocument`, se o status for `401` e `lastBearerValue()` não for nulo, retenta
a busca uma única vez anexando esse valor como `Authorization: Bearer <valor>`; se a segunda
tentativa também falhar, ou não houver valor guardado, mantém o comportamento atual
(`loadError`).

**Pronto quando:** spec cobrindo as três situações (401 sem valor guardado: erro direto, igual
hoje; 401 com valor guardado e retentativa bem-sucedida: documento carrega; 401 com valor guardado
e retentativa também falhando: erro, sem loop infinito).

## Tarefa 36.8: aviso de alternativa de autorização não detecta esquema não aplicável (item 13)

`endpoint-form.component.ts:127-135`, `noAlternativeFilled` considera uma alternativa "preenchida"
só checando `this.credentials.authValue(name).trim() !== ''`, sem checar se o esquema é um dos que
`buildAuthApplication` (`openapi-form.ts:205`) de fato consegue aplicar. Um esquema `http`/`basic`
preenchido (que `isSupportedAuthScheme` já recusa, linha 173-174) faz o campo contar como
"preenchido" pro aviso, que silencia mesmo a requisição saindo sem nenhum header de autorização
real: 401 mudo. Trocar o predicado de `Object.keys(alternative).every((name) =>
this.credentials.authValue(name).trim() !== '')` por uma checagem que usa
`buildAuthApplication(scheme, value) !== null` (já exportado, já encapsula exatamente "este valor,
pra este esquema, produz alguma coisa aplicável"), buscando o `scheme` de cada `name` via
`schemesByName` (já existe na linha 106-108).

**Pronto quando:** teste novo cobrindo exatamente o cenário do achado original (D-131): alternativa
cujo único esquema é `http`/`basic`, preenchida, confirmando que `noAlternativeFilled` continua
`true` (o aviso não some), diferente do comportamento atual.

## Tarefa 36.9: documentar o limite de `/traces/live` com múltiplas réplicas (item 17, parte 1)

`GET /traces/live` lê o `FlowRecorder` em memória de um único processo (confirmado contra
`DashboardApiEndpoints.cs` na D-132): com múltiplas réplicas atrás de um balanceador, `/flow`
mostra só o que a réplica que respondeu à conexão SSE viu, nunca o agregado de todas. Um parágrafo
nas seções de limitações conhecidas de `README.md`/`README.pt-BR.md` (mesma seção onde R-11,
`net8.0`/SSE manual e outras limitações já estão documentadas), explicando a causa em uma frase e
recomendando storage persistente (`GET /traces`, não `/traces/live`) como fonte de verdade
agregada quando o host roda múltiplas réplicas. Resolver de verdade (visão ao vivo caindo pro
`ITraceStore` com id de instância por trace) continua fora desta tarefa, sem fase atribuída
(D-142).

**Pronto quando:** parágrafo presente nos dois READMEs, conteúdo equivalente (regra de idioma do
`CLAUDE.md`).

## Tarefa 36.10: projeto de verificação AOT pro caminho manual (item 20)

Projeto novo em `tests/manual-verification/`, irmão de `AotAnnotationVerification` (D-136). Aquele
projeto já prova que `AddFlowTracked`/`DecorateFlowTracked` emitem os avisos `IL3050`/`IL2026`
esperados quando usados sob `PublishAot=true`. Falta o outro lado da mesma frase, a que o próprio
atributo recomenda ao usuário como alternativa segura ("use `NetApiFlow.Step` ou o interceptor do
EF Core em vez disso", README e comentário XML do atributo): nenhum teste prova que essa alternativa
é de fato AOT-safe.

Projeto novo (`AotManualPathVerification` ou nome equivalente), `PublishAot=true`, chamando só
`AddNetApiFlow()` mais `NetApiFlow.Step(...)` mais o interceptor do `NetApiFlow.EntityFrameworkCore`,
sem `AddFlowTracked`/`DecorateFlowTracked`. Critério de pronto: `dotnet publish` com zero aviso de
AOT ou trimming.

Se o `dotnet publish` acusar algum aviso: **não forçar nem contornar**. Isso significa que a frase
do README/comentário XML está errada, recomendando como "alternativa segura" algo que não é
AOT-safe de verdade. Nesse caso, o achado em si é o entregável desta tarefa: documentar o aviso
exato no resumo de fim de fase, sem tentar calar o avisador, e seguir para a próxima tarefa. A
correção da frase (se for o caso) é decisão da Planejadora a partir desse achado, não desta tarefa.

**Pronto quando:** projeto criado, `dotnet publish -c Release` rodado contra ele, e o resultado
(zero aviso, ou a lista exata de avisos) registrado no resumo de fim de fase.

## Fora de escopo

- Item 2 (forma da legenda do grafo): decisão de forma ainda em aberto, fora do loop (D-142).
- Resolver de verdade o limite de `/traces/live` (item 17, parte 2): decisão de arquitetura, fora
  do loop (D-142).

## Aceite da fase

`dotnet build NetApiFlow.slnx -c Release` e `dotnet format NetApiFlow.slnx --verify-no-changes`
limpos nos dois TFMs. `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build`
limpos no `ClientApp`. `npx impeccable detect` no Runner (tarefas 36.2 e 36.4), claro e escuro,
desktop e `390x844`, sem achado novo de `low-contrast`/`undersized-ui-text`. Suíte completa
(`.NET` e Angular) verde, incluindo os testes novos de cada tarefa.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos), contagem de avisos do
Impeccable antes/depois (regra de `.claude/rules/design-quality.md`), o que ficou pendente.
