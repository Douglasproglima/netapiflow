# O release da `0.6.0`: a checklist, passo a passo (D-292)

Quem segue: a `AUDITORIA`, com a `EXECUTORA-RELEASE` nos passos de código (D-272, D-273). Esta página junta a skill `/release` (`.claude/skills/release/SKILL.md`) com o que é específico da `0.6.0`. Em conflito, vale a skill
para o passo e esta página para o que a `0.6.0` muda. Nenhum passo público (push, PR, merge, tag, Release, advisory) sem a confirmação de quem pediu.

## Antes de abrir a branch: os portões

| # | Portão | Quem confere |
|---|---|---|
| 1 | A fila de fases fechada: 68 (fechada), 60 (Partes A e B, com a 60.12) e 59. Cada uma registrada em `decisoes.md` e com a linha do `README.md` do plano em "fechada" | A Planejadora registra, a auditoria fecha |
| 2 | A segunda passada feita, limpa nos dois níveis (D-212), com o que está na seção "A segunda passada" abaixo | A auditoria |
| 3 | A auditoria de base fechada (D-205, passo 0 da skill): o README frase a frase num app novo, o mapa de cobertura do CI, a matriz de autorização, a matriz de integrações, todas as telas e a demo, `PathBase` e proxy reverso | A auditoria. A Planejadora não fecha nenhum item no lugar dela |
| 4 | Os quatro advisories confirmados pelo dono, em `advisories-0.6.0.md` | O dono |
| 5 | A política de Trusted Publishing do nuget.org aponta para este repositório e para `release.yml` (tarefa 7.7). O job de publicação declara `id-token: write`, que o login exige | O dono ou a auditoria, no site do nuget.org |
| 6 | O `develop` com os 8 checks verdes no head, e `git status` limpo | A `EXECUTORA-RELEASE` |
| 7 | O `[Unreleased]` do `CHANGELOG.md` lido contra o que entrou: cada fase da `0.6.0` aparece, cada advisory tem a linha em `### Security`, e o que muda para quem atualiza está em `### Changed` | A auditoria lê, a Planejadora conferiu a forma |
| 8 | A Tarefa 62.4 (o site) pronta para o dia da publicação. Ela é **depois** da publicação, e não antes | A auditoria |

## Os passos

### A. A `EXECUTORA-RELEASE`, na branch `release/0.6.0` (passos 1 a 9 da skill)

1. `git status` limpo em `develop`, `git pull --ff-only`, e a versão confirmada: `0.6.0` (minor, pré-1.0).
2. **O teste com o pacote real (D-176), antes de abrir a branch.** Pacote empacotado de verdade com `-p:Version=0.6.0-verify.<n>` (nunca `0.6.0`, D-153 e D-174), em `tests/manual-verification/`: `AddNetApiFlow()` sem lambda, em `Development`, com storage real, uma requisição real.
   Mais o caminho do usuário (D-186): um `dotnet new webapi` novo, o README ao pé da letra no `net10.0` **e no `net8.0`** (o template do `net8.0` usa Swashbuckle, D-277), e cada tela do painel e da demo num navegador de verdade.
3. `git switch -c release/0.6.0`. No `CHANGELOG.md`, `[Unreleased]` vira `[0.6.0] - <data>`, e `[Unreleased]` fica vazio.
4. `<Version>` em `Directory.Build.props` para `0.6.0`, e no mesmo commit o `"version"` de `src/NetApiFlow.Dashboard/ClientApp/src/assets/demo-fixtures/config.json` (o `DemoFixtureVersionTests` deixa a PR vermelha se divergirem).
5. `dotnet pack NetApiFlow.slnx -c Release`: os 9 pacotes, `.nupkg` e `.snupkg`, com `0.6.0` no nome. Contar os 9.
6. **O que NÃO se mexe antes da tag.** `tests/real-package/previous-published-version.txt` fica em `0.5.3`, e `expected-differences.json` fica **cheio** (hoje 9 linhas): o job `comparison` roda na tag e faz parte do `needs` da publicação (D-260).
   `src/NetApiFlow.Mcp/CompatibilitySuppressions.xml` também fica. A baseline do `pack` (`PackageValidationBaselineVersion` em `src/Directory.Build.props`) lê o `previous-published-version.txt`, então o `pack` da `0.6.0` ainda compara com a `0.5.3`, e a supressão cobre o `CP0001` das tools do MCP que viraram `internal`.
7. Parar. Push e `gh pr create --base main` só com a confirmação explícita de quem pediu.

