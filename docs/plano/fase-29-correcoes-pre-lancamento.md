# Fase 29: Correções pré-lançamento

Achados reais de um teste de instalação do NetApiFlow do zero, num projeto de produção alheio
(`rajamineiothub`), seguindo só o que o README promete, sem conhecimento de bastidor (D-111).
Todos os achados abaixo foram conferidos pela Planejadora contra o código real antes de virar
tarefa aqui.

A tarefa 29.1 é bloqueante de divulgação pública: é um defeito de segurança, não uma lacuna de
documentação. As demais podem acompanhar a mesma fase, mas não bloqueiam sozinhas.

## Tarefa 29.1: rota crua gravada para requisição sem rota casada (R-26, ADR-0007)

`NetApiFlowMiddleware.InvokeAsync` (`src/NetApiFlow/Middleware/NetApiFlowMiddleware.cs`) chama
`recorder.TryStartTrace(traceId, method, context.Request.Path.Value ?? "/", ...)` **antes** de
`next(context)` rodar, porque o middleware é o mais externo do pipeline (via
`NetApiFlowStartupFilter`) e roda antes do `UseRouting`. O valor inicial é o caminho resolvido de
verdade, não um template. Só no `finally`, depois do `next(context)`, o código lê
`(context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText` e, se não for nulo, chama
`recorder.UpdateRouteTemplate(traceId, routeTemplate)` trocando pelo template real.

Para rota casada isso funciona exatamente como o README promete. Para qualquer requisição sem
rota casada (404 de verdade), `context.GetEndpoint()` é nulo, a troca nunca acontece, e o caminho
resolvido original fica gravado no `FlowTrace.RouteTemplate`, violando a garantia de
`.claude/rules/tracing.md` e do `ADR-0007` ("rota sempre como template, nunca a URI real").
Confirmado em produção real: um 404 contra uma rota com um identificador no caminho (ex. um IMEI)
grava esse identificador no trace persistido.

**Correção**: trocar o valor inicial passado a `TryStartTrace` por um placeholder fixo, não o
caminho resolvido (ex. `"(unmatched)"` ou equivalente, nome final livre pra quem implementar,
desde que nunca seja derivado da URI real). O restante do fluxo (`UpdateRouteTemplate` no
`finally`, quando a rota casa) não muda.

**Pronto quando**: um teste de integração dispara uma requisição a uma rota inexistente contendo
um valor sensível no caminho (ex. um número de identificação qualquer) e confirma que o
`FlowTrace` persistido, em nenhum campo, contém esse valor. O teste tem que falhar contra o código
atual antes da correção, prova de que reproduz o problema de verdade, não só testa a saída
esperada.

## Tarefa 29.2: bloco de código do Quick Start não compila

O bloco de código em `README.md`/`README.pt-BR.md`, seção "Quick start", não tem nenhum `using`.
`AddNetApiFlow` está em `namespace NetApiFlow`, `AddNetApiFlowDashboard` em
`namespace NetApiFlow.Dashboard`, `UseNetApiFlowDashboard` em
`namespace NetApiFlow.Dashboard.Endpoints`: três namespaces diferentes. Um copiar-colar literal do
bloco atual não compila. O sample real (`samples/SampleApi.Controllers/Program.cs`) já tem as três
linhas de `using`, só a prosa do README que nunca as mostrou.

**Correção**: acrescentar as três linhas de `using` ao bloco de código do Quick Start, nos dois
READMEs (inglês e português).

**Pronto quando**: o bloco de código do Quick Start, copiado e colado isoladamente num
`Program.cs` novo com as duas referências de pacote do próprio Quick Start, compila sem erro.

## Tarefa 29.3: `NetApiFlow.EntityFrameworkCore` não é "automático" sem um passo a mais

O README descreve `NetApiFlow.EntityFrameworkCore` como rastreamento "automático" de comandos EF
Core (tabela de pacotes opcionais e a tabela de formas de marcação). O XML doc do próprio método
`AddNetApiFlowEntityFrameworkCore` (`src/NetApiFlow.EntityFrameworkCore/...`) já documenta
corretamente que isso só registra o interceptor no DI; é preciso anexá-lo ao
`DbContextOptionsBuilder` à mão (`optionsBuilder.AddInterceptors(interceptor)`), porque o EF Core
não tem mecanismo de interceptor global. Essa informação nunca chegou no README. O sample real já
faz os dois passos certos; é só a prosa do README que omite o segundo.

**Correção**: na linha de `NetApiFlow.EntityFrameworkCore` do README (os dois idiomas), trocar
"automatic tracking" por uma frase que não prometa automatismo que não existe, e acrescentar um
snippet de código mostrando os dois passos reais (`AddNetApiFlowEntityFrameworkCore()` +
`options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>())`), no mesmo formato
dos outros exemplos de configuração já existentes no README.

