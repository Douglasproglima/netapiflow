# Fase 49: piso do EF Core e política de pisos das dependências (D-181, D-182, D-183)

**Hotfix `0.5.1`.** O `NetApiFlow.EntityFrameworkCore` publicado pede EF Core 9 e quebra um app em EF
Core 8. Branch `hotfix/0.5.1-efcore-floor` a partir de `main` (tag `v0.5.0`), PR para `main`, tag
`v0.5.1`, página de Release no GitHub e merge de `main` de volta em `develop`, tudo como diz
`docs/gitflow.md`, seção "Hotfix".

Hotfix pelo critério do D-141: falha reproduzida, PATCH, nenhuma mudança de API pública. O dano é de
adoção: qualquer app .NET 8 com EF Core 8 fica de fora do pacote. O D-177 ("não existe 0.5.1")
decidia onde entrava a Fase 47, antes de a `0.5.0` existir, e não vale aqui.

A fase também leva duas mudanças de configuração que só funcionam em `main`: o `dependabot.yml`
(Tarefa 49.6) e o `release.yml` (Tarefa 49.7). Nenhuma das duas altera o conteúdo de um pacote.

## Ponto de parada: regra de recuo

A correção é uma linha de versão mais os testes que a provam. Se a Executora vir qualquer um destes
sinais, **ela para e relata, sem contornar**:

- Um teste existente do interceptor falhando quando roda em EF Core 8.0.10. Quer dizer que o código usa
  algo do EF 9 que a medição da Planejadora não pegou.
- Um projeto de teste que passa a falhar o `restore` com `NU1605` (downgrade) e que um override
  condicional de versão, como o do `NetApiFlow.SqlClient.IntegrationTests`, não resolve.
- O `.nuspec` de qualquer um dos nove pacotes mudando em algo além do que a Tarefa 49.4 lista.

## O achado

O `.nuspec` da `0.5.0` publicada pede, nos grupos `net8.0` e `net10.0`,
`Microsoft.EntityFrameworkCore` e `Microsoft.EntityFrameworkCore.Relational` `>= 9.0.22`. A causa é
`Directory.Packages.props`: a versão que está lá serve de build e de piso publicado ao mesmo tempo.
O `9.0.22` é a versão com que o repositório compila, não a que o código exige.

Um app em EF Core 8 com provedor do EF 8 tem o EF Core elevado a 9 pelo restore, e o provedor, que foi
compilado contra o EF 8, quebra em execução com `TypeLoadException`.

Reproduzido duas vezes:

