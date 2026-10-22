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

1. Criar `release/<versao>` a partir de `develop`.
2. Atualizar o `CHANGELOG.md` (mover "Unreleased" para a versão, formato Keep a Changelog).
3. Abrir PR de `release/<versao>` para `main`. Merge com **squash**.
4. Criar a tag `v<versao>` no commit de `main`. A tag dispara `release.yml`: empacota os seis
   pacotes e publica no nuget.org via Trusted Publishing (OIDC, sem API key guardada).
5. Fazer merge de `main` de volta em `develop`.

Metadados e regras de empacotamento NuGet em [.claude/rules/packaging.md](../.claude/rules/packaging.md).
A skill `/release` do Claude Code guia esse processo.

## Hotfix

1. Criar `hotfix/<versao>` a partir de `main`, com o `PATCH` incrementado.
2. Corrigir, atualizar o `CHANGELOG.md`.
3. PR para `main` (squash), tag `v<versao>`.
4. Merge de `main` de volta em `develop`.

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
