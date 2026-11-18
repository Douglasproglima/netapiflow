# Fase 46: Responsividade de verdade em `/history`, `/components` e `/runner`

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em `src/NetApiFlow`,
nenhum pacote opcional, nenhum `.csproj`, nenhuma mudança em `docs/plano/contratos.md`.

Depende da Fase 43 estar pronta e aceita: a cadeia `html`/`body`/`app-root` troca `height: 100vh`
fixo por `min-height: 100vh` ali, e é uma mudança global, não restrita a `/flow`. Esta fase parte
de uma base que já permite a página rolar quando o conteúdo pedir mais altura do que a viewport
tem; investigar as outras três telas antes dessa correção landing geraria achado presa à mesma
causa raiz que a Fase 43 já resolve, sem precisar de nada novo.

## Origem

As Fases 43/44 nasceram de um achado real em `/flow`, visto ao vivo pelo dono do projeto. Ao
revisar o plano, o pedido foi expandido para o dashboard inteiro: "o layout tem que ser
responsivo igual ao Bootstrap... usabilidade real em resoluções de tela diferentes." O modelo de
breakpoints discretos do guia (seção 5.2, só documentado para `/flow`) está certo em espírito; o
que falta é confirmar, com a mesma régua de prova por geometria real das Fases 43/44, que
`/history`, `/components` e `/runner` se comportam bem no mesmo tipo de varredura, não só em `/flow`.

Diferente das Fases 43/44, **não existe achado prévio confirmado para estas três telas**. Esta
fase começa investigando, não corrigindo (mesmo padrão de "investigação antes do código" já usado
em Fase 19/23): é possível que a varredura não encontre nada, e isso é um resultado válido, não
uma fase mal aproveitada.

## Tarefa 46.1: varredura sistemática das três telas

Para cada uma de `/flow/history`, `/flow/components`, `/flow/runner`, mesma régua da Fase 43:

- Largura: um ponto perto de cada borda e um no meio de cada faixa da seção 5.2 do guia (1600,
  1920, 2560 · 1200, 1366, 1599 · 1024, 1100, 1199 · 768, 900, 1023 · 320, 360, 393, 480, 767).
- Altura: pelo menos um ponto curto (`×700`) e um confortável (`×900`) em cada largura relevante,
  cruzando os dois limiares de altura da Fase 43 (800px/720px) onde fizer sentido para a tela
  (uma tela sem o mesmo tipo de card fixo pode não ter o mesmo limiar; medir o que existe de
  verdade em cada tela, não assumir que o número de `/flow` se repete).
- Redimensionamento contínuo (arrastar, não só pular entre valores) atravessando cada faixa.
- Tema claro e escuro.
- Com dado real: `/history` com volume suficiente para paginação real aparecer, `/components`
  com mais de um componente, `/runner` com uma lista de endpoints real (sample padrão do
  repositório, mesma régua das Fases 43/44).

Cada achado real confirmado por geometria (`getBoundingClientRect`/`elementFromPoint`, não só
inspeção visual): elemento cortado, sobreposto, rolagem horizontal da página, texto truncado,
botão fora da área clicável, tabela/grade que não se adapta à largura disponível.

**Pronto quando:** as três telas varridas pela matriz acima, documentado em
`tests/manual-verification/ResponsiveSweepHistoryComponentsRunner/README.md` (mesmo padrão dos
READMEs já existentes em `tests/manual-verification/`), com a lista de achados reais (se houver)
ou a confirmação explícita de que nenhum apareceu.

## Tarefas seguintes: uma por achado real

Esta fase não lista tarefas de correção de antemão, porque não existe achado prévio. Cada achado
real confirmado na Tarefa 46.1 vira uma tarefa própria (46.2, 46.3, ...), com o mesmo padrão das
Fases 43/44: descrição do achado, correção, critério de pronto medido por geometria real antes e
depois.

**Nenhum achado teórico ou suspeita de leitura de código vira correção sem reprodução** (D-141):
se a varredura não reproduzir um problema, ele não entra nesta fase, mesmo que pareça plausível.
Registrar como item de `docs/plano/roadmap-valor.md` para investigação futura, não corrigir "só
por garantia".

## Fora de escopo

- Qualquer mudança em `/flow`, `/flow?trace=`: coberto pelas Fases 43/44.
- Redesenho de `/history`, `/components` ou `/runner` além do necessário para os achados reais da
  Tarefa 46.1. É correção de responsividade, não oportunidade de redesenho.

## Aceite da fase

Tarefa 46.1 completa, com o README de varredura documentado. Se houver achados, os critérios de
pronto de cada tarefa de correção adicional. `npx impeccable detect` nas três telas, tema claro e
escuro, nos pontos de largura da varredura que também façam parte dos dois viewports exigidos por
`.claude/rules/design-quality.md`. Contagem de avisos antes e depois no resumo final; qualquer
achado novo de `low-contrast`/`undersized-ui-text` bloqueia a fase.

## Ao terminar

Resumo para a Planejadora: o resultado da varredura (achados reais ou nenhum), o que foi corrigido
por tarefa, como verificar, o que ficou pendente.
