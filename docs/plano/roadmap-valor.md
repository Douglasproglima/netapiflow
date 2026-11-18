# Roadmap por peso de valor de entrega

Registrado em D-79. Diferente de `decisoes.md` (append-only, histórico de decisões), este arquivo
é **vivo**: a Planejadora atualiza a tabela sempre que um item é entregue ou a prioridade muda,
sem precisar de uma decisão nova para isso. Mudar a nota de peso ou a posição de um item não
entregue é ajuste de planejamento normal; decidir entregar um item fora de ordem, ou mudar o
critério de peso em si, é decisão nova (vira linha em `decisoes.md`, referenciando este arquivo).

## Critério de peso

Quatro eixos, nenhum peso numérico fixo: impacto estratégico (o que o audit de mercado, D-58/D-60,
já apontou como lacuna perigosa), dor real do dev fechada, esforço de implementação, e potencial de
divulgação orgânica (projeto ainda sem lançamento público divulgado, D-70).

## Backlog, por peso

| # | Item | Fonte | Peso | Status |
|---|---|---|---|---|
| 1 | **Publicar a demo navegável** (já construída, Fase 40) no `netapiflow-site`: o trabalho caro (fixtures, interceptor, build `demo`) está feito, falta só o passo de hospedar o resultado. Repositório diferente, fora do alcance da Executora deste repositório | D-156, reavaliação de 18/11: "é o único item de toda a lista onde o esforço restante é desproporcionalmente menor que o retorno" | 🔴 Alto, maior retorno por esforço de toda a lista, e o trabalho caro já está pago | Não planejada aqui: decisão e execução cabem a quem opera `netapiflow-site`, fora da governança deste repositório |
| 2 | **Divulgar `NetApiFlow.Testing` como coisa separada**, não só "nono pacote" dentro de um produto que se vende pelo grafo. O pacote já existe (Fase 39) com a superfície certa (`GetFlowTrace`, `ShouldCallAtMost`, `ShouldNeverCall`, `ShouldHaveCalled`, `ShouldHaveNoErrors`); falta conteúdo de divulgação (post, exemplo isolado, seção própria no README/site) tratando-o como caminho de entrada independente do dashboard | D-156: "o melhor gancho de divulgação que o produto tem, melhor que o dashboard. 'Quebre o build quando um endpoint fizer N+1' é uma frase que um dev .NET repassa" | 🔴 Alto, caminho de entrada mais fácil que o produto já teve | Não planejada: decisão de conteúdo/canal, não de código deste repositório |
| 3 | Rastreamento de Service/Repository sem nenhuma marcação manual, degrau 2: resolver sem exigir interface registrada no DI (hoje `AddFlowTracked`/`DecorateFlowTracked` dependem disso). O degrau 1 (`ExternalSources` ligado por padrão em `Development`) já foi entregue (Fase 41); este é o que falta, e é o que decide se o produto compete de verdade com quem instrumenta sozinho | D-132, seção 4; D-146, D-156 ("continua sendo o que decide se o produto é útil em larga escala, e continua sendo o mais caro") | 🟡 Médio-Alto, fecha o único ponto onde concorrentes reais ganham a primeira impressão | Não planejada: a investigação já aponta que só source generator resolve, decisão de arquitetura real, precisa de ADR e fase própria antes de qualquer código |
| 4 | Renomear `public static class NetApiFlow` (hoje no `namespace NetApiFlow`, violando CA1724): breaking change real, sem alias de compatibilidade possível, mesma régua do D-75. A janela pra decidir fecha na 1.0, depois disso o custo de corrigir só cresce | D-133, D-136 (evidência concreta: o snippet do README não compilava em `Program.cs` top-level, contornado com `using static`, "essa linha é ela mesma um sinal do problema", D-156) | 🔴 Alto, irreversível depois da 1.0 | Não planejada: decisão da Planejadora, breaking change real |
| 5 | Legenda do grafo reserva espaço fixo em pixel no `viewBox`; a 31.16 (D-122) já limita a explosão num canvas baixo, mas a proporção reservada ainda cresce conforme o canvas encolhe, por qualquer motivo, não só replay. Decisão de forma em aberto: legenda proporcional, colapsar abaixo de um limiar, ou sair do esquema de overlay | Achado da própria Executora durante a 31.16, registrado no resumo de fim de fase da Fase 31 | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: decisão de forma ainda em aberto, precisa da Planejadora antes de virar fase |
| 6 | Tela de Arquitetura: seletor de rota mostrando primeiro as rotas com execução na janela (com contagem); estado vazio com próximo passo (link pro Executor com a rota pré-selecionada); rota e janela selecionadas refletidas na URL (link compartilhável, recarga sem perder estado) | Pedido do usuário ao relatar o A5 (D-124) | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: múltiplas decisões de UX, sem plano ainda |
| 7 | Cenários de demonstração configuráveis pelo host: `NetApiFlowOptions` ganha uma lista de cenários, `GET /config` espelha, a aba "Scenarios" só aparece quando o host configurou pelo menos um, seletor e header de atraso artificial (`X-NetApiFlow-Demo-Delay-Ms`) seguem junto da aba, os dois samples deste repositório configuram os próprios cenários | D-122, escopo retirado da tarefa 31.1 por exigir API pública nova (MINOR), incompatível com a Fase 31 ter saído como hotfix PATCH `0.3.1` | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: é reconstrução de feature, não dívida técnica |
| 8 | Multi-instância: `GET /traces/live` só enxerga o `FlowRecorder` em memória da réplica que respondeu a conexão SSE, sem visão agregada atrás de um load balancer sem sessão fixa (documentar isso já está feito, Fase 36). Resolver de verdade, quando existe storage persistente configurado, exige a visão ao vivo cair pro store em vez do `FlowRecorder`, e cada trace carregar o id da instância que a processou. Um dos dois itens que abrem a porta de adoção corporativa maior, junto do item "fora do ranking" sobre entry points não-HTTP (D-61) | D-132, achado da segunda auditoria externa do site; D-156: "é um dos dois que abrem a porta corporativa, e continua intocado" | 🟡 Médio, abre adoção corporativa maior, sem urgência enquanto storage persistente for pouco usado | Ideia registrada, não planejada: decisão de arquitetura |
| 9 | Suspeita (não reproduzida ainda, achado por leitura de código): o dedup entre `NetApiFlow.EntityFrameworkCore` e os drivers de interop nativo (`ExternalActivitySource.Npgsql`, desde a Fase 19; `ExternalActivitySource.SqlClient`, Fase 37) pode estar checando a coisa errada. A checagem (`IsCommandAlreadyCoveredByAStep`/`IsAlreadyCoveredByAStep`) compara só `Activity.Current?.Source.Name == "NetApiFlow"`, sem checar se esse step ambiente é do tipo `ComponentType.Database`. Qualquer `NetApiFlow.Step(...)` (inclusive um `Service`/`Repository` tracked via `DecorateFlowTracked`) já deixa `Activity.Current` com esse `Source.Name`, então uma chamada Dapper/`SqlCommand` crua feita **de dentro** de um método rastreado (o próprio cenário que motivou o item 3 deste roadmap, "código misto EF Core + Dapper") seria suprimida como se já tivesse span, mesmo sem nenhum `Database`-step real aberto. A suíte atual nunca testou esse caso: o teste do Npgsql se chama `RawNpgsqlCommand_WithoutEntityFrameworkCore_...`, só cobre a chamada crua sem nenhum step ambiente de nenhum tipo | Achado da Planejadora revisando o PR da Fase 37 (D-150), por leitura de código e semântica determinística do `ActivitySource`/`Activity.Current`, sem teste real rodado ainda | 🟡 Médio-Alto, se confirmado, afeta duas fontes (uma já em produção desde a Fase 19, uma recém-mergeada) e esvazia o motivo original do item que já motivou a interop com SQL Server | Não planejada: primeira tarefa de qualquer fase que pegar isto é reproduzir com um teste real (`DecorateFlowTracked` envolvendo uma chamada Npgsql/SqlClient crua), antes de decidir se corrige. Critério da D-141: sem reprodução, não é hotfix |
## Já entregues (fora deste ranking, histórico em `docs/plano/README.md`)

