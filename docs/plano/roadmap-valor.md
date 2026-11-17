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
| 1 | Fase 33: autenticação automática no painel "Testar um endpoint" (ler `securitySchemes` do OpenAPI, campo "Autorizar" por esquema, header aplicado automaticamente por rota) | D-128, achado em dogfooding real contra API de terceiro com autenticação | 🔴 Alto, inviabiliza o Executor contra qualquer API real protegida | Implementada e mergeada em `develop` (PR #235), ainda sem release. Falta o teste real de ponta a ponta no `rajadocs-backend`, mesma API que motivou a fase, antes de decidir a versão MINOR |
| 2 | Resolver `PathBase` separado de `DashboardPath` pro painel "Testar um endpoint": hoje o cliente só enxerga o `<base href>` combinado (`{PathBase}{DashboardPath}/`), então qualquer chamada resolvida contra ele ganha um `/flow` indevido quando o host não usa `PathBase` nenhum. Exige campo novo em `GET /config`, API pública nova | D-126, metade da tarefa 32.11 tentada e revertida de propósito pela Executora (trocava um bug raro por um bug comum), fora de escopo de um hotfix PATCH | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: a validação de path param vazio (a outra metade da 32.11) já está em `0.3.2`, só a parte de `PathBase` ficou de fora |
| 3 | Legenda do grafo reserva espaço fixo em pixel no `viewBox`; a 31.16 (D-122) já limita a explosão num canvas baixo, mas a proporção reservada ainda cresce conforme o canvas encolhe, por qualquer motivo, não só replay. Decisão de forma em aberto: legenda proporcional, colapsar abaixo de um limiar, ou sair do esquema de overlay | Achado da própria Executora durante a 31.16, registrado no resumo de fim de fase da Fase 31 | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: o caso grave já está coberto, isto é refinamento visual |
| 4 | `app.ts`, contador "em andamento" do topbar: `switchMap(() => this.api.getLiveTraces())` de `ngOnInit` sem `catchError`, mesma classe de bug que a 31.13 corrigiu em `flow-page`, fora do escopo literal daquela tarefa | Achado da Executora durante a Fase 31, registrado no resumo de fim de fase | ⚪ Baixo, não bloqueante | Ideia registrada, não planejada: falha de rede mataria essa assinatura silenciosamente, sem mensagem de erro |
| 5 | Tela de Arquitetura: seletor de rota mostrando primeiro as rotas com execução na janela (com contagem); estado vazio com próximo passo (link pro Executor com a rota pré-selecionada); rota e janela selecionadas refletidas na URL (link compartilhável, recarga sem perder estado) | Pedido do usuário ao relatar o A5 (D-124) | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada, sem plano ainda |
| 6 | Cenários de demonstração configuráveis pelo host: `NetApiFlowOptions` ganha uma lista de cenários, `GET /config` espelha, a aba "Scenarios" só aparece quando o host configurou pelo menos um, seletor e header de atraso artificial (`X-NetApiFlow-Demo-Delay-Ms`) seguem junto da aba, os dois samples deste repositório configuram os próprios cenários | D-122, escopo retirado da tarefa 31.1 por exigir API pública nova (MINOR), incompatível com a Fase 31 ter saído como hotfix PATCH `0.3.1` | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: a Fase 31 removeu a aba hardcoded (achado A1) em vez de torná-la configurável; esta é a reconstrução da função, do jeito certo, numa fase futura que já nasça MINOR |
| 7 | Indicador visual (ícone de cadeado) na lista de rotas do Executor mostrando quais exigem autenticação, antes mesmo de selecionar a rota | Deixado fora de escopo da Fase 33 de propósito, nice-to-have que complementa o "Autorizar" | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada |
| 8 | `SqlServerTraceStore` compara `method` por igualdade exata de string, sem `ILIKE`/case-insensitive como `PostgreSqlTraceStore` já faz; inconsistência entre os dois storages persistentes | D-124 (achado incidental ao investigar o A5) | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: impacto prático baixo (verbo HTTP já normalmente canônico), mas é inconsistência real entre os dois backends |
| 9 | Ingestão de Dapper sobre SQL Server via `ExternalActivitySource` novo | D-114 (achado 4, dogfooding) | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: código misto EF Core + Dapper perde o nó `Database` na parte em Dapper, mesma lacuna que `ExternalActivitySource.Npgsql` já resolveu pro lado PostgreSQL |
| 10 | Piso em pixels na lista do painel "Testar um endpoint" (`.endpoint-form__list-wrapper`), hoje só `max-height: 55%` | D-116 (achado 2, dogfooding), R-28 | ⚪ Baixo, não bloqueante | Planejada: decisão de forma passa por `docs/design/flow-workspace/guide.md` antes do código |
| 11 | Investigar de uma vez o conjunto de testes `.NET` sensíveis a timing/agendamento sob carga de CI (três ocorrências já registradas: D-54, D-125, D-127), em vez de tratar cada falha isolada na hora que aparece | D-54, D-125, D-127 | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada |
| 12 | No painel "Autorizar" (Fase 33), decodificar o `exp` de um JWT colado e mostrar tempo até expirar, ou um aviso quando já expirado | D-129, sugestão da segunda IA que revisou o desenho da Fase 33, custo baixo mas não essencial pra fase valer a pena | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada, deixada fora da Fase 33 de propósito |
| 13 | Se a própria busca do documento OpenAPI devolver 401 (API que tranca `/openapi/v1.json` fora de ambiente de desenvolvimento), reenviar a busca já com o token guardado em vez de o Runner morrer sem carregar nada | D-129, sugestão da segunda IA que revisou o desenho da Fase 33 | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada, deixada fora da Fase 33 de propósito |
| 14 | No painel "Autorizar" (Fase 33), o aviso de "nenhuma alternativa preenchida" não sabe que um campo `http`/`basic` preenchido nunca é de fato aplicado (sem preview nem aplicação automática): preencher só esse campo faz o aviso sumir mesmo a requisição saindo sem header, 401 mudo num caso raro | D-131, achado do merge-auditor antes do merge da Fase 33 | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: esquema raro em ASP.NET Core, já fora do plano original da Fase 33 |
| 15 | Renomear `public static class NetApiFlow` (hoje no `namespace NetApiFlow`, violando CA1724): breaking change real, sem alias de compatibilidade possível, mesma régua do D-75. A janela pra decidir fecha na 1.0, depois disso o custo de corrigir só cresce | D-133, terceira auditoria externa pós-lançamento; D-136, evidência concreta nova: `NetApiFlow.Step(...)`, exatamente como o README documenta, não compila quando chamado da global namespace (`Program.cs` de top-level statements), por causa dessa mesma colisão de nome | 🔴 Alto, deixou de ser só estético: hoje já é uma falha de compilação real no caminho mais comum de chamar o produto, contornada na Tarefa 34.8 só no texto do README, não na causa | Ideia registrada, não planejada: decisão da Planejadora, não da auditoria; se decidir manter como está, o argumento também precisa ficar escrito, não só o silêncio, e precisa explicar por que o contorno do README (`using static`) é aceitável como solução permanente |
| 16 | Fase de investigação sobre `net11.0` e sobre o furo de doze meses do ADR-0003 (achado em D-134): confirmar se a suíte inteira compila e passa em `net11.0`, se os atributos nativos de semantic convention do OpenTelemetry que o ASP.NET Core 11 passou a emitir por padrão na mesma `Activity` HTTP usada como `TraceId` do NetApiFlow duplicam ou conflitam com os spans do produto, se a propagação de `Activity.Current` por `ExecutionContext` sobrevive ao runtime-async (já ligado no shared framework do ASP.NET Core em `net11.0`, confirmado nas release notes oficiais, mesmo sem o NetApiFlow optar pelo preview), e sobretudo decidir o que fazer com `net8.0` agora: a cláusula original do ADR-0003 ("trocar por `net12.0` quando `net8.0` sair de suporte") não tem como ser cumprida, `net8.0` já saiu de suporte e `net12.0` só deve lançar em novembro de 2027, quase um ano de lacuna que o ADR nunca previu | D-133 e D-134, achado novo confirmado contra documentação oficial da Microsoft (breaking change documentado do ASP.NET Core 11, e a própria aritmética das datas de suporte) | 🟡 Médio-Alto, toca o coração do produto (propagação de `Activity.Current`) e uma lacuna real do ADR-0003 que já existe hoje, não é hipotética | Ideia registrada, não planejada: molde das Fases 19/23/27 (investigar antes de escrever código de produção), substitui ou não o ADR-0003 só depois do resultado |

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
auditoria de frontend achados Alta/Média (Fase 31, `v0.3.1`) e Baixa (Fase 32, `v0.3.2`). Roadmap
de valor pós-`0.1.0` original (D-32, D-58, D-60) zerado; Fases 31/32 foram um ciclo à parte, de
uma auditoria real, não parte dele; Fase 33 é um ciclo novo, de dogfooding real contra uma API de
terceiro.

## Fora do ranking, decisão de escopo em aberto

Entry points não-HTTP (D-61): não é item de roadmap, é tensão de escopo sem decisão.
