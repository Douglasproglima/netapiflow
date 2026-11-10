---
paths:
  - "src/**/*.cs"
---

# Regras duras de rastreamento

Estas regras não têm exceção por conveniência de implementação. Se uma tarefa do plano parecer
pedir o contrário, o conflito é motivo para parar e relatar à Planejadora (seção 16.6 do
`netapiflow-prompt.md`), não para flexibilizar a regra.

## Nunca capturar dado sensível (ADR-0007)

- Nenhum span ou trace grava corpo de requisição/resposta, argumento de método, retorno, header
  ou query string. Em nenhuma circunstância, nem atrás de flag de configuração.
- Rota é sempre gravada como **template** (`/api/orders/{id}`), nunca a URI real
  (`/api/orders/42`).
- Span `Database` grava só tipo de comando e tabela principal. SQL completo (sem parâmetros) só
  quando `CaptureSqlText = true` (desligado por padrão). Valor de parâmetro de comando SQL nunca
  é gravado, mesmo com `CaptureSqlText = true`. Nuance confirmada na investigação do ADR-0018: um
  valor lido de `const`/`static readonly` numa consulta LINQ do EF Core não vira parâmetro, o EF
  Core embute o valor como literal na própria consulta antes de chegar no driver. Isso não é falha
  de sanitização (não existe parâmetro para sanitizar), é o SQL de verdade contendo o valor; o
  mesmo risco de sempre do texto completo, não um caso novo.
- Mensagem de erro é truncada (300 caracteres) **antes** de sanitizar, e sanitizada removendo
  e-mail, CPF, CNPJ, sequência numérica longa e os pares `password=`/`token=`/`authorization`/
  `api-key`/`secret`.
- Tags são só as explicitamente adicionadas via `FlowStep.Tag(...)`, com limite de quantidade e
  tamanho (ver `docs/plano/riscos.md`, limites da seção 6.7 do prompt).

## Nunca derrubar, travar ou atrasar a API do usuário

- Toda exceção lançada pela própria instrumentação (listener, interceptor, `DispatchProxy`,
  middleware, writer de persistência) é capturada e logada, nunca propagada para o código do
  usuário nem para a resposta HTTP.
- Nenhum I/O de persistência roda na thread da requisição. `ITraceStore.SaveAsync` só é chamado
  pelo `PersistenceWriter` (um `BackgroundService` consumindo um `Channel`), nunca diretamente
  pelo middleware.
- Os seis limites da seção 6.7 do prompt são respeitados à risca: 500 traces em memória, 300
  spans por trace, fila de persistência de 1000, fila de SSE de 5000, buffer de replay de 1000,
  50 clientes SSE simultâneos. Exceder qualquer um deles tem um comportamento definido (descartar
  e contar, nunca lançar exceção ou travar).
- Todo contador de descarte é exposto via `System.Diagnostics.Metrics`, `Meter("NetApiFlow")`.

## Correlação e hierarquia

- `TraceId` do NetApiFlow é o `TraceId` da `Activity` HTTP que o ASP.NET Core já cria por
  requisição (W3C `traceparent`).
- Hierarquia de spans vem de `Activity.Parent`, que flui por `AsyncLocal`. Todo código que abre
  um span precisa continuar funcionando corretamente dentro de `async`/`await`, `Task.Run` e
  `Task.WhenAll`. Isso é testado explicitamente na Fase 1 (tarefa 1.13) e não pode regredir
  depois.
- As próprias rotas do NetApiFlow (dashboard, API REST, stream SSE) nunca são rastreadas.

## Dashboard (ADR-0006)

- Fora de `IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization` ou
  `IDashboardAuthorizationFilter` configurado explicitamente, toda rota do dashboard responde
  404. Não existe modo "parcialmente aberto" por padrão.
