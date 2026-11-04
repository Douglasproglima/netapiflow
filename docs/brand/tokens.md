# Tokens de marca do NetApiFlow

Fonte: `Guia de Marca v1` (out/2026), ver ADR-0009. Referência para qualquer tela ou peça de
comunicação que precise das cores, tipografia ou espaçamento oficiais do projeto.

## Cores · Obsidian (tema escuro, principal)

| Token | Hex | Uso |
|---|---|---|
| `--naf-bg` | `#090B10` | Fundo |
| `--naf-surface` | `#131722` | Superfície |
| `--naf-border` | `#232B3E` | Divisórias |
| `--naf-flow-600` | `#7C3AED` | Botão primário (texto branco) |
| `--naf-flow-500` | `#8B5CF6` | Traço, gráficos, foco |
| `--naf-flow-400` | `#A78BFA` | Texto/links sobre fundo escuro |
| `--naf-success` | `#10B981` | Concluído |
| `--naf-warning` | `#F59E0B` | Lento, retry |
| `--naf-danger` | `#EF4444` | Erro, fallback |
| `--naf-idle` | `#64748B` | Aguardando, não executado |
| `--naf-text` | `#F8FAFC` | Texto principal |
| `--naf-text-muted` | `#94A3B8` | Texto secundário |

## Cores · Stage Light (tema claro)

| Token | Hex | Uso |
|---|---|---|
| Paper | `#FFFFFF` | Fundo |
| Mist | `#F1F5F9` | Superfície |
| Borda de controle | `#64748B` | Borda, contraste ≥ 3:1 |
| Flow 700 | `#6D28D9` | Marca, AAA 7,1:1 |
| Emerald 700 | `#047857` | Concluído, AA 5,5:1 |
| Âmbar 700 | `#B45309` | Lento, AA 5,0:1 |
| Coral 700 | `#B91C1C` | Erro, AA 6,4:1 |
| Ink | `#0F172A` | Texto principal, AAA 18:1 |

## Tipografia

| Família | Função | Pesos |
|---|---|---|
| Plus Jakarta Sans | Display, títulos | SemiBold 600, Bold 700, ExtraBold 800 (wordmark) |
| Inter | Interface, corpo | Regular 400, Medium 500, SemiBold 600 |
| JetBrains Mono | Métricas, código, HTTP | Regular 400, Medium 500, tabular nums |

## Espaçamento (base 4)

`space-1` 4px · `space-2` 8px · `space-3` 12px · `space-4` 16px · `space-6` 24px · `space-8` 32px ·
`space-12` 48px.

## Raio de borda

`radius-sm` 8px (controles) · `radius-md` 12px (cards pequenos) · `radius-lg` 18px (cards) ·
`pill` 999px (status).

## Cores de texto sobre fundo tingido (on-tint)

Achado do Impeccable (detector de qualidade visual, ver ADR-0014), 2026-11-04: banner, chip e
etiqueta usam a cor semântica base (`--naf-danger`, `--naf-warning`, `--naf-success`) como texto
sobre o próprio fundo tingido dela (`--naf-danger-tint` etc.), e esse par cai abaixo de 4,5:1 em
alguns casos, mesmo a cor base passando em AA sobre fundo liso. Corrigido com um token de texto
próprio para esse contexto, calculado contra o fundo tingido real (WCAG 2.1, fórmula de
luminância relativa), não contra o fundo liso da tabela acima.

| Token | Tema | Hex | Fundo tingido testado | Razão |
|---|---|---|---|---|
| `--naf-danger-on-tint` | Obsidian (escuro) | `#F87171` | `#2D1C26` (banner "Origem do erro") | 5,81:1 |
| `--naf-danger-on-tint` | Obsidian (escuro) | `#F87171` | `#2B1A22` (etiqueta DELETE) | 5,96:1 |
| `--naf-warning-on-tint` | Stage Light (claro) | `#92400E` | `#EBE5E1` (chip de lentidão) | 5,68:1 |
| `--naf-warning-on-tint` | Stage Light (claro) | `#92400E` | `#EFE8E3` (etiqueta PUT) | 5,85:1 |
| `--naf-success-on-tint` | Stage Light (claro) | `#065F46` | `#D9E9E9` (etiqueta POST) | 6,14:1 |
| `--naf-success-on-tint` | Stage Light (claro) | `#065F46` | `#DDEBEB` (etiqueta POST, variante) | 6,28:1 |

Pares não listados aqui (`--naf-danger-on-tint` no tema claro, `--naf-warning-on-tint` e
`--naf-success-on-tint` no tema escuro) não foram reportados como falha pelo detector: o token
`-on-tint` reaproveita a cor base (`--naf-danger-on-tint: var(--naf-danger)` etc.) até uma
medição apontar o contrário. Qualquer componente novo que ponha texto semântico sobre um fundo
tingido usa o token `-on-tint`, nunca a cor base diretamente.

## Estados de nó

Cor, ícone e texto juntos, nunca só cor (`.claude/rules/tracing.md` não exige isso, mas é regra
de marca e de acessibilidade). Ícones em `docs/brand/node-states/`, 16×16, mapeando
`NodeVisualState`:

| Estado | Arquivo | Cor |
|---|---|---|
| Aguardando | `waiting.svg` | `--naf-idle` |
| Em execução | `running.svg` | `--naf-flow-400` |
| Concluído | `completed.svg` | `--naf-success` |
| Lento | `slow.svg` | `--naf-warning` |
| Erro | `error.svg` | `--naf-danger` |
| Não executado | `skipped.svg` | `--naf-idle`, traço tracejado |

## Ícones técnicos de fluxo

Grid 64×64, traço 2px, terminações arredondadas, marcas de canto estilo HUD. Preenchimento só no
elemento que carrega estado. Em `docs/brand/icons/`:

| Arquivo | Papel |
|---|---|
| `inbound.svg` | Nó de entrada: recebe o payload HTTP e abre o trace |
| `pipeline.svg` | Etapa de processamento: handler, service ou repository |
| `outbound.svg` | Entrega consolidada: resposta enviada com status validado |

## Geometria do monograma

Grid 8×8, módulo 25u. Área de respiro de 1/8 do lado (25u). Traço de 14u com cantos arredondados,
diagonal em 45° exatos. Nós sólidos em raio 16, nós em anel com raio 13 e traço de 8u. Legível a
partir de 24px (só traço e dois nós); abaixo de 40px os anéis e o payload central somem.
Variantes em `docs/brand/logo-mark-dark.svg` (acento `#8B5CF6`) e `logo-mark-light.svg`
(acento `#6D28D9`).
