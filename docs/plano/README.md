# Plano do NetApiFlow

Fonte única de verdade do produto: `netapiflow-prompt.md` (mantido fora deste repositório, em
`G:\Meu Drive\Controle\2026\00 - NetApiFlow\netapiflow-prompt.md`; peça à Planejadora se precisar
do conteúdo integral). Em conflito entre esta pasta e o prompt original, **o prompt prevalece**.

Este diretório é escrito e mantido só pela IA Planejadora. A IA Executora lê, não edita.

## Onde está cada coisa

| Arquivo | Conteúdo |
|---|---|
| `contratos.md` | API pública do núcleo, formato do evento SSE, visão geral da API REST. Não implementar divergindo sem avisar a Planejadora |
| `decisoes.md` | Decisões travadas (D-01 em diante), append-only, com justificativa |
| `riscos.md` | Riscos técnicos, mitigação e como cada um é provado por teste |
| `fase-0-fundacao.md` … `fase-12-tempo-nao-explicado.md` | Tarefas de cada fase, uma por commit, com critério de pronto |

## Estado das fases

| Fase | Nome | Status | Observação |
|---|---|---|---|
| 0 | Fundação | **Aceita e mergeada** (2026-10-19) | PR #1, CI verde nos 4 checks (build-test ×2 SO, pr-lint, secret-scan) |
| 1 | Núcleo de captura | **Aceita e mergeada** (2026-10-19) | PR #2, CI verde nos 4 checks. 54 testes (49 unitários + 4 integração + 1 placeholder). `contratos.md` atualizado com `NetApiFlowOptions` e os DTOs provisórios de `ITraceStore` |
| 2 | Formas de marcação | **Aceita e mergeada** (2026-10-19) | PR #3, CI verde nos 4 checks. 83 testes por TFM. `contratos.md` atualizado com `AddFlowTracked`/`DecorateFlowTracked`/`WithNetApiFlowTracking`/registros do MediatR e EF Core. R-01 e R-02 mitigados; D-17 registra a prova de Postgres via SQLite como aceita |
| 3 | API do dashboard e SSE | **Aceita e mergeada** (2026-10-20) | PR #4, CI verde nos 4 checks. 107 testes por TFM. R-04 e R-10 mitigados. D-20 (ping de handshake, heartbeat diverge por TFM) e D-21 (`IFlowTenantResolver` conectado, tarefa 3.8) resolvidos |
| 4 | Dashboard Angular | **Aceita e mergeada** (2026-10-20) | PR #5, CI verde nos 4 checks. 73 testes Angular + testes .NET de SSE/dashboard/config. Tarefa 4.14 (D-24, `GET /config`) concluída. Verificação visual confirmada (tema claro e escuro limpos, formulário do histórico sem vazamento horizontal em viewport reduzida) |
| 5 | Empacotamento | **Aceita e mergeada** (2026-10-20) | PR #8, CI verde nos 4 checks. Verificação da tarefa 5.6 rodada pessoalmente pela Planejadora (`dotnet pack` + `dotnet test` em `tests/manual-verification`, 4 testes verdes). PR #9 corrige o "pronto quando" da tarefa 5.5 (D-25) e congela a superfície pública de `NetApiFlow.Dashboard` em `contratos.md` |
| 6 | Storages persistentes | **Aceita e mergeada** (2026-10-20) | PR #12, CI verde nos 4 checks (incluindo Testcontainers de verdade no `build-test (ubuntu-latest)`). Testcontainers rodado pessoalmente com Docker local antes do merge: 20 testes por TFM contra Postgres e SQL Server reais. PR #11 congela a superfície pública dos dois pacotes de storage em `contratos.md` |
| 7 | Documentação e release | **Aceita e mergeada, `v0.1.0` e `v0.1.1` publicadas** (`v0.1.0` em 2026-10-22, `v0.1.1` em 2026-11-03) | PRs #17, #24 a #39 (identidade visual, workspace denso, idioma PT-BR/EN e tema manual, ordenação em `/history`, GIF, correções D-37 a D-51). `release.yml` publicou os 6 pacotes no nuget.org via Trusted Publishing na tag `v0.1.0`, confirmado por log (12 "Your package was pushed"). Duas tentativas anteriores de tag falharam e foram corrigidas: D-49 (corrida de build do Angular sem a etapa isolada que `ci.yml` já tinha) e D-50 (chave de Trusted Publishing nunca repassada ao `dotnet nuget push`). `v0.1.1` (PATCH) corrigiu o bundle do dashboard compilado contra o `@angular/build` vulnerável (D-53/D-56). `release.yml` ganhou espelho no GitHub Packages (D-57) |
| 8 | Interop OpenTelemetry, parte 1 (ingestão de `HttpClient`) | **Aceita e mergeada** (2026-11-04) | PR #86, CI verde nos 5 checks. Primeiro item do roadmap D-58 a sair do papel (D-68, ADR-0012), escopo deliberadamente estreito: só `ActivitySource` nativa do `HttpClient`, opt-in via `NetApiFlowOptions.ExternalSources`. Npgsql (duplicaria span do `NetApiFlow.EntityFrameworkCore`, R-06) e MassTransit ficam fora, por desenho, para uma fase futura própria. Achado real durante a tarefa 8.3 (D-69): `net8.0` não produz tag nenhuma na `Activity` nativa do `HttpClient` (só `net9.0+`); corrigido com um backfill de tag via `DiagnosticListener` isolado em `#if NET8_0`. Revisão de código na mesma branch achou e corrigiu um vazamento real de query string nesse backfill antes do merge. R-13 mitigado, provado por `HttpClientActivityTagBackfillTests` (inspeciona a tag crua da `Activity`) e `HttpClientInteropIntegrationTests` (ponta a ponta) |
| 9 | Detector de N+1 | **Aceita e mergeada** (2026-11-04) | PR #90, CI verde nos 5 checks. Segundo item do roadmap D-58 (D-71), 100% frontend: `graph-builder.ts` agrupa spans `Database` por `(Component, Operation)` em todo o trace (cross-parent, não por irmão direto, provado pelo teste mais importante da fase), selo de ícone+texto no grafo, painel de detalhes com contagem e "pular para a primeira". Sem mudança no núcleo .NET nem em `contratos.md`. Limiar fixo de 3, não configurável nesta fase |
| 10 | Servidor MCP embutido (`NetApiFlow.Mcp`) | **Aceita e mergeada** (2026-11-04) | PR #99, CI verde nos 5 checks. Item A do roadmap D-60 (D-72, ADR-0013). Pacote novo, única dependência nova fora do núcleo nesta rodada: `ModelContextProtocol.AspNetCore` (Apache-2.0, verificado pela Planejadora). Três tools, só leitura (listar traces, obter trace, estatísticas por componente), mesma régua de segurança da Dashboard. Achado real na tarefa 10.3 (D-75/ADR-0015): `IDashboardAuthorizationFilter` só existia em `NetApiFlow.Dashboard`, contradizendo a decisão de `NetApiFlow.Mcp` não depender desse pacote; corrigido movendo a interface para o núcleo como `IFlowAccessFilter`. R-14 mitigado, provado por `NetApiFlowMcpEndpointTests` e confirmado pela Planejadora no `.nuspec` real do pacote empacotado (não só no código-fonte). Sétimo pacote publicado na mesma tag |
| 11 | Impeccable, guarda de qualidade visual | **Aceita e mergeada** (2026-11-04) | PR #102, CI verde nos 6 checks (inclui o novo `visual-quality`). Governança/ferramenta de dev, fora do roadmap D-58/D-60 (D-73/D-76/D-77, ADR-0014). Tokens `-on-tint` aplicados em `_tokens.scss`, hierarquia tipográfica alinhada à escala do guia (D-74), botão "Testar um endpoint" movido pra topbar, `errbox` achatado. Rollout cauteloso confirmado na prática (D-76): só `low-contrast`/`undersized-ui-text` bloqueiam, resto é aviso; job `visual-quality` roda em modo aviso. Três achados reais corrigidos durante a revisão (D-77): `.claude/skills/impeccable/` (vendoring de terceiro, ~21 mil linhas) desrastreado do git; tabela de tipografia consolidada em `docs/brand/tokens.md`; serviço Postgres real adicionado ao job de CI depois de o log mostrar que o sample não conseguia sequer subir sem ele. R-15 mitigado, confirmado no log real do job (Postgres conectando, trace de erro gerado, detector rodando contra URL real, sem `--no-sandbox` necessário) |
| 12 | Tempo não explicado no waterfall | **Planejada, aguardando a Executora** | Item B do roadmap D-60 (D-78). 100% frontend (`details/waterfall.ts`), nenhuma mudança no núcleo nem em `contratos.md`. Conta certa travada no plano: união de intervalos `[startedAt, endedAt)`, não soma ingênua, porque span aninhado e branches paralelas de `Task.WhenAll` fariam a soma ingênua contar tempo em dobro. Só calculado para trace completo. Sem métrica de infraestrutura, fora de escopo por desenho |

