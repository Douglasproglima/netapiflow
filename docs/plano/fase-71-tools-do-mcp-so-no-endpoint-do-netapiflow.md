# Fase 71: as tools do NetApiFlow só existem no endpoint do NetApiFlow (D-278, S9, R-36)

Um app que hospeda o próprio servidor MCP (`AddMcpServer().WithHttpTransport()` e `MapMcp("/mcp")`) e usa o `NetApiFlow.Mcp`
serve as tools do NetApiFlow no endpoint dele. A auditoria reproduziu com o pacote real, na `0.5.3` publicada e na head da #481:
em `Production`, sem nenhuma autorização configurada, o `/flow/mcp` responde `404` ao `initialize`, como o README promete, mas o `/mcp`
do app lista `get_component_stats`, `get_trace` e `list_recent_traces`, e o `list_recent_traces` devolve as traces (id, método, rota,
status e duração).

Vai no `0.6.0` pela exceção de segurança do D-232 (D-278). **Não é hotfix** (D-205): não é regressão, e a exposição exige que o app
hospede um MCP próprio. O dono decidiu em 24/11/2026 corrigir na `0.6.0` e publicar um quarto GitHub Security Advisory junto do release, como os outros três (D-279).

## O achado e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| S9 | O `AddNetApiFlowMcp` registra as tools no servidor MCP único do processo, e qualquer endpoint `MapMcp` do app serve essas tools. A regra "Same security rule as the dashboard" do README não vale para o endpoint do app | **Reproduzido pela auditoria**, com o pacote real, na `0.5.3` e na PR. A Planejadora não reproduziu e não leu o código |

O que vaza é o que as tools devolvem. O `list_recent_traces` devolveu id, método, rota, status e duração, e o `get_trace` devolve a
trace com as etapas, que é mais do que a lista mostrou. Não há corpo, argumento, header, query string nem SQL com parâmetro, porque o
NetApiFlow nunca os captura (ADR-0007). A auditoria põe a severidade na faixa dos outros advisories.

## Quando entra

**Depois da #481 (Fase 67) mergeada, e antes da Fase 58.** A 67 muda o mesmo código do MCP (as tools viram `internal`, o
`serverInfo.name` passa a `NetApiFlow` e, na correção da auditoria, só quando o app não deu o dele). Uma correção de segurança não
espera as outras fases, mas não pode ser escrita sobre código que está mudando.

## Ponto de parada

- Se nenhum desenho mantiver o `/flow/mcp` com **as mesmas regras de acesso** (`404` em `Production` sem autorização, `403` para
  `Origin` e `Host` alheios) e **as mesmas três tools**, **para e relata**.
- Se o SDK (`ModelContextProtocol.AspNetCore` 2.3.1) não tiver como separar as tools por endpoint sem um servidor à parte, **para e
  relata**: a escolha entre um servidor separado e outra saída é da Planejadora e da auditoria.
- Se a correção mudar o que um app **sem** MCP próprio vê (o `tools/list` do `/flow/mcp`, o `serverInfo`), **para**.
- Se as tools do próprio app deixarem de funcionar no `/mcp` dele, **para**.

## Tarefa 71.0: reproduzir e medir, antes de qualquer código

Host real, `net8.0` e `net10.0`, com o pacote `NetApiFlow.Mcp` e um app que tem o MCP dele: `AddMcpServer().WithHttpTransport()`,
uma tool própria (`app-tool`) e `MapMcp("/mcp")`, mais `AddNetApiFlowMcp` e `MapNetApiFlowMcp`.

1. **A reprodução.** Em `Production` sem autorização, e em `Development`: o `tools/list` do `/mcp` lista as tools do NetApiFlow, e o
   `tools/call list_recent_traces` pelo `/mcp` devolve traces. Visto falhando antes, com a saída.
2. **O espelho.** O `tools/list` do `/flow/mcp`, com a tool própria do app registrada, lista só as três tools do NetApiFlow, ou
   lista também a `app-tool`? Se listar, o `/flow/mcp` (que o NetApiFlow protege) serve uma tool do app, e isso entra no escopo.
