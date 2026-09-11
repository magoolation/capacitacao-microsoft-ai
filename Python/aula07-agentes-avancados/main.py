"""
Capacitação Microsoft AI — Aula 7: agentes avançados
Tools, function calling, aprovação humana, MCP e workflow multi-agente.

O que este exemplo demonstra:
  1. Function calling completo: o agente da aula 6 ganha quatro tools e o
     programa mostra, chamada a chamada, o ciclo "modelo pede → app executa
     → modelo continua".
  2. Human-in-the-loop na tool: o envio de e-mail exige aprovação. O agente
     PARA e devolve um pedido; o programa pergunta ao humano e retoma.
  3. MCP: o Microsoft Learn MCP Server como tool, sem escrever integração.
  4. Workflow sequencial com três agentes (Pesquisador → Validador → Redator):
     o padrão de pipeline editorial do slide, com streaming por participante.

O setup (endpoint, identidade, FoundryChatClient) é o mesmo da aula 6. O que
muda é a lista `tools=` do Agent — e é isso que transforma um agente que só
conversa num agente que age.
"""

import asyncio
import json
import os

from agent_framework import Agent, FunctionApprovalRequestContent, MCPStreamableHTTPTool, Message
from agent_framework.foundry import FoundryChatClient
from agent_framework.orchestrations import SequentialBuilder
from azure.identity.aio import DefaultAzureCredential
from dotenv import load_dotenv

from ferramentas import calc, get_weather, read_kb, send_email_mock

PERSONA = """\
Você é o Zé, assistente interno de uma agência de viagens brasileira.
Fale em português do Brasil, de forma direta.
Use as ferramentas disponíveis sempre que a pergunta pedir dados (políticas
internas, clima, cálculos) — não invente valores que uma ferramenta pode dar.
Ao enviar e-mails, confirme o destinatário e o assunto antes.
Se uma ferramenta devolver erro, explique o erro ao usuário em vez de tentar
contornar."""


def imprimir_chamadas_de_tool(resposta) -> None:
    """Varre as mensagens da resposta e imprime cada chamada de função e cada
    resultado. É o "log por etapa" do hands-on — e a base do tracing da aula 8."""
    for mensagem in resposta.messages:
        for conteudo in mensagem.contents:
            if conteudo.type == "function_call":
                print(f"  → tool {conteudo.name}({conteudo.arguments})")
            elif conteudo.type == "function_result":
                print(f"  ← {conteudo.result}")


# =============================================================================
# DEMO 1 — Function calling com o ciclo visível
# =============================================================================
async def function_calling(client: FoundryChatClient) -> None:
    agente = Agent(client=client, name="Zé", instructions=PERSONA, tools=[read_kb, get_weather, calc])
    sessao = agente.create_session()

    roteiro = [
        "Qual é o teto de jantar em viagem nacional? E quanto dá para 5 jantares?",
        "Como está o tempo em Fortaleza agora?",
        "Qual é a política de home office para engenharia?",
    ]
    print("\nRoteiro de três perguntas — repare nas chamadas de tool entre elas.\n")
    for pergunta in roteiro:
        print(f"Você: {pergunta}")
        resposta = await agente.run(pergunta, session=sessao)
        imprimir_chamadas_de_tool(resposta)
        print(f"{agente.name}: {resposta}\n")

    print("Agora é sua vez. Digite perguntas (ou 'sair').\n")
    while True:
        prompt = input("Você: ").strip()
        if not prompt:
            continue
        if prompt.lower() == "sair":
            return
        resposta = await agente.run(prompt, session=sessao)
        imprimir_chamadas_de_tool(resposta)
        print(f"{agente.name}: {resposta}\n")


# =============================================================================
# DEMO 2 — Human-in-the-loop
# =============================================================================
# Com @tool(approval_mode="always_require"), o agente não executa a tool:
# devolve um FunctionApprovalRequestContent e PARA. O programa mostra a chamada
# pretendida, pergunta ao humano e devolve a resposta na mesma sessão. Só então
# a tool roda (ou não) e o agente conclui.
async def human_in_the_loop(client: FoundryChatClient) -> None:
    agente = Agent(client=client, name="Zé", instructions=PERSONA, tools=[read_kb, send_email_mock])
    sessao = agente.create_session()

    pedido = (
        "Mande um e-mail para financeiro@auroralog.example com o assunto 'Teto de jantar' "
        "informando o valor do teto de jantar em viagem nacional."
    )
    print(f"\nVocê: {pedido}")
    resposta = await agente.run(pedido, session=sessao)

    # Enquanto houver pedidos de aprovação pendentes, o agente está pausado.
    pendentes = [r for r in resposta.user_input_requests if isinstance(r, FunctionApprovalRequestContent)]
    while pendentes:
        decisoes = []
        for pedido_de_aprovacao in pendentes:
            chamada = pedido_de_aprovacao.function_call
            print(f"\n  [HITL] O agente quer chamar {chamada.name} com {json.dumps(chamada.parse_arguments() or {}, ensure_ascii=False)}")
            aprovado = input("  Aprovar? (s/n): ").strip().lower() == "s"
            # A decisão volta como conteúdo de uma mensagem do usuário.
            decisoes.append(pedido_de_aprovacao.to_function_approval_response(aprovado))

        resposta = await agente.run(Message(role="user", contents=decisoes), session=sessao)
        pendentes = [r for r in resposta.user_input_requests if isinstance(r, FunctionApprovalRequestContent)]

    imprimir_chamadas_de_tool(resposta)
    print(f"\n{agente.name}: {resposta}")