`0.1.1` publicada, Fases 8 a 11 aceitas e mergeadas, Fase 12 planejada. Roadmap pós `0.1.0`
registrado em
D-32 (zoom, legenda, sparkline, cenários, delay slider, follow-live) e ampliado por duas rodadas
de auditoria de mercado: D-58 (interop OpenTelemetry como prioridade 1, detector de N+1, sampling
tail-based, diff entre execuções, correlação de log, asserção de fluxo em teste), D-60 (servidor
MCP embutido, tempo não explicado no waterfall, trace portátil, visibilidade de pré-pipeline, mapa
de arquitetura, visão agregada estilo Pulse). D-59 trava a regra de implementação: uma feature por
vez, clarificada pelo D-70 enquanto o produto não é divulgado publicamente (não precisa esperar
uso real em produção entre fases). D-61 registra entry points não-HTTP como tensão de escopo em
aberto, sem decisão. D-62 conecta todo ADR aceito a uma Issue pública no GitHub.

## Regra de avanço

A Executora só avança de fase quando todos os critérios de aceite da fase atual passarem
(seção 13 do prompt). Ao fim de cada fase, a Executora entrega o resumo da seção 16.7: o que foi
feito, como verificar, o que ficou pendente. A Planejadora roda `/revisar-fase <N>` conferindo
cada critério contra o código real antes de atualizar a tabela acima e liberar a fase seguinte.

Decisão nova tomada durante a execução que valha além da tarefa corrente vira ADR em
`docs/adr/`, escrito pela Planejadora.
