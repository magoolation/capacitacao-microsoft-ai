"""
A metade "R" do RAG: criar o índice, indexar o corpus e recuperar trechos.

Tudo que é específico do Azure AI Search mora aqui. O main.py só pede
"me traga os trechos relevantes para esta pergunta" e monta o prompt.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Callable

from azure.core.credentials import TokenCredential
from azure.core.exceptions import ResourceNotFoundError
from azure.search.documents import SearchClient
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import (
    HnswAlgorithmConfiguration,
    SearchableField,
    SearchField,
    SearchFieldDataType,
    SearchIndex,
    SemanticConfiguration,
    SemanticField,
    SemanticPrioritizedFields,
    SemanticSearch,
    SimpleField,
    VectorSearch,
    VectorSearchProfile,
)
from azure.search.documents.models import QueryType, VectorizedQuery
from openai import OpenAI

# Nomes usados na definição do índice.
PERFIL_VETORIAL = "perfil-vetorial"
ALGORITMO_VETORIAL = "hnsw-padrao"
CONFIGURACAO_SEMANTICA = "semantica-padrao"

# Dimensão do vetor. text-embedding-3-small e text-embedding-ada-002 usam 1536;
# text-embedding-3-large usa 3072. Trocar o modelo exige recriar o índice — o
# serviço rejeita vetores de tamanho diferente do declarado.
DIMENSOES_DO_EMBEDDING = 1536


# -----------------------------------------------------------------------------
# O documento como ele vive no índice
# -----------------------------------------------------------------------------
# Um "chunk" é um pedaço de um documento, não o documento inteiro. É essa a
# unidade que é vetorizada, recuperada e citada — por isso ele carrega, além do
# texto, de onde veio (documento, titulo) e a própria representação vetorial.
@dataclass
class Chunk:
    id: str            # chave do índice: só letras, dígitos, _, - e =
    documento: str     # nome do arquivo de origem — é o que aparece na citação
    titulo: str        # título do documento, para dar contexto ao trecho
    url: str           # URL fictícia; num caso real, o link na intranet
    conteudo: str      # o texto do trecho — é isto que vai para o modelo
    vetor: list[float] = field(default_factory=list)  # o embedding do conteúdo


class Modo(Enum):
    """Como recuperar."""

    KEYWORD = "keyword"    # só palavras-chave (BM25): acerta o literal, erra o sinônimo
    VETORIAL = "vetorial"  # só vetorial: acerta o sinônimo, erra o código exato
    HIBRIDA = "hibrida"    # as duas, reordenadas pelo ranqueador semântico


class IndiceRag:
    def __init__(
        self,
        search_endpoint: str,
        credential: TokenCredential,
        openai: OpenAI,
        modelo_de_embedding: str,
        nome_do_indice: str,
    ) -> None:
        # Azure AI Search precisa de DOIS papéis distintos:
        #   Search Service Contributor    → criar e apagar índices
        #   Search Index Data Contributor → gravar e ler documentos
        self._index_client = SearchIndexClient(search_endpoint, credential)
        self._search_client = SearchClient(search_endpoint, nome_do_indice, credential)
        self._openai = openai
        self._modelo_de_embedding = modelo_de_embedding
        self._nome_do_indice = nome_do_indice

    # -------------------------------------------------------------------------
    # 1. Criação do índice
    # -------------------------------------------------------------------------
    # O índice declara três coisas independentes: os CAMPOS, como fazer busca
    # VETORIAL (algoritmo + perfil) e como fazer reordenação SEMÂNTICA (quais
    # campos representam título e conteúdo). Busca keyword não precisa de
    # configuração: sai de graça de todo campo marcado como pesquisável.
    def criar_ou_atualizar_indice(self) -> None:
        campos = [
            SimpleField(name="id", type=SearchFieldDataType.String, key=True, filterable=True),
            SearchableField(name="documento", type=SearchFieldDataType.String, filterable=True, facetable=True),
            # O analisador em português importa para a metade keyword da busca
            # híbrida: é ele que faz "reembolsos" casar com "reembolso".
            SearchableField(name="titulo", type=SearchFieldDataType.String, analyzer_name="pt-BR.microsoft"),
            SimpleField(name="url", type=SearchFieldDataType.String),
            SearchableField(name="conteudo", type=SearchFieldDataType.String, analyzer_name="pt-BR.microsoft"),
            # hidden=True: a aplicação nunca lê o vetor de volta. Trazer 1536
            # floats por resultado seria desperdício puro.
            SearchField(
                name="vetor",
                type=SearchFieldDataType.Collection(SearchFieldDataType.Single),
                searchable=True,
                hidden=True,
                vector_search_dimensions=DIMENSOES_DO_EMBEDDING,
                vector_search_profile_name=PERFIL_VETORIAL,
            ),
        ]

        indice = SearchIndex(
            name=self._nome_do_indice,
            fields=campos,
            vector_search=VectorSearch(
                algorithms=[HnswAlgorithmConfiguration(name=ALGORITMO_VETORIAL)],
                profiles=[VectorSearchProfile(name=PERFIL_VETORIAL, algorithm_configuration_name=ALGORITMO_VETORIAL)],
            ),
            semantic_search=SemanticSearch(
                configurations=[
                    SemanticConfiguration(
                        name=CONFIGURACAO_SEMANTICA,
                        prioritized_fields=SemanticPrioritizedFields(
                            title_field=SemanticField(field_name="titulo"),
                            content_fields=[SemanticField(field_name="conteudo")],
                        ),
                    )
                ]
            ),
        )

        self._index_client.create_or_update_index(indice)

    def apagar_indice(self) -> None:
        try:
            self._index_client.delete_index(self._nome_do_indice)
        except ResourceNotFoundError:
            pass  # índice já não existe: apagar de novo não é erro

    # -------------------------------------------------------------------------
    # 2. Chunking
    # -------------------------------------------------------------------------
    # A estratégia mais simples que funciona: quebrar por parágrafo e juntar
    # parágrafos vizinhos até chegar perto de um tamanho-alvo.
    #
    # Chunk grande demais dilui o sinal — o embedding vira a média de vários
    # assuntos. Pequeno demais perde o contexto que dá sentido à frase. O número
    # abaixo existe para ser alterado ao vivo: é o experimento mais barato e
    # mais instrutivo desta aula.
    @staticmethod
    def fatiar(texto: str, alvo_de_caracteres: int = 800) -> list[str]:
        paragrafos = [p.strip() for p in texto.split("\n\n") if p.strip()]
        pedacos: list[str] = []
        atual = ""

        for paragrafo in paragrafos:
            # Fecha o chunk atual antes de estourar o alvo.
            if atual and len(atual) + len(paragrafo) > alvo_de_caracteres:
                pedacos.append(atual)
                atual = ""
            atual = f"{atual}\n\n{paragrafo}" if atual else paragrafo

        if atual:
            pedacos.append(atual)
        return pedacos

    # -------------------------------------------------------------------------
    # 3. Embeddings
    # -------------------------------------------------------------------------
    def _embeddings(self, textos: list[str]) -> list[list[float]]:
        # Um lote só: bem mais rápido que uma chamada por chunk.
        resposta = self._openai.embeddings.create(model=self._modelo_de_embedding, input=textos)
        return [item.embedding for item in resposta.data]

    # -------------------------------------------------------------------------
    # 4. Indexação
    # -------------------------------------------------------------------------
    # O embedding é gerado UMA vez por chunk, na indexação. Na hora da pergunta,
    # gera-se só o embedding da pergunta. É essa assimetria que torna RAG barato
    # em tempo de consulta.
    def indexar(self, pasta_do_corpus: Path, log: Callable[[str], None] | None = None) -> int:
        chunks: list[Chunk] = []

        for caminho in sorted(pasta_do_corpus.glob("*.md")):
            texto = caminho.read_text(encoding="utf-8")

            # Primeira linha "# Título" vira o título do documento.
            primeira_linha = texto.split("\n", 1)[0].lstrip("# ").strip()

            pedacos = self.fatiar(texto)
            if log:
                log(f"  {caminho.name:<34} {len(pedacos)} chunk(s)")

            for i, pedaco in enumerate(pedacos):
                chunks.append(
                    Chunk(
                        id=f"{caminho.stem}-{i:03d}",  # a chave não aceita ponto nem acento
                        documento=caminho.name,
                        titulo=primeira_linha,
                        url=f"https://intranet.auroralog.example/politicas/{caminho.name}",
                        conteudo=pedaco,
                    )
                )

        vetores = self._embeddings([c.conteudo for c in chunks])
        for chunk, vetor in zip(chunks, vetores):
            chunk.vetor = vetor

        self._search_client.upload_documents([c.__dict__ for c in chunks])
        return len(chunks)

    # -------------------------------------------------------------------------
    # 5. Recuperação
    # -------------------------------------------------------------------------
    def buscar(self, pergunta: str, modo: Modo, top_k: int = 4) -> list[tuple[Chunk, float | None]]:
        parametros: dict = {
            "top": top_k,
            # Sem isto, o campo `vetor` viria de volta em todo resultado.
            "select": ["id", "documento", "titulo", "conteudo", "url"],
        }

        # A metade vetorial: precisa do embedding DA PERGUNTA.
        if modo in (Modo.VETORIAL, Modo.HIBRIDA):
            vetor_da_pergunta = self._embeddings([pergunta])[0]
            parametros["vector_queries"] = [
                VectorizedQuery(vector=vetor_da_pergunta, k_nearest_neighbors=top_k, fields="vetor")
            ]

        # A metade semântica só existe no modo híbrido.
        if modo is Modo.HIBRIDA:
            parametros["query_type"] = QueryType.SEMANTIC
            parametros["semantic_configuration_name"] = CONFIGURACAO_SEMANTICA

        # No modo puramente vetorial não se envia texto de busca: o None é que
        # diz ao serviço "ignore BM25, use só o vetor".
        texto_de_busca = None if modo is Modo.VETORIAL else pergunta

        resultados: list[tuple[Chunk, float | None]] = []
        for item in self._search_client.search(search_text=texto_de_busca, **parametros):
            chunk = Chunk(
                id=item["id"],
                documento=item["documento"],
                titulo=item["titulo"],
                url=item["url"],
                conteudo=item["conteudo"],
            )
            # No modo híbrido o score que interessa é o do reranker semântico.
            score = item.get("@search.reranker_score") or item.get("@search.score")
            resultados.append((chunk, score))

        return resultados
