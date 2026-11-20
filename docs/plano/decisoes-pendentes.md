# Decisões que esperam o dono

Arquivo vivo. A Planejadora prepara as opções e diz o que recomenda, mas **não decide** o que está aqui.
Quando o dono decide, a decisão vira uma linha em `decisoes.md` e o item sai deste arquivo.

As quatro decisões da rodada de 20/11/2026 (veículo da Fase 50, TFMs, divulgação e o README na `main`)
já foram tomadas e estão em D-188, D-189, D-190 e D-191. A escolha sobre os workflows do hotfix `0.5.2`
também: D-194.

A data de saída do `net8.0` foi confirmada no D-195.

## 1. A delegação da decisão final de qualidade à auditoria

A `AUDITORIA-16-11` informou que o dono lhe deu, por escrito, a decisão final de qualidade do projeto.
A Planejadora não viu o texto do dono, e ele muda quem decide. Precisa de confirmação direta: o alcance
da delegação (só qualidade e aprovação de PR, ou também escolha de versão e exceções ao gitflow) e se
uma decisão da auditoria vale sem o dono ver.

## 2. O README (achados A e B) na `main`, segundo uso da exceção D-191

O D-191 manda o dono confirmar cada uso da exceção que leva documentação à `main` sem versão e sem tag.
A auditoria autorizou este uso, e o dono não o confirmou diretamente. O texto novo do `README.md` e do
`README.pt-BR.md` (dashboard sob `FallbackPolicy` e `writeToProviders: true` no Serilog) já foi testado num
app novo e está em `develop`. Falta só a PR de documentação para `main` por uma branch `hotfix/*`, e o
merge de volta em `develop`.

Recomendação: confirmar. É só texto, reversível, e corrige uma frase errada do README que o usuário novo
lê hoje.
