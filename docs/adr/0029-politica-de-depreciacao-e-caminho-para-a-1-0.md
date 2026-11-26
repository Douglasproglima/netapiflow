# ADR-0029: política de depreciação, um ciclo publicado antes de remover, e o caminho para a 1.0

## Status
Proposto

## Discussion
A criar quando o ADR for aceito, na mesma PR que o aceita.

## Contexto

Antes da `1.0`, o SemVer deixa quebrar a API pública em qualquer versão. O NetApiFlow já usou essa folga, e quem
atualizou sentiu: o `ITraceStore` mudou de assinatura numa minor, na `0.5.0`, e a auditoria do dono pelo caminho do
usuário (D-185) apontou o caso como o motivo de o projeto precisar de uma política de depreciação. No mesmo registro
ela notou o ritmo: 13 versões em 30 dias e 4 hotfixes em 2 dias.

A `0.6.0` abriu o primeiro precedente do outro jeito. A Fase 67 trocou três nomes públicos e deixou os antigos com
`[Obsolete]`, encaminhando para os novos: a classe `NetApiFlow` (hoje `Flow`), `UseNetApiFlowDashboard` e
`UseNetApiFlowMcp`. Foi uma decisão da fase (D-237 e D-277), e não uma regra. A próxima versão que mexer na API
(o grupo B do `versao-0.7.0.md`) vai trocar muito mais coisa de uma vez, e precisa saber como fazer.

O item 22 do roadmap ("Caminho para a 1.0") listava três pontos: congelar a API pública, a política de depreciação e a
política de cadência. O dono respondeu em 26/11/2026, pelo relato da auditoria, que a Planejadora escreve o ADR agora,
para a versão da API já nascer com ele.

Opções para a depreciação:

| Opção | Resultado |
|---|---|
| **A. Um ciclo publicado: a minor N marca `[Obsolete]` e encaminha, a minor N+1 pode remover** | **Escolhida** |
| B. Quebrar livre antes da `1.0`, como o SemVer permite | Descartada: foi o que aconteceu com o `ITraceStore`, e o custo recai em quem instalou o pacote |
| C. Nunca remover, só marcar `[Obsolete]` | Descartada: a superfície pública só cresce, e o `PublicAPI.Shipped.txt` carrega nomes que ninguém devia usar |
| D. Uma janela de tempo (por exemplo, seis meses) | Descartada: a cadência é irregular, e uma janela de tempo não se confere no `pack`, enquanto "uma minor publicada" se confere |

## Evidência

**Código e processo** (`develop`, conferido pela Planejadora em 26/11/2026):

- `src/Directory.Build.props` liga `EnablePackageValidation` e lê a linha de base de
  `tests/real-package/previous-published-version.txt` (hoje `0.6.0`). Remover ou mudar um membro público derruba o
  `pack` contra a última versão publicada, e o `Microsoft.CodeAnalysis.PublicApiAnalyzers` exige a linha no
  `PublicAPI.Shipped.txt` ou no `PublicAPI.Unshipped.txt`.
- Existem três `[Obsolete]` no código, os da Fase 67: `src/NetApiFlow/NetApiFlow.cs`,
  `NetApiFlowDashboardEndpointRouteBuilderExtensions.cs` e `NetApiFlowMcpEndpointRouteBuilderExtensions.cs`. As mensagens
  dizem só "Use ...", sem a versão em que o membro sai.
- A âncora de tempo do `ITraceStore` (Fase 57) entrou como membro com implementação padrão (D-233), e os três
  `ITraceStore` de teste do repositório provaram por que (D-246).
- O CHANGELOG já separa `### Changed`, `### Fixed` e `### Security`, e o `comparison` do pacote real exige que toda
  diferença de comportamento contra a versão publicada esteja em `expected-differences.json` e no CHANGELOG (D-310).

**Não verificado pela Planejadora:** quantas pessoas usam o pacote hoje. A decisão não depende disso, mas o custo de
quebrar cresce com esse número.

## Decisão

Opção A.

- **O que conta como API pública:** a superfície dos `PublicAPI.Shipped.txt` e `PublicAPI.Unshipped.txt`, e os contratos
  de borda de `docs/plano/contratos.md`: rotas REST, eventos SSE, nomes das tools do MCP, atributos exportados no OTLP,
  o `schema.sql` das stores e as chaves de configuração `NetApiFlow:`. Uma linha nova ou removida no `PublicAPI` já conta
  como mudança, e quem decide se uma mudança conta é a auditoria (D-318).
- **O ciclo, antes da `1.0`:** remover um membro, ou mudar o significado dele, leva duas minors. Na minor N o membro
  antigo ganha `[Obsolete]`, com a mensagem dizendo o substituto e a primeira versão em que sai, e **encaminha** para o
  novo com o mesmo comportamento. O CHANGELOG da N anota em `### Deprecated`. A minor N+1 pode remover, e anota em
  `### Removed`.
- **Contrato de borda:** o mesmo ciclo quando o custo é razoável, emitindo os dois nomes por uma minor (um atributo, uma
  chave). Quando não dá para emitir os dois, a mudança vai em `### Changed`, com a linha em `expected-differences.json` e
  a justificativa na PR.
- **Interface que terceiros implementam** (`ITraceStore`, `IFlowAccessFilter` e as parecidas): só ganha membro com
  implementação padrão. Mudar a assinatura de um membro existente é remover e acrescentar, e segue o ciclo, com o
  membro novo ao lado do antigo.
- **Sem ciclo:** correção de segurança, que sai com advisory, e membro que nunca funcionou. Cada caso é registrado em
  `decisoes.md` e na PR, e a auditoria concorda antes do merge.
- **Depois da `1.0`:** SemVer estrito. Remoção só numa versão major, e o `[Obsolete]` vem numa minor antes.
- **Cadência:** sem calendário. Uma versão sai quando os grupos combinados fecham e a auditoria fecha o D-318. Correção
  que não é urgente espera a minor, e hotfix segue o D-205 (regressão nova ou falha de severidade alta). Não é regra
  nova, só fica escrita junto da depreciação.
- **O caminho para a `1.0`:** a `0.7.0` sem quebra de API, depois a versão da API com a migração toda de uma vez e o
  ciclo acima, e então o congelamento: o `pack` já falha para quebra sem ciclo, e a partir daí nenhuma linha sai do
  `PublicAPI.Shipped.txt` sem o ciclo cumprido. **O critério que fecha a `1.0` é do dono.** A proposta da Planejadora:
  pelo menos uma minor depois da versão da API sem nenhuma linha removida do `PublicAPI.Shipped.txt`, e o inventário
  completo do D-318.

## Consequências

- Quem atualiza de uma minor para a seguinte nunca perde um nome de uma vez: vê um aviso do compilador na minor N, e o
  aviso diz para onde ir.
- A API pública cresce por uma minor depois de cada troca, e o `PublicAPI.Shipped.txt` fica com as duas formas nesse
  tempo.
- Os três `[Obsolete]` da `0.6.0` cumpriram a minor N. Podem sair a partir da versão da API, e as mensagens ganham a
  versão em que saem. A `0.7.0` não remove nada.
- Um membro novo em interface pública sem implementação padrão passa a ser defeito de revisão.
- Não reabrir sem justificativa técnica nova: o ciclo de uma minor, e a regra de que o critério da `1.0` é do dono.
