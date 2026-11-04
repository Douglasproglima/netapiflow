# ADR-0001: Gitflow clássico com SemVer, não `VERSAO.ANOMES.RELEASE.FIX`

## Status
Aceito

## Issue
[#61](https://github.com/Douglasproglima/netapiflow/issues/61)

## Contexto
O NetApiFlow é distribuído como pacote NuGet público. Outros projetos pessoais do autor (Palpitt)
usam gitflow com versão no formato `VERSAO.ANOMES.RELEASE.FIX`, mas esse formato não carrega
semântica de compatibilidade: um consumidor de pacote não consegue saber, só pelo número, se uma
atualização quebra a API pública.

O `netapiflow-prompt.md` (seção 12) já manda "versionamento semântico, começar em 0.1.0, tratar a
API como instável até 1.0.0".

## Decisão
Gitflow clássico (`main`, `develop`, `feature/*`, `bugfix/*`, `chore/*`, `release/*`,
`hotfix/*`), com **SemVer** (`MAJOR.MINOR.PATCH`) em vez do formato por data usado no Palpitt.
Os seis pacotes são versionados e publicados juntos, na mesma tag `v<versao>`.

## Consequências
- NuGet resolve range de versão (`>= 0.1.0, < 1.0.0`) com o significado padrão do ecossistema .NET.
- Breaking change exige decisão consciente de subir `MAJOR` (ou `MINOR` enquanto `MAJOR` for `0`),
  não um incremento automático de contador de data.
- Divergência proposital do padrão usado em outros projetos do autor: documentado aqui para não
  ser "corrigido" de volta por hábito.
