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
| 3 | API do dashboard e SSE | Planejada | Tarefa 3.1 deve congelar `contratos.md` antes da Fase 4 começar |
| 4 | Dashboard Angular | Planejada | Depende do contrato REST congelado na Fase 3 |
| 5 | Empacotamento | Planejada | Depende das Fases 1-4 |
| 6 | Storages persistentes | Planejada | Pode rodar em paralelo com a Fase 4/5 depois da Fase 1 |
| 7 | Documentação e release | Planejada | Última fase antes da `0.1.0` |

Próximo passo: a Fase 3 começa pela tarefa 3.1 (congelar o contrato REST detalhado em
`contratos.md`), que é trabalho da Planejadora, não da Executora. Antes de a Executora começar a
implementar qualquer rota da Fase 3, a Planejadora precisa entregar esse contrato.

## Regra de avanço

A Executora só avança de fase quando todos os critérios de aceite da fase atual passarem
(seção 13 do prompt). Ao fim de cada fase, a Executora entrega o resumo da seção 16.7: o que foi
feito, como verificar, o que ficou pendente. A Planejadora roda `/revisar-fase <N>` conferindo
cada critério contra o código real antes de atualizar a tabela acima e liberar a fase seguinte.

Decisão nova tomada durante a execução que valha além da tarefa corrente vira ADR em
`docs/adr/`, escrito pela Planejadora.
