# Fase 44: Layout do workspace `/flow`, mobile em modo consulta

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em `src/NetApiFlow`,
nenhum pacote opcional, nenhum `.csproj`, nenhuma mudança em `docs/plano/contratos.md`.

Depende da Fase 43 estar pronta e aceita: o mecanismo de scroll de página real, o piso do grafo, o
rodapé que cede por altura, a minilinha do sparkline e a rolagem única do "Try an endpoint" são a
base estrutural compartilhada. Esta fase constrói a camada mobile em cima dela, não reimplementa
nenhum dos mecanismos da Fase 43.

## Decisão de escopo: mobile é modo consulta (D-159, precisa de aprovação antes de iniciar)

O caso de uso real do mobile é abrir, no celular, um link `?trace=` compartilhado por alguém,
não operar o dashboard inteiro com o mesmo propósito do desktop. Esta fase assume **modo
consulta**: o mobile precisa mostrar bem o grafo, os detalhes do nó e o replay. Runner,
Arquitetura e Insights podem ter experiência simplificada, sem paridade completa com desktop.

Essa é uma decisão de escopo de produto, não só de layout: limita o que "suporte mobile" significa
daqui para a frente. Registrar em `decisoes.md`, com data, só depois de confirmação explícita, não
antes.

## Tarefa 44.1: topbar compacta abaixo de ~480px

Achado real: a barra superior não quebra linha hoje; tema e chip "Live" ficam fora da tela, com
rolagem horizontal da página inteira (nenhuma rolagem horizontal é permitida em nenhum critério
de aceite deste produto).

1. Tema e idioma entram num menu (ícone único) abaixo de ~480px.
2. "Try an endpoint" vira só ícone (sem rótulo de texto) no mesmo breakpoint.
3. Confirmar que nenhum elemento da topbar força `scrollWidth` da página a exceder `innerWidth` em
   `360×800` e `393×852`.

**Pronto quando:** `document.documentElement.scrollWidth <= window.innerWidth` nos dois viewports
mobile, tema e idioma acessíveis pelo menu, nenhuma perda de funcionalidade.

## Tarefa 44.2: grafo antes das métricas

Achado real: hoje o usuário rola por seis cartões de métrica e pelo sparkline antes de ver o
grafo, em qualquer viewport mobile. No modo consulta, o grafo é o motivo de abrir o link.

1. Reordenar: grafo primeiro, métricas depois, abaixo de um breakpoint mobile (mesmo critério de
   largura já usado em outros pontos do guia, `max-width: 767px`).
2. Métricas em faixa com rolagem horizontal, ou recolhidas por padrão com um controle para expandir
   (a Executora decide qual das duas, documentando o motivo no resumo da fase).

**Pronto quando:** em `360×800` e `393×852`, o grafo aparece sem rolar a página, confirmado por
`getBoundingClientRect()` do canvas do grafo mostrando `top < window.innerHeight` sem scroll.

## Tarefa 44.3: grafo legível em mobile, sem o layout horizontal forçado

Achado real: o grafo mantém o layout horizontal em mobile hoje (quatro nós em ~300px de largura,
texto microscópico, nós colados no rodapé do canvas, resto do canvas vazio).

Depende da Tarefa 43.5 (orientação automática por proporção do canvas): em retrato, o canvas
mobile é naturalmente mais alto que largo, então `rankdir: 'TB'` já deveria se aplicar sozinho.
Esta tarefa confirma que o critério de proporção da Fase 43 também produz um resultado legível em
mobile de verdade, não só tecnicamente correto.

1. Confirmar altura mínima de fonte em pixels de tela para o texto do nó (definir o valor exato,
   documentar o motivo).
2. Confirmar que `rankdir: 'TB'` evita o layout horizontal forçado em `360×800`/`393×852` com um
   trace de 4 spans e com um trace de 20+ spans.

