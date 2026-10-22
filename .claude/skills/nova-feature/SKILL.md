---
name: nova-feature
description: Cria uma branch feature/*, bugfix/* ou chore/* a partir de develop atualizado, seguindo o gitflow do NetApiFlow. Use quando a Executora for começar uma tarefa de fase ou correção nova.
argument-hint: "<nome-da-feature> [bugfix|chore]"
---

Crie a branch para: $ARGUMENTS

1. Rode `git status`. Se houver alteração não commitada, pare e pergunte o que fazer com ela.
2. Monte o nome: `feature/<nome>` (padrão), `bugfix/<nome>` ou `chore/<nome>` se o argumento
   indicar. Minúsculo, sem acento, palavras separadas por hífen, em inglês quando possível (ex.:
   `feature/mediatr-pipeline-behavior`).
3. Atualize a base:
   ```bash
   git switch develop
   git pull --ff-only
   ```
4. Crie a branch: `git switch -c <tipo>/<nome>`.
5. Se existir issue relacionada (`gh issue list --search "<nome>"`), mostre o número para usar
   nos commits.
6. Diga a branch criada e lembre o formato de commit: Conventional Commits em inglês
   (`feat(core): ...`, `fix(dashboard): ...`), ver `docs/gitflow.md`.
7. Se a branch corresponde a uma tarefa de `docs/plano/fase-N-*.md`, aponte o arquivo de fase
   relevante para a Executora ler antes de começar.

Não faça push agora. O push acontece quando a pessoa pedir para abrir o PR.
