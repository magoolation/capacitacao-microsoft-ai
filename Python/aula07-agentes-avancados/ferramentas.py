"""
As quatro tools do agente da aula 7.

Uma tool é uma função Python comum. O que a transforma em "ferramenta" é o
Agent Framework, que gera o schema a partir da assinatura: o nome da função
vira o nome da tool, os type hints viram o schema de argumentos e a docstring
(mais as descrições em `Annotated`) vira a documentação que o modelo lê para
decidir QUANDO chamar cada uma.

Três regras que valem para toda tool, e que este arquivo demonstra:

  1. O MODELO NÃO EXECUTA NADA. Ele emite uma intenção ("chame get_weather com
     city=Fortaleza"); quem executa é o seu código. Logo, validar os argumentos
     é obrigação sua — o modelo pode alucinar valores inválidos.
  2. Trate os argumentos como entrada hostil. Uma instrução maliciosa num
     documento lido pelo agente pode virar uma chamada de tool. Escopo mínimo,
     sem segredos no retorno, e erro claro em vez de exceção solta.
  3. Ação irreversível pede aprovação humana. Aqui, o envio de e-mail é
     decorado com @tool(approval_mode="always_require") — a função em si não
     muda.

Nenhuma delas fala com serviço real: são mocks deterministas. O ponto da aula é
o CICLO do function calling, não o clima de verdade.
"""

from typing import Annotated

from agent_framework import tool

BASE_DE_CONHECIMENTO = {
    "reembolso": "Teto de jantar em viagem nacional: R$ 90,00 por pessoa. Lançar em até 15 dias corridos.",
    "ferias": "Solicitar com 30 dias de antecedência. Períodos de 10, 15 ou 30 dias.",
    "remoto": "Times de engenharia: 2 dias por semana no escritório, medidos por trimestre.",
    "sla": "Severidade 1: primeira resposta em 30 minutos, 24x7.",
}


def read_kb(topic: Annotated[str, "Assunto a consultar, em uma ou duas palavras (ex.: reembolso)."]) -> str:
    """Consulta a base de conhecimento interna da empresa por um assunto."""
    # Validação: argumento vazio ou absurdo vira erro legível para o modelo, não
    # exceção. O modelo lê a mensagem e corrige a chamada.
    if not topic or len(topic) > 40:
        return "Erro: informe um assunto curto (ex.: 'reembolso')."
    chave = topic.strip().lower()
    if chave in BASE_DE_CONHECIMENTO:
        return BASE_DE_CONHECIMENTO[chave]
    return f"Não há entrada para '{topic}'. Assuntos disponíveis: {', '.join(BASE_DE_CONHECIMENTO)}."


def get_weather(city: Annotated[str, "Nome da cidade, ex.: Fortaleza."]) -> str:
    """Retorna a previsão do tempo atual para uma cidade brasileira."""
    if not city or not 2 <= len(city) <= 60:
        return "Erro: nome de cidade inválido."
    # Determinista de propósito: a mesma cidade sempre devolve o mesmo resultado.
    temperatura = 22 + sum(ord(c) for c in city.lower()) % 12
    return f"Em {city.strip()}: {temperatura}°C, parcialmente nublado."


@tool(approval_mode="always_require")  # HITL: o framework pede aprovação antes de executar
def send_email_mock(
    to: Annotated[str, "Endereço do destinatário."],
    subject: Annotated[str, "Assunto do e-mail."],
    body: Annotated[str, "Corpo do e-mail."],
) -> str:
    """Envia um e-mail em nome do usuário. AÇÃO IRREVERSÍVEL: exige confirmação humana."""
    if "@" not in to or len(to) > 120:
        return "Erro: destinatário inválido."
    if not subject.strip():
        return "Erro: o assunto é obrigatório."
    # Mock: nada é enviado.
    return f"[mock] E-mail enviado para {to} com assunto '{subject}' ({len(body)} caracteres)."


def calc(
    a: Annotated[float, "Primeiro operando."],
    op: Annotated[str, "Operador: +, -, * ou /."],
    b: Annotated[float, "Segundo operando."],
) -> str:
    """Calcula uma expressão aritmética simples com dois operandos (ex.: 90 * 5)."""
    # Sem eval: a superfície de ataque de uma tool é proporcional ao que ela aceita.
    match op:
        case "+":
            r = a + b
        case "-":
            r = a - b
        case "*":
            r = a * b
        case "/" if b != 0:
            r = a / b
        case _:
            return "Erro: operador inválido ou divisão por zero."
    return f"{r:.4f}".rstrip("0").rstrip(".")
