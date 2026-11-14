# Fase 32: Correções da auditoria de frontend (achados Baixa)

Continuação da Fase 31 (D-121/D-122), cobrindo os achados de severidade Baixa da mesma auditoria
externa do dashboard, a partir do commit `2e900be` de `main`. A auditoria original agrupou esses
achados em 6 blocos de texto, não em rótulos individuais como fez com Alta (A1-A4) e Média
(M1-M10); lidos com atenção, os 6 blocos descrevem 17 problemas atômicos distintos, não 10 como o
resumo anterior (D-121) registrou por engano. Corrigido em D-123.

Todos os 17 foram reverificados pela Planejadora contra o código atual antes deste plano, com
arquivo e linha. Nenhum exige API pública nova: a fase inteira cabe em PATCH, sequenciada depois da
0.3.1 (Fase 31), como `0.3.2`.

## Regra geral desta fase

Toda tarefa de correção de bug escreve primeiro o teste que reproduz o problema, confirma que ele
falha, só depois corrige.

## Atenção, sequenciamento

A Tarefa 32.3 toca os mesmos arquivos que a Tarefa 31.2 (A2) da Fase 31 mexeu por último
(`metric-cards.component.ts`, `recent-executions.component.ts`). Reconferir contra o código já com
a Fase 31 mergeada antes de implementar: a lógica de `auditTime`/`switchMap` da 31.2 pode ter
deslocado os números de linha citados aqui, mesmo sem mudar o problema em si.

## Tarefa 32.1: `localStorage` sem try/catch trava o carregamento de tema e idioma

`theme.service.ts` (linhas 8 e 50) e `language.service.ts` (linhas 8 e 24) chamam
`localStorage.getItem`/`setItem` direto, sem tratamento. Em navegação privada em alguns
navegadores, cota de armazenamento esgotada, ou o dashboard embutido num iframe com storage
bloqueado, essas chamadas lançam exceção e o app não inicializa.

Envolver as quatro chamadas em try/catch, caindo no valor padrão (preferência de tema do sistema,
idioma padrão) quando `localStorage` não está disponível, nos dois serviços.

**Pronto quando:** teste com `localStorage.getItem`/`setItem` mockado pra lançar confirma que os
dois serviços inicializam normalmente, com o valor padrão, em vez de propagar a exceção.

## Tarefa 32.2: nenhuma rota coringa, URL inválida deixa a tela em branco

`app.routes.ts` não tem nenhum `path: '**'`. Uma URL com erro de digitação, um link antigo
quebrado, ou um bookmark de uma rota removida caem fora de todas as rotas declaradas e o Angular
Router não renderiza nada no `router-outlet`, sem nenhuma pista do que aconteceu.

Acrescentar `{ path: '**', redirectTo: '' }` (ou uma tela dedicada de "página não encontrada", se
fizer mais sentido com o restante do app) como última entrada da lista de rotas.

**Pronto quando:** teste de rota navega pra um caminho inexistente e confirma que o router cai de
volta pra tela inicial (ou pra tela de não encontrado), nunca fica em branco.

## Tarefa 32.3: erro de métricas e de execuções recentes nunca some depois de uma recarga bem-sucedida

`metric-cards.component.ts`, `loadMetrics()`: o `error` callback do `subscribe` faz
`this.error.set('metrics.error')`, mas o `next` callback correspondente nunca faz
`this.error.set(null)`. Mesmo problema em `recent-executions.component.ts`, `loadTraces()`. Uma
falha de rede passageira deixa a mensagem de erro fixa na tela pro resto da sessão, mesmo depois
que o próximo carregamento (disparado por SSE) já trouxe dado novo com sucesso.

Em ambos os `next` callbacks, zerar o signal de erro no início (`this.error.set(null)`) antes de
aplicar o resultado.

**Pronto quando:** teste simula uma chamada que falha seguida de uma que sucede e confirma que o
estado de erro desaparece depois da segunda.

## Tarefa 32.4: nó do grafo não responde à tecla Espaço nem anuncia estado pressionado

