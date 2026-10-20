# Fase 3: API do dashboard e SSE

Projeto: `src/NetApiFlow.Dashboard`. Testes: `tests/NetApiFlow.IntegrationTests`.

Pré-requisito: Fase 2 aceita.

## Tarefas

### 3.1: Congelar `docs/plano/contratos.md` — **feito em 2026-10-19**

Contrato completo das 10 rotas (query string, corpo, DTO de resposta com exemplo JSON) já está
na seção 3 de `contratos.md`, baseado nos tipos reais entregues na Fase 1
(`FlowTrace`/`FlowSpan`/`PagedResult<T>`/`TraceQuery`/`ComponentStat`/`StatsQuery`). A Executora
implementa as tarefas 3.2 em diante contra esse contrato. Se encontrar algo que não dá para
implementar como descrito, **parar e avisar a Planejadora antes de divergir**, não ajustar o
contrato sozinha.

### 3.2: Rotas de consulta

`GET /traces` (paginada, filtros: status, método, rota, tenant, período, duração mínima, seção
7.1 item 3 do prompt), `GET /traces/live`, `GET /traces/{traceId}` (procura no `FlowRecorder` em
memória primeiro, depois no `ITraceStore`), `GET /traces/{traceId}/spans/{spanId}` (com
ancestrais, filhos e diferença para a mediana da operação), `GET /blueprint?method=&route=` (204
quando não há execução bem-sucedida anterior), `GET /endpoints` (via `EndpointDataSource` e,
quando disponível, o documento OpenAPI da própria API hospedeira), `GET /metrics`,
`GET /metrics/components`.

**Pronto quando:** um teste de integração por rota, incluindo o caso 204 do `/blueprint` e o caso
de paginação vazia do `/traces`.

### 3.3: `DELETE /traces`

Restrito a `IHostEnvironment.IsDevelopment()` ou autorização configurada (mesma regra do
dashboard inteiro, ver ADR-0006 e tarefa 5.4).

**Pronto quando:** teste de integração com `UseEnvironment("Production")` sem autorização
configurada espera 404 (ou 403, se a rota existir mas for barrada por policy). Escolher um dos
dois e manter consistente com a tarefa 5.4, documentando a escolha no resumo da fase.

### 3.4: `GET /stream` em `net10.0`

`TypedResults.ServerSentEvents`, conforme ADR-0003 e o contrato de evento em `contratos.md`,
seção 2.

**Pronto quando:** teste de integração conecta, dispara uma requisição rastreada em paralelo, e
recebe os eventos esperados na ordem certa.

### 3.5: `GET /stream` em `net8.0` (risco R-04)

Implementação manual: `text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`,
`IHttpResponseBodyFeature.DisableBuffering()`, escrever `id:`/`event:`/`data:` manualmente e
`FlushAsync()` depois de cada evento. `#if NET10_0_OR_GREATER` isola as duas implementações no
menor número de arquivos possível (ADR-0003).

**Pronto quando:** as mesmas asserções da tarefa 3.4, rodando no TFM `net8.0`.

### 3.6: Buffer de replay e `Last-Event-ID`

Janela circular de 1000 eventos (seção 6.7). Cliente que reconecta enviando `Last-Event-ID`
recebe os eventos que perdeu, sem duplicar os que já tinha.

**Pronto quando:** teste desconecta um cliente simulado, gera eventos, reconecta com o último
`eventId` recebido, e confere que a sequência recebida não tem buraco nem duplicata.

### 3.7: Limite de clientes e fila por cliente (risco R-10)

Máximo de 50 clientes SSE simultâneos (seção 6.7): conexão 51 recebe 503. Fila de envio por
cliente bounded em 5000, `BoundedChannelFullMode.DropOldest`.

**Pronto quando:** teste abre 51 conexões simuladas e confere que a última recebe 503; teste com
um consumidor que não lê o stream prova que o processo não cresce em memória sem limite (medir
com `GC.GetTotalMemory` antes/depois, ou um teto de iterações que não deveria ter efeito
cumulativo).

## Aceite da fase

Todos os 10 endpoints cobertos por teste de integração. Reconexão com `Last-Event-ID` recupera
os eventos perdidos. Eventos de traces diferentes nunca se misturam no mesmo stream (teste com
dois traces concorrentes, cada evento carrega seu próprio `traceId`).

## Ao terminar

Resumo para a Planejadora, destacando se `contratos.md` precisou de ajuste em relação ao que foi
congelado na tarefa 3.1 (não deveria precisar. Se precisou, é um sinal de que o contrato não
estava completo o suficiente). `/revisar-fase 3` confere o contrato linha a linha contra a
implementação antes de liberar a Fase 4.
