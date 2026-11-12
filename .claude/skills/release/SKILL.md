---
name: release
description: Calcula a próxima versão SemVer, abre release/<versao> a partir de develop, prepara o CHANGELOG e o PR para main. Use quando o grupo de fases planejado estiver pronto para publicar no nuget.org.
argument-hint: "[major|minor|patch]"
---

Prepare a release: $ARGUMENTS (padrão: `minor`, a menos que haja breaking change na API pública
listada em `docs/plano/contratos.md` desde a última release, caso em que é `major`, mas
`MAJOR` fica em `0` até a `1.0.0`, seção 12 do `netapiflow-prompt.md`, então antes da v1.0.0 uma
"major" vira incremento de `MINOR`).

1. Rode `git status` em `develop`. Working tree precisa estar limpo.
2. Leia `CHANGELOG.md`, seção `[Unreleased]`, e `docs/plano/README.md` para confirmar quais fases
   foram concluídas desde a última tag (`git tag --sort=-v:refname | head -1`).
3. Calcule a versão SemVer seguinte (`docs/gitflow.md`, seção "Versionamento"). Confirme com quem
   pediu antes de prosseguir se houver ambiguidade entre `minor` e `patch`.
4. Crie a branch:
   ```bash
   git switch develop
   git pull --ff-only
   git switch -c release/<versao>
   ```
5. Atualize `CHANGELOG.md`: mova o conteúdo de `[Unreleased]` para `[<versao>] - <data>`, deixe
   `[Unreleased]` vazio para a próxima rodada.
6. Confira que os 8 pacotes NuGet compilam e empacotam (`NetApiFlow`, `NetApiFlow.Dashboard`,
   `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`, `NetApiFlow.Mcp`,
   `NetApiFlow.OpenTelemetry`, `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`):
   `dotnet pack NetApiFlow.slnx -c Release`.
7. Pare aqui e peça confirmação explícita antes de `git push` e `gh pr create --base main`. Push,
   PR e tag são ações públicas (ver `.claude/rules/git-workflow.md`).
8. Depois do merge em `main` (squash): criar a tag `v<versao>` (**só com confirmação explícita**),
   que dispara `release.yml` (Trusted Publishing, sem API key guardada). Confirme antes de criar
   a tag que a política de Trusted Publishing já foi configurada no nuget.org (tarefa 7.7 em
   `docs/plano/fase-7-documentacao.md`); se for a primeira release, esse passo manual precisa ter
   sido feito antes.
9. Depois da tag: merge de `main` de volta em `develop` (PR ou fast-forward, conforme o grupo
   preferir).

Nunca criar a tag nem publicar sem confirmação explícita de quem pediu, mesmo que os passos
anteriores já tenham sido aprovados.