**Pronto quando:** texto do nó legível sem zoom manual (critério objetivo de tamanho mínimo em
pixels de tela, não "parece legível"), grafo ocupa o canvas disponível sem nós colados numa borda
e o resto vazio.

## Tarefa 44.4: ações do card num menu "⋯" quando não couberem

Achado real: as ações do card ("Back to live", "Replay") vazam à direita em mobile, e o título
"Request path" some.

1. Abaixo de um breakpoint mobile, ações que não couberem na linha do cabeçalho entram num menu
   "⋯" (mesmo padrão de menu compacto da Tarefa 44.1).
2. O título "Request path" nunca desaparece: se precisar de espaço, as ações cedem primeiro.

**Pronto quando:** em `360×800`/`393×852`, nenhuma ação vaza para fora do card, "Request path"
sempre visível, confirmado por `getBoundingClientRect()` de cada elemento do cabeçalho contra a
largura do card.

## Tarefa 44.5: regressão da base estrutural (Fase 43) em viewport mobile

Confirmar que os mecanismos da Fase 43 seguem corretos em mobile, não só em desktop:

- Rodapé cede por altura (Tarefa 43.3): abas únicas abaixo do limiar de altura também em mobile.
- Sparkline dentro do cartão (Tarefa 43.4): não aparece distorcida em `metric-cards` com 2
  colunas (`max-width: 767px`, grade já existente).
- "Try an endpoint" com rolagem única (Tarefa 43.6): botão "Send request" sempre visível também
  em mobile.
- Valores nunca truncados (Tarefa 43.7): mesmo critério, mesmos valores de teste.

**Pronto quando:** os quatro pontos acima confirmados por geometria real em `360×800` e
`393×852`, tema claro e escuro.

## Fora de escopo

- Paridade completa de Runner, Arquitetura e Insights em mobile: fora do modo consulta decidido
  nesta fase. Pode virar item de roadmap próprio, não parte desta fase.
- Qualquer mecanismo novo de layout que não exista já na Fase 43: esta fase é só a camada mobile
  sobre a base, não um segundo conjunto de mecanismos paralelo.

## Aceite da fase

Critérios de pronto das cinco tarefas acima, medidos por geometria real, tema claro e escuro, nos
modos ao vivo e replay, com o trace de 4 spans do sample e com um trace de pelo menos 20 spans.

**Varredura por faixa, mesma régua da Fase 43 (guia seção 5.1, pedido explícito do usuário).**
Largura, um ponto perto de cada borda e um no meio da faixa `< 768` do guia (5.2): 320 (menor
dispositivo real comum), 360, 393, 480, 767 (borda). Os dois viewports originais do achado
(`360×800`, `393×852`) continuam fazendo parte do conjunto. Altura, pelo menos um ponto curto
(`×700`) e um confortável (`×900`) cruzado com uma das larguras acima, confirmando que os dois
limiares de altura da Fase 43 (800px/720px) também disparam corretamente em viewport estreito.

Além dos pontos discretos, redimensionar a janela continuamente através da faixa `< 768`,
confirmando que nada quebra em nenhuma largura intermediária não listada.

`npx impeccable detect` nas mesmas quatro telas da Fase 43, nos pontos de largura acima. Contagem
de avisos antes e depois no resumo final; qualquer achado novo de `low-contrast`/
`undersized-ui-text` bloqueia a fase.

R-24 já fecha na Fase 43 (Tarefa 43.1, causa estrutural é a cadeia `html`/`body`/`app-root`, não
depende de largura de tela). Esta fase só confirma, como regressão, que o scroll de página real
continua funcionando nos dois viewports mobile depois das mudanças desta fase (Tarefa 44.5).

## Ao terminar

Resumo para a Planejadora: o que foi feito por tarefa, como verificar, o que ficou pendente.
Atualizar `docs/plano/riscos.md` (fechar R-24) é tarefa da Planejadora depois de revisar esta
fase, não da Executora.
