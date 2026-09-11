"""
Capacitação Microsoft AI — Aula 6: primeiro agente com o Microsoft Agent Framework

O que este exemplo demonstra:
  1. Um agente = modelo + identidade (nome) + regras (instructions), criado
     sobre o mesmo endpoint do projeto das aulas 1 a 5, em UMA construção: Agent.
  2. run(): a resposta chega inteira. Sem sessão, cada chamada é independente.
  3. AgentSession + run(stream=True): multi-turn com a resposta token a token.

Contraste com a aula 3: lá o programa guardava o previous_response_id. Aqui o
framework faz as duas coisas — o agente carrega a persona e a sessão carrega
a memória da conversa. O modelo, o endpoint e a identidade são exatamente os
mesmos; o que muda é a camada.

Tools, MCP, memória de longo prazo e multi-agent ficam para a aula 7 — este
agente ainda só conversa. É de propósito: o objetivo de hoje é que cada aluno
saia da sala com um agente próprio rodando.
"""

import asyncio
import os

# agent_framework: o Agent Framework. Os tipos que usamos são Agent (o agente)
# e AgentSession (a conversa). FoundryChatClient é o cliente do Foundry —
# equivalente ao AsAIAgent(...) sobre o AIProjectClient da trilha .NET.
from agent_framework import Agent
from agent_framework.foundry import FoundryChatClient

# azure-identity (variante assíncrona): descobre "quem é você" a partir do
# ambiente. Sem chave de API.
from azure.identity.aio import DefaultAzureCredential
from dotenv import load_dotenv

INSTRUCOES = """\
Você é o Zé, assistente de viagens de uma agência brasileira.
Fale em português do Brasil, de forma simpática e direta.
Ajude com roteiros, destinos, melhor época para viajar e dicas práticas.
Faça no máximo uma pergunta por vez quando faltar informação.
Se o assunto não for viagem, diga que só cuida de viagens e ofereça ajuda
com isso — sem inventar competência que não tem.
Não invente preços nem disponibilidade: diga que precisam ser confirmados."""


async def main() -> None:
    # -------------------------------------------------------------------------
    # 1. Configuração
    # -------------------------------------------------------------------------
    load_dotenv()
    endpoint = os.getenv("FOUNDRY_ENDPOINT")  # o mesmo endpoint DO PROJETO das aulas 1 a 5
    model = os.getenv("FOUNDRY_MODEL")
    if not endpoint or not model:
        print("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL no .env antes de executar.")
        return

    # -------------------------------------------------------------------------
    # 2. Autenticação
    # -------------------------------------------------------------------------
    # Entra ID, como sempre. Autenticar não é autorizar: sua conta precisa do
    # papel "Foundry User" no recurso. Ser Owner da subscription NÃO basta.
    async with DefaultAzureCredential() as credential:
        # ---------------------------------------------------------------------
        # 3. O agente
        # ---------------------------------------------------------------------
        # Os três argumentos são a "anatomia do agente" do slide: o modelo
        # (LLM), a identidade (name) e as regras (instructions — o system
        # message). Tools e memória de longo prazo entram na aula 7.
        #
        # Nada é criado no serviço: o agente vive neste processo. É o que o
        # slide "MAF × Foundry Agent Service" chama de code-first.
        agente = Agent(
            client=FoundryChatClient(project_endpoint=endpoint, model=model, credential=credential),
            name="Zé",
            instructions=INSTRUCOES,
        )

        # ---------------------------------------------------------------------
        # 4. run() — uma pergunta, uma resposta inteira, sem memória
        # ---------------------------------------------------------------------
        # Sem sessão, o agente não lembra de nada entre chamadas. A segunda
        # pergunta só faz sentido se ele lembrar da primeira — e ele NÃO vai
        # lembrar. É o mesmo efeito da aula 2 sem a lista de mensagens.
        print("=== 1. run(), sem sessão ===\n")

        primeira = "Quero passar 5 dias em Fortaleza em julho. Vale a pena?"
        print(f"Você: {primeira}")
        resposta = await agente.run(primeira)
        print(f"{agente.name}: {resposta}\n")

        segunda = "E quantos dias você sugeriu mesmo?"
        print(f"Você: {segunda}")
        resposta = await agente.run(segunda)
        print(f"{agente.name}: {resposta}\n")

        # ---------------------------------------------------------------------
        # 5. AgentSession + run(stream=True) — conversa com memória, token a token
        # ---------------------------------------------------------------------
        # A sessão é o objeto que carrega o histórico. O programa não guarda
        # lista de mensagens nem previous_response_id: passa a MESMA sessão em
        # todas as chamadas e o framework cuida do resto.
        print("=== 2. run(stream=True), com sessão ===")
        print("Converse com o Zé. Digite 'sair' para encerrar.\n")

        sessao = agente.create_session()

        while True:
            prompt = input("Você: ").strip()
            if not prompt:
                continue
            if prompt.lower() == "sair":
                break

            print(f"{agente.name}: ", end="", flush=True)

            # O mesmo laço da aula 3, com o tipo do framework no lugar do evento
            # da Responses API: cada atualização carrega um pedaço da resposta.
            async for update in agente.run(prompt, session=sessao, stream=True):
                if update.text:
                    print(update.text, end="", flush=True)

            print("\n")


if __name__ == "__main__":
    asyncio.run(main())
