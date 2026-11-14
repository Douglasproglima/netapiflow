# ADR-0014: Impeccable como guarda de qualidade visual do dashboard

## Status
Aceito

## Discussion
[#186](https://github.com/Douglasproglima/netapiflow/discussions/186)

## Contexto
ADR-0009 define o guia de marca (`docs/brand/tokens.md`) como referência canônica de cor,
tipografia e espaçamento, mas registra, na própria seção de consequências, o risco de cada fase
futura que toca UI reinventar um valor em vez de consultar o guia. Isso já aconteceu de forma
concreta: a cor semântica base (`--naf-danger`, `--naf-warning`, `--naf-success`) vinha sendo usada
como texto sobre o próprio fundo tingido dela em banner, chip e etiqueta, caindo abaixo de 4,5:1
de contraste em seis combinações (duas no tema escuro, quatro no claro), apesar de cada cor passar
em AA sobre fundo liso isoladamente. Ninguém tinha medido o par específico "texto sobre fundo
tingido" antes.

[Impeccable](https://impeccable.style) (Paul Bakaus, Apache-2.0, `github.com/pbakaus/impeccable`)
é um kit de vocabulário de design para agentes de IA: skills (`/impeccable critique`,
`/impeccable audit`), um detector de 62 regras que roda sem IA (por arquivo, por URL ou contra uma
página aberta no Chromium via `--cdp`), saída para CI (`--json`, `--github`, código de saída), e
exceções registradas (`impeccable ignores add-rule`/`add-value`/`add-file`). Lê contexto do
projeto em `PRODUCT.md` e `DESIGN.md`, na raiz do repositório (confirmado contra a documentação
oficial: `init` escreve `PRODUCT.md`, `document` escreve `DESIGN.md`, descritos como "artefatos de
projeto compartilhados, pertencem ao repositório").

Rodado contra o build atual do dashboard (`/flow`, `/history`, `/components`, `/runner`, dois
temas, 390×844) e contra os dois mockups de `docs/design/`: ~30 avisos, a maioria do mesmo achado
de contraste já descrito acima, mais um rótulo de versão abaixo do tamanho mínimo de texto de
interface (10px, mínimo 11px) e um aviso leve de hierarquia tipográfica plana.

## Decisão
Impeccable entra como **ferramenta de desenvolvimento**, não como dependência do produto. Nada do
que ele instala (skill, CLI via `npx`, eventual entrada em `package.json` como `devDependency`)
entra no `.nupkg` nem em `ClientApp/dist/` (ADR-0005): quem instala um pacote NetApiFlow nunca
ganha dependência nova por causa disto, nem em build time nem em runtime.

`PRODUCT.md` e `DESIGN.md` ficam na raiz do repositório, em inglês (o repositório é público e
internacional, mesma razão de `README.md`, D-02). `DESIGN.md` é derivado de
`docs/brand/tokens.md`: qualquer mudança de token entra primeiro em `docs/brand/tokens.md`
(fonte canônica, ADR-0009), depois é refletida em `DESIGN.md`. Os dois arquivos nunca divergem
por mais que uma fase.

**Exceções conscientes**, registradas via `impeccable ignores add-value` (não `add-rule`, que
descartaria a regra inteira em vez de só o valor específico da marca):

- `ai-color-palette`: o detector marca roxo/violeta como a paleta padrão de interface gerada por
  IA. Flow 600 (`#7C3AED`)/Flow 700 (`#6D28D9`) é a identidade do projeto (ADR-0009), não um
  acidente de geração. A distinção visual real não vem só da cor: vem dos ícones técnicos de
  fluxo com marcas de canto estilo HUD e dos ícones de estado de nó (`docs/brand/node-states/`),
  ambos desenhados especificamente para este produto.
- `overused-font`: Inter em ~92% do texto é a fonte de interface definida no guia (`docs/brand/
  tokens.md`), não um padrão não examinado.

Trocar a paleta ou a fonte principal exige um ADR novo que substitua o ADR-0009 explicitamente,
não um ajuste silencioso para calar o detector.

## O que isto não cobre
Foco e navegação por teclado, ordem de leitura de leitor de tela, e qualquer coisa em código
.NET. Impeccable audita o dashboard Angular renderizado (HTML/CSS/layout), não acessibilidade de
interação nem o núcleo da biblioteca.

## Consequências
- Aviso novo do detector vira critério de bloqueio de fase (ver `.claude/rules/design-quality.md`),
  a não ser que já esteja nas exceções acima.
- `docs/brand/tokens.md` ganha uma seção de cor de texto sobre fundo tingido (`-on-tint`), com a
  razão de contraste de cada par medido, porque "a cor base passa em AA" não é a mesma garantia
  que "o par real usado em produção passa em AA".
- Dois mockups de `docs/design/` corrigidos para usar os tokens `-on-tint` e o tamanho mínimo de
  texto, mantendo mockup e implementação real na mesma régua visual.
