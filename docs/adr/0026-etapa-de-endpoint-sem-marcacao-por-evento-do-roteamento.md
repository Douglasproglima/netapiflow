# ADR-0026: etapa de endpoint sem marcação, aberta pelo evento `EndpointMatched` do roteamento

## Status
Aceito

## Discussion
[#332](https://github.com/Douglasproglima/netapiflow/discussions/332)

## Contexto

O modelo padrão do .NET (`dotnet new webapi`) usa Minimal API, e um endpoint de Minimal API só virava
etapa com `.WithNetApiFlowTracking()`. Quem seguia o Quick start via o trace com 0 etapas e o grafo em
branco, sem nenhuma explicação (reproduzido pela auditoria do dono e pela Planejadora com o pacote
`0.5.0`, D-185). Os controllers MVC já ganham a etapa pelo `FlowControllerActionFilter`. O dono decidiu
que todo endpoint de Minimal API aparece sem marcação (D-187).

A restrição que molda o desenho: o `NetApiFlowMiddleware` entra no pipeline pelo `IStartupFilter`, antes
do roteamento, então quando ele começa o endpoint ainda não foi escolhido. E o filtro de endpoint do
Minimal API é montado rota por rota: a página "Filters in Minimal API apps" da Microsoft Learn só
documenta `AddEndpointFilter`, `AddEndpointFilter<T>` e `AddEndpointFilterFactory`, por endpoint ou por
grupo, e não descreve nenhuma forma global.

Opções:

| Opção | Resultado |
|---|---|
| **A. Listener no evento `EndpointMatched` do roteamento, etapa fechada pelo middleware externo** | **Escolhida** |
| B. Decorar o `EndpointDataSource`, trocando cada `RouteEndpoint` por outro com o `RequestDelegate` embrulhado | Descartada: troca instâncias de endpoint que outro código pode guardar, e tem de repassar o token de mudança |
| C. Sintetizar uma raiz ao fechar o trace e reparentear as etapas de topo | Descartada: o SSE já publicou cada etapa quando ela terminou, e o pai delas mudaria depois |
| D. Um filtro de endpoint global | Não existe API documentada |
| E. Um `MapGroup` do usuário | Só vale para o que for mapeado dentro dele, e exige código do usuário |

## Evidência

**Código-fonte do ASP.NET Core**, `src/Http/Routing/src/EndpointRoutingMiddleware.cs`, branch `main` do
`dotnet/aspnetcore`: a constante `DiagnosticsEndpointMatchedKey` vale
`"Microsoft.AspNetCore.Routing.EndpointMatched"` (linha 22). O middleware escreve o evento no
`DiagnosticListener`, com o `HttpContext` como payload, quando `IsEnabled` é verdadeiro (linhas 152 a
154 e 190 a 193), e só depois chama `_next(httpContext)` (linha 186).

**Documentação.** A página "ASP.NET Core Middleware" diz que o `WebApplication` adiciona o roteamento
sozinho: "UseRouting is added second, if the user code didn't already call UseRouting and endpoints are
configured". Não encontrei o evento `EndpointMatched` descrito nessas duas páginas.

**Protótipo da Planejadora**, descartável, fora do repositório, em `net8.0` (8.0.34), `net10.0`
(10.0.15) e `net11.0` (11.0.0), com o mesmo resultado nos três:

- o evento dispara com o `HttpContext`, e o endpoint já está resolvido;
- uma `Activity` aberta nesse ponto tem a `Microsoft.AspNetCore.Hosting.HttpRequestIn` como pai, o
  handler a enxerga em `Activity.Current`, e uma `Activity` aberta dentro do handler a tem como pai;
- o middleware externo, no `finally`, fecha a etapa, e `Activity.Current` volta para a
  `HttpRequestIn`;
- num controller o evento também dispara, com `ControllerActionDescriptor` nos metadados, e nenhuma
  etapa é aberta.

## Decisão

Opção A.

- Um listener assina o `DiagnosticListener` `"Microsoft.AspNetCore"` com o **predicado** do nome do
  evento, para o hosting não gerar os outros eventos por causa do NetApiFlow. O mecanismo tem
  precedente neste repositório: `SqlClientCommandListener` e `FlowAuthorizationFilterListener` também
  abrem etapa a partir de evento de `DiagnosticListener`.
- Abre a etapa `Endpoint` só para um `RouteEndpoint` sem `ActionDescriptor` nos metadados (nem MVC, nem
  Razor Pages) e com `MethodInfo`, e **só quando o `NetApiFlowMiddleware` marcou a requisição como
  rastreada**. Requisição que o NetApiFlow não rastreia (`ExcludePaths`, as rotas do dashboard,
  `OpenApiDocumentPath`, `Enabled = false`) nunca ganha etapa. Não basta confiar em `NetApiFlow.Step`
  devolver etapa vazia: ele só faz isso quando nenhuma fonte tem ouvinte, e com um segundo
  `ActivityListener` na fonte `NetApiFlow` (o do `NetApiFlow.OpenTelemetry`) a `Activity` nasce mesmo
  sem trace ativo e vai para o exportador.
- O componente e a operação são os mesmos do `FlowEndpointFilter`, extraídos para um só lugar.
- O `NetApiFlowMiddleware` fecha a etapa no `finally` da requisição. O `FlowEndpointFilter` vira no-op
  quando a etapa implícita já está aberta, então `.WithNetApiFlowTracking()` continua válido e nunca
  duplica. A etapa de erro tem a mesma paridade de hoje, inclusive atrás de `UseExceptionHandler`, pelo
  `IExceptionHandlerFeature`.
- Opção `NetApiFlowOptions.CaptureMinimalApiEndpoints`, padrão `true`, decidida pelas opções
  **resolvidas**, nunca pela lambda do `AddNetApiFlow` (D-175).

## Consequências

- O grafo deixa de ficar vazio no modelo padrão do .NET, sem nenhuma marcação. Controllers e
  `.WithNetApiFlowTracking()` continuam como estão.
- **O evento não é API documentada.** Está no código-fonte do ASP.NET Core e eu não o achei na
  documentação oficial que li. Uma versão futura do framework pode mudar o nome ou o payload, e o grafo
  voltar a ficar vazio sem erro. Registrado como R-32. A mitigação é um teste de host real nos três
  runtimes que falha de forma visível quando um endpoint sem marcação grava um trace com 0 etapas, e o
  `.WithNetApiFlowTracking()`, que continua sendo o caminho explícito.
- **Muda o comportamento padrão de quem atualizar.** Cada endpoint de Minimal API ganha uma etapa por
  requisição: cerca de 8 µs pelo benchmark publicado, a medir de novo, e mexe em `span_count`, no mapa
  de arquitetura e no limite de 300 etapas por trace. Quem não quer desliga com
  `CaptureMinimalApiEndpoints = false`. Entra em `### Changed` do `CHANGELOG.md`.
- A duração da etapa implícita vai do roteamento ao fim da requisição, então inclui os middlewares entre
  os dois, como a autenticação. O `FlowEndpointFilter` mede só os filtros e o handler. A diferença é
  aceita e documentada.
- Não reabrir sem justificativa técnica nova: abrir etapa sem checar que a requisição é rastreada, e
  sintetizar a raiz do trace ao fechar (opção C).
