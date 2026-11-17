# Fase 39: pacote `NetApiFlow.Testing`

Item 16 do `roadmap-valor.md`, destravado pela Planejadora (ADR-0022): superfície pública
congelada em `docs/plano/contratos.md`, sem dependência de terceiro. MINOR (pacote novo), sem
mudança em nenhum pacote existente.

## Tarefa 39.1: projeto novo, metadados de empacotamento

`src/NetApiFlow.Testing/NetApiFlow.Testing.csproj`, `net8.0;net10.0` (ADR-0003), só
`ProjectReference` pra `NetApiFlow` (núcleo). `PackageId=NetApiFlow.Testing`, `PackageReadmeFile`,
`PackageIcon`, `PackageTags`, `.snupkg`, mesmos metadados herdados de `Directory.Build.props` que
todo pacote já tem (`.claude/rules/packaging.md`). `README.md` próprio do pacote, curto, com
exemplo de uso e link pro README principal.

**Pronto quando:** `dotnet pack` gera `NetApiFlow.Testing.nupkg` e `.snupkg` sem aviso NU5048/NU5047.

## Tarefa 39.2: `FlowTraceAssertions`

As cinco extensões sobre `FlowTraceDetail` da assinatura congelada em `contratos.md`
(`ShouldHaveStatus`, `ShouldHaveNoErrors`, `ShouldCallAtMost`, `ShouldNeverCall`,
`ShouldHaveCalled`), cada uma devolvendo a própria `FlowTraceDetail` pra encadeamento, cada falha
lançando `FlowAssertionException` com mensagem acionável (diz o que esperava, o que achou: ex.
`"Expected at most 2 Database calls, found 5."`). `ShouldNeverCall`/`ShouldHaveCalled` com
`operation` nulo casam só por `Component`; com `operation` preenchido, por `(Component, Operation)`
exato.

**Pronto quando:** `[Theory]` cobrindo sucesso e falha de cada uma das cinco, incluindo o texto
exato da mensagem de exceção no caso de falha (mensagem acionável é parte do contrato, não
detalhe de implementação).

## Tarefa 39.3: `FlowTestingHttpResponseExtensions.GetFlowTrace`

Lê `X-NetApiFlow-TraceId` da resposta (mesmo header que o middleware já grava desde a Fase 1),
chama `NetApiFlow.GetRecentTrace(services, traceId)`, lança `FlowAssertionException` quando o
header está ausente ou `GetRecentTrace` devolve nulo (trace envelheceu do buffer de 500, ou
tracing desligado), em vez de forçar o usuário a repetir essa checagem em todo teste.

**Pronto quando:** teste de integração real com `WebApplicationFactory` (cenário do próprio
README da Fase 24) prova o caminho feliz (`response.GetFlowTrace(factory.Services)` devolve a
trace certa) e o caminho de falha (header ausente lança `FlowAssertionException` com mensagem
dizendo que o header não veio, não um `NullReferenceException` nem um retorno nulo silencioso).

## Tarefa 39.4: exemplo real no README do pacote e no README principal

Exemplo completo, no estilo que a Fase 24 já define pra `GetRecentTrace`: requisição via
`WebApplicationFactory`, `response.GetFlowTrace(factory.Services).ShouldCallAtMost(ComponentType.Database, 3).ShouldHaveNoErrors()`,
fechando o cenário de N+1 direto no teste, que é o próprio argumento de valor do pacote
(D-132, seção 4).

**Pronto quando:** o exemplo do README roda de verdade (não é pseudocódigo), mesmo padrão de
verificação que a Fase 24 já aplicou ao próprio `GetRecentTrace`.

## Fora de escopo

- Qualquer asserção além das cinco congeladas no ADR-0022: pedido real de usuário decide isso,
  não suposição agora.
- Integração com Shouldly/NSubstitute/qualquer outro framework: decisão travada no ADR-0022,
  não reabrir sem justificativa técnica nova.

## Aceite da fase

`dotnet build NetApiFlow.slnx -c Release` e `dotnet format NetApiFlow.slnx --verify-no-changes`
limpos nos dois TFMs. Suíte completa verde, incluindo os testes novos das tarefas 39.2 e 39.3.
`dotnet pack NetApiFlow.slnx -c Release` conferido, oitavo pacote novo publicável na próxima tag
(nono, junto com `NetApiFlow.Mcp`, que já foi o sétimo).

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos), o que ficou pendente.
