# Fase 50: verificação em navegador dos achados do caminho do usuário

Prova, num navegador de verdade (Playwright) e contra um host real, os achados (b) e (c) da
Fase 50, e guarda a reprodução do (e) antes de qualquer correção. O (d) é provado por teste de host
real em `tests/NetApiFlow.IntegrationTests/OpenApiDocumentTracingIntegrationTests.cs`, visto
falhando antes. Nenhum teste de `NetApiFlow.slnx` cobre o que o navegador mostra, então isto roda
à mão.

## O host

`UnmarkedEndpointHost` referencia o núcleo e o dashboard por `ProjectReference` e mapeia duas
rotas: `/plain`, sem marcação, e `/tracked`, com `.WithNetApiFlowTracking()`.

```bash
cd src/NetApiFlow.Dashboard/ClientApp
npm ci
npm run build
cd ../../../tests/manual-verification/Fase50CaminhoDoUsuarioVerification/UnmarkedEndpointHost
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5253
```

O dashboard embute o `dist/` na hora do `dotnet build`, então o Angular vem primeiro. Em outro
terminal, `curl -D - -o /dev/null http://localhost:5253/plain` mostra o `X-NetApiFlow-TraceId`, e
`http://localhost:5253/flow?trace=<id>` abre o trace.

## Achado (b): o grafo vazio ensina a marcar

**Antes.** O pacote `0.5.1` do nuget.org, num app novo de `dotnet new webapi` com uma rota sem
marcação: o trace tem `spanCount: 0`, o grafo tem 0 nós e o canvas fica em branco. O painel diz
"0 Executed". Captura: `b-antes-0.5.1-en-escuro.png`.

**Depois.** Com a mesma rota no host acima, o canvas mostra o estado vazio: ícone na cor da marca,
"A requisição foi gravada, mas nenhuma etapa foi marcada." e os três caminhos (controllers MVC
sozinhos, `.WithNetApiFlowTracking()` para Minimal API, `NetApiFlow.Step(...)` ou `AddFlowTracked`
para o resto). O trace de `/tracked` continua desenhando o grafo.

| Combinação | Captura |
|---|---|
| Português, claro, 1366x800 | `b-depois-pt-claro.png` |
| Português, escuro, 1366x800 | `b-depois-pt-escuro.png` |
| Inglês, claro, 1366x800 | `b-depois-en-claro.png` |
| Inglês, escuro, 1366x800 | `b-depois-en-escuro.png` |
| Inglês, claro, 390x844 | `b-depois-390x844-en-claro.png` |

No 390x844 o estado vazio ocupa 341 px de largura (de 17 a 358) e a página não rola na horizontal.
O estado não aparece enquanto o trace ainda está `Running` e sem etapas: nesse caso as etapas podem
estar chegando, e o texto estaria errado. Isso tem teste de componente.

### Detector Impeccable, mesmo trace sem marcação

Contagem de achados, nas mesmas quatro combinações de tema e viewport.

| | Claro 1280x800 | Escuro 1280x800 | Claro 390x844 | Escuro 390x844 |
|---|---|---|---|---|
| `0.5.1`, canvas em branco | 2 | 2 | 0 | 0 |
| Estado vazio novo | 0 | 0 | 0 | 0 |

Os 4 achados do "antes" são `text-occlusion`: o `svg.flow-graph` vazio cobre 50% das abas
"Execuções recentes" e "Eventos ao vivo". A primeira versão do estado vazio era um cartão com borda
dentro do cartão do grafo, e o detector acusou `nested-cards` nas quatro combinações, mais um aviso
`straggler` no mobile. O estado vazio passou a seguir os que já existem (texto e ícone, sem caixa)
e o título ganhou `text-wrap: balance`. Nenhuma combinação tem `low-contrast` nem
`undersized-ui-text`, as duas categorias que bloqueiam.

## Achado (c): o painel de tempo sem cobertura com etapas de microssegundos

Os `startedAt` e `endedAt` do JSON trazem 6 dígitos fracionários, ou seja, microssegundo. A conta
usava `Date.parse`, que corta em milissegundo, e uma etapa que começa e termina no mesmo
milissegundo virava um intervalo de largura zero.

**Antes, num host real.** Pacote `0.5.1`, `GET /weatherforecast` depois de aquecido. O trace tem 385 µs
e uma etapa de 60 µs, com `startedAt` de `.486464` a `.486525` e o trace de `.486333` a `.486718`. O
painel diz "100% of the time (385 µs of 385 µs) is not covered by any recorded step".

**Antes e depois, na demo.** A trace gravada `7fdb4a0816e4a76920a5b96799beb5cf` (`GET
/api/orders/sequential`, 349 µs, três etapas de 34, 21 e 14 µs, todas dentro do mesmo milissegundo):

| | Painel | `margin-left` das três barras |
|---|---|---|
| Antes | "100% do tempo (349 µs de 349 µs) não está coberto por nenhuma etapa registrada." | 0%, 0%, 0% |
| Depois | "90% do tempo (314 µs de 349 µs) não está coberto por nenhuma etapa registrada." | 70,2006%, 72,2063%, 73,3524% |

O 314 é a conta certa: a união das três etapas aninhadas cobre 35 µs (de `.186840` a `.186875`), e
349 - 35 = 314. Capturas: `c-antes-trace-349us.png` e `c-depois-trace-349us.png`.

## Achado (e): a demo, reprodução antes de corrigir

Build `npm run build:demo` servido por `python -I -m http.server` em `dist/demo/browser`.

- O "Testar um endpoint" da demo lista as rotas internas (`GET` e `DELETE /flow/api/traces`,
  `/flow/api/traces/live`, até `/flow/api/stream` e `GET /flow/{path}`) junto com as do sample.
- "Enviar requisição" em `GET /api/orders/parallel` termina em "A requisição falhou.", a URL
  continua em `/` e nenhum trace é aberto.

A correção do (e) entra depois que o documento OpenAPI sem as rotas internas chegar a `develop`.

## Duas armadilhas deste roteiro

**O navegador guarda o `index.html` antigo.** O servidor estático do Python não manda
`Cache-Control`, e o perfil do Playwright sobrevive ao fechamento do navegador. Depois de reconstruir
o `dist/`, a página continuou rodando o `main-*.js` antigo e mostrou o resultado de antes como se
fosse o de depois. Antes de medir, compare o script carregado com o do `dist/`:

```js
[...document.scripts].map((s) => s.src.split('/').pop()).filter(Boolean);
```

Trocar `127.0.0.1` por `localhost` (outra chave de cache) resolve na hora.

**`impeccable detect <url>` dá timeout contra um host vivo no Windows.** O stream SSE do dashboard
mantém uma conexão aberta e a navegação nunca "assenta" (`Navigation timeout of 30000 ms exceeded`).
Contra a demo estática funciona. Para o host vivo, o detector aceita `--cdp`, que escaneia uma
página já aberta sem navegar:

```bash
msedge.exe --headless=new --remote-debugging-port=9333 --user-data-dir=<pasta temporária> \
  --window-size=1280,800 --blink-settings=preferredColorScheme=1 "http://localhost:5253/flow?trace=<id>"
npx impeccable detect --cdp 9333 --target 5253
```

`preferredColorScheme=1` força o tema escuro e `=0` o claro. Saída vazia significa zero achados, e
`--json` mostra `[]` de forma explícita.
