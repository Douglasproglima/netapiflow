# Fase 54: os segredos nunca saem, nem pelo OTLP (D-210, S1 e S2)

O README promete que o NetApiFlow "never captures, under any configuration" o corpo, os argumentos, os
headers, a query string e o valor de parâmetro de SQL, e que a mensagem de erro é truncada em 300 caracteres e
sanitizada (`.claude/rules/tracing.md`, ADR-0007). A auditoria de base achou dois furos nessa promessa. Esta fase
fecha os dois, nos dois sentidos: o dado não sai, e o teste que o prova passa por cada saída.

Vai no `0.6.0` (D-210, D-212). É defeito antigo e não regressão, então não é hotfix (D-205).

## Os achados e o que a Planejadora conferiu

| # | Achado (auditoria de base) | Conferi |
|---|---|---|
| S1 | O OTLP exporta a mensagem de erro crua, sem sanitizar e sem os 300 caracteres. O painel mostra `password=[redacted]`, e o OTLP recebeu `password=PW_SECRET` com 745 caracteres | **Código.** `FlowStep.MarkError` (`FlowStep.cs:44`) grava `exception.Message` cru na tag `netapiflow.error.message` da atividade. É o **único** ponto que grava essa tag. Quem lê a tag, o `FlowActivityListener` (linha 204), sanitiza, e é o que alimenta o painel. Um exportador que lê a atividade direto não passa por ali. **Reproduzido pela auditoria** com um coletor real nos 13 casos abaixo, contra o pacote publicado `0.5.2`: o OTLP vaza em todos. A medição original foi do revisor independente C. Eu não a reproduzi de novo |
| S2 | Brechas no sanitizador: `Authorization: Bearer X` apaga só `Bearer` e deixa `X`; `access_token=`, `client_secret=` e `DB_PASSWORD=` passam, porque `\b` não casa depois de `_`; `api_key` não está na lista; o corte em 300 vem **antes** da limpeza | **Código.** Li a regex `\b(password\|token\|authorization\|api-key\|secret)\s*[:=]\s*\S+` e a ordem em `ErrorMessageSanitizer.Sanitize` (corta, depois limpa). Cada brecha decorre do texto da regex. **Reproduzido pela auditoria** nos 13 casos abaixo: o **painel** vaza em 10 das 12 formas de segredo, e só pega `password=` simples e `Password=` de connection string. Eu não reproduzi de novo |

**O mapa de por onde a mensagem de erro entra em cada saída**, que a Planejadora fez lendo o código. Todo o resto
já sanitiza antes de entrar no modelo de domínio, e é o modelo de domínio que alimenta o painel (REST e SSE), o
MCP e os dois storages:

| Ponto | Sanitiza? |
|---|---|
| `FlowRecorder` (`CompleteTrace`, linha 122) | Sim |
| `FlowActivityListener` (linhas 204, 279 e 305) | Sim |
| `FlowLogger` (linhas 36 e 38) | Sim |
| **`FlowStep.MarkError` (linha 44), a tag da atividade** | **Não, e é o que o OTLP exporta** |

Por isso o S1 tem um ponto de correção só, e o S2 vale para todas as saídas, incluindo o OTLP depois do S1. E
o S2 é maior do que parecia: como o REST, o SSE, o MCP, o log e o storage usam o mesmo sanitizador, **todos**
vazam hoje as formas que a regex não pega, não só o OTLP.

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
- **S2.** Os **13 casos literais** da auditoria (a tabela acima), mais cada linha da tabela da Tarefa 54.1,
  contra o sanitizador, pelo caminho que o painel usa. O vermelho de antes tem de ser **comparável** com o da
  auditoria: a mesma entrada, e o resultado de hoje (painel e OTLP) é o da tabela. Hoje falham 10 das 12 formas
  no painel e os 13 casos no OTLP.

A primeira execução parte dos comandos e das entradas que a auditoria mediu (a Planejadora os cola aqui
quando chegarem), e o relatório diz o que foi reproduzido e o que não.

## Tarefa 54.1: uma função só, na ordem certa