`flow-graph.component.html`, o grupo do nó tem `tabindex="0"` e `(keydown.enter)`, mas nenhum
`(keydown.space)` nem `[attr.aria-pressed]`. Quem navega só pelo teclado não consegue selecionar um
nó com Espaço (comportamento padrão esperado de qualquer elemento com papel de botão), e leitor de
tela não anuncia se o nó selecionado está "pressionado".

Acrescentar `(keydown.space)` chamando o mesmo `onNodeClick(node)` do Enter (com
`event.preventDefault()`, pra Espaço não rolar a página), e `[attr.aria-pressed]` refletindo se
aquele nó é o selecionado atual.

**Pronto quando:** teste dispara `keydown.space` no nó e confirma que o mesmo evento de seleção do
Enter dispara; teste confirma que `aria-pressed` muda pra `"true"` só no nó selecionado.

## Tarefa 32.5: menu lateral sem `<nav>` nem `aria-current`

`app.html`: o menu lateral é um `<mat-sidenav>` com itens de link, sem nenhum `<nav>` envolvendo a
lista e sem `aria-current` marcando qual item corresponde à rota ativa. Leitor de tela não
identifica aquilo como navegação nem aponta onde o usuário está.

Envolver a lista de itens num `<nav aria-label="...">` e acrescentar `[attr.aria-current]`
(`'page'` ou `null`) em cada item, calculado a partir da rota ativa, do mesmo jeito que o estilo
visual de "ativo" já deve calcular hoje.

**Pronto quando:** teste confirma que o item correspondente à rota atual tem `aria-current="page"`
e os demais não têm o atributo.

## Tarefa 32.6: chip de conexão SSE sem `aria-live`

`app.html`, linhas 95 a 106: o `.naf-chip` muda de classe (`--live`, `--warning`, `--danger`) e de
texto conforme `connectionState()`, mas nada no elemento avisa um leitor de tela da mudança; quem
não enxerga a cor nem o texto não sabe que a conexão caiu.

Acrescentar `aria-live="polite"` no elemento do chip.

**Pronto quando:** teste confirma o atributo presente e que ele não muda conforme o estado (sempre
`"polite"`, só o conteúdo textual interno muda).

## Tarefa 32.7: velocidade 4x do replay anula o intervalo visual mínimo, e pausa longa não tem teto

`replay-player.service.ts`, linhas 121 e 127: `MIN_VISUAL_DELAY_MS / speed` divide o próprio piso
pela velocidade, então em 4x o piso vira 1/4 do valor (37,5 ms em vez de 150 ms), deixando de
cumprir a função de garantir um mínimo visualmente perceptível entre passos. Do lado oposto, uma
trace com um gap real muito grande entre dois spans (um timeout longo, por exemplo) não tem teto
máximo: em velocidade 1x, o replay trava nesse intervalo pelo tempo real inteiro.

`MIN_VISUAL_DELAY_MS` deixa de ser dividido pela velocidade (o piso visual é constante,
independente de `speed`); `Math.max(realGapMs / speed, MIN_VISUAL_DELAY_MS)` garante o piso sem
escalar com a velocidade. Acrescentar um teto máximo de intervalo (constante nova, ex. 3 ou 5
segundos) pro replay não travar numa pausa real desproporcional.

**Pronto quando:** teste com `speed = 4` confirma que o intervalo nunca fica abaixo de
`MIN_VISUAL_DELAY_MS`; teste com um gap real de dezenas de segundos confirma que o intervalo
aplicado não passa do teto novo.

## Tarefa 32.8: status "Running" sem tradução na lista de execuções ao vivo

Achado de i18n: em algum lugar da lista "em andamento" (ao vivo), o texto do status `Running` é
mostrado sem passar pela chave de tradução correspondente, ficando em inglês fixo mesmo com o
idioma em português selecionado.

Localizar o ponto exato (lista de traces ao vivo na `flow-page` ou componente irmão) e trocar o
texto fixo pela chave de tradução já usada pros outros status (`node.state.running` ou equivalente
de trace).

