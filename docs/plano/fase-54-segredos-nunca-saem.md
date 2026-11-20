# Fase 54: os segredos nunca saem, nem pelo OTLP (D-210, S1 e S2)

O README promete que o NetApiFlow "never captures, under any configuration" o corpo, os argumentos, os
headers, a query string e o valor de parâmetro de SQL, e que a mensagem de erro é truncada em 300 caracteres e
sanitizada (`.claude/rules/tracing.md`, ADR-0007). A auditoria de base achou dois furos nessa promessa. Esta fase
fecha os dois, nos dois sentidos: o dado não sai, e o teste que o prova passa por cada saída.

Vai no `0.6.0` (D-210, D-212). É defeito antigo e não regressão, então não é hotfix (D-205).

## Os achados e o que a Planejadora conferiu

| # | Achado (auditoria de base) | Conferi |
|---|---|---|
| S1 | O OTLP exporta a mensagem de erro crua, sem sanitizar e sem os 300 caracteres. O painel mostra `password=[redacted]`, e o OTLP recebeu `password=PW_SECRET` com 745 caracteres | **Código.** `FlowStep.MarkError` (`FlowStep.cs:44`) grava `exception.Message` cru na tag `netapiflow.error.message` da atividade. É o **único** ponto que grava essa tag. Quem lê a tag, o `FlowActivityListener` (linha 204), sanitiza, e é o que alimenta o painel. Um exportador que lê a atividade direto não passa por ali. A medição no OTLP foi do revisor independente C, e a auditoria a reproduz agora (correção dela: ela só tinha confirmado a causa no código). Eu não a reproduzi |
| S2 | Brechas no sanitizador: `Authorization: Bearer X` apaga só `Bearer` e deixa `X`; `access_token=`, `client_secret=` e `DB_PASSWORD=` passam, porque `\b` não casa depois de `_`; `api_key` não está na lista; o corte em 300 vem **antes** da limpeza | **Código.** Li a regex `\b(password\|token\|authorization\|api-key\|secret)\s*[:=]\s*\S+` e a ordem em `ErrorMessageSanitizer.Sanitize` (corta, depois limpa). Cada brecha decorre do texto da regex. Não rodei |

**O mapa de por onde a mensagem de erro entra em cada saída**, que a Planejadora fez lendo o código. Todo o resto
já sanitiza antes de entrar no modelo de domínio, e é o modelo de domínio que alimenta o painel (REST e SSE), o
MCP e os dois storages:

| Ponto | Sanitiza? |
|---|---|
| `FlowRecorder` (`CompleteTrace`, linha 122) | Sim |
| `FlowActivityListener` (linhas 204, 279 e 305) | Sim |
| `FlowLogger` (linhas 36 e 38) | Sim |
| **`FlowStep.MarkError` (linha 44), a tag da atividade** | **Não, e é o que o OTLP exporta** |

Por isso o S1 tem um ponto de correção só, e o S2 vale para todas as saídas, incluindo o OTLP depois do S1.

## Ponto de parada

Se a Executora não conseguir reproduzir o S1 pelo OTLP real ou o S2 pelas entradas da auditoria, **para e
relata**, em vez de corrigir o que supôs. Idem se descobrir outro ponto que grave texto de exceção cru numa
tag ou num campo de saída além do `FlowStep.MarkError`: é achado novo, e o mapa acima deixa de estar
completo.

## Tarefa 54.0: reproduzir antes de corrigir

Dois testes de **host real**, vistos falhando antes de qualquer mudança (`.claude/rules/testing.md`):

- **S1.** Um host com o `NetApiFlow.OpenTelemetry` configurado e um **exportador real** (o de memória do SDK,
  `OpenTelemetry.Exporter.InMemory`, que passa pelo mesmo pipeline), um endpoint marcado que lança uma exceção com `password=PW_SECRET` e mais de 300 caracteres. O teste
  lê o que o exportador recebeu e exige que `PW_SECRET` não apareça e que a mensagem não passe de 300
  caracteres. Hoje falha. Não vale um `ActivityListener` próprio no lugar do exportador: é o pipeline do
  pacote que a promessa cobre.
