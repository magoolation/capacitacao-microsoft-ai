"""
A metade nova da aula: medir a qualidade da resposta com um modelo juiz.

Os quatro avaliadores do pacote azure-ai-evaluation olham para pares
diferentes de coisas. Confundi-los é o erro conceitual mais comum, e a tabela
abaixo é o slide de métricas em forma de código:

  Groundedness  resposta × contexto recuperado   "isto que foi afirmado está no material?"
  Relevance     resposta × pergunta              "respondeu o que foi perguntado?"
  Retrieval     trechos  × pergunta              "os trechos trazidos serviam?"
  Similarity    resposta × gabarito              "está CERTA?" (só com gold)

Todos devolvem de 1 a 5, a maioria com uma justificativa em texto. A
justificativa vale tanto quanto a nota: é ela que transforma "caiu de 4.2
para 3.1" em uma tarefa.

E repare no que separa as duas primeiras linhas da terceira. Se a resposta está
ruim e o RETRIEVAL está bom, o defeito é da geração ou do prompt. Se o retrieval
caiu junto, o defeito é do chunking, do embedding ou do índice. Medir as duas
coisas separadas é o que troca adivinhação por diagnóstico.

Uma ressalva honesta, para dizer em voz alta na aula: o juiz é um LLM. Ele
erra, é sensível ao próprio prompt e custa uma chamada por métrica. Ele não é a
verdade — é um instrumento barato o bastante para rodar em toda mudança, o que
nenhum painel humano é. As duas conferências determinísticas no fim desta
classe (recuperou os documentos certos? recusou quando devia?) não dependem de
juiz nenhum, e são as primeiras a olhar quando a nota surpreende.
"""

from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from enum import Enum
from typing import Callable

from azure.ai.evaluation import (
    AzureOpenAIModelConfiguration,
    GroundednessEvaluator,
    RelevanceEvaluator,
    RetrievalEvaluator,
    SimilarityEvaluator,
)
from azure.core.credentials import TokenCredential

from conjunto_de_avaliacao import PerguntaAvaliada
from indice_rag import Modo
from pipeline_rag import PipelineRag, RespostaRag


class ConjuntoDeMetricas(Enum):
    """Quais métricas rodar. Cada uma custa uma chamada ao modelo juiz."""

    # Groundedness + Retrieval: as duas que respondem "qual etapa quebrou?".
    # É o conjunto da varredura de top-k: metade do custo, todo o diagnóstico.
    DIAGNOSTICO = "diagnostico"
    # As quatro. Para o relatório final e para a análise de uma pergunta.
    COMPLETO = "completo"


@dataclass(frozen=True)
class Notas:
    """As notas de uma resposta. None significa "esta métrica não foi rodada"."""

    groundedness: float | None
    razao_groundedness: str | None
    relevance: float | None
    retrieval: float | None
    razao_retrieval: str | None
    correctness: float | None
    recuperou_os_documentos_esperados: bool
    recusou_como_esperado: bool


@dataclass(frozen=True)
class LinhaDoRelatorio:
    """Uma linha do relatório: o caso, o que o sistema respondeu e as notas."""

    caso: PerguntaAvaliada
    resposta: RespostaRag
    notas: Notas


@dataclass(frozen=True)
class Resumo:
    """O agregado de uma configuração inteira (um top-k, um modo)."""

    top_k: int
    modo: Modo
    groundedness: float | None
    relevance: float | None
    retrieval: float | None
    correctness: float | None
    acerto_de_recuperacao: float
    acerto_de_recusa: float
    tokens_de_entrada_medios: float
    latencia_media_s: float

    @staticmethod
    def de(top_k: int, modo: Modo, linhas: list[LinhaDoRelatorio]) -> "Resumo":
        def media(valores: list[float | None]) -> float | None:
            presentes = [v for v in valores if v is not None]
            return sum(presentes) / len(presentes) if presentes else None

        return Resumo(
            top_k=top_k,
            modo=modo,
            groundedness=media([l.notas.groundedness for l in linhas]),
            relevance=media([l.notas.relevance for l in linhas]),
            retrieval=media([l.notas.retrieval for l in linhas]),
            correctness=media([l.notas.correctness for l in linhas]),
            acerto_de_recuperacao=sum(l.notas.recuperou_os_documentos_esperados for l in linhas) / len(linhas),
            acerto_de_recusa=sum(l.notas.recusou_como_esperado for l in linhas) / len(linhas),
            tokens_de_entrada_medios=sum(l.resposta.tokens_de_entrada or 0 for l in linhas) / len(linhas),
            latencia_media_s=sum(l.resposta.latencia_s for l in linhas) / len(linhas),
        )


