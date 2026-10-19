# ADR-0006: Dashboard acessível só em Development por padrão

## Status
Aceito

## Contexto
O dashboard expõe a estrutura interna da aplicação do usuário: nomes de componente, operação,
duração e origem de erro. `netapiflow-prompt.md` (seção 6.10) exige que isso não vaze por
padrão em produção.

## Decisão
O middleware do dashboard responde 404 sempre que `IHostEnvironment.IsDevelopment()` é falso e
nenhuma autorização foi configurada. Para habilitar fora de Development, o usuário precisa de um
passo explícito: `o.RequireAuthorization("PolicyName")` ou um `IDashboardAuthorizationFilter`
próprio (ver risco R-09 em `docs/plano/riscos.md`).

## Consequências
- Comportamento seguro por padrão: ninguém expõe o dashboard em produção por esquecimento.
- README precisa deixar isso em destaque (seção 11, item 9, do prompt), porque é o tipo de
  configuração que o usuário só nota quando falha.
- `DELETE /flow/api/traces` segue a mesma regra: só em Development ou com autorização.
