# Fase 43: Layout do workspace `/flow`, base estrutural e desktop

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em `src/NetApiFlow`,
nenhum pacote opcional, nenhum `.csproj`, nenhuma mudança em `docs/plano/contratos.md`.

Origem: achado real do dono do projeto, visto ao vivo em três monitores próprios (1920×1080,
2560×1080, 1366×728), confirmado por medição de geometria (`getBoundingClientRect`), não só
inspeção visual (mesmo padrão exigido pelo hotfix 0.4.4, PR #285). D-159 corrige duas imprecisões
do diagnóstico original antes desta fase existir: os números da seção 5.1 do guia nunca bateram
com o código real, e a fase fecha R-24 e R-28, não "a causa raiz do R-24" como uma leitura
anterior supôs. D-160 trava três decisões de forma, já levadas à crítica da skill `impeccable`
antes de qualquer tarefa abaixo, já refletidas em `docs/design/flow-workspace/guide.md` (seções
5.1, 6.2, 6.3, 6.4, 6.9 e a nota de correção da seção 8).

Esta fase é a base: o mecanismo de scroll de página, o piso do grafo, o rodapé que cede, a
minilinha do sparkline e a rolagem única do "Try an endpoint" são usados tanto em desktop quanto
em mobile. A Fase 44 (mobile, modo consulta) depende desta fase estar pronta antes de começar.

Separada do hotfix 0.4.4 (PR #285): esse hotfix já corrige o recorte do botão de play da barra de
replay (`.flow-workspace__graph-area` isolando o que pode encolher do que não pode). Tarefa 43.1
confirma que esse fix continua válido depois do piso novo de 360px, não o reimplementa.

## Tarefa 43.1: scroll de página de verdade abaixo de uma altura mínima (fecha R-24)

Achado real, confirmado por medição: em `1366×728`, o card central (`.flow-workspace__center`)
chega a ~70px de altura e **o grafo não aparece**. Causa: nada na cadeia
`html`/`body`/`app-root` → `.flow-workspace` → `.flow-workspace__body` → `.flow-workspace__center`
garante um mínimo; tudo cede proporcionalmente até sumir.

1. `.flow-workspace__center` ganha `min-height: 360px` de verdade (não `clamp`, não percentual).
   Abaixo desse valor o grafo deixa de ser útil; é o piso que decide quando outra coisa precisa
   ceder primeiro.
2. A cadeia `html`/`body`/`app-root` (ou o que for o ancestral de altura fixa hoje) troca
   `height: 100vh` por `min-height: 100vh`. Isso permite a página crescer além da viewport quando
   o conteúdo pedir mais do que ela tem, em vez de clipar.
3. Dois limiares de `window.innerHeight`, derivados da soma dos custos fixos (guia seção 5.1):
   **abaixo de 800px**, o rodapé vira abas únicas (Tarefa 43.3); **abaixo de 720px**, mesmo com o
   rodapé em aba única, a página rola de verdade. São números arredondados da conta, não medidos
   pixel a pixel contra o DOM renderizado: confirmar contra a renderização real antes de travar no
   código, documentando no resumo se o valor final divergiu do calculado e por quê.
4. **Não mexer na correção do R-27 (D-114):** `.flow-workspace` continua `height: 100%` (não
   `min-height: 100%`). R-27 resolve um problema diferente (dar altura real para os filhos
   calcularem porcentagem contra); a página por baixo poder crescer não muda esse contrato. Leia
   o comentário que já existe em `.flow-workspace { height: 100% }` antes de tocar ali.
5. Ordem de quem cede, da última pra primeira coisa que perde espaço: rodapé (Tarefa 43.3) cede
   primeiro, até seu próprio mínimo; se ainda não couber, a página rola. O grafo nunca é o
   primeiro a ceder.

**Pronto quando:** em `1366×728`, com e sem trace, o grafo aparece inteiro ou a página rola até
ele aparecer, nunca os dois ao mesmo tempo (nem sumido, nem cortado). `getBoundingClientRect()` de
`.flow-workspace__center` mostra altura `>= 360px` em qualquer viewport testada. Nenhuma regressão
no R-27: com as mesmas 248 rotas reais que validaram R-27 (D-116), o painel de ação continua
limitado por `max-height`/`overflow-y: auto`, não volta a esticar a página.

## Tarefa 43.2: investigação do grafo cortado em `2560×1080`

Achado real: em `2560×1080` sobram ~210px de folga na tela e, mesmo assim, **nós do grafo saem
cortados embaixo do canvas**. É o oposto do problema da Tarefa 43.1 (sobra espaço, não falta), e a
causa não está confirmada.

Investigar antes de qualquer correção, mesmo padrão de "investigação antes do código" já usado em
Fase 19/23: medir o `viewBox` real calculado por `computeGraphViewBox`
(`flow-graph-viewbox.ts`, Fase 25/D-107) contra essa proporção específica de canvas, e confirmar
se o cálculo de auto-fit assume um teto de altura que não existe mais depois da Tarefa 43.1, ou se
é um problema pré-existente, não relacionado a esta fase.

**Pronto quando:** causa raiz identificada e documentada (mesmo que a correção em si seja pequena)
e nós visíveis por inteiro em `2560×1080`, com e sem replay, confirmado por
`getBoundingClientRect()` de cada nó contra os limites do canvas.

## Tarefa 43.3: rodapé cede por altura (fecha o bug real do D-159, direção do D-160)

Hoje `.flow-workspace__footer` é `flex: 0 0 220px` sempre. Cada cartão (`Execuções recentes`,
`Eventos ao vivo`) já tem uma seta de recolher (`collapsed` signal, chevron que gira), mas
colapsar um não devolve altura nenhuma ao grafo, porque o pai nunca muda de tamanho: confirmado no
código antes desta fase (D-159), bug real, não hipótese.

1. Abaixo de **800px de `window.innerHeight`** (Tarefa 43.1), o rodapé troca de "dois cartões lado
   a lado" para **abas únicas**: "Execuções recentes" | "Eventos ao vivo", uma de cada vez, só a
   aba ativa ocupando altura. Acima do limiar, os dois continuam lado a lado como hoje.
2. Corrigir a seta de recolher existente pra efetivamente devolver altura ao grafo quando usada
   (hoje não devolve): vira um ajuste manual por cima do automático, útil mesmo em tela alta se o
   usuário quiser mais espaço para o grafo num caso específico.
3. Memória entre sessões (aba ativa, cartão recolhido) não é obrigatória nesta fase; se não for
   implementada, documentar como decisão consciente no resumo da fase, não como pendência.

**Pronto quando:** abaixo do limiar de altura, o rodapé mostra uma aba por vez e o grafo ganha o
espaço devolvido (confirmado por `getBoundingClientRect` de `.flow-workspace__center` antes e
depois de cruzar o limiar). Usar a seta de recolher manual de qualquer um dos dois cartões, em
qualquer altura de viewport, aumenta mensuravelmente a altura de `.flow-workspace__center`.

## Tarefa 43.4: sparkline de p95 dentro do próprio cartão (direção do D-160)

Hoje o sparkline é uma linha própria de largura total (`grid-column: 1 / -1`, Fase 25/D-107). Com
poucos pontos de dado (ex. duas execuções), vira uma única linha diagonal esticada por ~1800px,
sem comunicar nada.

1. Minilinha dentro do próprio cartão "p95 latency" (`.metric-cards__card--sparkline` deixa de ser
   `grid-column: 1 / -1`; a sparkline vira parte do mesmo cartão de 88px dos outros seis), atrás
   ou abaixo do valor numérico.
2. Abaixo de um número mínimo de amostras (a Executora decide o piso exato e documenta o motivo no
   resumo da fase), a minilinha não aparece: poucos pontos não são sinal, são ruído.
3. `docs/design/flow-workspace/guide.md` seção 6.2 e a nota de correção da seção 8 já documentam
   essa direção; não reabrir a decisão de forma, só implementar.

**Pronto quando:** a faixa de métricas volta a ter seis cartões de 88px numa linha só, nenhuma
linha extra abaixo. Com um trace de 2 execuções (cenário real do achado), a minilinha não aparece
ou aparece sem virar uma diagonal sem significado (critério objetivo: nenhum pixel do SVG a mais
de 32px de distância vertical da baseline do cartão).

## Tarefa 43.5: orientação automática do grafo por proporção do canvas (direção do D-160)

Hoje o layout `dagre` é sempre `rankdir: 'LR'`. Trocar para `rankdir: 'TB'` quando o canvas (o
espaço real de desenho do grafo, não a viewport inteira) for mais alto do que largo, medido por
`ResizeObserver` do próprio canvas.

Sem controle manual nesta fase (decisão do D-160): a proporção certa depende do espaço disponível,
não de preferência pessoal estável. Só considerar um toggle manual numa fase futura se o uso real
mostrar a troca automática atrapalhando em algum caso concreto.

**Pronto quando:** redimensionar a janela até o canvas ficar mais alto que largo troca o grafo
para `TB` sem interação do usuário, e volta para `LR` ao redimensionar de volta. `graph-builder.ts`
não muda, só o parâmetro de layout repassado a ele (mesmo tipo de mudança da Fase 7, D4).

## Tarefa 43.6: "Try an endpoint" com uma única área de rolagem (fecha R-28)

Achado real em `1920×1080` sem trace: o painel tem duas barras de rolagem empilhadas (lista e
formulário), o botão "Send request" sai cortado fora da área visível.

1. Lista e formulário do painel de ação passam a compartilhar **uma área de rolagem só**. O botão
   "Send request" fica fixo (fora da área que rola), sempre visível.
2. O teto de 55% da lista (`max-height: 55%`, R-28, Fase 30) deixa de ser o mecanismo que evita o
   transbordo: com rolagem única, o limite passa a ser o card inteiro.

**Pronto quando:** em `1920×1080` sem trace, uma única barra de rolagem no painel de ação, botão
"Send request" visível sem rolar nada, confirmado por `getBoundingClientRect()` do botão contra os
limites visíveis do card.

## Tarefa 43.7: valores numéricos nunca truncados

Achado real: `.metric-cards__value` já tem `overflow: hidden; text-overflow: ellipsis` desde a
Fase 25 (D-107), mas um valor formatado longo ("346.9…", "511.1…") ainda estoura e a reticência
engole a própria unidade.

1. Corrigir no formatador (`shared/duration-format.ts`), não na fonte: formato compacto no lugar
   da reticência (ex. `347 ms`, `0,51 s`), nunca cortando o valor no meio.
2. Fonte responsiva (`clamp`) é complementar, não substitui a correção de formato: resolve o
   tamanho da letra, não o comprimento do texto.

**Pronto quando:** nenhum valor de métrica (`metric-cards`, cartões de duração em qualquer outra
tela que use o mesmo formatador) aparece com reticência, testado com os valores reais do achado
(346.9, 511.1 e qualquer outro valor de 3+ dígitos antes da vírgula/ponto decimal).

## Fora de escopo

- Mobile (modo consulta, topbar compacta, reordenação grafo-antes-de-métricas): Fase 44, depende
  desta fase estar pronta.
- Toggle manual de `rankdir`: só se o uso real pedir, fase futura.
- Memória de sessão para aba ativa/cartão recolhido do rodapé: opcional nesta fase (Tarefa 43.3).
- Qualquer redesenho maior de `.flow-workspace__center` além do necessário para os critérios
  acima. É correção de layout, não oportunidade de redesenho.

## Aceite da fase

Critérios de pronto das sete tarefas acima, medidos por geometria real
(`getBoundingClientRect`/`elementFromPoint`, nunca só inspeção visual), nos modos ao vivo e
replay, com o trace de 4 spans do sample e com um trace de pelo menos 20 spans.

**Varredura por faixa, não só pontos isolados (guia seção 5.1, pedido explícito do usuário: o
layout precisa ser responsivo de verdade em qualquer resolução, como o Bootstrap, não só nas que
expuseram bug).** Largura, um ponto perto de cada borda e um no meio de cada faixa da seção 5.2 do
guia:

| Faixa (guia 5.2) | Larguras testadas |
|---|---|
| ≥ 1600 | 1600, 1920, 2560 |
| 1200 a 1599 | 1200, 1366, 1599 |
| 1024 a 1199 | 1024, 1100, 1199 |
| 768 a 1023 | 768, 900, 1023 |

Altura, cruzando os dois limiares novos desta fase (800px e 720px), numa largura comum (1920):
1080 (confortável), 870 (cenário real do hotfix 0.4.4), 800 e 720 (nas bordas dos limiares, testar
os dois lados de cada um), 650 (bem abaixo, confirma que a página rola sem quebrar mais nada).
Mais as duas combinações específicas dos achados reais: `1366×728` (Tarefa 43.1) e `2560×1080`
(Tarefa 43.2).

Além dos pontos discretos acima, **redimensionar a janela continuamente** (arrastar, não só pular
entre valores) atravessando cada faixa de largura e os dois limiares de altura, confirmando que
nada quebra (nenhum elemento cortado, sobreposto ou com `elementFromPoint` divergente) em nenhum
valor intermediário não listado na tabela.

`npx impeccable detect` em `/flow`, `/flow/history`, `/flow/components` e `/flow/runner`, tema
claro e escuro, nos dois viewports exigidos por `.claude/rules/design-quality.md`, mais os pontos
de desktop da tabela acima. Contagem de avisos antes e depois no resumo final; qualquer achado
novo de `low-contrast`/`undersized-ui-text` bloqueia a fase.

Se o Impeccable continuar travando em modo URL por causa da conexão SSE que nunca fecha (achado já
registrado no hotfix 0.4.4, PR #285), usar o mesmo contorno documentado ali (modo arquivo estático)
e registrar no resumo, não bloquear a fase por uma limitação já conhecida da ferramenta.

## Ao terminar

Resumo para a Planejadora: o que foi feito por tarefa, como verificar (comandos exatos, viewports
exatos), o que ficou pendente. Atualizar `docs/plano/riscos.md` (R-24, R-28) é tarefa da
Planejadora depois de revisar esta fase, não da Executora.