class AvaliadorRag:
    def __init__(self, account_endpoint: str, modelo_juiz: str, credential: TokenCredential) -> None:
        # O juiz é um deployment como qualquer outro. Ele PODE ser o mesmo modelo
        # que gerou a resposta — é o padrão aqui, por simplicidade — mas usar um
        # modelo diferente reduz o viés de um modelo se achar ótimo. A variável
        # FOUNDRY_JUDGE_MODEL existe justamente para trocar isso ao vivo.
        #
        # Sem api_key: os avaliadores autenticam por Entra ID com a credencial.
        config = AzureOpenAIModelConfiguration(azure_endpoint=account_endpoint, azure_deployment=modelo_juiz)
        self._groundedness = GroundednessEvaluator(config, credential=credential)
        self._relevance = RelevanceEvaluator(config, credential=credential)
        self._retrieval = RetrievalEvaluator(config, credential=credential)
        self._similarity = SimilarityEvaluator(config, credential=credential)

    def avaliar(
        self, caso: PerguntaAvaliada, resposta: RespostaRag, metricas: ConjuntoDeMetricas = ConjuntoDeMetricas.COMPLETO
    ) -> Notas:
        completo = metricas is ConjuntoDeMetricas.COMPLETO

        # As chamadas são independentes; rodar em paralelo é a diferença, numa
        # varredura de top-k, entre a turma esperar um minuto e esperar quatro.
        with ThreadPoolExecutor(max_workers=4) as pool:
            f_ground = pool.submit(
                self._groundedness, query=caso.pergunta, context=resposta.contexto, response=resposta.texto
            )
            f_retrieval = pool.submit(self._retrieval, query=caso.pergunta, context=resposta.contexto)
            f_relevance = pool.submit(self._relevance, query=caso.pergunta, response=resposta.texto) if completo else None
            f_similarity = (
                pool.submit(
                    self._similarity,
                    query=caso.pergunta,
                    response=resposta.texto,
                    ground_truth=caso.resposta_esperada,
                )
                if completo
                else None
            )

            ground = f_ground.result()
            retrieval = f_retrieval.result()
            relevance = f_relevance.result() if f_relevance else None
            similarity = f_similarity.result() if f_similarity else None

        return Notas(
            groundedness=_nota(ground, "groundedness"),
            razao_groundedness=ground.get("groundedness_reason"),
            relevance=_nota(relevance, "relevance"),
            retrieval=_nota(retrieval, "retrieval"),
            razao_retrieval=retrieval.get("retrieval_reason"),
            correctness=_nota(similarity, "similarity"),
            # --- As duas conferências que NÃO custam uma chamada de modelo ------
            # Rodam sempre, são determinísticas e, quando discordam do juiz,
            # costumam estar certas. Comece por elas ao investigar uma queda.
            recuperou_os_documentos_esperados=all(
                esperado.lower() in [d.lower() for d in resposta.documentos] for esperado in caso.documentos_esperados
            ),
            recusou_como_esperado=resposta.recusou == caso.deve_recusar,
        )

    def avaliar_conjunto(
        self,
        pipeline: PipelineRag,
        casos: list[PerguntaAvaliada],
        top_k: int,
        modo: Modo,
        metricas: ConjuntoDeMetricas,
        progresso: Callable[[str], None] | None = None,
    ) -> list[LinhaDoRelatorio]:
        """Roda o conjunto inteiro numa configuração. Três perguntas de cada vez:
        mais que isso e o deployment começa a devolver 429 — que é, por sinal,
        uma ótima deixa para falar de cota."""

        def avaliar_caso(indice_e_caso: tuple[int, PerguntaAvaliada]) -> LinhaDoRelatorio:
            i, caso = indice_e_caso
            resposta = pipeline.responder(caso.pergunta, top_k, modo)
            notas = self.avaliar(caso, resposta, metricas)
            if progresso:
                progresso(f"    {i + 1}/{len(casos)}  {encurtar(caso.pergunta, 58)}")
            return LinhaDoRelatorio(caso, resposta, notas)

        with ThreadPoolExecutor(max_workers=3) as pool:
            # map preserva a ordem do conjunto (fácil → difícil), deliberada.
            return list(pool.map(avaliar_caso, enumerate(casos)))


def _nota(resultado: dict | None, metrica: str) -> float | None:
    if resultado is None:
        return None
    valor = resultado.get(metrica)
    return float(valor) if valor is not None else None


def encurtar(texto: str, limite: int) -> str:
    linha = " ".join(texto.split())
    return linha if len(linha) <= limite else linha[: limite - 3] + "..."