**Pronto quando:** teste com idioma pt-BR confirma que o texto do status ao vivo não é a string
`"Running"` literal.

## Tarefa 32.9: `translate()` substitui só a primeira ocorrência de cada placeholder

`language.service.ts`, linha 34: `text.replace(\`{${placeholder}}\`, String(values[placeholder]))`.
`String.prototype.replace` com um argumento de string (não regex global) troca só a primeira
ocorrência. Uma chave de tradução que repete o mesmo placeholder mais de uma vez no texto
(`"{count} de {count} executado"`, por exemplo) só substitui a primeira.

Trocar pra `replaceAll` (suportado nos navegadores alvo do projeto) ou pra uma regex com flag `g`.

**Pronto quando:** teste com uma chave de tradução cujo texto repete o mesmo placeholder duas vezes
confirma que as duas ocorrências são substituídas.

## Tarefa 32.10: painel "Testar um endpoint" não tem campo de headers

`endpoint-form.component.ts` monta a requisição só com path, query e corpo; não existe nenhum
campo de UI nem lógica pra enviar um header customizado. Testar um endpoint que exige
`Authorization` (Bearer token, API key em header) não é possível pelo painel, só por fora dele.

Acrescentar uma seção opcional de headers (lista de pares chave/valor, like os campos de query já
existentes), repassados pra `callEndpoint`, que precisa aceitar um `HttpHeaders`/objeto de headers
novo além de `method`/`path`/`body`.

**Pronto quando:** teste confirma que um header configurado no formulário chega na requisição
disparada (`HttpTestingController` ou equivalente).

## Tarefa 32.11: path param vazio vira URL quebrada sem aviso, e a base da URL ignora `PathBase`

Dois problemas na mesma função (`endpoint-form.component.ts`, `send()`, construção de `fullPath`):
`path = path.replace(\`{${field.name}}\`, encodeURIComponent(raw))` não valida `raw` antes de
substituir, então um path param vazio vira uma URL com segmento vazio (`/api/orders/` em vez de
`/api/orders/5`) sem nenhum aviso, só o 404/400 do backend depois. E `callEndpoint`
(`api.service.ts`, `this.http.request(method, path, ...)`) manda `path` como veio, raiz-relativo:
se o host roda atrás de `UsePathBase("/algo")`, a chamada sai direto da raiz do domínio, sem o
prefixo, e nunca chega no host de verdade.

Validar que todo path param tem valor antes de disparar (bloquear o envio, destacar o campo vazio,
mensagem clara). Resolver `fullPath` contra o `PathBase` atual do documento (mesmo mecanismo que o
resto do dashboard já usa pra resolver caminho relativo à própria instalação) antes de passar pro
`callEndpoint`.

**Pronto quando:** teste confirma que path param vazio bloqueia o envio com mensagem, sem disparar
a requisição; teste de integração com `UsePathBase` configurado confirma que a URL final inclui o
prefixo.

## Tarefa 32.12: orçamento de build do dashboard

Build de produção atual: bundle inicial de 806 kB, acima do orçamento de aviso de 500 kB
configurado no `angular.json`; seis arquivos SCSS acima do limite de 4 kB também configurado;
`provideNoopAnimations` (usado em `app.config.ts`) em rota de descontinuação no Angular, substituto
a confirmar na versão do framework que o projeto usa nesta fase.

Investigar a composição do bundle (`ng build --stats-json` ou equivalente) pra decidir entre
reduzir o que está sendo carregado adiantado (lazy loading de rota, por exemplo) ou simplesmente
ajustar o orçamento declarado pro tamanho real já maduro do projeto, documentando a decisão.
Dividir ou simplificar os seis SCSS acima do limite. Confirmar se `provideNoopAnimations` tem
substituto estável disponível na versão do Angular do projeto; trocar se tiver, documentar a
decisão de manter se não tiver.

**Pronto quando:** `npm run build` não emite aviso de orçamento (seja por redução real, seja por
orçamento ajustado e justificado no commit), nenhum SCSS acima do limite configurado, decisão sobre
`provideNoopAnimations` registrada (trocado ou mantido com o motivo).
