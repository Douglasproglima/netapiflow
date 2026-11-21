# Fase 70: a rede contra regressão (D-253, decisão do dono)

Palavras do dono, relatadas pela auditoria em 21/11/2026: "Temos criar mecanismo que impedem de voltar bugs e colaterais já
ajustado em fases passadas, do contrário o backlog só aumenta". À pergunta "fazer agora ou deixar para a 0.7.0", a resposta
literal foi "Fazer agoea".

Entra na `0.6.0`, **logo depois da Fase 53 e antes de todas as outras que faltam**, por decisão do dono. A lista da `0.6.0`
continua congelada (D-232): esta fase entrou porque o dono decidiu, não porque o congelamento abriu.

## O diagnóstico, medido

| Afirmação | Conferi |
|---|---|
| Nenhum job do CI usa o pacote como o usuário | **Confirmado.** O `ci.yml` tem quatro jobs: `detect-changes`, `build-test`, `visual-quality` e `secret-scan`. O único passo que empacota é `Pack (verification only)`, e ele só roda `dotnet pack`: nada instala o `.nupkg` num app novo. O `release.yml` tem um job, `release` |
| Nenhum job compila o README | **Confirmado.** O Quick start só era compilado pela passada manual da auditoria |
| Nenhum job abre a demo construída | **Confirmado.** O `visual-quality` constrói o painel e roda o **sample vivo** com o Impeccable em modo de aviso. O `npm run build:demo` não roda em workflow nenhum |
| Nada trava a API pública | **Confirmado.** Não há `PublicApiAnalyzers` nem teste de snapshot da superfície |
| Quatro regressões desta rodada passaram por essas lacunas | O Quick start da #409 (`CS1529`, achado pelo teste integrado da auditoria), a demo da #416 (404 em 26 de 26 traces), o piso do EF Core da `0.5.1` (R-30, D-181) e as opções da Fase 47 (D-176) |
| Não há biblioteca de navegador no front | **Confirmado.** O `ClientApp/package.json` tem só `impeccable`, `jsdom` e `vitest` |
| Já existe um template de PR | `.github/pull_request_template.md`. Não existe regra de "quem consome o contrato" |

Hoje quem pega essas regressões é a passada manual da auditoria, e isso não escala. A fase troca a passada manual por passos de CI
que ficam vermelhos.

## O que a Planejadora conferiu sobre o `PublicApiAnalyzers`

Consultado no nuget.org em 21/11/2026: `Microsoft.CodeAnalysis.PublicApiAnalyzers`, autor Microsoft, licença **MIT**, marcado como
`developmentDependency`, última estável `5.6.0` publicada em 2026-07-02, fonte `github.com/dotnet/roslyn`. Cumpre os itens 2 e 3 de
`.claude/rules/dependencies.md`. **Não medido:** se uma versão `5.x` carrega no compilador do SDK do `global.json` (`10.0.100` com
`latestFeature`) e se convive com `net8.0;net10.0`. A Tarefa 70.4 mede.

## Ponto de parada

- **Uma guarda que não foi vista falhando contra o defeito antigo não é guarda.** Se não der para reintroduzir o defeito num
  branch de prova e ver o passo ficar vermelho, para e relata.
- **Dependência nova** (biblioteca de navegador, `PublicApiAnalyzers`): licença, manutenção e vulnerabilidade conferidas como em
  `.claude/rules/dependencies.md`, versão exata fixada, só em `devDependencies` ou `PrivateAssets=all`. Se qualquer uma entrar no
  `.nupkg`, no `.nuspec` ou no `dist/`, para.
- **Instável é pior que ausente.** Cada cenário novo roda 20 vezes seguidas, localmente ou no CI, sem falhar, antes do merge. Nenhum
  `continue-on-error`, nenhum `if: always()` que esconda falha, nenhum teste com `Skip`.
- **Tempo.** Se o job do pacote real, sozinho, passar de 15 minutos, para e pergunta. A Executora mede e diz o número.
- Se um cenário só passar mudando um bloco de código do README, **para**: o README é a fonte, e o job tem de provar o que está nele.

## Como a fase entrega

Cinco PRs, uma por parte, nesta ordem. Cada uma traz as suas provas vermelhas. A parte 6 é da Planejadora e já está escrita.

| PR | Tarefas | O que entra |
|---|---|---|
| 1 | 70.1 e 70.2 | O job do pacote real e o snapshot dos `.nuspec` |
| 2 | 70.3 | A demo construída no CI |
| 3 | 70.4 | A linha de base da API pública |
| 4 | 70.5 | O registro de guardas |
| (Planejadora) | 70.6 | A regra dos consumidores do contrato |