- **S2.** Cada entrada da tabela da Tarefa 54.1 contra o sanitizador, pelo caminho que o painel usa. Hoje falham
  as quatro famílias que a auditoria listou.

A primeira execução parte dos comandos e das entradas que a auditoria mediu (a Planejadora os cola aqui
quando chegarem), e o relatório diz o que foi reproduzido e o que não.

## Tarefa 54.1: uma função só, na ordem certa

Uma função interna sanitiza **e** trunca, **na ordem sanitizar e depois truncar**, e é a que toda saída usa. O
corte antes da limpeza pode deixar um pedaço que a regra já não reconhece (um número longo, um CPF ou um CNPJ
cortados no meio). Para o custo não crescer com uma mensagem enorme, a entrada pode ser limitada a um teto
generoso antes de sanitizar, e o teto exato é escolha da Executora, com teste.

**O que a função tem de pegar**, cada caso com teste:

| Entrada | Esperado |
|---|---|
| `Authorization: Bearer abc.def.ghi` | O esquema **e** o token somem, não só `Bearer` |
| `access_token=XYZ`, `client_secret=S`, `DB_PASSWORD=P` | Somem. O casamento não depende de `\b` junto de `_` |
| `api_key=K`, `api-key=K`, `apikey=K` | Somem |
| `password=PW`, `token=T`, `secret=S`, `authorization=A` | Continuam sumindo, em qualquer caixa |
| Um valor entre aspas ou com espaço (`password="a b"`) | Some inteiro, ou o teste diz o que sobra e por quê |
| JSON com a chave entre aspas: `"password":"x"` e `"access_token": "x"` | O valor some. Uma exceção de desserialização costuma trazer esse formato |
| Par de connection string: `Password=x;` e `Pwd=x;` | O valor some até o `;`. Uma exceção de banco costuma trazer esse formato |
| `Bearer x` **sem** a palavra `Authorization` antes | O token some |
| Uma mensagem de 297 caracteres seguida de um segredo | Nada do segredo na saída |
| Uma mensagem já sanitizada | Sanitizar de novo não muda nada (idempotência) |

**Restrições que a Planejadora leu e a Executora confirma:** todo padrão continua com
`RegexOptions.NonBacktracking`, que roda em tempo linear e é a garantia de que nenhum padrão sofre
retrocesso catastrófico, e esse modo **não aceita** `lookbehind`, `lookahead`, referência de volta nem grupo
atômico. O padrão novo tem de ser escrito sem eles. A lista de chaves cresce, mas o teste de desempenho
(mensagem longa) continua passando.

## Tarefa 54.2: o `FlowStep.MarkError` grava a mensagem já sanitizada (S1)

O `FlowStep.MarkError` passa a gravar na tag a mensagem **sanitizada e truncada**, pela função da 54.1. Os
pontos de leitura que já sanitizam (o `FlowActivityListener`) continuam chamando a mesma função, o que é
inofensivo porque a função é idempotente (54.1), e nenhuma cópia da regra nasce.

**Por que sanitizar no `MarkError`, antes de gravar a tag (decisão da auditoria):** todo consumidor da
`Activity`, o do NetApiFlow e o do usuário, recebe a versão limpa, e **nenhum listener precisa lembrar de
sanitizar**. Por isso o teste da 54.3 prova o que sai da própria `Activity`, não só o que o painel mostra.

Antes de fechar, a Executora confere no `NetApiFlow.OpenTelemetry` e em qualquer outro leitor da atividade se
**alguma outra tag** do NetApiFlow leva texto de exceção ou dado do usuário sem passar pela função. As tags que o
usuário grava com `FlowStep.Tag(...)` ficam como estão: o README já as declara como a exceção, guardadas e servidas
sem sanitização.

