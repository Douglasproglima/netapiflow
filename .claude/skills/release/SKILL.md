---
name: release
description: Calcula a próxima versão SemVer, abre release/<versao> a partir de develop, prepara o CHANGELOG e o PR para main. Use quando o grupo de fases planejado estiver pronto para publicar no nuget.org.
argument-hint: "[major|minor|patch]"
---

Prepare a release: $ARGUMENTS (padrão: `minor`, a menos que haja breaking change na API pública
listada em `docs/plano/contratos.md` desde a última release, caso em que é `major`, mas
`MAJOR` fica em `0` até a `1.0.0`, seção 12 do `netapiflow-prompt.md`, então antes da v1.0.0 uma
"major" vira incremento de `MINOR`).

0. **Para a `0.6.0`:** confirme com a `AUDITORIA-16-11` que a auditoria de base está fechada
   (`.claude/rules/planning.md`, D-205). Sem esse fechamento, pare e relate. Confirme também a Tarefa 62.4 (o site conferido contra os pacotes `0.6.0` e atualizado no mesmo dia).
1. Rode `git status` em `develop`. Working tree precisa estar limpo.
2. Leia `CHANGELOG.md`, seção `[Unreleased]`, e `docs/plano/README.md` para confirmar quais fases
   foram concluídas desde a última tag (`git tag --sort=-v:refname | head -1`).
3. Calcule a versão SemVer seguinte (`docs/gitflow.md`, seção "Versionamento"). Confirme com quem
   pediu antes de prosseguir se houver ambiguidade entre `minor` e `patch`.
4. **Teste obrigatório com o pacote real em `tests/manual-verification/`, antes de abrir a branch (D-176).** Pacote
   empacotado de verdade (nunca `ProjectReference`), com `-p:Version=<próxima>-verify.<n>`
   (`.claude/rules/packaging.md`, D-174; nunca `-p:VersionSuffix=verify`, que não funciona neste
   repositório). Rodar `AddNetApiFlow()` **sem nenhuma lambda**, em `Development`, com storage
   real configurado (Postgres ou SQL Server via Testcontainers/instância local), fazendo pelo
   menos uma requisição real. Foi o teste com o pacote real, não a suíte automatizada, que achou
   tanto o `MissingMethodException` do `MediatR` (D-140) quanto o bug de opção ligada só por
   configuração (D-175): os dois passavam limpo em `dotnet test NetApiFlow.slnx`, que roda contra
   `ProjectReference`/`InMemoryTraceStore`, não contra o pacote publicado com a configuração mais
   simples que um usuário real usaria. Achado real aqui vira achado antes do release, não depois.
   A receita pronta está em `tests/manual-verification/README.md`, na seção "Options set only in
   configuration" (app `OptionsFromConfigVerification`). Rodar os comandos de lá com um
   `-verify.<n>` novo, nos dois alvos e nos dois modos, sem reescrever nada.
   **E o caminho do usuário (D-186).** Criar um app novo com `dotnet new webapi` e seguir o README ao
   pé da letra, sem completar nada de cabeça. Conferir o documento OpenAPI do app: nenhuma rota do
   NetApiFlow nele. Usar cada tela do dashboard e da demo num navegador de verdade (Playwright), não só
   conferir status HTTP: a auditoria que achou sete problemas (D-185) passou pelas rodadas anteriores
   porque elas conferiam só diffs, CI e status.
5. Crie a branch:
   ```bash
   git switch develop
   git pull --ff-only
   git switch -c release/<versao>
   ```
6. Atualize `CHANGELOG.md`: mova o conteúdo de `[Unreleased]` para `[<versao>] - <data>`, deixe
   `[Unreleased]` vazio para a próxima rodada.