- Pela Executora, com `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.4.
- Pela Planejadora, com `Microsoft.EntityFrameworkCore.Sqlite` 8.0.34 sob EF Core 9.0.22:
  `TypeLoadException: Method 'get_LockReleaseBehavior' in type 'SqliteHistoryRepository'`.

**O código nunca precisou do EF 9.** Medição da Planejadora, num projeto descartável fora do
repositório, com o `FlowDbCommandInterceptor` real, sem nenhuma alteração, e um `DbContext` com Sqlite
(`EnsureCreated`, `SaveChanges` e uma consulta, quatro steps `Database` esperados):

| Interceptor compilado contra | Roda com EF Core | Runtime | Resultado |
|---|---|---|---|
| 8.0.0 | 8.0.0 | `net8.0` | 4 steps |
| 8.0.0 | 8.0.34 | `net8.0` | 4 steps |
| 8.0.0 | 9.0.23 | `net8.0` | 4 steps |
| 8.0.0 | 9.0.23 | `net10.0` | 4 steps |
| 8.0.0 | 10.0.15 | `net10.0` | 4 steps |
| 8.0.10 | 8.0.10 | `net8.0` | 4 steps |
| 8.0.10 | 9.0.23 | `net8.0` | 4 steps |
| 8.0.10 | 10.0.15 | `net10.0` | 4 steps |

Compila sem aviso contra EF Core Relational 8.0.0, 8.0.10, 8.0.34, 9.0.0 e 9.0.22.

## A decisão do piso (D-182): `8.0.10`, igual nos dois TFMs

Não é `8.0.0`. O `dotnet list package --vulnerable --include-transitive` num projeto em EF Core
Relational 8.0.0 aponta `Microsoft.Extensions.Caching.Memory` 8.0.0, severidade alta
(GHSA-qj66-m88j-hmgj). A `8.0.0` até a `8.0.8` dependem dele. A `8.0.9` não existe no nuget.org. A
`8.0.10` depende da `8.0.1` e passa limpa. O piso com vulnerabilidade conhecida não serve de piso
(`.claude/rules/dependencies.md`).

Um piso só para `net8.0` e `net10.0`. Um app em `net10.0` com EF Core 8 é legítimo. EF Core 6 e 7
estão fora de suporte e abaixo do `net8.0`, então não entram.

## Tarefa 49.0: reproduzir contra o pacote publicado, antes de corrigir

Antes de qualquer mudança, o app de `tests/manual-verification/EfCoreFloorVerification` (Tarefa 49.3)
roda contra o `NetApiFlow.EntityFrameworkCore` `0.5.0` **do nuget.org**, com `net8.0` e
`Microsoft.EntityFrameworkCore.Sqlite` 8.0.10, e a Executora cola no resumo o `TypeLoadException`.
Sem a falha vista contra o pacote real, a correção não está provada.

## Tarefa 49.1: o piso

`Directory.Packages.props`: `Microsoft.EntityFrameworkCore` e `Microsoft.EntityFrameworkCore.Relational`
de `9.0.22` para `8.0.10`. Mais nada muda nesse arquivo. O `Microsoft.EntityFrameworkCore.Sqlite`
`9.0.22` e o `.SqlServer` `9.0.22`, que são só de teste, ficam: é por eles que a suíte continua
provando "compila no piso, roda no EF 9".

Conferir que todo projeto de teste ainda restaura e compila. Os projetos que trazem EF Core pelo
Sqlite ou pelo SqlServer resolvem o EF 9 por dependência transitiva, o que o NuGet aceita sem
`NU1605`. Se algum projeto referenciar `Microsoft.EntityFrameworkCore.Relational` direto e quebrar,
usar o mesmo recurso do `NetApiFlow.SqlClient.IntegrationTests`: um `PackageVersion` condicional.

**Pronto quando:** `dotnet build NetApiFlow.slnx` e `dotnet test` limpos nos dois TFMs.

## Tarefa 49.2: um projeto de teste fixo em EF Core 8.0.10

Projeto novo, `tests/NetApiFlow.EntityFrameworkCore.Ef8Tests`, que prova no CI o caso que quebrava:
o pacote funcionando sobre o próprio piso. Sqlite em memória, sem Docker, então roda em todos os
sistemas. O EF Core do projeto fica em `8.0.10` por um `PackageVersion` condicional em
`Directory.Packages.props` para o `Microsoft.EntityFrameworkCore.Sqlite`, como o do
`NetApiFlow.SqlClient.IntegrationTests`.

O que o projeto prova, com um `DbContext` de verdade e o `FlowDbCommandInterceptor` anexado:

- uma consulta grava um step `Database` com `Component` `EntityFrameworkCore`;
- um `SaveChanges` com `INSERT` grava o seu;
- o interceptor anexado duas vezes ao mesmo contexto continua gravando um step por comando.

A Executora reaproveita o molde dos testes que já existem para o interceptor em
`tests/NetApiFlow.Tests`, sem copiar a classe inteira se um arquivo linkado resolver.

**Pronto quando:** o projeto passa em `net8.0` e `net10.0`, e a Executora mostra que ele **falha** se o
`PackageVersion` do Sqlite for trocado por um que arraste o EF Core 9 sobre o provedor 8 (a prova de
que o teste testa o piso, não o acaso).

## Tarefa 49.3: teste com o pacote real

`tests/manual-verification/EfCoreFloorVerification`, no molde das receitas anteriores
(`OptionsFromConfigVerification` e `NpgsqlCommandInsideStepVerification`): `verify.sh`, `global.json` e
uma seção própria no `README.md` da pasta com os comandos exatos. Pacote empacotado de verdade,
`-p:Version=0.5.1-verify.<n>`, nunca o número real, nunca `-p:VersionSuffix`, `<n>` novo a cada rodada
(`.claude/rules/packaging.md`). `AddNetApiFlow()` sem lambda, em `Development`. O app usa Sqlite,
então não precisa de container, e uma requisição de verdade passa por um controller que consulta o
`DbContext` com o interceptor.

| TFM do app | EF Core e provedor | Esperado |
|---|---|---|
| `net8.0` | 8.0.10 (`Microsoft.EntityFrameworkCore.Sqlite`) | Um span `Database` `EntityFrameworkCore`, sem exceção |
| `net8.0` | 9.0.x | O mesmo |
| `net11.0` (resolve `lib/net10.0`) | 10.0.x | O mesmo |

O terceiro caso é o único lugar do plano em que o EF Core 10 é exercitado. Não é CI, é a receita.

Rodar também `dotnet list package --vulnerable --include-transitive` **sem filtro de `grep`** em cada
app e colar a saída literal no resumo.

## Tarefa 49.4: conferir os `.nuspec`

Depois do `dotnet pack` de verificação:

- `NetApiFlow.EntityFrameworkCore`: nos dois grupos de TFM, `Microsoft.EntityFrameworkCore` e
  `Microsoft.EntityFrameworkCore.Relational` com `version="8.0.10"`.
- Os outros oito pacotes: a lista de dependências igual à do `.nuspec` publicado da `0.5.0`
  (`https://api.nuget.org/v3-flatcontainer/<id minúsculo>/0.5.0/<id minúsculo>.nuspec`), exceto a
  versão do `NetApiFlow` que os pacotes pedem. Colar a comparação no resumo.

