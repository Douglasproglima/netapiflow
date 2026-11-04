# Fase 10: Servidor MCP embutido (`NetApiFlow.Mcp`)

Projeto novo: `src/NetApiFlow.Mcp`. Testes: `tests/NetApiFlow.Mcp.Tests` (novo projeto, mesmo
molde dos outros pacotes opcionais).

Pré-requisito: Fase 1 aceita (`ITraceStore`, núcleo). Terceiro item do roadmap a sair do papel
(D-60 item A, D-72, ADR-0013), liberado por D-70 sem esperar as fases anteriores "em produção".

Contrato congelado em `docs/plano/contratos.md`: `AddNetApiFlowMcp`, `UseNetApiFlowMcp`. Decisões
de segurança e escopo (só leitura, sem exportação, sem escrita) travadas em ADR-0013, não
renegociáveis dentro desta fase.

## Checagens antes de começar

- ID de pacote `NetApiFlow.Mcp` já confirmado livre no nuget.org pela Planejadora (2026-11-04,
  `v3-flatcontainer`, 404). Não precisa reconferir.
- `ModelContextProtocol.AspNetCore` já verificado pela Planejadora: Apache-2.0, `net8.0`/`net9.0`/
  `net10.0`, versão `2.3.1` (2026-11-03). Adicionar em `Directory.Packages.props`, versão
  centralizada, igual a todo outro pacote.

## Tarefas

### 10.1: Scaffold do projeto `NetApiFlow.Mcp`

Metadados obrigatórios de `.claude/rules/packaging.md` (`PackageId=NetApiFlow.Mcp`,
`PackageReadmeFile`, ícone, tags, `RepositoryUrl`, `PackageLicenseExpression=MIT`, herdados de
`Directory.Build.props` onde já se aplica). Depende de `NetApiFlow` (núcleo, `ProjectReference`) e
`ModelContextProtocol.AspNetCore` (`PackageReference`). **Não** depende de
`NetApiFlow.Dashboard`.

**Pronto quando:** `dotnet build NetApiFlow.slnx` inclui o projeto novo nos dois TFMs,
`dotnet pack` gera `.nupkg`/`.snupkg` para ele junto dos outros seis.

### 10.2: `AddNetApiFlowMcp` e `UseNetApiFlowMcp`

`AddNetApiFlowMcp` registra `AddMcpServer().WithHttpTransport(...).WithToolsFromAssembly()` (ou
equivalente real do SDK; confirmar a forma exata contra a documentação do
`ModelContextProtocol.AspNetCore` instalado, a API pode ter mudado entre o que foi checado nesta
revisão e a versão que resolver no restore). `UseNetApiFlowMcp(basePath = "/flow/mcp")` monta
`app.MapMcp(...)` sob o `basePath`, devolvendo `IEndpointConventionBuilder` para encadear
`RequireAuthorization(...)`.

**Pronto quando:** teste de integração confirma que o endpoint responde a uma chamada MCP básica
(handshake/listagem de tools) em `Development`, com `UseNetApiFlowMcp()` chamado sem nenhuma
configuração extra.

### 10.3: Régua de segurança (prova R-14)

Fora de `IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization` nem `IFlowAccessFilter`
(núcleo, ADR-0015; reaproveitado, não duplicado) registrado no DI, toda rota do MCP responde 404.

**Pronto quando:** teste de integração com `UseEnvironment(Environments.Production)` real, sem
nenhuma configuração de autorização, confirma 404 em toda rota do MCP. Mitiga R-14.

### 10.4: Tool `list_recent_traces`

Lista traces recentes via `ITraceStore.QueryAsync`, parâmetros equivalentes ao filtro básico de
`GET /traces` (status, método, rota, limite). Resposta da tool é texto/JSON estruturado, nunca o
`FlowTrace` bruto com dado que a API REST já não exporia (a tool não contorna nenhuma regra de
sanitização existente, porque lê da mesma fonte que a API REST já lê).

**Pronto quando:** teste confirma que a tool devolve as traces esperadas com cada filtro, e que
nenhum filtro inválido derruba o servidor MCP (erro tratado, nunca exceção não tratada
propagando).

### 10.5: Tool `get_trace`

Obtém uma trace por id via `ITraceStore.GetAsync`. Trace inexistente devolve uma resposta de
"não encontrado" da própria tool, não uma exceção.

**Pronto quando:** teste cobre trace existente e trace inexistente.

### 10.6: Tool `get_component_stats`

Estatísticas por componente via `ITraceStore.GetComponentStatsAsync`, mesmos parâmetros do
`GET /metrics/components`.

**Pronto quando:** teste confirma a forma da resposta contra um conjunto de traces conhecido.

### 10.7: Amostra e verificação manual

`samples/SampleApi.Controllers` (ou outro sample) ganha `AddNetApiFlowMcp()`/`UseNetApiFlowMcp()`
na configuração, com uma nota no `README` do sample (ou comentário no `Program.cs`, já que
`.claude/rules/dotnet-core.md` restringe comentário ao porquê, isto cabe como passo de
verificação, não comentário de código) de como apontar um cliente MCP real (Claude Code,
`.mcp.json` apontando para a URL do sample) para testar manualmente.

**Pronto quando:** rodar o sample e configurar um cliente MCP real mostra as três tools
disponíveis e cada uma devolve dado real do sample em execução. Print ou transcrição no resumo da
fase.

### 10.8: Documentação

README e README.pt-BR.md ganham uma seção curta sobre o servidor MCP: o que é, como instalar o
pacote, aviso de segurança em destaque (só Development por padrão, mesma régua do dashboard,
nunca exposto sem autorização explícita). `docs/architecture.md` ganha uma nota equivalente à
da Dashboard.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente, aviso de segurança
visível sem precisar rolar a página.

## Aceite da fase

Todos os critérios de pronto acima. R-14 mitigado e provado. Nenhuma tool de escrita existe nesta
fase (nem apagar trace, nem disparar replay). `dotnet pack` continua gerando 7 pacotes (6 mais
`NetApiFlow.Mcp`), todos com `.snupkg`.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como
configurar um cliente MCP real contra o sample), qual forma exata da API do
`ModelContextProtocol.AspNetCore` foi usada (caso tenha divergido do que a Planejadora verificou
nesta revisão), o que ficou pendente. A Planejadora roda `/revisar-fase 10` antes de decidir a
próxima fatia do roadmap.
