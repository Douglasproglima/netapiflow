# Gitflow do NetApiFlow

## Branches

| Branch | Nasce de | Volta para | Como |
|---|---|---|---|
| `main` | | | Produção. Só recebe `release/*` e `hotfix/*`. Nunca commit direto. |
| `develop` | `main` | | Integração. Base de todo trabalho do dia a dia. |
| `feature/<nome>` | `develop` | `develop` | PR com checks de CI verdes, squash merge. |
| `bugfix/<nome>` | `develop` | `develop` | Igual a `feature/*`. Correção sem urgência. |
| `chore/<nome>` | `develop` | `develop` | Governança, CI, dependências, configuração. Sem mudar comportamento do pacote. |
| `release/<versao>` | `develop` | `main` e `develop` | Só ajuste final: changelog, bump de versão, bug de última hora. |
| `hotfix/<versao>` | `main` | `main` e `develop` | Bug urgente em produção. |

Nome de branch em minúsculo, palavras separadas por hífen: `feature/mediatr-pipeline-behavior`,
`bugfix/sse-reconnect-duplicate-events`.

## Fluxo de uma feature

```bash
git switch develop
git pull
git switch -c feature/minha-feature
# ... commits ...
git push -u origin feature/minha-feature
gh pr create --base develop
```

O CI precisa passar antes do merge.

## Release

1. Teste com o pacote real (empacotado de verdade) em `tests/manual-verification/`, antes de abrir
   qualquer branch (D-176): `AddNetApiFlow()` sem lambda, em `Development`, com storage real,
   versão de verificação (`-p:Version=<próxima>-verify.<n>`, nunca o número real).
2. Criar `release/<versao>` a partir de `develop`.
3. Atualizar o `CHANGELOG.md` (mover "Unreleased" para a versão, formato Keep a Changelog).
4. Abrir PR de `release/<versao>` para `main`. Merge com **squash**.
5. Criar a tag `v<versao>` no commit de `main`. A tag dispara `release.yml`: empacota todos os
   pacotes de `src/` e publica no nuget.org via Trusted Publishing (OIDC, sem API key guardada).
6. Fazer merge de `main` de volta em `develop`.
7. **Depois da publicação, nunca antes da tag (D-260):** num PR de `chore/*` no `develop`, apontar
   `tests/real-package/previous-published-version.txt` para a versão nova e esvaziar
   `tests/real-package/expected-differences.json` (`[]`). A tag publica com a lista cheia, porque o job `comparison` roda nela.

Metadados e regras de empacotamento NuGet em [.claude/rules/packaging.md](../.claude/rules/packaging.md).
A skill `/release` do Claude Code guia esse processo.

## Hotfix

1. Criar `hotfix/<versao>` a partir de `main`, com o `PATCH` incrementado.
2. Corrigir, atualizar o `CHANGELOG.md`, subir o `Version` do `Directory.Build.props` e o `version` de
   `src/NetApiFlow.Dashboard/ClientApp/src/assets/demo-fixtures/config.json`, que o
   `DemoFixtureVersionTests` compara com o `Directory.Build.props` (D-193).
3. Teste com o pacote real (empacotado de verdade) em `tests/manual-verification/`, antes da PR: mesma
   regra do passo 4 da skill `/release` (D-176), `-p:Version=<próxima>-verify.<n>`,
   `AddNetApiFlow()` sem lambda, em `Development`, com storage real e pelo menos uma requisição.
   Os dois bugs que motivaram a regra (o `MissingMethodException` do `MediatR`, D-140, e as opções
   ligadas só por configuração, D-175) foram achados assim, os dois em contexto de hotfix.
4. PR para `main` (squash), tag `v<versao>`.
5. `release.yml` só publica pacote, nunca cria a página de Release no GitHub (mesma lacuna que a
   skill `/release` já documenta no passo 11, achada ali em `v0.2.0`/`v0.3.0`). Sem este passo
   manual, a tag existe e o nuget.org recebe o pacote, mas `gh release list` continua mostrando a
   versão anterior como "Latest": aconteceu de verdade com `v0.4.1` a `v0.4.4`, quatro hotfixes
   seguidos sem Release nenhum, porque nenhum dos dois (`release.yml` e o resumo curto que existia
   aqui, sem este passo) cobria o passo. Criar com
   `gh release create v<versao> --title v<versao> --notes-file <corpo> --target main`, corpo
   sendo a seção `[<versao>]` que acabou de sair de `CHANGELOG.md`.
