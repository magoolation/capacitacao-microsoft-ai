"""
Capacitação Microsoft AI — Aula 3: Foundry SDK, chat multi-turn com streaming

O que este exemplo demonstra:
  1. A mesma montagem das outras aulas: Foundry SDK + cliente OpenAI.
  2. Streaming token a token com `stream=True`.
  3. Contexto mantido pelo SERVIÇO, via `previous_response_id`.
  4. Retry exponencial em erros transientes (429, 5xx) com `tenacity`.
  5. Contagem local de tokens com `tiktoken` e saída formatada com `rich`.

Contraste com a aula 1: lá a resposta chega inteira, de uma vez. Aqui ela
chega em pedaços, e a diferença aparece na percepção de latência — é o mesmo
modelo, o mesmo endpoint, a mesma identidade e o MESMO cliente. A troca é
`stream=True` e um laço sobre os eventos.
"""

import os
import time

import tiktoken
from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv
from openai import APIConnectionError, APIStatusError, OpenAI, RateLimitError
from rich.console import Console
from tenacity import retry, retry_if_exception_type, stop_after_attempt, wait_exponential

console = Console()


# -----------------------------------------------------------------------------
# Retry exponencial
# -----------------------------------------------------------------------------
# 429 (cota) e 503 (serviço indisponível) são transientes: esperar e tentar de
# novo resolve. Erros 4xx de negócio (400, 401, 403, 404) NÃO são — repetir só
# atrasa a mensagem de erro. O predicado abaixo separa os dois grupos.
def _transiente(exc: BaseException) -> bool:
    if isinstance(exc, (RateLimitError, APIConnectionError)):
        return True
    return isinstance(exc, APIStatusError) and exc.status_code >= 500


@retry(
    retry=retry_if_exception_type((RateLimitError, APIConnectionError, APIStatusError)) & _transiente,
    wait=wait_exponential(multiplier=1, min=1, max=20),
    stop=stop_after_attempt(5),
    reraise=True,
)
def criar_resposta_em_streaming(openai: OpenAI, model: str, prompt: str, previous_response_id: str | None):
    """Abre o stream. O retry cobre a ABERTURA da chamada; um stream que cai no
    meio precisa de tratamento próprio (fora do escopo desta aula)."""
    return openai.responses.create(
        model=model,
        input=prompt,
        previous_response_id=previous_response_id,
        stream=True,
    )


def main() -> None:
    load_dotenv()
    endpoint = os.getenv("FOUNDRY_ENDPOINT")
    model = os.getenv("FOUNDRY_MODEL")
    if not endpoint or not model:
        console.print("[red]Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL no .env antes de executar.[/red]")
        return

    # tiktoken: contagem LOCAL de tokens. Serve para estimar custo antes de
    # rodar em lote e para não descobrir o estouro da janela só na resposta de
    # erro. O encoding é aproximado para modelos que não são da OpenAI.
    encoding = tiktoken.get_encoding("o200k_base")

    with (
        DefaultAzureCredential() as credential,
        AIProjectClient(endpoint=endpoint, credential=credential) as project,
    ):
        openai = project.get_openai_client()

        # Quem guarda o histórico é o SERVIÇO: o programa só lembra do id da
        # última resposta (compare com a demo 1 da aula 2).
        previous_response_id: str | None = None

        console.print("[bold]Chat iniciado.[/bold] Digite sua pergunta (ou 'sair' para encerrar).\n")

        while True:
            prompt = console.input("[cyan]Você:[/cyan] ").strip()
            if not prompt:
                continue
            if prompt.lower() == "sair":
                break

            tokens_da_pergunta = len(encoding.encode(prompt))
            inicio = time.perf_counter()

            console.print("[green]IA:[/green] ", end="")

            # A chamada devolve um fluxo de eventos. O que interessa aqui são
            # dois tipos: o delta de texto (escrevemos na hora, sem esperar o
            # resto) e o evento de conclusão, que traz o id da resposta e o uso.
            stream = criar_resposta_em_streaming(openai, model, prompt, previous_response_id)

            uso = None
            for event in stream:
                if event.type == "response.output_text.delta":
                    console.print(event.delta, end="", highlight=False)
                elif event.type == "response.completed":
                    # O id chega no fim: é ele que liga esta resposta à próxima.
                    previous_response_id = event.response.id
                    uso = event.response.usage

            duracao = time.perf_counter() - inicio
            console.print()

            # Métricas básicas — o começo da observabilidade da aula 8.
            if uso is not None:
                console.print(
                    f"[dim]  {duracao:.1f}s · entrada {uso.input_tokens} tokens "
                    f"(pergunta ≈ {tokens_da_pergunta} local) · saída {uso.output_tokens} tokens[/dim]\n"
                )


if __name__ == "__main__":
    main()