Uma função interna sanitiza **e** trunca, **na ordem sanitizar e depois truncar**, e é a que toda saída usa. O
corte antes da limpeza pode deixar um pedaço que a regra já não reconhece (um número longo, um CPF ou um CNPJ
cortados no meio). Para o custo não crescer com uma mensagem enorme, a entrada pode ser limitada a um teto
generoso antes de sanitizar, e o teto exato é escolha da Executora, com teste.

## Os 13 casos que a auditoria mediu, o ponto de partida da tabela de teste

Pacotes publicados `0.5.2`, `net10.0`, Development. Cada caso é uma exceção lançada por um passo
(`step.MarkError(new InvalidOperationException(...))`), lida no painel (`errorMessage` de
`GET /flow/api/traces/{id}`) e no atributo `netapiflow.error.message` do span que um coletor OpenTelemetry
real recebeu. **Cada um tem de sair limpo em toda saída.**

| # | Entrada | Painel hoje | OTLP hoje |
|---|---|---|---|
| 0 | `password=PW_SECRET joe@example.com ` mais 700 `x` | Limpo, mas com 302 caracteres | **Vaza**, 735 caracteres, sem truncar |
| 1 | `Authorization: Bearer TOKEN_A` | **Vaza** (`Authorization=[redacted] TOKEN_A`) | **Vaza** |
| 2 | `access_token=TOKEN_B` | **Vaza** | **Vaza** |
| 3 | `client_secret=TOKEN_C` | **Vaza** | **Vaza** |
| 4 | `DB_PASSWORD=TOKEN_D` | **Vaza** | **Vaza** |
| 5 | `api_key=TOKEN_E` | **Vaza** | **Vaza** |
| 6 | `apikey=TOKEN_F` | **Vaza** | **Vaza** |
| 7 | `{"password":"TOKEN_G"}` | **Vaza** | **Vaza** |
| 8 | `Server=db;Password=TOKEN_H;` | Limpo | **Vaza** |
| 9 | `Server=db;Pwd=TOKEN_I;` | **Vaza** | **Vaza** |
| 10 | `Bearer TOKEN_J` | **Vaza** | **Vaza** |
| 11 | `xpassword=TOKEN_K` | **Vaza** | **Vaza** |
| 12 | 296 `A` seguidos de ` password=TOKEN_L` | Limpo, porque o corte cortou o segredo | **Vaza**, 313 caracteres |

O OTLP vaza nos 13 casos. O painel vaza em 10 das 12 formas de segredo. O caso 0 mostra um terceiro achado: como o
corte vem antes da limpeza, a mensagem limpa **passa do limite** (302 caracteres), então o teste confere o
tamanho **depois** de sanitizar.

O caso 11 (`xpassword=`) decide o desenho do padrão (decisão da auditoria): a regra é "a chave **contém** uma
das palavras", e não "a chave é igual a uma palavra". As palavras são `password`, `passwd`, `pwd`, `token`,
`secret`, `api-key` (também com `_` e sem separador) e `authorization`, em qualquer caixa. Falso positivo (por
exemplo, `tokenizer=...`) é preferível a vazar, e a Executora lista no relatório os que o teste achou.

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
| O tamanho depois de sanitizar | Nunca passa de 300 caracteres. Hoje o caso 0 sai com 302 no painel |
| Chave que **termina** com (ou contém) uma palavra sensível, em qualquer caixa: `DB_PASSWORD`, `xpassword`, `userPassword`, `X-Api-Key`, `refresh_token` | O valor some. A regra é "a chave contém uma das palavras", e a lista inclui `passwd` e `pwd` |
| `Authorization: Basic dXNlcjpwYXNz`, e um esquema desconhecido (`Authorization: Foo abc`) | O valor inteiro some, não só o nome do esquema |

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

Um teste de host real que lança, para **cada um dos 13 casos literais** (e para as linhas da tabela da 54.1), a
exceção com aquela entrada, lê **cada** saída e exige que nenhum valor secreto apareça e que a mensagem não passe
de 300 caracteres **depois** de sanitizar (o caso 0 dá 302 hoje):

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
