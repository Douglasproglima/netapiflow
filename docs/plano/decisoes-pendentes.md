# Decisões que esperam o dono

Arquivo vivo. A Planejadora prepara as opções e diz o que recomenda, mas **não decide** o que está aqui.
Quando o dono decide, a decisão vira uma linha em `decisoes.md` e o item sai deste arquivo.

As quatro decisões da rodada de 20/11/2026 (veículo da Fase 50, TFMs, divulgação e o README na `main`)
já foram tomadas e estão em D-188, D-189, D-190 e D-191. A escolha sobre os workflows do hotfix `0.5.2`
também: D-194.

## 1. A data de saída do `net8.0`

O ADR-0025 manda anunciar a saída do `net8.0` com data. A Planejadora propõe **10 de maio de 2027**,
seis meses depois do fim de suporte do .NET 8, que foi em 10 de novembro de 2026 (Microsoft Lifecycle).
A saída acontece na primeira versão publicada a partir dessa data, e o aviso vai para o `README.md` e
para o `CHANGELOG.md` do `0.6.0`.

Opções:

1. **10 de maio de 2027.** Seis meses de prazo para quem ficou em `net8.0` migrar, e o projeto deixa de
   pagar CI e pacote de um alvo fora de suporte antes de `net12.0` existir.
2. **10 de novembro de 2027, um ano depois.** Mais folga para o público conservador, e coincide com a
   estimativa de lançamento do `net12.0` que o ADR-0023 usava. Custo: mais seis meses com o alvo fora
   de suporte na matriz.
3. **Outra data.**

Recomendação: a opção 1. O produto é uma ferramenta de desenvolvimento, versão `0.x`, e quem precisa de
`net8.0` depois da data continua com a última versão publicada que o tem. O anúncio no `README.md` só
sai depois que o dono confirmar a data.