6. Merge de `main` de volta em `develop`.

**A comparação com o pacote publicado, no hotfix (D-260, D-262).** O primeiro commit do branch do hotfix aponta
`tests/real-package/previous-published-version.txt` para a versão que está **na última tag da `main`** naquele momento
(`git describe --tags --abbrev=0 main`), e não para a última lida no nuget.org na hora, para o hotfix não depender de rede nem de um publish
paralelo, e esvazia `tests/real-package/expected-differences.json`. As diferenças do próprio hotfix entram na lista, com a entrada do
CHANGELOG que as anuncia. Assim o hotfix é comparado com o que está em produção. **No back-merge para o `develop`,** a versão anterior
passa a ser a do hotfix, e a lista fica com a do `develop` (as diferenças do ciclo dele) sem as linhas que só o hotfix acrescentou:
esvaziá-la por inteiro derrubaria o primeiro CI do `develop`, porque ela tem as diferenças do ciclo seguinte. O conflito de merge em
`expected-differences.json` se resolve ficando com a lista do `develop`.

**Se o `develop` mudou o `.gitleaks.toml` depois da última release, o hotfix leva a mesma mudança (D-239).** O `secret-scan` do CI varre o histórico de **todas as refs**, o `develop` incluído, com a config da branch que está sendo testada. Uma `[allowlist]` que só existe no `develop` faz o scan da `main` falhar com achados do histórico do `develop`. Copiar o arquivo inteiro do `develop` evita o conflito no back-merge.

**Exceção: hotfix só de documentação (D-191).** Um PR cujo diff inteiro é documentação que o público vê
na página do repositório (o `README.md` e o `README.pt-BR.md`) pode ir para `main` por uma branch
`hotfix/*` **sem número de versão e sem tag**, seguido do merge de `main` de volta em `develop`. O dono
confirma cada uso. Qualquer outro caso continua exigindo versão e tag.

## Versionamento: SemVer (D-01)

Exemplo: `0.1.0`, depois `0.1.1` (patch), `0.2.0` (minor), `1.0.0` (primeira versão estável).

| Campo | Significado | Quando muda |
|---|---|---|
| `MAJOR` | Quebra de compatibilidade na API pública | Decisão da Planejadora, nunca automático. Fica em `0` até a v1.0.0 (seção 12 do prompt) |
| `MINOR` | Funcionalidade nova, compatível | A cada `release/*` que adiciona algo |
| `PATCH` | Correção, compatível | A cada `hotfix/*` ou `release/*` só de bugfix |

Os seis pacotes NuGet (`NetApiFlow`, `NetApiFlow.Dashboard`, `NetApiFlow.EntityFrameworkCore`,
`NetApiFlow.MediatR`, `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`) são
versionados juntos, na mesma tag, mesmo que só um tenha mudado. Simplifica a matriz de
compatibilidade que o usuário precisa entender.

## Mensagens de commit

Conventional Commits, em inglês, uma linha. Escopo opcional entre parênteses, geralmente o
pacote (`core`, `dashboard`, `ef-core`, `mediatr`, `storage-postgres`, `storage-sqlserver`) ou a
área (`ci`, `docs`).

| Prefixo | Uso |
|---|---|
| `feat` | Funcionalidade nova |
| `fix` | Correção de bug |
| `refactor` | Mudança de código sem mudar comportamento |
| `test` | Só testes |
| `docs` | Só documentação |
| `chore` | Build, CI, dependências, configuração |

Exemplos: `feat(core): add FlowStep manual marking API`,
`fix(dashboard): rewrite base href under reverse proxy path base`.

O título da PR segue o mesmo formato, porque vira a mensagem do squash em `develop`. O CI valida
isso (`pr-lint.yml`, criado na Fase 0, tarefa 0.9).

## Proteção de branches no GitHub

Configurar em Settings → Branches (ou Rulesets) para `main` e `develop`:

- Exigir Pull Request antes do merge.
- Exigir os checks do CI passando: `build-test` (matrix Linux/Windows), `secret-scan`, `pr-lint`.
- Bloquear force push e deleção.
- Em `main`, permitir só squash merge.

Em repositório privado no plano gratuito, o GitHub mostra a regra como "Not enforced". Este
projeto é público, então a proteção vale de verdade assim que configurada.