## Tarefa 70.1: o job do pacote real

No `ci.yml` **e** no `release.yml`, na mesma lista (D-183). No `release.yml` o job é pré-requisito da publicação (`needs`), para o
porteiro da publicação ficar igual ao do CI. Só no `ubuntu-latest`. Pode usar `needs: build-test` e baixar o `dist` já construído
em vez de construir o Angular de novo, se a Executora medir que compensa.

**Montagem.**

1. `dotnet pack` com `-p:Version=<versão base>-ci.<run_number>` (nunca o número real e nunca `-p:VersionSuffix`, D-153 e D-174),
   para uma pasta fora do repositório, usada como feed local. O restore do app usa um `NUGET_PACKAGES` próprio do job, para o
   cache não se contaminar.
2. `dotnet new webapi -f net10.0` numa pasta limpa fora do repositório (o `Directory.Build.props` da raiz não pode herdar no app).
3. **O Quick start sai do README por script, na ordem, sem editar nenhum bloco.** Um script em `scripts/` (Node, sem dependência,
   como os outros `.mjs`) lê o `README.md`. Os blocos são marcados na **linha de abertura da cerca**, sem HTML e sem texto visível:
   `csharp quickstart:program` para o arquivo inteiro e `csharp quickstart:registrations` para os comandos que entram antes de
   `builder.Build()`. O GitHub usa só a primeira palavra da linha de abertura para o realce, e a Executora confere isso na
   pré-visualização da PR. O script **falha** se um marcador sumir, para uma edição do README quebrar o CI e não desligar o teste
   em silêncio. Os `dotnet add package` do bloco `bash` do Quick start também saem do README, com `--version` e `--source` do feed.
4. O que o README deixa por conta do usuário (as interfaces `IOrderService` e `IOrderRepository` com as classes, um endpoint que as
   chame, um endpoint que lance exceção) o job acrescenta em **arquivos separados**, como o usuário faria.
5. `dotnet build -c Release`: **0 avisos e 0 erros**.
6. Sobe o app em Development e em Production (`ASPNETCORE_ENVIRONMENT`), em `127.0.0.1`, espera a prontidão, roda o roteiro HTTP
   (`scripts/`, Node, `fetch`) e derruba o app num passo que sempre roda.

**O roteiro.** Cada cenário cita a promessa de onde sai e o defeito que guarda. Os valores são os que o README e a Fase 56 já
prometem. Se um valor não bater, é achado, e a Executora para e relata.

