---
paths:
  - "src/NetApiFlow.Dashboard/ClientApp/**"
---

# Dashboard Angular

## Base

- Angular 22, **standalone** components, **zoneless**
  (`provideZonelessChangeDetection()`), Signals (`signal()`, `computed()`, `effect()`).
- Control flow `@if`/`@for`/`@switch`. Nunca `*ngIf`/`*ngFor`.
- Injeção via `inject()`. Nunca DI por construtor.
- Sem `any`. Tipar a resposta de cada rota com o modelo correspondente de
  `docs/plano/contratos.md`.
- Angular Material, tema próprio claro e escuro respeitando `prefers-color-scheme`
  (seção 4.2 do prompt).

## Organização (seção 7.4 do prompt)

```text
src/app/
  core/          # ApiService, SseService, modelos, resolução do base path
  features/
    flow/
      graph/     # graph-builder (função pura) + componente SVG
      playback/  # timeline e player (live/replay)
      details/   # painel de detalhes e waterfall
    history/
    components/
    runner/      # executar requisição via OpenAPI
  shared/        # formatação de tempo, ícones, utilitários
```

## Regras específicas

- **Só URL relativa** ao caminho base do dashboard. Nunca hardcode de `/flow`: o dashboard pode
  estar montado em qualquer caminho (risco R-03 em `docs/plano/riscos.md`). Sem CORS.
- **`graph-builder` é função pura**: `(spans) => { nodes, edges }`, sem import de Angular, sem
  `HttpClient`, testável sem `TestBed`. É a peça mais crítica do frontend (seção 7.4 do prompt) e
  tem cobertura mínima de 80% de linha (D-10).
- Grafo é SVG próprio. Bibliotecas prontas de grafo são proibidas (seção 4.2 do prompt);
  `@dagrejs/dagre` é usado só para calcular posição (layout), nunca para renderizar.
  `d3-zoom` para zoom e pan.
- Estado visual de nó (seção 7.2 do prompt) nunca depende só de cor: sempre cor + ícone + texto.
- Toda chamada HTTP que decide algo (service, guard, interceptor) tem `*.spec.ts` ao lado.
  Componente que só renderiza não precisa de spec. Spec de scaffold (`should create` sozinho) não
  conta como teste.
- `HttpTestingController` e `httpMock.verify()` no `afterEach` para teste de `ApiService`.
- Sem `setTimeout` para esperar assincronia em teste. Usar `await fixture.whenStable()` ou
  esperar o `signal`/`Promise` relevante.
- Estados de carregando, vazio e erro tratados explicitamente em toda tela que busca dado
  (histórico, componentes, detalhes).
- Replay: duração exibida é sempre a medida real, nunca ajustada pela velocidade de reprodução
  (seção 7.3 do prompt). Só a cadência da animação muda com a velocidade.
- **Comentário só quando o código sozinho não explica o porquê.** Nome de variável, método e
  tipo bem escolhido substitui comentário que descreve o quê. Nunca comentar o óbvio.

## Antes de finalizar

- `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` passando.
- Nunca apagar ou pular teste para o CI passar. Se o teste está errado, corrigir o teste e
  explicar o porquê no resumo da fase.
