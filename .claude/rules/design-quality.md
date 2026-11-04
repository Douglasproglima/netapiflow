# Qualidade visual do dashboard (Impeccable)

Ferramenta de desenvolvimento (ADR-0014), nunca entra no `.nupkg` nem em `ClientApp/dist/`
(ADR-0005). Contexto do detector: `PRODUCT.md` e `DESIGN.md`, na raiz do repositório, derivados de
`docs/brand/tokens.md` (fonte canônica, ADR-0009).

## Quando rodar

Toda tarefa que toca `src/NetApiFlow.Dashboard/ClientApp` roda o detector nas telas afetadas
antes de a Executora dizer que terminou:

```
npx impeccable detect <url-ou-arquivo> --viewport 390x844
```

Nos dois temas (claro e escuro) e no viewport padrão de desktop, além do `390x844` acima. Rodar
contra as quatro telas (`/flow?trace=<id>`, `/history`, `/components`, `/runner`) quando a
mudança afeta algo compartilhado entre elas (tema, tipografia, espaçamento); só a tela afetada
quando a mudança é local.

## Regra de bloqueio

Aviso novo do detector bloqueia a tarefa, a não ser que já esteja registrado como exceção
consciente (ADR-0014: `ai-color-palette` para o roxo da marca, `overused-font` para a Inter).
Exceção nova não documentada em ADR não é motivo para rodar `impeccable ignores`: primeiro avisa
a Planejadora, que decide se vira ADR ou se o código precisa corrigir o achado de verdade.

Preferir `impeccable ignores add-value` (ignora um valor específico, ex. uma cor da marca) a
`impeccable ignores add-rule` (ignora a regra inteira) sempre que o valor específico resolver o
caso. `add-rule` só quando a regra inteira não se aplica a este projeto, decisão que também passa
pela Planejadora antes de virar exceção permanente.

## Resumo de fim de fase

Toda fase/tarefa que toca o dashboard traz, no resumo de fim de fase, a contagem de avisos do
detector antes e depois da mudança (ex.: "12 avisos antes, 0 depois" ou "0 antes, 0 depois, sem
regressão"). Não é só para provar que não piorou: é para a Planejadora saber se algum achado novo
apareceu e precisa virar decisão.