3. **O que o SDK 2.3.1 oferece** para separar as tools por endpoint: a configuração por sessão
   (`HttpServerTransportOptions.ConfigureSessionOptions`, que recebe o `HttpContext` da requisição de `initialize`), os filtros de
   `tools/list` e de `tools/call`, e um servidor separado. Para cada um: funciona nos dois TFMs? Preserva as tools e o
   `ConfigureSessionOptions` do app? Qual o custo? A Executora escolhe o menor que passa nos critérios da 71.1 e diz qual.

4. **Desde quando,** para o intervalo do advisory: desde qual versão do `NetApiFlow.Mcp` o registro das tools no servidor MCP do processo é o mesmo (`git log -S` e a leitura do código do `v0.2.0` em diante), e a reprodução em ao menos uma versão publicada anterior à `0.5.3`.

**Pronto quando:** os quatro pontos respondidos com saída real, e o desenho escolhido dito antes do código.

## Tarefa 71.1: as tools só no endpoint do NetApiFlow

O desenho é o que a 71.0 escolher. Os critérios, todos testados:

- O `tools/list` do `/mcp` do app **não** traz as tools do NetApiFlow, e o `tools/call` delas pelo `/mcp` falha (a tool não existe).
- A `app-tool` continua funcionando no `/mcp`, e o `ConfigureSessionOptions` que o app já tenha continua valendo.
- O `/flow/mcp` fica **igual**: as mesmas três tools, as mesmas respostas, as mesmas regras de acesso, o mesmo `serverInfo`.
- Se a 71.0 mostrou o espelho, o `/flow/mcp` também **não** lista a tool do app.
- Um app **sem** MCP próprio não vê diferença nenhuma.
- Sem API pública nova. Se o desenho pedir uma, **para** e avisa.

## Tarefa 71.2: testes, o pacote real, README e CHANGELOG

- **Teste de host real, visto falhando antes,** nos dois TFMs, para cada critério da 71.1.
- **Um cenário permanente no job do pacote real** (`scripts/package-scenarios.mjs`): o app do job ganha um MCP próprio, e em `Production`
  sem autorização o `/mcp` não lista as tools do NetApiFlow, enquanto o `/flow/mcp` segue em `404`. Todo achado da auditoria que dá para
  reproduzir de fora vira cenário permanente (`.claude/rules/contratos-e-consumidores.md`). A vermelha vista no CI.
- **A linha do `guards.json`:** `S9-mcp-tools-not-in-the-app-endpoint`.
- **README, nos dois idiomas:** a frase "Same security rule as the dashboard" passa a dizer onde as tools existem (só no `/flow/mcp`) e que
  um app com MCP próprio não as serve no endpoint dele.
- **`CHANGELOG.md`, em inglês, `### Security`:** o que vazava, para quem (um app que hospeda o próprio MCP), desde quando (até a `0.5.3`) e o
  que mudou. O advisory é a Tarefa 71.3.

## Tarefa 71.3: o advisory (D-279)

O dono decidiu publicar um quarto GitHub Security Advisory junto do release da `0.6.0`, como os outros três (D-203, D-206 e o da Fase 56). **A Planejadora escreve o rascunho nesta fase, depois da correção,** no molde da Tarefa 50.10 (título, pacote e versões afetadas, CWE, descrição, impacto, o que não é afetado, a correção e o contorno, em inglês e sem dado de app real). **A severidade e o vetor CVSS a auditoria propõe depois da correção, com o pacote real,** como fez nos outros, e o dono confirma. As versões afetadas saem do item 4 da 71.0. Quem publica é a auditoria, junto do Release (D-198), e a Executora não publica nada.

## Fora de escopo

- A publicação do advisory: é da auditoria, junto do Release (D-198).
- Mudar o que as tools devolvem ou o nome delas.
- O `/flow/mcp` de um app que não usa o MCP do SDK de outra forma.

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Cada critério da 71.1 com teste de host real **visto falhando antes**, e o cenário do pacote real visto vermelho no CI.
- A auditoria refaz a reprodução com o pacote real, num app novo com MCP próprio, e reconfere a frase do README.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/71-tools-do-mcp` a partir de `develop` atualizado, **depois** da #481.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

A reprodução e o espelho (71.0). O desenho escolhido e por quê. Para cada critério: o teste, a saída dele falhando antes e passando
depois. O cenário do pacote real, visto vermelho.