Interop OTel ingestão (Fase 8), detector de N+1 (Fase 9), servidor MCP (Fase 10), Impeccable
(Fase 11, governança, não é item de roadmap de produto), tempo não explicado (Fase 12),
exportação OTLP (Fase 13), visão agregada "Insights" (Fase 14), sampling tail-based (Fase 15),
mapa de arquitetura vivo (Fase 16), log/exceção correlacionados por etapa (Fase 17), trace
portátil export/import (Fase 21), diff entre execuções (Fase 22), visibilidade de pré-pipeline
(Fase 23), asserção de fluxo em teste (Fase 24), controles e sinais visuais do grafo ao vivo
(Fase 25), cenários de demonstração e atraso artificial configurável (Fase 26), correções
pré-lançamento da primeira (Fase 29) e segunda (Fase 30) rodada de dogfooding, visibilidade de
cancelamento (Fase 27), anotação de deploy na timeline do histórico (Fase 28), correções da
auditoria de frontend achados Alta/Média (Fase 31, `v0.3.1`) e Baixa (Fase 32, `v0.3.2`),
autenticação automática no painel "Testar um endpoint" lendo `securitySchemes` do OpenAPI (Fase
33, `v0.4.0`, validada de ponta a ponta contra uma API real de terceiro antes do release). Roadmap
de valor pós-`0.1.0` original (D-32, D-58, D-60) zerado; Fases 31/32/33 foram um ciclo à parte, de
auditoria e dogfooding reais, não parte dele.

Terceira auditoria externa pós-lançamento (D-133/D-134) e a rodada autônoma que ela motivou
fecharam quase toda a frente de solidez em três dias (D-156): hotfix de credibilidade (Fase 34,
`v0.4.1`), hotfix do binário quebrado do `MediatR` (Fase 35, `v0.4.2`), lote de dívida técnica
pequena (Fase 36: itens 3/6/7/9/10/11/12/13/17-documentação do ranking anterior, mais o projeto de
verificação AOT do caminho manual), interop SqlClient e `hostPathBase` (Fase 37, `v0.4.3`),
pacote novo `NetApiFlow.Testing` sem dependência de terceiro (Fase 39, ADR-0022), demo navegável
construída e não publicada (Fase 40, ver item 1 do ranking acima), `ExternalSources` ligado por
padrão em `Development` (Fase 41, primeiro degrau), investigação `net11.0` que virou ADR-0023
(Fase 38, fecha o furo de doze meses do ADR-0003), e o hotfix que tornou o núcleo AOT-limpo de
verdade mais a guarda de `_disposed` nas três classes que escutam `DiagnosticListener` (Fase 42,
`v0.4.3`).

## Fora do ranking, decisão de escopo em aberto

Entry points não-HTTP (D-61): não é item de roadmap, é tensão de escopo sem decisão. A segunda
auditoria externa do site (D-132) reforça o argumento a favor de decidir: toda empresa tem
worker, job agendado ou consumidor de fila, e o produto hoje não enxerga nenhum deles. Não muda a
natureza da tensão, só aumenta a urgência de resolver. D-156 liga isso ao item 8 do ranking acima
(multi-instância): os dois juntos são o que abre a porta de adoção corporativa maior, e os dois
continuam intocados.
