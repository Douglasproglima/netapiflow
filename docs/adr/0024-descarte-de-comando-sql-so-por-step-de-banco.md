# ADR-0024: comando SQL do driver só é descartado quando um step de banco já o cobre

## Status
Aceito, substitui o critério de descarte do ADR-0018 (o resto do ADR-0018 continua valendo)

## Discussion
[#310](https://github.com/Douglasproglima/netapiflow/discussions/310)

## Contexto

O ADR-0018 criou uma regra para não gravar o mesmo comando SQL duas vezes: quando o
`NetApiFlow.EntityFrameworkCore` já abriu um step `Database` em volta da chamada, a `Activity`
nativa do Npgsql é descartada. A Fase 37 copiou a mesma regra para o `SqlClientCommandListener`.

A regra escrita no código ficou larga demais. Ela descarta o comando quando existe **qualquer** step
do NetApiFlow em volta (`Source.Name == "NetApiFlow"`), sem olhar o tipo dele. O ADR-0018 dizia
"tipicamente a aberta por `FlowDbCommandInterceptor`", e o "tipicamente" virou "sempre" na
implementação.

O estrago é grande. Numa action MVC o `FlowControllerActionFilter` abre um step `Controller` em toda
requisição, então `Activity.Current` é sempre uma `Activity` do NetApiFlow quando o código do usuário
roda. Todo SQL direto (Dapper, `NpgsqlCommand`, `SqlCommand`) feito dentro de um controller, de um
serviço ou de um repositório rastreado com `AddFlowTracked` some do trace. O mesmo vale dentro de um
`NetApiFlow.Step(...)` manual e dentro de um endpoint Minimal API com `WithNetApiFlowTracking()`.
Só aparecia o SQL feito fora de qualquer step, que é um caso raro em app real.

**Reprodução com pacote real** (Executora, 2026-11-19), sem EF Core, SQL direto:

- Npgsql 8.0.5, Postgres 16, `net8.0`: vira span só fora de step e fora de controller. Dentro de
  controller, dentro de `NetApiFlow.Step` ou nos dois, só aparecem os steps.
- `Microsoft.Data.SqlClient` 7.1.2, SQL Server 2022, `net8.0` e `net11.0`: mesmo resultado.

O item 8 do roadmap ("dedup checando a coisa errada") já tinha levantado essa suspeita por leitura de
código, sem reprodução. Agora tem.

**Por que os testes não pegaram.** Os testes de interop dos dois drivers montam o trace com uma
`Activity` raiz solta, que não é do NetApiFlow, e rodam o comando direto embaixo dela. Nenhum step fica
aberto em volta do comando, então a regra larga nunca disparava. É a mesma classe de lacuna do D-176:
o teste não tinha a forma do uso real.

Opções consideradas:

1. **Descartar só quando o step em volta é do tipo `Database`.** O interceptor do EF Core abre um
   step `Database`, e qualquer outro tipo (`Endpoint`, `Controller`, `UseCase`, `Service`,
   `Repository`, `Http`, `Custom`) é quem chamou o comando, não quem o representa.
2. Marcar com uma tag reservada os steps que representam um comando e descartar só diante dela. É
   mais preciso, mas exige tag nova, mudança no interceptor e no listener do SqlClient, e não evita
   nenhum caso que a opção 1 deixe passar.
3. Decidir por tempo de sobreposição ou por `CommandId`. Frágil sob concorrência, e o `CommandId` do
   EF Core não chega ao driver.
4. Tirar o descarte e deixar o usuário escolher. Reabre o R-06: o EF Core com interceptor passaria
   a gravar cada consulta duas vezes.

## Decisão

Opção 1. Um comando SQL ingerido do driver (Npgsql e `Microsoft.Data.SqlClient`) é descartado
**somente** quando o step imediatamente em volta é do NetApiFlow **e** do tipo
`ComponentType.Database`. Em qualquer outro caso o comando vira um span `Database`, filho do step que
o emitiu.

- O critério é um só, num lugar só, usado pelas duas fontes. Duas cópias da regra, uma por driver, é
  como o erro nasceu nos dois.
- Npgsql: o "step imediatamente em volta" é `activity.Parent`, na checagem de `OnActivityStarted`, e
  `Activity.Current`, na checagem barata do `Sample`. As duas camadas do ADR-0018 continuam, porque o
  motivo delas continua (outro `ActivityListener` no processo força a criação da `Activity`).
- SqlClient: o "step em volta" é `Activity.Current` no evento `CommandBefore`, como hoje.
- A leitura do tipo vem da tag reservada `netapiflow.type`, que `NetApiFlow.Step` já grava na criação
  do step. Só é lida quando a fonte é a do driver, então o `HttpClient` e os steps do próprio
  NetApiFlow não pagam nada novo.
- Só o step imediato conta, como já era. O ADR-0018 provou o `Parent` imediato sob 3800+ `Activity`.
  Procurar nos ancestrais não tem caso real que o justifique.

## Consequências

- SQL direto dentro de controller, serviço, repositório rastreado, handler ou step manual passa a
  aparecer no trace, filho do step que o chamou. O código misto de EF Core com Dapper, que foi o
  motivo da interop com o SQL Server, passa a funcionar dentro de um controller, e o detector de N+1
  (Fase 9) passa a enxergar um laço de Dapper dentro de um repositório.
- EF Core **sem** o interceptor registrado, com a fonte ligada, passa a mostrar o comando pelo driver.
  Antes ficava invisível dentro de qualquer controller.
- EF Core **com** o interceptor continua gravando uma vez só: o step `Database` do interceptor é o pai
  imediato do comando do driver. Esta é a propriedade que o relaxamento da regra põe em risco, então
  é a que a Fase 48 prova com host real, driver real e banco real, nos dois drivers (R-29).
- Um step manual do tipo `Database` em volta de SQL direto engole os comandos do driver que ficam
  dentro dele: o step já representa a chamada, e o NetApiFlow não tenta adivinhar o contrário. Isso
  vai para a documentação pública.
- Quem tinha a fonte ligada e usava Npgsql direto dentro de controller passa a ver spans novos depois
  de atualizar. É a correção, e o `CHANGELOG.md` registra o caso do Npgsql em `Fixed`. O caso do
  SqlClient nasce no `0.5.0`, então a entrada de `Added` dele só passa a ser verdadeira.
- A superfície pública não muda. Só o texto de documentação de `ExternalActivitySource.Npgsql` e
  `ExternalActivitySource.SqlClient` (`contratos.md`).
- Não reabrir sem justificativa técnica nova: descartar por `Parent` imediato, a checagem em duas
  camadas do Npgsql e a curadoria de tags do ADR-0018.
