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
| `fase-0-fundacao.md` … `fase-7-documentacao.md` | Tarefas de cada fase, uma por commit, com critério de pronto |

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
| 7 | Documentação e release | **Aceita e mergeada, `v0.1.0` publicada** (2026-10-22) | PRs #17, #24 a #39 (identidade visual, workspace denso, idioma PT-BR/EN e tema manual, ordenação em `/history`, GIF, correções D-37 a D-51). `release.yml` publicou os 6 pacotes no nuget.org via Trusted Publishing na tag `v0.1.0`, confirmado por log (12 "Your package was pushed"). Duas tentativas anteriores de tag falharam e foram corrigidas: D-49 (corrida de build do Angular sem a etapa isolada que `ci.yml` já tinha) e D-50 (chave de Trusted Publishing nunca repassada ao `dotnet nuget push`) |

`0.1.0` publicada. Próximo passo: roadmap pós `0.1.0` já registrado em D-32 (zoom, legenda, sparkline, cenários, delay slider, follow-live), com i18n/tema (D-35/D-36) e ordenação (D-41) já adiantados nesta versão.

## Regra de avanço

A Executora só avança de fase quando todos os critérios de aceite da fase atual passarem
(seção 13 do prompt). Ao fim de cada fase, a Executora entrega o resumo da seção 16.7: o que foi
feito, como verificar, o que ficou pendente. A Planejadora roda `/revisar-fase <N>` conferindo
cada critério contra o código real antes de atualizar a tabela acima e liberar a fase seguinte.

Decisão nova tomada durante a execução que valha além da tarefa corrente vira ADR em
`docs/adr/`, escrito pela Planejadora.