# =============================================================================
# DEMO 3 — MCP
# =============================================================================
# Uma tool MCP é declarada, não implementada: o servidor expõe as ferramentas e
# o framework as descobre. Aqui usamos o Microsoft Learn MCP Server — o agente
# passa a responder com a documentação oficial, sem uma linha de integração.
# É o "USB dos agentes" do slide.
async def mcp(client: FoundryChatClient) -> None:
    async with MCPStreamableHTTPTool(name="MSLearn", url="https://learn.microsoft.com/api/mcp") as mslearn:
        agente = Agent(
            client=client,
            name="Zé Docs",
            instructions=(
                "Você responde perguntas técnicas sobre Azure e Microsoft Foundry usando a "
                "documentação oficial via MCP. Cite os links que encontrar. Responda em português."
            ),
            tools=[mslearn],
        )
        sessao = agente.create_session()
        print("\nPergunte algo sobre Azure/Foundry (ou 'sair'). O agente consulta o Microsoft Learn via MCP.\n")
        while True:
            prompt = input("Você: ").strip()
            if not prompt:
                continue
            if prompt.lower() == "sair":
                return
            print(f"{agente.name}: ", end="", flush=True)
            async for update in agente.run(prompt, session=sessao, stream=True):
                if update.text:
                    print(update.text, end="", flush=True)
            print("\n")


# =============================================================================
# DEMO 4 — Workflow sequencial: Pesquisador → Validador → Redator
# =============================================================================
# Três agentes, cada um com uma função estreita, ligados num pipeline. A saída
# de um é a entrada do próximo. Comparado a um agente único com muitas tools, o
# workflow troca flexibilidade por PREVISIBILIDADE: a ordem é conhecida, cada
# etapa é testável e o custo é estimável.
async def workflow_sequencial(client: FoundryChatClient) -> None:
    pesquisador = Agent(
        client=client,
        name="Pesquisador",
        instructions=(
            "Levante, em tópicos curtos, os fatos relevantes para o pedido do usuário usando a "
            "base de conhecimento. Não escreva o texto final — só os fatos, com a fonte."
        ),
        tools=[read_kb, get_weather],
    )
    validador = Agent(
        client=client,
        name="Validador",
        instructions=(
            "Revise os fatos recebidos: marque o que está inconsistente, faltando ou fora do "
            "escopo do pedido. Devolva a lista corrigida, sem redigir o texto final."
        ),
    )
    redator = Agent(
        client=client,
        name="Redator",
        instructions=(
            "Com os fatos validados, escreva a resposta final ao usuário em português, "
            "em no máximo 8 linhas, citando os fatos usados."
        ),
    )

    workflow = SequentialBuilder(participants=[pesquisador, validador, redator]).build()
    # Um workflow também pode ser usado como agente — e aí o streaming diz
    # quem está falando (author_name).
    pipeline = workflow.as_agent(name="Pipeline editorial")

    pedido = input("\nPedido para o pipeline (Enter para o exemplo padrão): ").strip() or (
        "Quero uma nota curta para a equipe sobre o teto de reembolso de jantar e a regra de "
        "dias no escritório, e diga como está o tempo em Fortaleza hoje."
    )

    autor_atual = None
    async for update in pipeline.run(pedido, stream=True):
        if update.author_name and update.author_name != autor_atual:
            autor_atual = update.author_name
            print(f"\n\n--- {autor_atual} ---")
        if update.text:
            print(update.text, end="", flush=True)
    print("\n\n=== Fim do workflow ===")


async def main() -> None:
    load_dotenv()
    endpoint = os.getenv("FOUNDRY_ENDPOINT")
    model = os.getenv("FOUNDRY_MODEL")
    if not endpoint or not model:
        print("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL no .env antes de executar.")
        return

    async with DefaultAzureCredential() as credential:
        client = FoundryChatClient(project_endpoint=endpoint, model=model, credential=credential)

        while True:
            print()
            print("=== Capacitação Microsoft AI — Aula 7: agentes avançados ===")
            print("1. Function calling: 4 tools, com o ciclo visível")
            print("2. Human-in-the-loop: aprovar o envio de e-mail")
            print("3. MCP: Microsoft Learn como tool")
            print("4. Workflow sequencial: Pesquisador → Validador → Redator")
            print("0. Sair")

            match input("Opção: ").strip():
                case "1":
                    await function_calling(client)
                case "2":
                    await human_in_the_loop(client)
                case "3":
                    await mcp(client)
                case "4":
                    await workflow_sequencial(client)
                case "0" | "":
                    return
                case _:
                    print("Opção inválida.")


if __name__ == "__main__":
    asyncio.run(main())
