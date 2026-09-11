"""
Capacitação Microsoft AI — Aula 5
RAG end-to-end no Microsoft Foundry, com avaliação automática

O que esta aula acrescenta à anterior:
  1. O pipeline devolve um OBJETO (resposta + trechos + contexto + custo),
     não texto solto — sem isso não há o que avaliar.
  2. Quatro métricas de qualidade com modelo juiz: groundedness, relevance,
     retrieval e correctness (similarity com o gabarito).
  3. A varredura de top-k (3 × 5 × 10) medida, e não chutada.
  4. Quanto o ranqueador semântico realmente ganha, no seu corpus.
  5. Um relatório em Markdown para comparar com o da semana que vem.

A frase que a aula inteira serve para sustentar: RAG não termina quando a
resposta aparece na tela. Termina quando você consegue dizer, com número, se
ela ficou melhor ou pior do que ontem — e QUAL etapa mudou.
"""

import os
from datetime import datetime
from pathlib import Path

from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv

from avaliador_rag import AvaliadorRag, ConjuntoDeMetricas, LinhaDoRelatorio, Resumo, encurtar
from conjunto_de_avaliacao import PERGUNTAS, TOP_KS_DA_VARREDURA
from indice_rag import IndiceRag, Modo
from pipeline_rag import PipelineRag


def nota(valor: float | None) -> str:
    return "  -  " if valor is None else f"{valor:.2f}"


def sim(valor: bool) -> str:
    return " ok " if valor else "FALHA"


def ler_inteiro(rotulo: str, padrao: int) -> int:
    texto = input(f"{rotulo} [{padrao}]: ").strip()
    return int(texto) if texto.isdigit() else padrao


