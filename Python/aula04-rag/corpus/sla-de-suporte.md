# Acordo de Nível de Serviço do Suporte — Aurora Log

Define os prazos de primeira resposta e de solução para chamados abertos por clientes do plano corporativo.

## Classificação de severidade

- **Severidade 1 — Crítica.** Serviço indisponível para todos os usuários do cliente, ou perda de dados em andamento. Não há alternativa viável.
- **Severidade 2 — Alta.** Funcionalidade central degradada ou indisponível para parte dos usuários. Existe alternativa, mas com impacto operacional.
- **Severidade 3 — Média.** Funcionalidade secundária com defeito. A operação segue normalmente.
- **Severidade 4 — Baixa.** Dúvida, solicitação de melhoria ou ajuste cosmético.

## Prazos de primeira resposta

Contados a partir da abertura do chamado, em horas corridas para severidade 1 e em horas úteis para as demais:

- Severidade 1: 30 minutos, 24x7
- Severidade 2: 2 horas úteis
- Severidade 3: 8 horas úteis
- Severidade 4: 24 horas úteis

## Prazos de solução

Severidade 1 tem esforço contínuo até a normalização, com atualização ao cliente a cada hora. Severidade 2 tem meta de 24 horas úteis. Severidades 3 e 4 entram na fila de produto e são endereçadas por release.

## Escalonamento

Chamados de severidade 1 sem primeira resposta em 30 minutos escalam automaticamente para o gerente de plantão. O cliente pode solicitar escalonamento a qualquer momento pelo próprio chamado.

## Janela de manutenção

Manutenções programadas ocorrem aos domingos, entre 2h e 6h de Brasília, com aviso de sete dias. Indisponibilidade em janela programada não conta para o cálculo de disponibilidade mensal.
