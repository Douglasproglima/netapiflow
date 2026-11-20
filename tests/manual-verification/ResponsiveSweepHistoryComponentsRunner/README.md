# Fase 46: varredura de responsividade em `/history`, `/components` e `/runner`

Varredura sistemática (Tarefa 46.1), mesma régua de geometria real das Fases 43/44, contra o
`samples/SampleApi.Controllers` rodando de verdade (Postgres local), com dado real: 13 traces
(`/history`), 9 combinações componente/operação de um trace N+1 real de 7 ids
(`/components`), lista de endpoints real (`/runner`).

Diferente das Fases 43/44, não havia achado prévio confirmado para estas três telas. Dois
apareceram na varredura, os dois reproduzidos por geometria (`getBoundingClientRect`, não
inspeção visual).

## Varredura

Larguras (DPR 0,9 deste ambiente, compensado em cada `setViewportSize`): 320, 360, 393, 480, 767,
768, 900, 1023, 1024, 1100, 1199, 1200, 1366, 1599, 1600, 1920, 2560. Para cada largura, em cada
uma das três telas: `document.documentElement.scrollWidth` contra `window.innerWidth`, e o
elemento com maior transbordo horizontal quando houver.

Alturas: 700 e 900 (as mesmas duas exigidas pela Fase 46), em `/history` e `/runner`, confirmando
`.app-content` (o `mat-drawer-content` que rola de verdade, mesmo mecanismo da Fase 43) sem
transbordo e sem elemento cortado.

## Achado 1: chip "Live" da topbar, não é desta fase

Abaixo de 360px de largura, a topbar (`.app-topbar`) ultrapassa a viewport em 28 a 73px,
dependendo da tela (o chip de conexão SSE, `.naf-chip--live`, é o elemento mais à direita e o que
mais estoura). Reproduzido nas três telas desta fase **e também em `/flow`** no mesmo teste
(`docScrollWidth: 547` em `/flow` a 360px, contra `433` em `/history`/`/runner`, a diferença é só
porque `/flow` tem mais itens na topbar: "Try an endpoint", contador "N in progress", limiar de
lentidão).

**Não é um achado desta fase.** É a mesma topbar compartilhada por todo o dashboard
(`app.component.html`/`app.scss`), e a correção já está planejada, com critério de pronto
explícito, na Tarefa 44.1 da Fase 44 ("abaixo de ~480px, tema e idioma entram num menu, 'Try an
endpoint' vira só ícone, confirmar que nenhum elemento da topbar força `scrollWidth` a exceder
`innerWidth`"). A Fase 44 está fora desta rodada (gate de aprovação de "mobile como modo
consulta" não encontrado em `decisoes.md`, ver resumo da rodada). Corrigir a topbar aqui seria
implementar parte do escopo da 44 sem a aprovação que o próprio gate exige. Registrado para a
Planejadora: quando a Fase 44 for liberada, a correção da topbar já resolve isto nas quatro telas
de uma vez, não só em `/flow`.

## Achado 2: tabela de `/components` vaza para fora do próprio cartão

Abaixo de 393px de largura, a tabela de oito colunas (`.components__table`, dentro de um
`<mat-card>`) precisa de rolagem horizontal (764px de conteúdo contra ~328px de card disponível).
A rolagem em si está correta: card é mais estreito que a tabela, isso é normal com oito colunas
numa tela pequena. O bug real: o `overflow-x: auto` estava no `<section class="components">`, por
fora do `<mat-card>`, e `.mat-mdc-card` usa `overflow-x: visible` por padrão (nenhuma regra deste
projeto sobrescrevia isso). Resultado medido antes da correção, em `360×810`:

```json
{ "cardWidth": 328, "cardScrollWidth": 792, "cardOverflowX": "visible" }
```

O card nunca clipa nem rola a própria tabela: ela vaza para a direita, sem o fundo/borda do card
por trás, até ser cortada só no limite da `<section>` (outro elemento, mais abaixo na árvore). Ao
rolar a tela horizontalmente, a maior parte das oito colunas aparece sem nenhum estilo de cartão
ao redor, só a tabela nua.

**Corrigido**: `overflow-x: auto` moveu de `.components` para o próprio `<mat-card>`
(`components__card`, classe nova no host do `mat-card`). O card agora é o contêiner de rolagem:
seu fundo e borda acompanham a tabela em qualquer posição de scroll. Medido depois, no mesmo
`360×810`:

```json
{ "cardScrollWidth": 821, "cardClientWidth": 326, "cardOverflowX": "auto", "docScrollWidth": 433 }
```

`docScrollWidth: 433` restante é só o Achado 1 (topbar), não a tabela: confirmado isolando a
mudança, a página nunca rola horizontalmente por causa da tabela em nenhuma largura testada.

## Resto da varredura: nenhum achado novo

Nas larguras e alturas listadas acima, sem o Achado 1 (já tratado como não sendo desta fase) e com
o Achado 2 corrigido: nenhuma outra largura, em nenhuma das três telas, produz
`scrollWidth > innerWidth`. `/history` já tinha seus próprios breakpoints (`max-width: 1365px`,
`1023px`, `767px`, confirmados na varredura, nenhuma transição quebra). `/runner` reaproveita o
`EndpointFormComponent` já corrigido e testado na Fase 43 (rolagem única, botão "Send request"
sempre visível): confirmado de novo aqui, isoladamente, em `1366×700` com um endpoint selecionado,
sem regressão.

Nenhum achado de altura (`×700`, `×900`): `.app-content` não transborda em nenhuma das três telas
nessas alturas, com o dado real listado acima.

## Impeccable

Mesmo contorno documentado nas Fases 43/44 e no hotfix 0.4.4 (modo URL trava pela conexão SSE
persistente). Modo arquivo estático:

| Arquivo | Achados |
|---|---|
| `components.component.html`/`.scss` | 0 |

0 antes, 0 depois. Nenhum achado de `low-contrast`/`undersized-ui-text`.

## Build, testes, lint e formato

```bash
cd src/NetApiFlow.Dashboard/ClientApp
npm run build        # OK
npm run test:ci      # 393 testes, 0 falhas (sem teste novo: mudança é só mover overflow-x para o host do mat-card)
npm run lint         # OK
npm run format:check # OK
```

## Reproduzindo

```bash
cd samples/SampleApi.Controllers
ASPNETCORE_ENVIRONMENT=Development dotnet run

# 9 combinações componente/operação reais:
curl "http://localhost:<port>/api/orders/sequential?$(python3 -c "print('&'.join(f'ids={i}' for i in range(1,8)))")"

# abrir /flow/components, /flow/history, /flow/runner, redimensionar a janela
```
