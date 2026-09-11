"""
O conjunto de avaliação: as perguntas contra as quais o sistema é medido.

Este arquivo é o artefato mais subestimado de um projeto de RAG. Sem ele,
"melhorou" é opinião: alguém mexe no chunking, faz três perguntas na mão, acha
que ficou melhor e sobe. Com ele, "melhorou" é um número que se compara com o
da semana passada.

Seis perguntas bastam para uma aula. Em produção o conjunto começa com as
perguntas que os usuários realmente fizeram — e cresce toda vez que alguém
reclama de uma resposta.

A composição foi escolhida, não sorteada. Há três perguntas fáceis (um
documento, um chunk), uma que exige DOIS documentos ao mesmo tempo, uma que
NÃO tem resposta no corpus e uma cuja palavra-chave não aparece no texto.
Cada uma quebra o sistema de um jeito diferente, e é isso que faz a média
significar alguma coisa.
"""

from dataclasses import dataclass

from pipeline_rag import RECUSA


@dataclass(frozen=True)
class PerguntaAvaliada:
    """Uma pergunta com gabarito."""

    pergunta: str
    # O gabarito, em uma frase. É o "gold" do slide de métricas: sem ele dá
    # para medir groundedness e relevance, mas não dá para medir se a resposta
    # está CERTA. Uma resposta pode ser perfeitamente aterrada no trecho errado.
    resposta_esperada: str
    # Quais arquivos deveriam ter sido recuperados. Permite conferir a
    # recuperação sem chamar modelo nenhum — a checagem mais barata do conjunto.
    documentos_esperados: tuple[str, ...]
    # Verdadeiro quando a resposta CERTA é admitir que não sabe. Sem casos assim,
    # a avaliação premia um sistema que inventa com confiança.
    deve_recusar: bool
    # Por que esta pergunta está no conjunto.
    o_que_ensina: str


PERGUNTAS: list[PerguntaAvaliada] = [
    PerguntaAvaliada(
        pergunta="Qual é o teto de reembolso para jantar em viagem nacional?",
        resposta_esperada="R$ 90,00 por pessoa, por refeição.",
        documentos_esperados=("politica-de-reembolso.md",),
        deve_recusar=False,
        o_que_ensina="Caso fácil: um documento, um chunk, número explícito. "
        "Se esta falhar, o problema não é de ajuste fino — é de indexação.",
    ),
    PerguntaAvaliada(
        pergunta="Qual é o prazo de primeira resposta para um chamado de severidade 1?",
        resposta_esperada="30 minutos, em regime 24x7.",
        documentos_esperados=("sla-de-suporte.md",),
        deve_recusar=False,
        o_que_ensina="Caso fácil com termo técnico literal ('severidade 1'). "
        "É onde a busca por palavra-chave brilha e a vetorial não agrega.",
    ),
    PerguntaAvaliada(
        pergunta="Quantos dias por semana os times de engenharia precisam ir ao escritório?",
        resposta_esperada="Dois dias por semana, definidos pelo próprio time, com a presença medida por trimestre.",
        documentos_esperados=("politica-de-trabalho-remoto.md",),
        deve_recusar=False,
        o_que_ensina="Fácil, mas com uma ressalva importante no mesmo chunk (medição trimestral). "
        "Mede se a resposta é completa, não só correta.",
    ),
    PerguntaAvaliada(
        pergunta="Vou trabalhar de outra cidade por vinte dias, a serviço. "
        "Preciso de aprovação e qual o teto de diária de hotel?",
        resposta_esperada="Até trinta dias basta comunicar ao gestor, sem aprovação de RH. "
        "O teto de hospedagem é R$ 420,00 em capitais e R$ 310,00 nas demais cidades.",
        documentos_esperados=("politica-de-trabalho-remoto.md", "politica-de-reembolso.md"),
        deve_recusar=False,
        o_que_ensina="A pergunta que EXIGE dois documentos. É a que mais falha, e falha de um jeito "
        "instrutivo: os dois arquivos SÃO recuperados e mesmo assim a resposta sai pela metade. "
        "Recuperar não é o mesmo que usar.",
    ),
    PerguntaAvaliada(
        pergunta="O SMS ainda é aceito como segundo fator de autenticação?",
        resposta_esperada="Não. O SMS foi descontinuado como segundo fator em janeiro de 2025; "
        "valem aplicativo autenticador ou chave física.",
        documentos_esperados=("seguranca-e-acessos.md",),
        deve_recusar=False,
        o_que_ensina="A resposta correta é uma NEGATIVA que está no documento. "
        "Diferente de recusar por ausência — e modelos confundem as duas.",
    ),
    PerguntaAvaliada(
        pergunta="Qual é a política de reembolso para viagens internacionais?",
        resposta_esperada=RECUSA,
        documentos_esperados=("politica-de-reembolso.md",),
        deve_recusar=True,
        o_que_ensina="O caso que vale a aula. O assunto ESTÁ no corpus (o documento diz que viagens ao "
        "exterior seguem processo próprio, não publicado), mas a resposta NÃO está. "
        "A recuperação acerta e a geração é que precisa se conter.",
    ),
]

# Os valores de top-k varridos pela opção 5. Três pontos bastam para a curva
# aparecer: um apertado demais, um razoável e um exagerado.
TOP_KS_DA_VARREDURA = [3, 5, 10]