def main() -> None:
    load_dotenv()
    endpoint = os.getenv("FOUNDRY_ENDPOINT")
    modelo_de_chat = os.getenv("FOUNDRY_MODEL")
    modelo_de_embedding = os.getenv("FOUNDRY_EMBEDDING_MODEL")
    search_endpoint = os.getenv("SEARCH_ENDPOINT")
    nome_do_indice = os.getenv("SEARCH_INDEX", "politicas-internas")

    # O juiz pode ser outro deployment. Trocar esta variável ao vivo e ver as
    # notas mudarem é a demonstração mais curta de que a avaliação é um
    # INSTRUMENTO, com as suas próprias características, e não uma medida absoluta.
    modelo_juiz = os.getenv("FOUNDRY_JUDGE_MODEL") or modelo_de_chat

    if not all([endpoint, modelo_de_chat, modelo_de_embedding, search_endpoint]):
        print(
            "Faltam variáveis de ambiente. Defina no .env:\n"
            "  FOUNDRY_ENDPOINT          https://<recurso>.services.ai.azure.com/api/projects/<projeto>\n"
            "  FOUNDRY_MODEL             deployment do modelo de chat\n"
            "  FOUNDRY_EMBEDDING_MODEL   deployment do modelo de embeddings\n"
            "  FOUNDRY_JUDGE_MODEL       deployment do juiz (opcional; padrão: FOUNDRY_MODEL)\n"
            "  SEARCH_ENDPOINT           https://<servico>.search.windows.net\n"
            "  SEARCH_INDEX              nome do índice (padrão: politicas-internas)"
        )
        return

    # Os avaliadores falam com o RECURSO (account), não com o projeto: derivamos
    # o endpoint do recurso a partir do endpoint do projeto.
    account_endpoint = endpoint.split("/api/projects/")[0]

    with (
        DefaultAzureCredential() as credential,
        AIProjectClient(endpoint=endpoint, credential=credential) as project,
    ):
        openai = project.get_openai_client()
        indice = IndiceRag(search_endpoint, credential, openai, modelo_de_embedding, nome_do_indice)
        pipeline = PipelineRag(indice, openai, modelo_de_chat)
        avaliador = AvaliadorRag(account_endpoint, modelo_juiz, credential)
        pasta_do_corpus = Path(__file__).parent / "corpus"

        # O que já foi medido nesta sessão. É daqui que sai o relatório da opção 7.
        resumos: list[Resumo] = []
        ultima_corrida: list[LinhaDoRelatorio] = []

        # ---------------------------------------------------------------------
        # Opções do menu
        # ---------------------------------------------------------------------
        def indexar() -> None:
            print("\nCriando o índice...")
            indice.criar_ou_atualizar_indice()
            print("Fatiando e vetorizando o corpus:")
            total = indice.indexar(pasta_do_corpus, print)
            print(f"\n{total} chunk(s) indexado(s) em '{nome_do_indice}'.")

        def perguntar() -> None:
            pergunta = input("\nPergunta: ").strip()
            if not pergunta:
                return
            top_k = ler_inteiro("top-k", 5)
            r = pipeline.responder(pergunta, top_k)
            print(f"\n{r.texto}\n")
            print(f"  trechos: {', '.join(r.documentos)}")
            print(f"  custo: {r.tokens_de_entrada} tokens de entrada · {r.tokens_de_saida} de saída · {r.latencia_s:.1f}s")

        def avaliar_uma() -> None:
            print()
            for i, caso in enumerate(PERGUNTAS, 1):
                print(f"  {i}. {encurtar(caso.pergunta, 70)}")
            i = ler_inteiro("Qual", 1) - 1
            caso = PERGUNTAS[max(0, min(i, len(PERGUNTAS) - 1))]
            r = pipeline.responder(caso.pergunta, ler_inteiro("top-k", 5))
            n = avaliador.avaliar(caso, r)
            print(f"\n  Gabarito : {caso.resposta_esperada}")
            print(f"  Resposta : {r.texto}")
            print(f"  Trechos  : {', '.join(r.documentos)}")
            print(
                f"  Notas    : groundedness {nota(n.groundedness)} · relevance {nota(n.relevance)} · "
                f"retrieval {nota(n.retrieval)} · correctness {nota(n.correctness)}"
            )
            print(f"  Docs certos {sim(n.recuperou_os_documentos_esperados)} · recusa certa {sim(n.recusou_como_esperado)}")
            if n.razao_groundedness:
                print(f"\n  Juiz (groundedness): {n.razao_groundedness}")
            print(f"\n  Por que esta pergunta está no conjunto: {caso.o_que_ensina}")

        def avaliar_conjunto() -> None:
            nonlocal ultima_corrida
            top_k = ler_inteiro("top-k", 5)
            print(f"\nAvaliando {len(PERGUNTAS)} pergunta(s) com as quatro métricas:")
            linhas = avaliador.avaliar_conjunto(
                pipeline, PERGUNTAS, top_k, Modo.HIBRIDA_COM_RERANKING, ConjuntoDeMetricas.COMPLETO, print
            )
            ultima_corrida = linhas
            resumo = Resumo.de(top_k, Modo.HIBRIDA_COM_RERANKING, linhas)
            resumos.append(resumo)
            imprimir_resumo(resumo)

        def varrer_top_k() -> None:
            print(f"\nVarrendo top-k {TOP_KS_DA_VARREDURA} sobre {len(PERGUNTAS)} pergunta(s).")
            print("Só groundedness e retrieval: são as duas que dizem qual etapa mudou.\n")
            da_varredura: list[Resumo] = []
            for top_k in TOP_KS_DA_VARREDURA:
                print(f"  top-k {top_k}:")
                linhas = avaliador.avaliar_conjunto(
                    pipeline, PERGUNTAS, top_k, Modo.HIBRIDA_COM_RERANKING, ConjuntoDeMetricas.DIAGNOSTICO, print
                )
                resumo = Resumo.de(top_k, Modo.HIBRIDA_COM_RERANKING, linhas)
                da_varredura.append(resumo)
                resumos.append(resumo)

            print("\n  top-k  Groundedness  Retrieval  Docs certos  Tokens in  Latência")
            print("  " + "-" * 66)
            for r in da_varredura:
                print(
                    f"  {r.top_k:>5}  {nota(r.groundedness):>12}  {nota(r.retrieval):>9}  "
                    f"{r.acerto_de_recuperacao:>10.0%}  {r.tokens_de_entrada_medios:>9.0f}  {r.latencia_media_s:>7.1f}s"
                )
            print(
                "\n  Como ler esta tabela:\n"
                "    Retrieval subiu e groundedness ficou igual  → mais contexto ajudou a achar.\n"
                "    Retrieval igual e tokens subiram            → você está pagando por nada.\n"
                "    Groundedness CAIU com top-k maior           → entrou ruído; o modelo se\n"
                "                                                  apoiou no trecho errado."
            )

        # O slide diz "+5 a +15 pontos de relevance". Esta opção existe para você
        # não precisar acreditar no slide: mede no seu corpus, com o seu conjunto.
        # Em um corpus de cinco documentos o ganho costuma ser pequeno — e dizer
        # isso em voz alta vale mais do que fingir um resultado bonito.
        def comparar_reranking() -> None:
            top_k = ler_inteiro("top-k", 5)
            for modo in (Modo.HIBRIDA, Modo.HIBRIDA_COM_RERANKING):
                print(f"\n  {modo.value}:")
                linhas = avaliador.avaliar_conjunto(
                    pipeline, PERGUNTAS, top_k, modo, ConjuntoDeMetricas.DIAGNOSTICO, print
                )
                resumo = Resumo.de(top_k, modo, linhas)
                resumos.append(resumo)
                print(
                    f"    groundedness {nota(resumo.groundedness)} · retrieval {nota(resumo.retrieval)} · "
                    f"docs certos {resumo.acerto_de_recuperacao:.0%} · {resumo.latencia_media_s:.1f}s"
                )

        # O entregável do desafio da aula. Um arquivo que se compara com o da
        # semana passada é o que transforma "acho que melhorou" em engenharia.
        def gravar_relatorio() -> None:
            if not resumos:
                print("\nNada medido ainda nesta sessão. Rode a opção 4, 5 ou 6 antes.")
                return

            linhas_md = [
                "# Relatório de avaliação RAG — Aula 5",
                "",
                f"- Gerado em: {datetime.now():%Y-%m-%d %H:%M}",
                f"- Modelo de geração: `{modelo_de_chat}`",
                f"- Modelo juiz: `{modelo_juiz}`",
                f"- Índice: `{nome_do_indice}`",
                f"- Perguntas no conjunto: {len(PERGUNTAS)}",
                "",
                "## Configurações medidas",
                "",
                "| Modo | top-k | Groundedness | Relevance | Retrieval | Correctness | Docs certos | Recusa certa | Tokens in | Latência |",
                "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
            ]
            for r in resumos:
                linhas_md.append(
                    f"| {r.modo.value} | {r.top_k} | {nota(r.groundedness)} | {nota(r.relevance)} | "
                    f"{nota(r.retrieval)} | {nota(r.correctness)} | {r.acerto_de_recuperacao:.0%} | "
                    f"{r.acerto_de_recusa:.0%} | {r.tokens_de_entrada_medios:.0f} | {r.latencia_media_s:.1f}s |"
                )

            if ultima_corrida:
                linhas_md += ["", "## Última corrida completa, pergunta a pergunta", ""]
                for l in ultima_corrida:
                    n = l.notas
                    linhas_md += [
                        f"### {l.caso.pergunta}",
                        "",
                        f"- **Gabarito:** {l.caso.resposta_esperada}",
                        f"- **Resposta:** {' '.join(l.resposta.texto.split())}",
                        f"- **Trechos:** {', '.join(l.resposta.documentos)}",
                        f"- **Notas:** groundedness {nota(n.groundedness)} · relevance {nota(n.relevance)} · "
                        f"retrieval {nota(n.retrieval)} · correctness {nota(n.correctness)}",
                        f"- **Por que esta pergunta está no conjunto:** {l.caso.o_que_ensina}",
                    ]
                    if n.razao_groundedness:
                        linhas_md.append(f"- **Justificativa do juiz (groundedness):** {' '.join(n.razao_groundedness.split())}")
                    linhas_md.append("")

            caminho = Path.cwd() / f"relatorio-rag-{datetime.now():%Y%m%d-%H%M}.md"
            caminho.write_text("\n".join(linhas_md), encoding="utf-8")
            print(f"\nRelatório gravado em:\n  {caminho}")

        def imprimir_resumo(r: Resumo) -> None:
            print(f"\n  Médias (top-k {r.top_k}, {r.modo.value}):")
            print(
                f"    groundedness {nota(r.groundedness)} · relevance {nota(r.relevance)} · "
                f"retrieval {nota(r.retrieval)} · correctness {nota(r.correctness)}"
            )
            print(f"    documentos certos {r.acerto_de_recuperacao:.0%} · recusa certa {r.acerto_de_recusa:.0%}")
            print(f"    custo médio {r.tokens_de_entrada_medios:.0f} tokens de entrada · {r.latencia_media_s:.1f}s por pergunta")

        # ---------------------------------------------------------------------
        # Menu
        # ---------------------------------------------------------------------
        while True:
            print()
            print("=== Capacitação Microsoft AI — RAG end-to-end com avaliação ===")
            print("1. Criar o índice e indexar o corpus")
            print("2. Perguntar (híbrida + reranking, com citações e custo)")
            print("3. Avaliar UMA pergunta do conjunto — as quatro métricas")
            print("4. Avaliar o conjunto inteiro")
            print("5. Varredura de top-k: 3 × 5 × 10")
            print("6. O que o ranqueador semântico ganha (com × sem reranking)")
            print("7. Gravar o relatório em Markdown")
            print("8. Apagar o índice")
            print("0. Sair")

            match input("Opção: ").strip():
                case "1":
                    indexar()
                case "2":
                    perguntar()
                case "3":
                    avaliar_uma()
                case "4":
                    avaliar_conjunto()
                case "5":
                    varrer_top_k()
                case "6":
                    comparar_reranking()
                case "7":
                    gravar_relatorio()
                case "8":
                    indice.apagar_indice()
                    print("Índice apagado.")
                case "0" | "":
                    return
                case _:
                    print("Opção inválida.")


if __name__ == "__main__":
    main()