**Pronto quando**: o README mostra o snippet completo, os dois passos, sem a palavra "automatic"
sozinha descrevendo o comportamento.

## Tarefa 29.4: caminho do documento OpenAPI fixo, sem configuração

`openApiUrl` em `EndpointFormComponent`
(`src/NetApiFlow.Dashboard/ClientApp/src/app/features/runner/endpoint-form.component.ts`) é um
`input<string>('/openapi/v1.json')` do Angular, mas nada do lado do servidor (`NetApiFlowOptions`,
`GET /config`) alimenta esse valor com um caminho diferente. Qualquer projeto que sirva o
documento OpenAPI em outro lugar (ex. Swashbuckle, `/swagger/v1/swagger.json`, o caso mais comum
no parque .NET hoje) perde a aba "Personalizado" inteira, com a mensagem de erro certa
("Verifique se o host chama MapOpenApi()") mas sem jeito nenhum de resolver.

**Correção**: nova opção pública (ex. `NetApiFlowOptions.OpenApiDocumentPath`, nome final livre
pra quem implementar, padrão `/openapi/v1.json` pra não quebrar quem já depende do valor atual),
exposta via `GET /config`, consumida por `EndpointFormComponent` em vez do valor fixo no `input`.

**Pronto quando**: configurar a opção nova com um caminho diferente faz a aba "Personalizado"
carregar o documento OpenAPI desse caminho, provado por teste de integração (`GET /config` reflete
o valor configurado) e por spec do componente Angular (usa o valor de `GET /config`, não o
padrão, quando configurado).

## Tarefa 29.5: investigação do span fantasma com interceptor duplicado (antes de qualquer código)

Achado no teste real, não confirmado pela Planejadora: anexar o mesmo `FlowDbCommandInterceptor`
singleton tanto em `AddDbContext` quanto em `AddDbContextFactory` do mesmo tipo de `DbContext`
produziu, 3 de 3 vezes, um span `Database` duplicado por comando real: um span correto e um
fantasma preso em `Running`, sem duração, aninhado como pai do span real, mesmo num trace já
concluído com HTTP 200. Anexando o interceptor numa só das duas configurações, o grafo ficou
correto.

Antes de qualquer correção: reproduzir isso dentro do próprio `NetApiFlow.Tests`/
`NetApiFlow.IntegrationTests`, com um `DbContext` de teste registrado via `AddDbContext` e
`AddDbContextFactory` ao mesmo tempo, mesma instância de interceptor nos dois, e confirmar (ou
refutar) o sintoma de forma controlada. `_steps` é um `ConcurrentDictionary<Guid, FlowStep>`
chaveado por `CommandEventData.CommandId`; a hipótese a confirmar é se `CommandId` pode colidir ou
se o mesmo comando real dispara os eventos do interceptor mais de uma vez quando o mesmo singleton
está anexado a duas configurações de options diferentes para o mesmo tipo de contexto.

Reportar à Planejadora: a causa raiz confirmada (ou a refutação do achado, se não reproduzir),
antes de propor qualquer correção. Se reproduzir, a fase 29 pode fechar sem essa correção se a
causa raiz exigir mais investigação do que cabe aqui: registrar como risco novo, não forçar uma
correção às pressas num defeito que corrompe dado silenciosamente.

## Fora de escopo, de propósito

- Item 5 (redirecionamento cosmético `/flow` → `/flow/flow`) e item 7 (regra de negócio tratada
  como `BadRequest` aparece como trace "Success", consequência prevista do próprio desenho, não
  defeito): registrados em D-111, sem tarefa nesta fase, baixa prioridade.
- Ruído em `GET /flow/api/endpoints` (rotas do Quartz, arquivos estáticos e health somadas às
  rotas de negócio, alimentando "rotas mortas" de `/insights` com falso positivo): achado real do
  teste, mas sem solução óbvia nem urgência de lançamento, backlog sem fase atribuída.

## Aceite da fase

Tarefa 29.1 com teste de regressão provando o cenário real (rota com valor sensível, 404, zero
vazamento). Tarefas 29.2 a 29.4 com os critérios de pronto acima. Tarefa 29.5 reporta achado, com
ou sem correção dependendo do que a investigação achar. Build/test/format dos dois lados.

## Ao terminar

Resumo pra Planejadora: confirmação de que o teste de regressão da 29.1 falha no código anterior e
passa depois da correção (prova real, não assumida), achado da investigação 29.5, o que ficou
pendente. A Planejadora roda `/revisar-fase 29` antes de considerar o projeto pronto pra
divulgação pública.
