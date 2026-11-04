# Fase 8: Interop OpenTelemetry, parte 1 (ingestão de `HttpClient`)

Projeto: `NetApiFlow` (núcleo). Nenhum pacote novo, nenhum pacote opcional novo.

Pré-requisito: Fase 1 aceita (`FlowActivityListener`, `FlowRecorder`). Primeiro item do roadmap
pós `0.1.0` a entrar em implementação (D-58, D-59), com escopo travado em ADR-0012: só a
`ActivitySource` nativa do `HttpClient`. Npgsql e MassTransit ficam fora desta fase (ver ADR-0012,
seção Contexto).

Contrato congelado em `docs/plano/contratos.md`: `ExternalActivitySource` (enum `[Flags]`) e
`NetApiFlowOptions.ExternalSources`.

## Tarefas

### 8.1: `ExternalActivitySource` e `NetApiFlowOptions.ExternalSources`

`[Flags] public enum ExternalActivitySource { None = 0, HttpClient = 1 }`, exposto em
`NetApiFlow` (namespace raiz, igual a `ComponentType`). `NetApiFlowOptions` ganha
`ExternalSources { get; set; } = ExternalActivitySource.None`.

**Pronto quando:** `NetApiFlowOptionsValidator`/binding de configuração aceita
`"NetApiFlow:ExternalSources": "HttpClient"` via `IConfiguration` (enum `Flags` já funciona com o
binder padrão, sem código extra). Teste cobre o valor padrão (`None`) e o binding por string.

### 8.2: `FlowActivityListener` passa a escutar `"System.Net.Http"`

`ShouldListenTo` aceita a fonte própria (sempre) e `"System.Net.Http"` quando `ExternalSources`
tem o flag `HttpClient`. Com `ExternalSources = None` (padrão), nenhuma mudança de comportamento
em relação ao que existe hoje.

**Pronto quando:** teste com `ExternalSources = HttpClient` mostra uma `Activity` de
`HttpClientHandler` sendo observada e virando `FlowSpan`; a suíte inteira de `NetApiFlow.Tests`
continua verde sem a flag (sem regressão).

### 8.3: Mapeamento da `Activity` de `HttpClient` para `FlowSpan`

`Component = "HttpClient"`, `Type = ComponentType.Http`, `Operation = "{método} {host}{path}"`
(ex.: `"GET api.example.com/users"`). Confirmar empiricamente os nomes de tag reais produzidos
pelo runtime em `net8.0` e em `net10.0` (podem divergir entre convenções de semântica OTel do
`HttpClientHandler`; documentar no resumo da fase qual nome de tag foi usado em cada TFM, com
`#if` se necessário).

**Pronto quando:** teste de integração com um `HttpClient` real contra um servidor de teste local
(`WebApplicationFactory` ou `HttpListener`) confere `Component`, `Type` e `Operation` do span
resultante, nos dois TFMs.

### 8.4: Sanitização da URL (prova R-13, ADR-0007 continua valendo)

Nunca gravar query string nem fragment da URL de saída, em nenhum campo do `FlowSpan` nem em tag.
Só `scheme://host[:porta]/path`, truncado em 300 caracteres.

**Pronto quando:** teste com uma chamada de saída para uma URL com query string sensível (ex.:
`?token=abc123&email=x@y.com`) confirma que nenhum campo do span resultante contém a query
string. Mitiga R-13.

### 8.5: Status do span a partir do código de resposta e de exceção de rede

Resposta HTTP 4xx/5xx marca o span como `FlowStatus.Error`. Exceção não tratada na chamada (ex.:
host inexistente, timeout) marca `IsErrorOrigin = true`, com `ErrorType`/`ErrorMessage`
sanitizados do mesmo jeito que qualquer outro span (`ErrorMessageSanitizer`).

**Pronto quando:** teste com resposta 500 marca `FlowStatus.Error`; teste com host inexistente
marca `IsErrorOrigin = true` e não derruba a aplicação que fez a chamada.

### 8.6: Hierarquia dentro do trace

A chamada HTTP de saída aparece como filha do step ativo no momento em que ela acontece (ex.:
dentro de um `Service` marcado com `NetApiFlow.Step(...)`, ou dentro de uma etapa `UseCase` do
MediatR).

**Pronto quando:** teste de integração com `WebApplicationFactory`, um endpoint que chama um
`HttpClient` nomeado dentro de um `Service` tracked, confere que o span `Http` aparece como filho
correto no grafo (mesmo `ParentSpanId` do step que fez a chamada).

### 8.7: Amostra end-to-end

`samples/SampleApi.Controllers` (ou `SampleApi.MinimalMediatR`) ganha um endpoint que chama um
serviço HTTP externo (pode ser outra rota do próprio sample, ou um serviço público estável e sem
dado sensível, ex. `https://api.github.com/zen`), com `ExternalSources = HttpClient` habilitado na
configuração do sample.

**Pronto quando:** rodar o sample e abrir o dashboard mostra o span `Http` no grafo, sem nenhum
código manual de `NetApiFlow.Step(...)` ao redor da chamada.

### 8.8: Documentação

README (seção de configuração) ganha a opção `ExternalSources`, com o aviso de que é opt-in e de
que a URL de saída é sempre sanitizada (sem query string). `docs/architecture.md` ganha uma nota
curta sobre o novo caminho de ingestão. Non-goals do README não mudam nesta fase (não há
exportação OTLP aqui, só ingestão).

**Pronto quando:** README e README.pt-BR.md atualizados, com conteúdo equivalente.

## Aceite da fase

Todos os critérios de pronto acima, incluindo a prova de R-13 (nenhuma query string vaza). Npgsql
e MassTransit continuam fora de escopo, registrados como pendência explícita em ADR-0012, não como
lacuna esquecida.

## Ao terminar

Resumo para a Planejadora: o que foi feito, nomes de tag confirmados por TFM, como verificar
(comando exato para rodar o sample e ver o span no dashboard), o que ficou pendente. A Planejadora
roda `/revisar-fase 8` antes de decidir, junto com o usuário, qual a próxima fatia da interop OTel
(D-58: Npgsql com dedupe, ou exportação OTLP via pacote opcional, ou outro item do roadmap).
