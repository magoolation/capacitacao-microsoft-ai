"""
Capacitação Microsoft AI — Aula 4: RAG com Azure AI Search e Microsoft Foundry

O que este exemplo demonstra:
  1. Indexação: chunking → embeddings → Azure AI Search.
  2. Recuperação nos três modos: keyword, vetorial e híbrida com reranking.
  3. A MESMA pergunta respondida SEM e COM RAG, lado a lado.
  4. Citações: de qual documento veio cada afirmação.

O ponto da aula não é "RAG é melhor". É: RAG resolve um problema específico —
o modelo não conhece os seus documentos. Fora desse problema, ele não ajuda,
e às vezes atrapalha. As três perguntas da opção 5 do menu foram escolhidas
para mostrar exatamente isso.

Diferença para a trilha .NET: lá a aula 4 precisa trocar o Azure.AI.Projects
pelo OpenAIClient direto (endpoint /openai/v1) por causa do pivô do pacote
OpenAI. Em Python não há esse conflito — o mesmo AIProjectClient das aulas 1
a 3 serve, com o mesmo endpoint do projeto.
"""

import os
from pathlib import Path

from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv
from openai import OpenAI

from indice_rag import IndiceRag, Modo

# As duas instruções que carregam todo o peso: responder SÓ pelo contexto e
# admitir quando não sabe. Sem a segunda, o modelo preenche a lacuna com o que
# ele acha que sabe — e a demo perde a graça.
INSTRUCAO_COM_RAG = """\
Você responde perguntas sobre as políticas internas da empresa.
Use EXCLUSIVAMENTE os trechos numerados fornecidos como contexto.
Cite a fonte de cada afirmação no formato [n].
Se a resposta não estiver nos trechos, responda exatamente:
"Não encontrei essa informação nos documentos disponíveis."
Não complete lacunas com conhecimento geral."""

INSTRUCAO_SEM_RAG = "Você responde perguntas sobre políticas internas de empresas."

# Três perguntas, escolhidas para mostrar três comportamentos diferentes.
# Rodar nesta ordem: cada uma desmonta a conclusão apressada da anterior.
ROTEIRO_DE_SALA = [
    (
        "Qual é o limite de reembolso para jantar em viagem nacional?",
        "Só o corpus responde. Sem RAG o modelo inventa um valor plausível; com RAG acerta e cita.",
    ),
    (
        "O que é uma VPN?",
        "Conhecimento geral. Os dois acertam — RAG não ajudou em nada aqui.",
    ),
    (
        "Qual é a política de reembolso para viagens internacionais?",
        "Parece estar no corpus, mas não está. Com RAG a resposta correta é admitir que não encontrou.",
    ),
]


def indexar(indice: IndiceRag, pasta_do_corpus: Path, nome_do_indice: str) -> None:
    print("\nCriando o índice...")
    indice.criar_ou_atualizar_indice()

    print("Fatiando e vetorizando o corpus:")
    total = indice.indexar(pasta_do_corpus, print)

    print(f"\n{total} chunk(s) indexado(s) em '{nome_do_indice}'.")
    print("O índice leva alguns segundos para ficar consultável.")


# Rodar isto com uma pergunta que usa SINÔNIMOS do corpus (e não as palavras
# exatas) é o que torna a diferença visível: a keyword não acha, a vetorial acha.
def comparar_recuperacao(indice: IndiceRag) -> None:
    pergunta = input("\nPergunta para buscar: ").strip()

    for modo in Modo:
        print(f"\n--- {modo.value} ---")
        achados = indice.buscar(pergunta, modo)
        if not achados:
            print("  (nada encontrado)")
            continue
        for trecho, score in achados:
            previa = trecho.conteudo.replace("\n", " ")
            if len(previa) > 90:
                previa = previa[:90] + "..."
            print(f"  [{score or 0:6.3f}] {trecho.documento:<30} {previa}")