7. Atualize `<Version>` em `Directory.Build.props` para `<versao>`. Não é automático: sem este
   passo, `dotnet pack` continua gerando os pacotes com a versão anterior (achado real na
   primeira vez que esta skill rodou até o fim, v0.2.0). No mesmo commit, atualize `"version"` em
   `src/NetApiFlow.Dashboard/ClientApp/src/assets/demo-fixtures/config.json` para o mesmo número:
   o diálogo Sobre da demo pública lê esse valor, e o teste `DemoFixtureVersionTests` deixa o PR
   vermelho se os dois divergirem.
8. Confira que todos os pacotes NuGet compilam e empacotam na versão nova. O total é o número de
   projetos de `src/` com `PackageId` (hoje 9: `NetApiFlow`, `NetApiFlow.Dashboard`,
   `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`, `NetApiFlow.Mcp`,
   `NetApiFlow.OpenTelemetry`, `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`,
   `NetApiFlow.Testing`): `dotnet pack NetApiFlow.slnx -c Release`, conferindo o número de versão
   no nome de cada `.nupkg`/`.snupkg` gerado. Pacote novo em `src/` entra nessa conta e na
   conferência do passo 10.
9. Pare aqui e peça confirmação explícita antes de `git push` e `gh pr create --base main`. Push,
   PR e tag são ações públicas (ver `.claude/rules/git-workflow.md`).
10. Depois do merge em `main` (squash): **só na `0.6.0`, a `AUDITORIA-16-11` torna `real-package / real-package` e `real-package /
    comparison` checks obrigatórios na `main`** (D-261, D-262). Os dois já são obrigatórios no `develop`. A `main` ficou de fora até aqui
    de propósito: o `ci.yml` dela ainda é o antigo, sem o job, e um `hotfix/*` nasce da `main` e nunca produziria os checks, então
    torná-los obrigatórios antes travaria qualquer `0.5.x`. A configuração é da auditoria, com a autorização do dono. Depois disso,
    criar a tag `v<versao>` (**só com confirmação explícita**),
    que dispara `release.yml` (Trusted Publishing, sem API key guardada). Confirme antes de criar
    a tag que a política de Trusted Publishing já foi configurada no nuget.org (tarefa 7.7 em
    `docs/plano/fase-7-documentacao.md`); se for a primeira release, esse passo manual precisa ter
    sido feito antes. Depois que o `release.yml` terminar, conte as linhas `Your package was
    pushed` no log: são três por pacote (`.nupkg` e `.snupkg` no nuget.org, mais o espelho no
    GitHub Packages), ou seja, 3 vezes o total do passo 8 (hoje 27). Menos que isso quer dizer que
    algum pacote não subiu.
11. `release.yml` só publica pacote, nunca cria a página de Release no GitHub. Sem este passo, a
    tag existe e o nuget.org recebe o pacote, mas `gh release list` continua mostrando a versão
    anterior como "Latest" (achado real, v0.2.0 e v0.3.0 ficaram sem Release por duas rodadas
    seguidas). Criar com `gh release create v<versao> --title v<versao> --notes-file <corpo>`,
    corpo sendo a seção `[<versao>]` que acabou de sair de `CHANGELOG.md`.
12. Depois da tag: merge de `main` de volta em `develop`. `main` e `develop` têm proteção de
    branch (push direto é rejeitado pelo próprio GitHub), então isso sempre é PR, nunca
    fast-forward direto. Mergear com "Create a merge commit" (nunca squash), senão a PR perde o
    vínculo de ancestralidade que o merge deveria estabelecer.
13. **Depois da publicação e do back-merge, nunca antes da tag (D-260),** num PR de `chore/*` no `develop`: apontar
    `tests/real-package/previous-published-version.txt` para a versão nova e esvaziar `tests/real-package/expected-differences.json`
    (`[]`). A tag publica com a lista cheia, porque o job `comparison` roda na tag e faz parte do `needs` da publicação: esvaziar
    antes travaria o release. Esvaziar logo depois é o que impede o primeiro CI do ciclo seguinte de cair, porque a regra "linha que
    não aparece mais falha" acusaria cada linha velha.

Nunca criar a tag nem publicar sem confirmação explícita de quem pediu, mesmo que os passos
anteriores já tenham sido aprovados.