Isso prova que o hotfix mexe só na dependência do EF Core.

## Tarefa 49.5: documentação pública

Achar toda frase sobre o EF Core 9 com `git grep -n "EF Core 9"` e trocar. Hoje o aviso está em:

- `README.md` e `README.pt-BR.md`: o parágrafo que diz que o pacote exige EF Core 9 ou mais novo, no
  `net8.0` também, e que um app em EF Core 8 falha com `TypeLoadException`. Passa a dizer que o pacote
  funciona com EF Core 8.0.10 ou mais novo. Dizer "testado em 8, 9 e 10" só se a Tarefa 49.3 tiver
  rodado nos três.
- `src/NetApiFlow.EntityFrameworkCore/README.md`, o README do próprio pacote.
- `docs/architecture.md`: "The package requires EF Core 9 or later, on `net8.0` as well."

Conteúdo equivalente nos dois idiomas, sem travessão.

`CHANGELOG.md`, seção `[0.5.1]`, em `### Fixed`, em termos de usuário: o pacote pedia EF Core 9 mesmo
no `net8.0`, então um app em EF Core 8 com provedor do EF 8 falhava com `TypeLoadException` assim que
o pacote era instalado. Agora pede EF Core 8.0.10, a primeira versão da linha 8 sem vulnerabilidade
conhecida, e funciona com EF Core 8, 9 e 10. Sem número de tarefa nem de fase.

## Tarefa 49.6: o `dependabot.yml`

No `.github/dependabot.yml`, no ecossistema `nuget`, o bloco `ignore` atual (que só ignora a versão
principal do `MediatR`) é **substituído** por este:

```yaml
    ignore:
      # Piso publicado: só sobe à mão, com motivo (.claude/rules/dependencies.md).
      - dependency-name: "MediatR"
      - dependency-name: "Microsoft.Data.SqlClient"
      - dependency-name: "Microsoft.EntityFrameworkCore*"
      - dependency-name: "Microsoft.Extensions.FileProviders.Embedded"
      - dependency-name: "ModelContextProtocol*"
      - dependency-name: "Npgsql*"
      - dependency-name: "OpenTelemetry*"
```

Os ecossistemas `github-actions` e `npm` não mudam. Cada entrada sem `update-types` ignora toda
atualização, e a atualização de segurança não é afetada (`target-branch` em `develop`, ver
`.claude/rules/dependencies.md`).