def perguntar(openai: OpenAI, model: str, indice: IndiceRag, pergunta: str, com_rag: bool) -> None:
    if not pergunta.strip():
        return

    if com_rag:
        achados = indice.buscar(pergunta, Modo.HIBRIDA)

        # O contexto entra numerado. É isso que permite ao modelo citar [1], [2]
        # e ao usuário conferir. Sem numeração não há citação verificável.
        contexto = "\n\n".join(
            f"[{i + 1}] ({trecho.documento}) {trecho.conteudo}" for i, (trecho, _) in enumerate(achados)
        )
        instrucoes = INSTRUCAO_COM_RAG
        entrada = f"Contexto:\n{contexto}\n\nPergunta: {pergunta}"

        documentos = sorted({trecho.documento for trecho, _ in achados})
        print(f"\n[recuperados {len(achados)} trecho(s): {', '.join(documentos)}]")
    else:
        instrucoes = INSTRUCAO_SEM_RAG
        entrada = pergunta

    print("\nCOM RAG: " if com_rag else "\nSEM RAG: ", end="")

    stream = openai.responses.create(model=model, instructions=instrucoes, input=entrada, stream=True)
    for event in stream:
        if event.type == "response.output_text.delta":
            print(event.delta, end="", flush=True)
    print()


def roteiro_de_sala(openai: OpenAI, model: str, indice: IndiceRag) -> None:
    for pergunta, observar in ROTEIRO_DE_SALA:
        print(f"\n{'=' * 78}")
        print(f"PERGUNTA: {pergunta}")
        print(f"O que observar: {observar}")
        print("=" * 78)

        perguntar(openai, model, indice, pergunta, com_rag=False)
        perguntar(openai, model, indice, pergunta, com_rag=True)

        input("\n(Enter para a próxima)")


def main() -> None:
    load_dotenv()
    endpoint = os.getenv("FOUNDRY_ENDPOINT")
    modelo_de_chat = os.getenv("FOUNDRY_MODEL")
    modelo_de_embedding = os.getenv("FOUNDRY_EMBEDDING_MODEL")
    search_endpoint = os.getenv("SEARCH_ENDPOINT")
    nome_do_indice = os.getenv("SEARCH_INDEX", "politicas-internas")

    if not all([endpoint, modelo_de_chat, modelo_de_embedding, search_endpoint]):
        print(
            "Faltam variáveis de ambiente. Defina no .env:\n"
            "  FOUNDRY_ENDPOINT          https://<recurso>.services.ai.azure.com/api/projects/<projeto>\n"
            "  FOUNDRY_MODEL             deployment do modelo de chat\n"
            "  FOUNDRY_EMBEDDING_MODEL   deployment do modelo de embeddings\n"
            "  SEARCH_ENDPOINT           https://<servico>.search.windows.net\n"
            "  SEARCH_INDEX              nome do índice (padrão: politicas-internas)"
        )
        return

    # Uma credencial só para os dois serviços. Nenhuma chave de API em lugar
    # nenhum: o Foundry e o AI Search autenticam por Entra ID, com papéis
    # diferentes.
    with (
        DefaultAzureCredential() as credential,
        AIProjectClient(endpoint=endpoint, credential=credential) as project,
    ):
        openai = project.get_openai_client()
        indice = IndiceRag(search_endpoint, credential, openai, modelo_de_embedding, nome_do_indice)
        pasta_do_corpus = Path(__file__).parent / "corpus"

        while True:
            print()
            print("=== Capacitação Microsoft AI — RAG ===")
            print("1. Criar o índice e indexar o corpus")
            print("2. Comparar recuperação: keyword × vetorial × híbrida")
            print("3. Perguntar SEM RAG")
            print("4. Perguntar COM RAG")
            print("5. Roteiro de sala: as três perguntas, com e sem RAG")
            print("6. Apagar o índice")
            print("0. Sair")

            match input("Opção: ").strip():
                case "1":
                    indexar(indice, pasta_do_corpus, nome_do_indice)
                case "2":
                    comparar_recuperacao(indice)
                case "3":
                    perguntar(openai, modelo_de_chat, indice, input("Pergunta: "), com_rag=False)
                case "4":
                    perguntar(openai, modelo_de_chat, indice, input("Pergunta: "), com_rag=True)
                case "5":
                    roteiro_de_sala(openai, modelo_de_chat, indice)
                case "6":
                    indice.apagar_indice()
                    print("Índice apagado.")
                case "0" | "":
                    return
                case _:
                    print("Opção inválida.")


if __name__ == "__main__":
    main()
