# ADR-0013: `NetApiFlow.Mcp`, servidor MCP embutido, só leitura, restrito a Development

## Status
Aceito

## Discussion
[#185](https://github.com/Douglasproglima/netapiflow/discussions/185)

## Contexto
D-60 item A registrou o servidor MCP embutido como a aposta de maior potencial de divulgação
orgânica do roadmap pós `0.1.0`, logo depois da interop OTel: expor a API REST que já existe
(`docs/plano/contratos.md`, seção 3) como tools de um servidor MCP (Model Context Protocol),
para que um agente de IA consiga consultar traces de uma API .NET em desenvolvimento sem nenhum
código de integração próprio.

A superfície pública da `NetApiFlow.Dashboard` já expõe tudo que esse pacote precisa consumir:
`ITraceStore` (núcleo, `docs/plano/contratos.md` seção 1) é a abstração certa para implementar as
tools, sem reimplementar query nem reusar os handlers internos da Dashboard (`MapNetApiFlowDashboardApi`
é `internal`, não pensado para ser consumido por outro pacote).

SDK verificado antes de decidir (`.claude/rules/dependencies.md`): `ModelContextProtocol.AspNetCore`
(nuget.org), Apache-2.0, mantido em colaboração com a Microsoft, versão `2.3.1` publicada em
2026-11-03, compatível com `net8.0`, `net9.0` e `net10.0`. ID de pacote `NetApiFlow.Mcp` confirmado
livre no nuget.org (`v3-flatcontainer`, 404) em 2026-11-04.

## Decisão
Novo pacote opcional `NetApiFlow.Mcp`, mesmo molde dos pacotes de storage e do MediatR/EF Core:
depende de `NetApiFlow` (núcleo) e de `ModelContextProtocol.AspNetCore` (única dependência nova,
só neste pacote, núcleo continua sem dependência externa). Não depende de
`NetApiFlow.Dashboard`: pode ser instalado sozinho, sem a UI Angular.

Superfície pública, mesmo molde da Dashboard:

```csharp
namespace NetApiFlow.Mcp;

public static class NetApiFlowMcpServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlowMcp(this IServiceCollection services);
}

namespace NetApiFlow.Mcp.Endpoints;

public static class NetApiFlowMcpEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder UseNetApiFlowMcp(
        this IEndpointRouteBuilder endpoints, string basePath = "/flow/mcp");
}
```

Três tools na v1, todas **só leitura** (nenhuma tool de escrita, nenhuma equivalente a `DELETE
/traces`): listar traces recentes com filtro básico (status, método, rota, limite), obter uma
trace por id, e obter estatísticas por componente. Implementadas sobre `ITraceStore`, não sobre a
API REST (sem round-trip HTTP interno).

Mesma régua de segurança da Dashboard (ADR-0006): fora de `IHostEnvironment.IsDevelopment()`, sem
`RequireAuthorization(...)` nem `IDashboardAuthorizationFilter` (reaproveitado, não duplicado em
uma segunda interface) configurado explicitamente, toda rota do MCP responde 404. Um agente de IA
com acesso a tools de leitura de trace tem o mesmo risco de exposição que o dashboard visual, a
mesma mitigação se aplica.

`NetApiFlow.Mcp` junta-se ao grupo de pacotes publicados na mesma tag `v<versao>` (extensão
aditiva ao D-01: sete pacotes agora, não seis).

## Consequências
- Primeira dependência externa de verdade fora do núcleo desde os pacotes de storage: aceita
  porque é opcional (quem não quiser MCP não instala o pacote) e o núcleo continua limpo.
- Escopo de leitura só, de propósito: reduz drasticamente a superfície de risco de um agente de
  IA autônomo interagindo com o servidor (sem comando que apague ou altere nada).
- Não resolve escrita (ex.: disparar replay, apagar trace) nem tools mais ricas (busca textual,
  diff entre execuções). Fica para quando houver pedido real, não construído por antecipação.
