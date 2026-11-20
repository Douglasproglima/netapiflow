# Decisões que esperam o dono

Arquivo vivo. A Planejadora prepara as opções e diz o que recomenda, mas **não decide** o que está aqui.
Quando o dono decide, a decisão vira uma linha em `decisoes.md` e o item sai deste arquivo.

## 1. Veículo da Fase 50

A Fase 50 tem duas partes (`fase-50-auditoria-do-caminho-do-usuario.md`).

| Parte | O que muda | SemVer |
|---|---|---|
| A | Rotas internas fora do OpenAPI, aviso no grafo vazio, conta do painel, OpenAPI fora do rastreamento, demo | PATCH, nenhuma API pública nova |
| B | `NetApiFlowOptions.EmitTraceIdHeader` e `AutoCreateSchema` nos dois storages | MINOR, duas opções públicas |

Opções:

1. **A como `0.5.2`, B no `0.6.0`.** O achado (a) chega a quem publica o documento OpenAPI mais cedo.
   Custo: mais um hotfix, quando a própria auditoria aponta a cadência como problema (13 versões em
   30 dias, 4 hotfixes em 2 dias).
2. **A e B juntas no `0.6.0`.** Uma versão só, menos ruído para quem acompanha. Custo: os achados da
   Parte A, o (a) incluído, ficam no pacote publicado até lá.
3. **A como `0.5.2` só se o (a) for considerado urgente**, e o resto no `0.6.0`.

Recomendação: a opção 2, com a exceção do item 4 abaixo. O (a) é ruim para quem publica o documento
(rota interna e um `DELETE` no Swagger e nos SDKs gerados), mas não é falha de segurança: as rotas
respondem 404 fora de Development. Correção que não é urgente vai junto com outras (D-185).

## 2. TFMs: `net8.0;net10.0;net11.0` (ADR-0023) ou manter só dois

A auditoria do zero contradiz o ADR-0023. Os dois lados, sem mudar o ADR.

**Lado do ADR-0023 e do D-173 (três TFMs, `net11.0` é o item principal do `0.6.0`).**

- `net11.0` ganha um asset próprio e o `#if` já fica pronto para quando existir API exclusiva do 11.
- Fecha o furo de sucessão do ADR-0003: o ADR-0023 registra que `net8.0` já saiu de suporte e que
  `net12.0` só deve lançar em novembro de 2027.
- Já está decidido, com quatro opções comparadas e a Fase 38 investigando antes.
- Custo: a matriz de CI, o `dotnet pack` e a suíte rodam uma terceira vez. O núcleo hoje não usa
  nenhuma API exclusiva do 11, então o asset novo seria igual ao do `net10.0`.

**Lado da auditoria (manter `net8.0` e `net10.0`).**

- Não adicionar `net11.0` como alvo próprio enquanto o código não usar API exclusiva do 11.
- Rodar a suíte no runtime .NET 11 contra o asset do `net10.0`. Isso já foi testado com o pacote real
  (D-173: um app `net11.0` consumindo o `lib/net10.0`, com storage real, funciona).
- Anunciar quando `net8.0` sai, em vez de depender de `net12.0` existir.
- Custo: contradiz o ADR-0023 e o D-173, e o item 16 do roadmap deixa de ser o item principal do
  `0.6.0`. Quem estiver em `net11.0` resolve o asset do `net10.0`, o que o .NET faz por padrão.

O que muda no plano conforme a escolha: o item 16 do roadmap, o D-173 e a lista de alvos de
`Directory.Build.props`. Escolhido o lado da auditoria, um ADR novo substitui o ADR-0023.

## 3. Quando divulgar de novo

A auditoria recomenda divulgar só depois de três coisas: o README corrigido **na `main`** (item 4), o
achado (a) e o achado (b) resolvidos. E com o enquadramento "ferramenta de desenvolvimento, versão
`0.x`". Hoje o produto já foi divulgado, e quem chega pelo Quick start antigo vê o grafo vazio.

Recomendação: seguir a auditoria. O (a) e o aviso do (b) são a Parte A da Fase 50, então a data
depende do veículo escolhido no item 1.

## 4. O README corrigido ainda não chegou à página do repositório

O Quick start corrigido (PR #325) está só no `develop`. A página do GitHub mostra o README da branch
padrão, a `main`, e ali o Quick start antigo continua: `git diff origin/main origin/develop -- README.md
README.pt-BR.md` mostra 17 e 21 linhas de diferença. A correção só aparece depois do próximo release
ou hotfix.

Opções:

1. **PR de documentação direto para `main`**, numa branch `hotfix/*` sem número de versão e sem tag. É
   uma exceção ao `docs/gitflow.md`, onde todo hotfix tem versão e tag, e vale pelo efeito imediato.
   Depois, merge de `main` de volta em `develop`.
2. **Esperar o próximo release.** Sem exceção ao fluxo, mas o Quick start antigo fica visível até lá.

Recomendação: a opção 1, uma vez, e registrar a exceção numa decisão. O Quick start é a primeira
coisa que o produto mostra, e o custo é uma PR só de documentação.