### B. A `AUDITORIA`, a PR para a `main`

8. A PR roda os checks. A `main` exige 5 (`pr-lint`, `secret-scan`, `detect-changes`, `build-test` no ubuntu e no windows). Olhar também `real-package / real-package`, `real-package / comparison` e `built-demo`, que rodam na PR mas ainda não são obrigatórios na `main`. Merge em squash.
9. **Logo depois do merge,** tornar obrigatórios na `main` os três checks `real-package / real-package`, `real-package / comparison` e `built-demo` (D-261, D-262, D-270), com a autorização do dono. A `main` passa de 5 para 8, igual ao `develop`.
   Ficaram de fora até aqui porque o `ci.yml` da `main` era o antigo, e um `hotfix/*` nasce da `main` e nunca produziria os checks. Conferir depois: `gh api repos/Douglasproglima/netapiflow/branches/main/protection/required_status_checks`.

### C. A tag e o `release.yml`

10. Confirmação explícita do dono, e a tag `v0.6.0` no commit do squash em `main`. A tag dispara o `release.yml`.
11. **Acompanhar o `release.yml` passo a passo.** Ele mudou muito desde a última tag que o exercitou (`v0.5.3`) e **nunca rodou nesta forma**. O que é novo, na ordem em que roda:
    - o job `real-package` (`uses: ./.github/workflows/real-package.yml`), que roda **antes** de tudo, com os jobs `real-package` e `comparison`. É o `needs` do job de publicação. Empacota com `0.6.0-ci.<n>`, e o `comparison` usa a lista cheia do `expected-differences.json`;
    - o job `pack-and-publish`, com `permissions` só nele (`id-token: write` e `packages: write`) e o resto do workflow com `contents: read`;
    - `11.0.x` no `setup-dotnet`, que flutua;
    - o `lint`, o `format:check` e o `test:ci` do painel, depois do build do painel e antes do build .NET;
    - os dez projetos de teste, com o `Npgsql10.IntegrationTests` e o `SqlClient.IntegrationTests`, e depois os mesmos dez no runtime .NET 11 contra o build de `net10.0`;
    - a publicação AOT do núcleo (`AotCoreVerification`), a do host `net8.0` (`AotCoreVerificationNet8`, que falha com qualquer `warning IL`) e a execução do binário nativo `net8.0`;
    - `dotnet pack NetApiFlow.slnx` (o nome do passo ainda diz "six packages", mas empacota os 9), `NuGet/login` (Trusted Publishing), o push para o nuget.org e o espelho no GitHub Packages, os dois com `--skip-duplicate`.
    **Onde falhar é seguro:** tudo até o push para o nuget.org não publica nada. Se a causa estiver fora do código (runner, rede, `setup-dotnet`), reexecuta-se o job. Se estiver no código, corrigir e mover a tag é decisão do dono e da auditoria, porque a tag já é pública, e nada disso se faz sem a confirmação deles. Falha **depois** de pacotes já publicados se reexecuta no mesmo job, sem mexer na tag: o `--skip-duplicate` pula o que já subiu.
12. Contar as linhas `Your package was pushed` no log: 3 por pacote (`.nupkg` e `.snupkg` no nuget.org, mais o espelho), **27**. Menos que isso quer dizer que algum pacote não subiu.
13. **Conferir o que foi publicado,** no nuget.org e não no feed local: os 9 pacotes em `0.6.0`; o `.nuspec` de cada um dos 8 satélites com `version="[0.6.0]"` na dependência do núcleo (`https://api.nuget.org/v3-flatcontainer/<id>/0.6.0/<id>.nuspec`); o README e o ícone de cada página; os símbolos.
14. **O pacote publicado num app novo.** `dotnet new webapi`, `dotnet add package NetApiFlow --version 0.6.0` do nuget.org (com o `nuget.config` limpo, sem fonte local), o Início rápido do README no `net10.0` e no `net8.0`, o painel aberto e uma requisição rastreada.

### D. Depois da tag (a `AUDITORIA`, e a `EXECUTORA-RELEASE` no último passo)

