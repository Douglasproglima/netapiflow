# ADR-0015: `IFlowAccessFilter` único no núcleo, compartilhado por Dashboard e MCP

## Status
Aceito

## Discussion
[#187](https://github.com/Douglasproglima/netapiflow/discussions/187)

## Contexto
ADR-0013 decidiu que `NetApiFlow.Mcp` reaproveita `IDashboardAuthorizationFilter` em vez de
duplicar uma interface equivalente, e que não depende de `NetApiFlow.Dashboard`. As duas decisões
são contraditórias na prática: `IDashboardAuthorizationFilter` está implementada em
`src/NetApiFlow.Dashboard/IDashboardAuthorizationFilter.cs`, namespace `NetApiFlow.Dashboard`, não
no núcleo. `docs/plano/contratos.md` já listava a interface (com esse nome) também na seção do
núcleo, mas essa entrada nunca correspondeu ao código real: documentação desatualizada, não um
contrato cumprido.

A Executora encontrou a contradição na prática, ao implementar a tarefa 10.3, e parou para
relatar em vez de escolher um dos dois lados sozinha (correto, por ser decisão de arquitetura, não
detalhe de implementação). Duas saídas óbvias: mover a interface para o núcleo, ou duplicá-la em
`NetApiFlow.Mcp`. Duplicar contradiz o texto do próprio ADR-0013 ("reaproveitado, não duplicado")
e cria duas interfaces concorrentes para o mesmo conceito, que dois pacotes diferentes podem
implementar de forma divergente sem nenhum aviso em tempo de compilação.

Uso real de `IDashboardAuthorizationFilter` hoje, antes de mover: um arquivo de produção
(`DashboardAccessGate.cs`, resolve via DI) e um teste (`DashboardAuthorizationTests.cs`). Projeto
ainda pré-1.0 (ADR-0001), sem lançamento público divulgado (D-70): mover e renomear agora é o
momento mais barato que vai existir para corrigir isso.

## Decisão
A interface migra para o núcleo (`NetApiFlow`), e muda de nome: `IDashboardAuthorizationFilter`
vira `IFlowAccessFilter`, mesma assinatura (`ValueTask<bool> AuthorizeAsync(HttpContext
context)`). O nome antigo citava "Dashboard" porque só existia um consumidor até aqui; o nome novo
não presume qual superfície de diagnóstico está sendo protegida, porque agora há duas
(`NetApiFlow.Dashboard`, `NetApiFlow.Mcp`) e pode haver mais no futuro.

`NetApiFlow.Dashboard` e `NetApiFlow.Mcp` dependem só de `NetApiFlow` (núcleo) para essa
interface, nenhum dos dois depende do outro. `UseNetApiFlowDashboard` e `UseNetApiFlowMcp`
continuam aceitando `RequireAuthorization(...)` encadeado e resolvendo `IFlowAccessFilter` do DI
do mesmo jeito que `IDashboardAuthorizationFilter` fazia antes, só a interface e o pacote onde ela
mora mudam.

Breaking change de namespace e nome, aceitável pré-1.0: quem já tiver uma implementação de
`IDashboardAuthorizationFilter` precisa trocar para `IFlowAccessFilter` na próxima versão. Sem
alias de compatibilidade: o projeto ainda não tem usuário externo divulgado (D-70), manter os
dois nomes só adicionaria confusão permanente por uma transição que ninguém de fora precisa.

## Consequências
- `docs/plano/contratos.md` corrigido nos três lugares que citavam a forma antiga: núcleo (onde a
  entrada já existia, só errada no texto de origem), seção da Dashboard (remove a declaração
  local) e seção do MCP (referencia a mesma interface do núcleo).
- `docs/plano/riscos.md`, R-09 e R-14: nota de atualização de nome, sem reabrir o que já foi
  mitigado.
- Próxima superfície de diagnóstico que precisar da mesma régua (ex. uma Fase futura do roadmap)
  reaproveita `IFlowAccessFilter` do núcleo direto, sem repetir esta decisão.
