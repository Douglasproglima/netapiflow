# Fase 62: o site diz a verdade (D-236, varredura final, item D)

O site (`netapiflow-site`, repositório à parte) **já está público**, e a varredura final achou que ele afirma coisas que o
produto não faz e que o quick start não compila. Por isso esta fase vem **logo depois do hotfix 0.5.3** (Fase 61), antes de
qualquer outra coisa da `0.6.0`.

A Executora **prepara e para**. A auditoria audita e publica (D-198). Nenhum push no site sem a auditoria.

## O que a varredura achou

| # | Achado | Conferi |
|---|---|---|
| 1 | O quick start (PT e EN) não compila: manda instalar só o `NetApiFlow`, mas usa o `NetApiFlow.Dashboard`, e não chama `.WithNetApiFlowTracking()` | **Não verifiquei.** Reportado pela varredura |
| 2 | Princípio 01, "sem reflexão no caminho crítico": é falso para o `AddFlowTracked` e o `DecorateFlowTracked` | Não verifiquei |
| 3 | Princípio 03, "filtro por tenant": o recurso não está documentado em lugar nenhum (0 ocorrências no README) | Não verifiquei |
| 4 | Princípio 04, "20 µs": só vale sem etapas | Não verifiquei. A tabela de performance é da `0.1.0` e será medida de novo na Fase 59 |
| 5 | O link "Documentação" do site em português leva ao README em inglês | Não verifiquei |
| 6 | O repositório do site não tem licença | Não verifiquei |

## Ponto de parada

- Se a Executora não reproduzir um achado, **não mexe nele**: relata e pede o comando à auditoria.
- Se corrigir o texto exigir uma decisão de posicionamento (o que o produto promete), para e pergunta. Posicionamento é do
  dono (D-232), e a Planejadora leva a ele.
- Nenhum `push` para o repositório do site.

## Tarefa 62.0: reproduzir

Cada achado, com a saída real. O quick start: um app novo (`dotnet new webapi`), **só** com os pacotes que o texto manda
instalar, seguindo o site como um usuário, nos dois idiomas. Os princípios: para cada afirmação, o trecho de código que a
sustenta ou a desmente (para o 02: onde o `AddFlowTracked` usa reflexão; para o 04: o cenário que dá os 20 µs).

## Tarefa 62.1: o quick start

Compila e roda com o que o texto manda instalar e chamar, nos dois idiomas. **Contra os pacotes publicados (`0.5.x`)**, não
contra o `develop`: o site descreve o que o usuário instala hoje. A Fase 51 muda o quick start de novo na `0.6.0` (o
endpoint de Minimal API ganha etapa sem marcação), e isso é a Tarefa 62.4.

## Tarefa 62.2: os princípios

Reescrever o 01, o 03 e o 04 para dizerem só o que é verdade, em frases curtas (`.claude/rules/writing-style.md`), sem
inventar número. Onde falta medida (o "20 µs"), o texto diz o que foi medido e em que cenário, ou tira o número até a
Fase 59 medir de novo. O 03 aponta para onde o filtro por tenant será documentado (Fase 59), ou deixa de citar o recurso até lá.

## Tarefa 62.3: o link e a licença

O "Documentação" do site em português leva ao `README.pt-BR.md`. O repositório do site ganha a licença **MIT**, a mesma do
produto. Se o dono quiser outra para o conteúdo do site, a Planejadora leva a ele antes.

## Tarefa 62.4: depois da `0.6.0` publicada

Uma segunda passada, **junto do release**: o quick start e os números conferidos de novo contra os pacotes `0.6.0`, e o
site atualizado no mesmo dia. Entra no passo 0 da skill `/release` para o `0.6.0`.

## Fora de escopo

- Redesenho, textos novos de marketing, novas páginas.
- Qualquer coisa que dependa de decisão de posicionamento.

## Critério que vale para a fase inteira

- O quick start nos dois idiomas compila e roda num app novo, com a saída real.
- Cada afirmação reescrita tem a fonte (trecho de código ou medida).
- `grep -rn "—"` sem ocorrência nos textos em português e nos READMEs do site.

## Versionamento e fluxo

- Repositório `netapiflow-site`. Branch e commits no padrão do próprio repositório do site. A Executora abre a PR e para.
  A auditoria audita e publica.

## Relatório final

Para cada achado: o comando e a saída antes e depois, nos dois idiomas. O diff do site.
