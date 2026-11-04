# ADR-0009: Guia de marca v1 como referência canônica de identidade visual

## Status
Aceito

## Issue
[#68](https://github.com/Douglasproglima/netapiflow/issues/68)

## Contexto
O projeto recebeu um guia de marca (`Guia de Marca v1`, out/2026), produzido por uma IA de
design a partir do briefing do produto. O documento define monograma, paleta de cores (temas
Obsidian e Stage Light), sistema tipográfico, ícones técnicos de fluxo, ícones de estado de nó e
tokens de espaçamento e raio de borda.

Sem um ponto único de referência no repositório, cada fase que toca UI (tema do dashboard em
Angular, ícone do pacote NuGet, README, material de divulgação) corre o risco de reinventar cor
ou ícone a partir do PDF ou de memória, gerando inconsistência.

## Decisão
Os assets extraídos do guia (SVG do monograma em duas variantes, três ícones técnicos de fluxo,
seis ícones de estado de nó) e os tokens de cor, tipografia e espaçamento ficam em
`docs/brand/`, com `docs/brand/tokens.md` como referência de leitura rápida. `docs/images/icon.png`
passa a ser gerado a partir de `docs/brand/logo-mark-dark.svg` (antes, placeholder da Fase 0).

Qualquer trabalho futuro de UI consome os tokens documentados em `docs/brand/tokens.md`, não o
PDF original nem uma nova interpretação da paleta.

## Consequências
- `docs/brand/` só tem SVG e Markdown. Nenhuma dependência nova, nenhum arquivo em `src/`.
- O tema Angular Material do dashboard (Fase 4, já mergeado) hoje usa cores padrão, não as
  deste guia. Retrofit é tarefa nova de `src/`, fora do escopo da IA Planejadora: fica como
  pendência a avaliar numa fase futura (Fase 7 ou chore dedicado), não decidida por este ADR.
- O ícone de estado de nó (`NodeVisualState`) já implementado no dashboard usa cor, ícone e
  texto juntos; os SVGs de `docs/brand/node-states/` documentam a forma visual oficial de cada
  estado, para controle de divergência se a Angular for retrabalhada.
- `docs/images/icon.png` do pacote NuGet passa a refletir a marca real, não um placeholder.
