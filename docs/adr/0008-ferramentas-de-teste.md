# ADR-0008: Shouldly para asserções, NSubstitute para mocks

## Status
Aceito

## Issue
[#67](https://github.com/Douglasproglima/netapiflow/issues/67)

## Contexto
`netapiflow-prompt.md` (seção 4.3) deixa em aberto a escolha entre FluentAssertions/Shouldly e
NSubstitute/Moq, pedindo decisão justificada da IA Planejadora.

FluentAssertions passou a exigir licença paga (Xceed) para uso comercial a partir da v8;
NetApiFlow é MIT e pode ser consumido por projeto comercial, então uma dependência de teste com
essa cláusula é um risco de licenciamento para quem fizer fork ou build próprio. Moq teve, na
v4.20, um pacote (`SponsorLink`) que coletava telemetria sem aviso claro, episódio que motivou
parte do ecossistema open source a migrar para alternativas.

## Decisão
**Shouldly** (BSD-3-Clause) para asserções fluentes. **NSubstitute** (BSD-3-Clause) para mocks.
Nenhum dos dois tem cláusula comercial ou telemetria.

## Consequências
- Toda dependência de teste do projeto é permissiva e sem pegadinha de licença, consistente com
  o próprio NetApiFlow sendo MIT.
- Sintaxe de mock difere do Moq (`Substitute.For<T>()` em vez de `new Mock<T>()`), documentado em
  `.claude/rules/testing.md` com exemplos, para a IA Executora não misturar os dois estilos.
- `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) continua sendo usado para teste de
  integração, sem relação com esta decisão.
