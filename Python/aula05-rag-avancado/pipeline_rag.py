"""
O pipeline RAG de ponta a ponta, numa chamada só: recuperar → montar contexto
→ gerar → devolver a resposta JUNTO COM tudo que a produziu.

A diferença desta aula para a anterior está no tipo de retorno. Na aula 4 a
resposta era escrita direto no console e desaparecia. Aqui ela volta como um
objeto que carrega também os trechos recuperados, o contexto exato que foi
enviado, a latência e os tokens.

Isso não é zelo de arquitetura: é o pré-requisito da avaliação. Não dá para
medir groundedness sem ter em mãos, ao mesmo tempo, a resposta e o contexto
que deveria sustentá-la.
"""

from __future__ import annotations

import time
from dataclasses import dataclass

from openai import OpenAI

from indice_rag import Chunk, IndiceRag, Modo

# A frase exata que o prompt manda usar quando a resposta não está no contexto.
# Compará-la com o texto devolvido é a checagem mais barata de groundedness que
# existe — e roda sem chamar modelo nenhum.
RECUSA = "Não encontrei essa informação nos documentos disponíveis."

# As duas instruções que carregam todo o peso: responder SÓ pelo contexto e
# admitir quando não sabe. A segunda é a que a avaliação de groundedness testa —
# e a que, sozinha, nem sempre basta.
INSTRUCAO = f"""\
Você responde perguntas sobre as políticas internas da empresa Aurora Log.
Use EXCLUSIVAMENTE os trechos numerados fornecidos como contexto.
Cite a fonte de cada afirmação no formato [n].
Se a resposta não estiver nos trechos, responda exatamente:
"{RECUSA}"
Não complete lacunas com conhecimento geral."""


@dataclass(frozen=True)
class TrechoCitado:
    """Um trecho recuperado, com o número que ele recebe na citação."""

    numero: int
    trecho: Chunk
    score: float | None


@dataclass(frozen=True)
class RespostaRag:
    """Tudo que uma pergunta produziu. É o insumo dos avaliadores e a linha do relatório."""

    pergunta: str
    texto: str
    trechos: list[TrechoCitado]
    contexto: str
    modo: Modo
    top_k: int
    latencia_s: float
    tokens_de_entrada: int | None  # cresce com o top-k: a metade "custo" do trade-off
    tokens_de_saida: int | None

    @property
    def documentos(self) -> list[str]:
        """Os documentos distintos que sustentaram a resposta."""
        vistos: dict[str, None] = {}
        for t in self.trechos:
            vistos.setdefault(t.trecho.documento, None)
        return list(vistos)

    @property
    def recusou(self) -> bool:
        return "não encontrei essa informação" in self.texto.lower()


class PipelineRag:
    def __init__(self, indice: IndiceRag, openai: OpenAI, modelo_de_chat: str) -> None:
        self._indice = indice
        self._openai = openai
        self._modelo = modelo_de_chat

    def responder(self, pergunta: str, top_k: int = 5, modo: Modo = Modo.HIBRIDA_COM_RERANKING) -> RespostaRag:
        inicio = time.perf_counter()

        achados = self._indice.buscar(pergunta, modo, top_k)
        trechos = [TrechoCitado(i + 1, trecho, score) for i, (trecho, score) in enumerate(achados)]

        # O contexto entra numerado. É isso que permite ao modelo citar [1], [2]
        # e ao usuário conferir. Sem numeração não há citação verificável.
        contexto = "\n\n".join(f"[{t.numero}] ({t.trecho.documento}) {t.trecho.conteudo}" for t in trechos)

        resposta = self._openai.responses.create(
            model=self._modelo,
            instructions=INSTRUCAO,
            input=f"Contexto:\n{contexto}\n\nPergunta: {pergunta}",
        )

        return RespostaRag(
            pergunta=pergunta,
            texto=resposta.output_text,
            trechos=trechos,
            contexto=contexto,
            modo=modo,
            top_k=top_k,
            latencia_s=time.perf_counter() - inicio,
            tokens_de_entrada=resposta.usage.input_tokens if resposta.usage else None,
            tokens_de_saida=resposta.usage.output_tokens if resposta.usage else None,
        )

    def responder_sem_rag(self, pergunta: str) -> RespostaRag:
        """A mesma pergunta SEM recuperação nenhuma. Serve de linha de base: é
        contra ela que se mede se o RAG ajudou — e, na pergunta de conhecimento
        geral, se ele só encareceu."""
        inicio = time.perf_counter()
        resposta = self._openai.responses.create(
            model=self._modelo,
            instructions="Você responde perguntas sobre políticas internas de empresas.",
            input=pergunta,
        )
        return RespostaRag(
            pergunta=pergunta,
            texto=resposta.output_text,
            trechos=[],
            contexto="",
            modo=Modo.KEYWORD,
            top_k=0,
            latencia_s=time.perf_counter() - inicio,
            tokens_de_entrada=resposta.usage.input_tokens if resposta.usage else None,
            tokens_de_saida=resposta.usage.output_tokens if resposta.usage else None,
        )