| # | Ambiente | Afirma | Guarda |
|---|---|---|---|
| a | | O Quick start compila (`dotnet build`, 0 avisos) com os blocos colados na ordem | `CS1529` do Quick start (D-247) |
| b | Development | Uma requisição ao endpoint do app grava uma trace, e `GET /flow/api/traces` e `GET /flow/api/traces/{id}` a devolvem com a etapa `Endpoint` e as etapas de Service e Repository pela convenção | O grafo vazio (50.2), a etapa sem marcação (51) |
| c | Development | `GET /openapi/v1.json` não tem nenhum caminho que comece com `/flow` | As rotas do painel no OpenAPI do app (Fase 52) |
| d | Development | `GET /flow/api/config` com `Host: evil.example` dá **403**; com `Origin: http://evil.example` dá **403**; com o host do app dá 200 | O DNS rebinding (S6, #398) |
| e | Development | A página `/flow/` e as respostas da API trazem `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` e uma `Content-Security-Policy` com `frame-ancestors 'none'`. A da página tem `script-src 'self'` **sem** `'unsafe-inline'`. A própria resposta `403` do cenário d também traz os três | O iframe e a CSP (S7, #401) |
| f | Dev e Production | O cabeçalho `X-NetApiFlow-TraceId`: presente em Development; **ausente** em Production por padrão; presente em Production com `EmitTraceIdHeader=true` e `UseExceptionHandler`, também no 500; ausente no 500 de Production com a opção e **sem** tratador (o README diz isso) | O cabeçalho sem opção (50.6), no 500 (50.13, D-241) e o padrão só em Development (50.15) |
| g | Production | Sem `AllowAnonymous` nem autorização, o painel responde **404** (`/flow/` e `/flow/api/traces`), como o README promete | O painel aberto em Production |

Regra permanente: **todo achado da auditoria que dá para reproduzir de fora vira um cenário aqui**, de forma permanente (70.6).

**Prova vermelha, no branch de prova, vista no CI.** Obrigatórias, as duas que a auditoria pediu: (1) o `using NetApiFlow.Marking;`
no meio do Quick start dá vermelho no cenário a; (2) tirar o portão de `Host` dá vermelho no cenário d. Além delas, a Executora
mostra o vermelho de um cenário de cabeçalho (e ou f).

**Fora do job:** o MCP, o OpenTelemetry e os storages num app novo. O job cobre o que o Quick start instala (`NetApiFlow` e
`NetApiFlow.Dashboard`). Os outros pacotes num app novo ficam para a `0.7.0`, salvo se a auditoria decidir outra coisa.

## Tarefa 70.2: o snapshot dos `.nuspec`

No mesmo job, depois do `dotnet pack`. Um script em `scripts/` abre cada `.nupkg` do feed (um zip), lê o `.nuspec` e escreve uma
forma normalizada: o id, e, por grupo de `targetFramework`, cada dependência com a faixa de versão (`[8.0.10, )` e parecidas),
mais as `frameworkReferences`. A versão do próprio pacote sai, e a versão exata do núcleo nos satélites (D-231) vira `{version}`,
para só a mudança real aparecer. Compara com um arquivo aprovado por pacote, em `tests/nuspec-snapshot/<PackageId>.txt`, nos **9
pacotes** (`NetApiFlow`, `.Dashboard`, `.EntityFrameworkCore`, `.MediatR`, `.Mcp`, `.OpenTelemetry`, `.Storage.PostgreSql`,
`.Storage.SqlServer` e `.Testing`). Se difere, o job falha mostrando o diff e diz como atualizar (`--update`). Quem sobe um piso
passa a ver o diff do arquivo na própria PR.

**Prova vermelha:** subir o piso do EF Core em `Directory.Packages.props` (o erro da `0.5.1`, que fez o pacote exigir o EF Core
`9.0.22`) dá vermelho.

## Tarefa 70.3: a demo construída no CI

Um job (ou passos no `ci.yml`, à escolha da Executora), em todo PR que não seja só documentação. Se a Executora medir que o custo
pede um filtro de caminho, o filtro inclui `ClientApp/`, `scripts/generate-demo-fixtures.mjs`, o `demo-fixtures/` **e**
`src/NetApiFlow.Dashboard/Endpoints/`, porque a #416 nasceu de uma mudança de parâmetro na API.

1. `npm ci` e `npm run build:demo`.
2. Servir `dist/demo/browser` com um servidor estático de Node **com `Cache-Control: no-store`** (o `python -m http.server` não manda
   o cabeçalho, e o navegador pode mostrar o build antigo, `.claude/rules/design-quality.md`).
3. Abrir, num **navegador de verdade e sem janela**, a trace de cada entrada `api/traces/<id>` do manifest, e as telas
   `/history`, `/components` e `/runner`. Falha com qualquer erro de console, `pageerror`, pedido sem resposta ou resposta com status
   de erro. **O número de traces visitadas tem de ser igual ao do manifest**, e o job o imprime (`0 erros em N de N`), para um laço
   que visite menos não passar verde.
4. Biblioteca de navegador: `playwright-core` ou `puppeteer-core`, ambas Apache 2.0, usando o Chrome que o `ubuntu-latest` já traz,
   sem baixar navegador. A Executora mede qual cabe, aplica `.claude/rules/dependencies.md` e registra o motivo no resumo. Só em
   `devDependencies`, versão exata, nunca no `dist/`.
5. **A semântica, não só o carregamento.** Estender o `demo-fixture-coverage.spec.ts` (ou um teste novo) para, para cada chave de
   blueprint do manifest, conferir que o arquivo apontado é a última trace `Success` do mesmo método e da mesma rota normalizada
   que começou **estritamente antes**, e que cada chave em `noContent` não tem nenhuma. É a mesma regra do
   `scripts/generate-demo-fixtures.mjs` e da API, e é o que o D-251 pede.

**Prova vermelha:** voltar o manifest ao estado antes da #416 (blueprint sem a chave com `before`) dá vermelho com o 404 e o erro de
console; apagar um arquivo de fixture que o manifest cita dá vermelho.

## Tarefa 70.4: a linha de base da API pública

Entra **depois da 53** (para a extensão pública `WithoutNetApiFlowTracking` já estar na linha de base) e **antes da 55, da 64 a 66
e da 67**, para essas mostrarem o diff.

- **Escolha, por medição.** Primeiro, `Microsoft.CodeAnalysis.PublicApiAnalyzers`: dependência só de build, `PrivateAssets=all`,
  versão central em `Directory.Packages.props`, fora do `.nuspec`, então sem piso (D-181). A Executora mede a **menor** versão que
  restaura, compila **sem** `CS8032` e sem aviso com o SDK do `global.json` e com `net8.0;net10.0`. Se a diferença entre os dois
  TFMs der mais atrito do que um arquivo por TFM resolve, ou se nenhuma versão carregar, o plano B é um **teste de snapshot da
  superfície pública** por reflexão, sem dependência, com o arquivo aprovado por pacote em `tests/public-api/`. Qual dos dois, e
  por quê, vai no resumo.
- **Os arquivos.** `PublicAPI.Shipped.txt` é a superfície da última versão publicada (`v0.5.3`), gerada do tag. `PublicAPI.Unshipped.txt`
  é o que a `0.6.0` acrescentou até agora. As regras de adição e de remoção (`RS0016` e `RS0017` e as vizinhas) valem como **erro**
  (o repositório já usa `TreatWarningsAsErrors`).
- **No release.** A skill `/release` ganha um passo: mover o `Unshipped` para o `Shipped`. A Planejadora escreve o passo quando esta
  tarefa entrar.

**Prova vermelha:** acrescentar um membro público sem atualizar o arquivo dá vermelho; renomear um tipo público (o que a Fase 67
fará) dá vermelho.

## Tarefa 70.5: o registro de guardas

Uma tabela em `tests/regression-guards/guards.json`, **fora de `docs/plano/`**, porque toda PR que corrige um defeito a edita, e a
Executora não escreve em `docs/plano/`. Em inglês. Cada linha: `id`, `defect` (uma linha), `fixedIn` (PR ou versão), `guard`
(`kind` = `dotnet`, `vitest` ou `ci-scenario`; e o projeto ou arquivo e o nome do teste ou o id do cenário) e `job` (o job que o roda).

Um passo do CI, num script em `scripts/`, **falha** se:

- um teste `dotnet` listado não for encontrado (roda `dotnet test --no-build --filter "FullyQualifiedName=<nome>" --logger trx` e
  exige ao menos 1 teste e o resultado `Passed`: teste apagado ou renomeado dá 0, e `Skip` dá `NotExecuted`);
- um teste `vitest` listado sumir ou estiver pulado;
- um cenário `ci-scenario` listado não existir na lista de cenários do job do pacote real ou da demo.

**As linhas de partida** são os defeitos desta rodada: S1 a S8 (ver D-210, D-216, D-218, D-221 e as Fases 50, 54 e 56), B1 a B3
(Fase 63), F1 (Fase 57), o Quick start da #409, a demo da #416, o piso do EF da `0.5.1` e as opções da Fase 47 (D-176). A Executora
mapeia cada defeito à guarda que existe e **mostra o mapa**. **Defeito sem guarda não entra na tabela e não se inventa teste na
pressa:** vai para uma lista de lacunas no relatório, e a auditoria decide quais fecham na `0.6.0`.

**Prova vermelha:** apagar (e, em outro branch, marcar com `Skip`) um teste registrado dá vermelho no passo.

## Tarefa 70.6: a regra dos consumidores do contrato (Planejadora, já escrita)

`.claude/rules/contratos-e-consumidores.md`, que generaliza o D-251. A Executora acrescenta, na PR 1 da fase, **uma linha** ao
`.github/pull_request_template.md` apontando para essa regra.

## Fora de escopo

- O MCP, o OpenTelemetry e os storages num app novo (ver a 70.1). O orçamento de performance no CI (item 21 do roadmap). O runtime
  .NET 11 (Fase 69). O Windows no job do pacote real.
- Reescrever o que já tem teste. A fase **acrescenta** as redes que faltam e **registra** as que existem.

## Critério que vale para a fase inteira

- **Cada guarda vista falhando contra o defeito antigo, reintroduzido de propósito num branch de prova, e verde no `develop`.** O
  relatório cola, por guarda, o link da execução vermelha no CI.
- As vermelhas obrigatórias: o `using` no meio do Quick start; o portão de `Host` tirado; o piso do EF Core subido; o manifest sem
  a chave `before`; um membro público sem o arquivo; um teste registrado apagado.
- Build, testes, formato e lint limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- O tempo de cada job novo, medido, no relatório.

## Versionamento e fluxo

- `0.6.0`, logo depois da Fase 53. Branches `chore/ci-pacote-real`, `chore/ci-demo-construida`, `chore/ci-api-publica` e
  `chore/guardas-de-regressao`, a partir de `develop` atualizado. Commits pequenos, em inglês, uma linha, sem linha de coautor.
- Tem workflow e código: abrir a PR e parar, o merge é da auditoria.

## Relatório final

Por PR: as provas vermelhas com o link da execução do CI, o tempo de cada job, e a lista do que não coube. Na 70.4, qual das duas
formas foi escolhida e a medição que decidiu. Na 70.5, o mapa defeito para guarda e a lista de lacunas.
