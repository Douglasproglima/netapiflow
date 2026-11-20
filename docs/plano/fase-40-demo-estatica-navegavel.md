# Fase 40: demo navegável estática, sem backend .NET vivo

Item 14 do `roadmap-valor.md` (D-132, segunda auditoria externa do site, achado 2.5/item 4): maior
retorno por esforço identificado pelas duas auditorias. Hoje, pra saber se o produto serve, a
pessoa precisa instalar dois pacotes numa API própria e marcar passos no código. Decisão de forma
fechada pela Planejadora nesta entrada, sem ADR: é configuração de build do `ClientApp`, não
decisão de arquitetura do núcleo, mesmo tratamento que a Fase 26 (cenários de demo) já teve.

Sem mudança em `contratos.md`: a demo consome o **mesmo formato** que a API REST já devolve hoje,
capturado em arquivo em vez de servido ao vivo. Nenhum pacote `.NET` muda.

**Fora de escopo, de propósito:** publicar o resultado no `netapiflow-site` ou em qualquer GitHub
Pages é trabalho de outro repositório, fora do alcance desta fase. Esta fase entrega o build
estático funcionando localmente (`npx http-server dist/demo/browser` abrindo as quatro telas sem
erro de console); wire-up de publicação é tarefa futura, em outro lugar.

## Tarefa 40.1: interceptor de fixtures, sem tocar `ApiService`

`HttpInterceptorFn` novo (`demo-fixture.interceptor.ts`), registrado só quando a aplicação builda
na configuração `demo` (ver 40.3). Intercepta toda chamada que `ApiService` faria pro backend e
devolve o corpo de um arquivo de fixture em vez de ir à rede, preservando o **mesmo contrato REST**
que já existe: nenhuma linha de `api.service.ts` muda, porque a fixture tem exatamente a forma que
o endpoint real devolveria. Decisão central da tarefa: a fixture mora em
`src/assets/demo-fixtures/`, um arquivo por rota mais chave relevante (`traces.json`, `config.json`,
`traces-{id}.json`, `metrics-components.json`, `architecture-{method}-{route}.json`, etc.), listados
num `manifest.json` que o interceptor lê pra resolver qual arquivo serve cada chamada.

`GET /stream` (SSE) não tem fixture: a demo não tenta simular streaming ao vivo (SSE não funciona
num host estático de qualquer forma). `SseService` ganha uma checagem de modo demo e nunca abre a
conexão nesse caso; as telas que dependem de eventos ao vivo (contador do topbar, player live)
simplesmente ficam sem atualização incremental, mostrando só o estado inicial da fixture, o que é
esperado e documentado na faixa da tarefa 40.4.

**Pronto quando:** spec prova que, com o interceptor ativo, uma chamada a cada rota do `ApiService`
devolve o conteúdo exato do arquivo de fixture correspondente, sem nenhuma requisição de rede real
disparada (`HttpTestingController` nunca vendo a requisição passar adiante).

## Tarefa 40.2: script de geração de fixtures a partir de uma execução real

Script Node novo (`scripts/generate-demo-fixtures.mjs`, fora do `ClientApp`, irmão de outros
scripts de build do repositório): sobe `samples/SampleApi.Controllers` com os cenários de demo da
Fase 26 (Sucesso, Lento, Erro, Sequencial N+1), dispara cada cenário algumas vezes (volume
suficiente pra `/history`, `/insights` e `/architecture` não ficarem vazios), e grava a resposta
real de cada rota do `ApiService` (sem edição manual) em `src/assets/demo-fixtures/`, mais o
`manifest.json` da tarefa 40.1. Reaproveita o mesmo payload que a Fase 21 (export/import) já grava
pra `FlowTraceDetail` individual, mesma forma, nenhum formato novo pra manter sincronizado com o
contrato real.

**Pronto quando:** rodar o script contra um `samples/SampleApi.Controllers` real gera um conjunto
de fixtures que o interceptor da tarefa 40.1 consegue servir sem erro de forma (mesma validação de
`isFlowTraceDetail` que a Fase 21 já tem, reaproveitada aqui pra garantir que a fixture gerada bate
com o contrato real).

## Tarefa 40.3: configuração de build `demo` no `angular.json`

Terceira `configuration` ao lado de `production`/`development` (mesmo mecanismo de
`fileReplacements` que as duas já usam), ativando o interceptor da tarefa 40.1 via um arquivo de
ambiente próprio (`environment.demo.ts`, `isDemoMode = true`). `npm run build:demo` novo em
`package.json`, saída em `dist/demo/browser` (convenção padrão do Angular CLI, pasta separada de
`dist/browser` que vai pro `.nupkg`). Este `dist/demo/` nunca entra no pacote NuGet
(ADR-0005 continua valendo só pro `dist/` de produção).

**Pronto quando:** `npm run build:demo` produz um build funcional, aberto localmente
(`npx http-server dist/demo/browser`) sem erro de console nas quatro telas (`/flow`, `/history`,
`/insights`, `/architecture`), cada uma mostrando dado real da fixture gerada na tarefa 40.2.

## Tarefa 40.4: faixa permanente de "modo demonstração"

Faixa visível e permanente no topo das quatro telas, só na build `demo` (lida de
`environment.demo.ts`), dizendo que os dados são gravados, não ao vivo, nos dois idiomas
(`TranslatePipe`, mesma régua de internacionalização do resto do dashboard). Nunca cor como único
sinal: texto explícito, sempre visível, não um banner que some sozinho.

**Pronto quando:** a faixa aparece nas quatro telas da build `demo`, e nunca aparece nas build
`production`/`development`. `npx impeccable detect` nas quatro telas da build `demo`, claro e
escuro, desktop e `390x844`, sem achado novo de `low-contrast`/`undersized-ui-text`.

## Fora de escopo

- Publicar o resultado em `netapiflow-site`/GitHub Pages: repositório diferente, fora do alcance
  desta fase.
- Qualquer tentativa de simular SSE/streaming na demo: decisão travada (40.1), replay e histórico
  são o próprio diferencial do produto, não uma limitação do modo demo.
- Qualquer mudança em `contratos.md` ou em qualquer pacote `.NET`: a demo consome o contrato REST
  que já existe, capturado em arquivo.

## Aceite da fase

`npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` (produção, inalterado)
limpos. `npm run build:demo` produz build funcional conferido manualmente nas quatro telas.
`npx impeccable detect` na build `demo`, mesma régua do resto do dashboard.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos, incluindo como abrir a
build `demo` localmente), o que ficou pendente (publicação cross-repo é tarefa futura, fora daqui).