15. A página de Release (passo 11 da skill): `gh release create v0.6.0 --title v0.6.0 --notes-file <corpo>`, o corpo sendo a seção `[0.6.0]` do `CHANGELOG.md`. O `release.yml` nunca cria a página.
16. Os quatro advisories, na ordem e com a condição de `advisories-0.6.0.md`.
17. A Tarefa 62.4: o quick start e os números do site conferidos contra os pacotes `0.6.0` publicados, e o site atualizado no mesmo dia.
18. O back-merge: PR de `main` para `develop`, com **"Create a merge commit"**, nunca squash, senão a PR perde o vínculo de ancestralidade (passo 12 da skill).
19. **A PR de `chore/*` no `develop`, só depois da publicação e do back-merge (D-260), com estes quatro itens juntos** (passo 13 da skill):
    - `tests/real-package/previous-published-version.txt` para `0.6.0`;
    - `tests/real-package/expected-differences.json` para `[]`;
    - em cada pacote, as linhas do `PublicAPI.Unshipped.txt` **na tag** (`git show v0.6.0:src/<Pacote>/PublicAPI.Unshipped.txt`) vão para o `PublicAPI.Shipped.txt`, e uma linha `*REMOVED*` tira a linha dela do `Shipped` e some (D-265);
    - apagar `src/NetApiFlow.Mcp/CompatibilitySuppressions.xml` (D-278).
    **A versão e a supressão vão na mesma PR,** porque a baseline do `pack` lê o `previous-published-version.txt`. Trocar a versão para `0.6.0` sem apagar o arquivo faz o `pack` falhar com "Supressões desnecessárias encontradas", que é erro (medido pela auditoria). Apagar o arquivo sem trocar a versão faz o `pack` do `NetApiFlow.Mcp` falhar com `CP0001` contra a `0.5.3`. Um sem o outro quebra o CI do ciclo seguinte.
20. A Planejadora registra o release (README do plano e `decisoes.md`) e abre a `0.7.0` com o roadmap.

## A segunda passada: o que ela cobre

A auditoria refaz com o pacote real, num app novo (`dotnet add package`), o que as fases entregaram com um critério pendente. A lista que ela já tem, e o que o plano acrescenta.

| # | O quê | A prova |
|---|---|---|
| 1 | Fases 50 e 51: o Quick start colado na ordem do leitor | Compila e funciona no `net10.0` e no `net8.0` com Swashbuckle (D-277). R-31 e R-32 viram Mitigado |
| 2 | Fase 53: `PathBase` e proxy reverso | Com `UsePathBase` e o pacote real, `ExcludePaths` respeitado e as rotas do painel não viram traces. A matriz de hospedagem da 53.0 (IIS in-process, IIS out-of-process e proxy com `X-Forwarded-Prefix`, D-205) conferida contra o que a Executora mediu. R-33 vira Mitigado |
| 3 | Fase 55: a identidade da trace | Nos dois storages, o mesmo `traceparent` não apaga a trace anterior e um cliente anônimo não sobrescreve outro |
| 4 | Fase 56, S6 e S7 | `Host` e `Origin` alheios dão 403 no painel e no `/flow/mcp` (POST `initialize`). CSP, `X-Frame-Options` e `nosniff` em `/flow`. O painel atrás de proxy ou com host declarado. **É a condição do advisory 3** (D-249) |
| 5 | Fase 57: F1a e F1b | A rota da trace vem do `EndpointMatched`, o estado "Aguardando" aparece e o blueprint é o último sucesso anterior |
| 6 | Cinco linhas sorteadas do `guards.json` (184 hoje) | O teste de cada linha guarda mesmo o defeito dela |
| 7 | **O plano acrescenta:** as Fases 60, 59 e 68 depois de prontas | 60 Partes A e B (com a 60.12), 59 (a 59.5, o README frase a frase nos dois idiomas, o `net8.0` e o `405` de `GET` e `DELETE` em `/flow/mcp` em `Production`) e a 68 já conferida |
| 8 | **O plano acrescenta:** a Fase 54, os segredos | O README do plano a lista "mergeada" e sem critério pendente. A auditoria diz se a conferiu com o pacote real (OTLP e painel, os 13 casos e as 12 formas do advisory 2) para a Planejadora a marcar fechada |
| 9 | **O plano acrescenta:** o inventário de base do D-205 | Cada um dos seis itens do portão 3, com o que já fechou e o que falta |
| 10 | **O plano acrescenta:** o `release.yml` | Os passos 11 a 13 acima, na tag |

**O que a Planejadora faz depois dela:** marcar R-29 e R-30 (Fases 48 e 49, publicadas na `0.5.0` e na `0.5.1`, ainda `Planejado` na tabela) e R-31, R-32 e R-33 como Mitigado, citando o arquivo de teste que prova cada um, depois de conferir que ele existe e é real; fechar no README as linhas das Fases 50, 51, 53, 55, 56 e 57 (e a 54, se a auditoria disser); e registrar a 60, a 59 e o release.
