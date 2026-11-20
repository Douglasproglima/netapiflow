# ADR-0025: manter `net8.0` e `net10.0`, testar no runtime .NET 11 e anunciar a saída do `net8.0`

## Status
Aceito, substitui o ADR-0023. A cláusula de sucessão do ADR-0003, que o ADR-0023 já tinha substituído,
passa a ser a deste ADR.

## Discussion
[#331](https://github.com/Douglasproglima/netapiflow/discussions/331)

## Contexto

O ADR-0003 fixou `net8.0;net10.0` e previu trocar `net8.0` por `net12.0` quando `net8.0` saísse de
suporte. O ADR-0023 (Fase 38) viu que essa troca não cabe no calendário: `net12.0` só deve lançar em
novembro de 2027. Decidiu três alvos, `net8.0;net10.0;net11.0`, e o D-173 deixou o terceiro alvo como
item principal do `0.6.0`.

Uma auditoria do zero, pedida pelo dono, voltou à pergunta com fatos que o ADR-0023 ainda não tinha:

- O núcleo não usa nenhuma API exclusiva do .NET 11. Não existe `#if NET11_0_OR_GREATER` em nenhum
  arquivo, e a Fase 38 mostrou que o núcleo compila e a suíte inteira passa em `net11.0` do mesmo
  jeito que em `net10.0`. O asset de `net11.0` seria idêntico ao de `net10.0`.
- Um app `net11.0` que consome o asset de `net10.0`, com storage real em SQL Server e o driver `7.1.2`,
  já foi testado com o pacote publicado e funciona (D-173). É o mecanismo padrão do NuGet: o asset mais
  próximo compatível.
- O .NET 11 lançou em 10 de novembro de 2026 como versão de suporte padrão, com suporte até 14 de
  novembro de 2028. O .NET 8 saiu de suporte no mesmo dia, 10 de novembro de 2026 (Microsoft Lifecycle,
  "Microsoft .NET and .NET Core"). O .NET 10 é o LTS corrente.

Opções:

1. **Manter o ADR-0023, três alvos.** Dá um asset próprio para `net11.0` e deixa o `#if` pronto. Custa
   uma terceira matriz de CI, `dotnet pack` e testes, para um asset que seria idêntico ao de `net10.0`.
2. **Manter `net8.0` e `net10.0`, rodar a suíte também no runtime .NET 11 contra o asset de `net10.0`, e
   anunciar a saída do `net8.0` com data.** Cobre o que importa (o pacote funciona num app `net11.0`)
   sem pagar uma terceira matriz, e fecha o furo de sucessão do ADR-0003 com uma data em vez de uma
   condição que depende de `net12.0`.
3. **Trocar `net8.0` por `net11.0` agora.** Quebra a instalação de quem ficou em LTS, o público mais
   conservador, numa troca de alvo que não é SemVer-neutra para ele. O ADR-0023 já a tinha descartado.
4. **Tirar `net8.0` já.** Ele está fora de suporte, mas ainda há quem o use, e o produto é uma
   ferramenta de desenvolvimento que essas pessoas instalam à parte da aplicação.

## Decisão

Opção 2.

- `Directory.Build.props` continua em `net8.0;net10.0`. `net11.0` só vira alvo próprio no dia em que o
  código precisar de uma API exclusiva do 11. Nesse dia entra `#if NET11_0_OR_GREATER`, no padrão que
  `net8.0` e `net10.0` já usam, e este ADR é revisto.
- O CI roda a suíte também no runtime .NET 11, contra a build de `net10.0`. Não é um terceiro alvo, é
  um teste de que o asset de `net10.0` funciona no runtime novo. A mecânica (instalar o runtime, a
  variável de roll-forward) é da fase que implementar.
- O `net8.0` sai do pacote numa **data anunciada**. A Planejadora propõe **10 de maio de 2027**, seis
  meses depois do fim de suporte: a saída acontece na primeira versão publicada a partir dessa data. O
  aviso vai para o `README.md` e para o `CHANGELOG.md` já no `0.6.0`. A data é uma proposta, e o dono a
  confirma antes de o anúncio sair (`docs/plano/decisoes-pendentes.md`).
- Quem está em `net11.0` resolve o asset de `net10.0`. Isso passa a ser dito no `README.md`.

## Consequências

- Mais fácil: a matriz de CI, o `dotnet pack` e a suíte ficam em dois alvos, sem uma terceira
  execução, e o item principal do `0.6.0` deixa de ser adicionar um alvo.
- Mais difícil: o suporte ao .NET 11 depende do asset de `net10.0`. Se uma mudança de comportamento do
  runtime 11 afetar o NetApiFlow, só o teste no runtime 11 mostra, por isso ele passa a existir.
- O D-173 é revisado pelo D-189: o terceiro alvo deixa de ser o item principal do `0.6.0`, e o item 16 do
  roadmap muda de forma.
- O risco latente do ADR-0023 continua valendo, sem mudança: a `Activity` HTTP nativa do ASP.NET Core 11
  carrega doze tags de semantic convention, `url.query` e `url.path` entre elas, e o NetApiFlow nunca a
  lê. Qualquer código novo que passe a ler essa `Activity` confere antes que não recopia essas tags sem
  sanitização. A nota de `.claude/rules/tracing.md` passa a citar este ADR.
- Não reabrir sem justificativa técnica nova: adicionar `net11.0` como alvo sem uma API exclusiva do 11
  que o justifique, e tirar o `net8.0` antes da data anunciada.