## Tarefa 54.3: o teste por cada saída

Um teste de host real, com a mesma exceção contendo `password=PW_SECRET`, `access_token=XYZ`,
`client_secret=S`, `DB_PASSWORD=P`, `api_key=K` e `Authorization: Bearer abc.def`, que lê **cada** saída e exige que
nenhum dos valores apareça e que a mensagem não passe de 300 caracteres:

| Saída | Como o teste lê |
|---|---|
| Painel, REST | O JSON de `GET /flow/api/traces/{id}` |
| Painel, SSE | O evento do `/flow/api/stream` |
| MCP | A ferramenta `get_trace` |
| Correlação de logs | O log ligado à etapa |
| Storage | Salvar e ler de volta pelo `ITraceStore` (a persistência grava o modelo de domínio) |
| **A própria `Activity`** | Um `ActivityListener` lê a tag `netapiflow.error.message`, que já tem de vir limpa |
| **OTLP** | O exportador em memória do SDK (`OpenTelemetry.Exporter.InMemory`), que passa pelo mesmo pipeline do SDK (a Tarefa 54.0). A auditoria reproduz com um coletor real à parte |

Visto falhando antes onde falha hoje (o OTLP), e passando depois.

## Tarefa 54.4: README e CHANGELOG

- `README.md` e `README.pt-BR.md`, seção "Sensitive data": a promessa passa a ser verdadeira para o OTLP também,
  e o texto diz o que é sanitizado (a lista de chaves, o esquema do `Authorization`), o teto de 300 caracteres,
  e que as tags do usuário são a exceção. A frase do nível 2 sobre o corpo voltar pela mensagem de exceção
  (`int.Parse` de um campo do corpo) é da Fase 59, não daqui.
- `CHANGELOG.md`, `[Unreleased]`, `### Security`, em inglês: a mensagem de erro agora é sanitizada e truncada antes
  de chegar ao OpenTelemetry, e o sanitizador cobre `access_token`, `client_secret`, `DB_PASSWORD`, `api_key` e
  `Authorization: Bearer ...`.

**Dependência de teste nova.** O `OpenTelemetry.Exporter.InMemory` só entra em projeto de teste. A Executora
confere a licença (Apache 2.0, compatível), a manutenção e o `dotnet list package --vulnerable`, e registra o
pacote no resumo com o motivo (`.claude/rules/dependencies.md`). Ele não entra no `.nuspec`.

## Pergunta em aberto, para a auditoria e o dono

O S1 faz uma promessa pública ("never captures") deixar de valer para quem exporta pelo OTLP. Isto também merece
um **GitHub Security Advisory**, como o do D-203, ou só a entrada de `### Security`? É decisão de comunicação
pública, do dono, e a Planejadora não a toma. A recomendação, para ele decidir: o advisory só se existir um
caminho real em que um segredo chegue à mensagem de uma exceção do usuário, o que o próprio teste da 54.0 mostra.

## Fora de escopo

- As tags do usuário (`FlowStep.Tag`), que o README declara como exceção.
- O `exception.message` das atividades de fontes externas (Npgsql, HttpClient), que o SDK do próprio host
  exporta por conta dele. O NetApiFlow só as lê e sanitiza o que grava no modelo de domínio.
- A frase do nível 2 do README sobre o corpo voltar pela exceção: Fase 59.

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs, e o `AotCoreVerification` continua com zero aviso.
- Cada achado com código tem teste de host real **visto falhando antes**.
- O teste da 54.3 cobre cada saída da tabela, e a auditoria reconfere a frase do README no fim.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/54-segredos-nunca-saem` a partir de `develop` atualizado. Uma PR, ou duas se a
  Executora preferir separar o S1 do S2.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. O que foi reproduzido e o que não
(S1 no OTLP real, as entradas do S2). A conferência de outras tags do NetApiFlow que levem texto de exceção. A
saída do `AotCoreVerification`.