Conferir depois do merge em `main`: aba Insights, Dependency graph, Dependabot, última verificação
sem erro de sintaxe. O curinga `*` está documentado para `allow`, e o `ignore` o aceita pelo que a
Planejadora pôde confirmar. Se o Dependabot reclamar do curinga, trocar pelas entradas por nome:
`Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`,
`Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.SqlServer`,
`ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, `Npgsql`,
`Npgsql.EntityFrameworkCore.PostgreSQL`, `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Exporter.OpenTelemetryProtocol` e `OpenTelemetry.Exporter.InMemory`.

**A PR #316 não se mergeia.** O dono a fecha (como fez com a #318 e a #320). Os bumps só de teste que
ela traz voltam no próximo ciclo do Dependabot, já sem os pacotes de piso.

## Tarefa 49.7: o `release.yml` roda a mesma lista do `ci.yml`

No passo `Test` do `.github/workflows/release.yml`, a lista fica assim:

```yaml
      - name: Test
        run: |
          set -e
          dotnet test tests/NetApiFlow.Tests -c Release --no-build
          dotnet test tests/NetApiFlow.IntegrationTests -c Release --no-build
          dotnet test tests/NetApiFlow.EntityFrameworkCore.Ef8Tests -c Release --no-build
          dotnet test tests/NetApiFlow.Storage.Tests -c Release --no-build
          dotnet test tests/NetApiFlow.Npgsql.IntegrationTests -c Release --no-build
          dotnet test tests/NetApiFlow.SqlClient.IntegrationTests -c Release --no-build
```

No `ci.yml`, o `NetApiFlow.EntityFrameworkCore.Ef8Tests` entra **antes** do bloco que só roda fora do
Windows, porque não usa Docker.

O workflow que roda é o que está no commit da tag, então o `release.yml` do próprio `v0.5.1` já usa a
lista nova. Risco aceito (D-183): um teste instável de container barra a publicação e exige reexecutar
o job.

## Critério que vale para a fase inteira

- Build, testes, formato e lint limpos nos dois TFMs, incluindo os projetos de interop rodados de
  verdade, com Docker, e o `Ef8Tests`.
- `tests/manual-verification/AotCoreVerification` continua com zero aviso.
- A comparação dos `.nuspec` da Tarefa 49.4 mostrando que só a dependência do EF Core mudou.

## Fora de escopo

- Revisar os outros pisos contra o princípio (OpenTelemetry 1.20.1, `ModelContextProtocol.AspNetCore`
  2.3.1, Npgsql 8.0.5 do storage, MediatR 12.5.0, `FileProviders.Embedded`): item 18 do roadmap (revisar cada piso
  publicado), um por vez, medindo como esta fase mediu o EF Core.
- EF Core 10 no CI (item 19 do roadmap, EF Core 10 na suíte automática): a receita da Tarefa 49.3 o
  exercita à mão, e a suíte automática continua em EF 9.
- O piso do `Microsoft.Data.SqlClient` (`5.2.2`, D-172) não muda.
- `Microsoft.SourceLink.GitHub` (PR #319): só de build, não entra no `.nuspec`, decisão à parte.

## Versionamento e fluxo

- Branch `hotfix/0.5.1-efcore-floor` a partir de `main`. O `pr-lint` só aceita minúscula, dígito,
  ponto e hífen no nome.
- O número `0.5.1` entra em `Directory.Build.props` como nos hotfixes anteriores
  (`git log -p -3 -- Directory.Build.props` mostra como foi feito), e a seção `[0.5.1]` do
  `CHANGELOG.md` leva a data do dia.
- Teste com o pacote real (Tarefa 49.3) antes da PR, como pede o `docs/gitflow.md`.
- Commits pequenos, em inglês, uma linha, sem linha de coautor.
- Ao terminar: abrir a PR contra `main` e parar. Tem código e mexe em workflow, então o merge, a tag e
  a página de Release exigem confirmação do dono.
- O merge de `main` de volta em `develop` pode conflitar em `Directory.Packages.props`,
  `.github/workflows/ci.yml` e `CHANGELOG.md`, porque `develop` andou. A Executora resolve mantendo o
  piso `8.0.10`, a lista de testes das duas workflows e as duas seções do changelog.

## Relatório final

O `TypeLoadException` contra o pacote `0.5.0` do nuget.org, e a mesma receita passando com o pacote
local, nos três casos da Tarefa 49.3. A saída literal do `--vulnerable` de cada app. A comparação dos
nove `.nuspec`. O `Ef8Tests` passando e a prova de que ele falha com o EF 9 sobre o provedor 8. O
resultado do `AotCoreVerification`. Qual arquivo de teste prova o R-30. O número da PR, a confirmação de que o `release.yml` do `v0.5.1`
rodou a lista nova, e a última verificação do Dependabot sem erro depois do merge em `main`.
