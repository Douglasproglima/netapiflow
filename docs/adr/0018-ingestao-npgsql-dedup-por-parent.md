# ADR-0018: Ingestão nativa da `ActivitySource` do Npgsql, dedup por cadeia de `Parent`

## Status
Aceito

## Issue
[#140](https://github.com/Douglasproglima/netapiflow/issues/140)

## Contexto
R-06 ficou aberto desde o bootstrap do projeto (Fase 2), carregando duas suposições nunca
confirmadas. A Fase 19 investigou as duas com um spike empírico (sem código de produção,
`tests/NetApiFlow.NpgsqlInteropSpike`, descartável) antes de qualquer decisão de arquitetura.

**Suposição 1, refutada:** achava-se que a `ActivitySource` nativa `"Npgsql"` se comportava como
a do `HttpClient` (ADR-0012), precisando de um pacote extra (`Npgsql.OpenTelemetry`) e
configuração explícita (`NpgsqlDataSourceBuilder.ConfigureTracing`) para emitir qualquer coisa.
O spike confirmou o contrário: Npgsql 9.0.3 emite `Activity` na fonte `"Npgsql"` sozinho, sem
nenhum pacote referenciado além do próprio driver, bastando um `ActivityListener`. Comportamento
idêntico ao `HttpClient`: sempre presente, só precisa de observador.

Essa correção muda o peso do risco, não resolve ele: como a fonte emite sozinha, ligar
`ExternalActivitySource.Npgsql` passaria a capturar span de comando SQL **sem nenhuma
configuração adicional do usuário**, o que torna a duplicação com
`NetApiFlow.EntityFrameworkCore` (que já produz um span `Database` via `FlowDbCommandInterceptor`
para a mesma chamada) o **caso comum**, não o raro, sempre que os dois estiverem ativos ao mesmo
tempo. A mitigação de dedup deixa de ser um detalhe, vira o próprio motivo de existir desta ADR.

**Suposição 2, confirmada com prova real:** `Activity.Id` não serve para dedup (são duas
`Activity` distintas, de fontes diferentes). A hipótese de dedup por cadeia de `Parent` (a
`Activity` nativa do Npgsql nasce com `Parent = Activity.Current` no momento em que o driver a
inicia; se um `NetApiFlow.Step` do `FlowDbCommandInterceptor` já estiver aberto em volta daquela
chamada, ele é o `Parent`) foi testada sob as condições mais adversas que a comunidade já relatou
como causa de quebra (pool de conexão padrão, multiplexing, `Task.WhenAll` concorrente), em
`net8.0` e `net10.0`, repetida em múltiplos processos: **3800+ `Activity` observadas, zero
divergência de `Parent`**. A hipótese se sustenta.

**Achado adicional, não previsto:** a tag `db.statement` nunca vaza valor de parâmetro quando o
EF Core de fato parametriza a consulta (variável local, `NpgsqlParameter`). Mas um valor
capturado de `const`/`static readonly` é resolvido pelo EF Core como **literal embutido na
própria consulta**, antes mesmo de chegar no driver; nesse caso o valor aparece no SQL de
verdade, não por falha de sanitização do Npgsql ou do NetApiFlow. É o mesmo risco que já existe
para `FlowDbCommandInterceptor.CaptureSqlText` (captura de SQL completo já é opt-in, por projeto,
desde a Fase 1), só que agora confirmado por prova real, não suposição. `.claude/rules/tracing.md`
ganha uma nota curta sobre essa nuance, aplicável aos dois mecanismos.

**Achado adicional sobre o tag set:** a `Activity` nativa do Npgsql carrega bem mais do que
`db.statement`: `db.connection_string` (sem senha, o próprio Npgsql já tira), `db.user`,
`db.name`, `net.peer.ip`, `net.peer.port`, entre outras. Nenhuma dessas tags é algo que
`FlowDbCommandInterceptor` já grava hoje (ele só grava tipo de comando e tabela principal, seção
`.claude/rules/tracing.md`). Copiar o tag set inteiro seria capturar mais do que a própria
instrumentação do NetApiFlow já se permite.

## Decisão
`ExternalActivitySource` ganha `Npgsql = 2` (`[Flags]`, ao lado de `HttpClient = 1`). Opt-in,
`None` continua padrão.

`FlowActivityListener.ShouldListenTo` passa a observar também a fonte `"Npgsql"` quando o flag
está ligado, mesmo padrão já usado para `"System.Net.Http"` (ADR-0012).

**Dedup acontece em `Sample`, não em `ActivityStarted`** (mesmo princípio de custo de R-05):
quando a fonte da `Activity` sendo criada é `"Npgsql"` e `Activity.Current?.Source.Name ==
"NetApiFlow"` (ou seja, já existe um `NetApiFlow.Step` aberto em volta, tipicamente o do
`FlowDbCommandInterceptor`), o sampler devolve `ActivitySamplingResult.None`: nenhum `FlowSpan` é
criado para aquela `Activity` nativa, o span do `FlowDbCommandInterceptor` já representa a
chamada sozinho. Sem esse `Parent` (uso de Npgsql puro, ou Dapper sobre Npgsql, sem
`NetApiFlow.EntityFrameworkCore`), a `Activity` nativa é a única representação da chamada e é
ingerida normalmente.

Curadoria de tag, não cópia: o `FlowSpan` resultante da ingestão nativa nunca copia o tag set
inteiro. `Component = "Npgsql"`, `Type = ComponentType.Database`, `Operation` derivado de
`db.statement` pelo mesmo algoritmo de `FlowDbCommandInterceptor.DescribeCommand`
("{CommandType} {MainTable}" via regex, nunca o texto inteiro), duplicado como um método interno
equivalente no núcleo (núcleo não pode depender do pacote `NetApiFlow.EntityFrameworkCore`,
duplicar ~15 linhas de regex é mais simples que inventar um pacote compartilhado só para isso).
Texto completo de `db.statement` só vira `FlowSpan.Tags["sql"]` quando
`NetApiFlowOptions.CaptureSqlText` está ligado, mesma régua já usada pelo
`FlowDbCommandInterceptor`. Nenhuma outra tag nativa (`db.connection_string`, `db.user`,
`net.peer.ip`, etc.) é copiada para `FlowSpan`, em nenhuma circunstância.

Status de erro vem de `Activity.Status`/`ActivityStatusCode` (API nativa do .NET, não parsing de
tag), mesmo mecanismo que `FlowStep.MarkError` já usa via `SetStatus(ActivityStatusCode.Error)`.

## Consequências
- Risco R-06 passa de "nunca investigado" para "mitigado, com prova real de 3800+ `Activity` sob
  condição adversa". Fica a cargo da Fase 19 (tarefas 19.2 em diante) a implementação de
  produção, incluindo confirmar o comportamento de `Activity.Status` numa consulta que falha de
  verdade (não testado pelo spike).
- Nenhuma dependência nova em `NetApiFlow` (núcleo): a fonte `"Npgsql"` é observada sem referenciar
  nenhum pacote do Npgsql; se a aplicação hospedeira não usa Npgsql, a fonte nunca existe e nada
  acontece.
- MassTransit continua fora de escopo (ADR-0012), falta de pesquisa própria, sem relação com esta
  ADR.
- `.claude/rules/tracing.md` ganha uma nota sobre a nuance de `const`/literal embutido, aplicável
  tanto ao `FlowDbCommandInterceptor` quanto a esta ingestão nova.
