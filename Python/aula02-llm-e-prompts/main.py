"""
Capacitação Microsoft AI — Aula 2: LLM e Prompts
Console em Python conversando com um modelo do Microsoft Foundry.

O que este exemplo demonstra:
  1. Como o histórico da conversa pode viver no CLIENTE, numa lista que cresce.
  2. Few-shot prompting: ensinar o formato da resposta por exemplos.
  3. Structured output: receber um objeto Pydantic tipado em vez de texto solto.

O setup (seções 1 a 3) é IDÊNTICO ao da aula 1: mesmo Foundry SDK, mesmo
endpoint, mesma identidade, mesmo cliente. Esta aula não troca de camada —
ela troca o que se faz com a camada.
"""

import os

from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv
from openai import OpenAI
from pydantic import BaseModel

# -----------------------------------------------------------------------------
# Dados de apoio
# -----------------------------------------------------------------------------
# Os mesmos três chamados atravessam as demos 2 e 3. Usar sempre a mesma
# entrada é o que torna a comparação entre as técnicas honesta.
CHAMADOS = [
    "Não consigo acessar o sistema, minha senha foi recusada três vezes seguidas.",
    "O relatório mensal está somando o total errado desde a atualização de ontem.",
    "Gostaria de sugerir um botão de exportar para Excel na tela de clientes.",
]


# -----------------------------------------------------------------------------
# O contrato da resposta (demo 3)
# -----------------------------------------------------------------------------
# Esta classe é, ao mesmo tempo, o tipo Python e a especificação enviada ao
# modelo: o SDK gera um JSON Schema a partir dela. Os nomes dos campos viram
# documentação para o modelo — por isso vale nomeá-los bem.
class TriagemChamado(BaseModel):
    categoria: str
    urgencia: str
    resumo: str
    requer_atencao_imediata: bool


# =============================================================================
# DEMO 1 — Chat simples: quem guarda o histórico?
# =============================================================================
# Contraste direto com a aula 1 — e repare que o cliente é o MESMO. Quem muda
# é a estratégia:
#
#   Aula 1: o SERVIÇO guarda a conversa (previous_response_id).
#   Aqui:   o CLIENTE guarda a conversa, numa lista que cresce e que inteira
#           trafega a cada pergunta. Nenhum previous_response_id é enviado.
#
# Nenhum dos dois é "melhor". O cliente guardando dá controle total sobre o que
# entra no contexto — e é exatamente esse controle que torna possível o
# few-shot da demo 2, onde inventamos um histórico que nunca aconteceu.
def chat_simples(openai: OpenAI, model: str) -> None:
    # A instrução de sistema define papel, tom e regras. Ela é reenviada em
    # todas as chamadas, por isso influencia a conversa inteira.
    instrucoes = (
        "Você é um assistente de suporte técnico. "
        "Responda em português do Brasil, de forma direta e objetiva."
    )
    conversa: list[dict[str, str]] = []

    print("\nChat iniciado. Digite sua pergunta (ou 'sair' para voltar ao menu).\n")

    while True:
        prompt = input("Você: ").strip()
        if not prompt:
            continue
        if prompt.lower() == "sair":
            return

        # Acumula a pergunta do usuário no histórico...
        conversa.append({"role": "user", "content": prompt})

        # ...e envia a lista INTEIRA. O modelo não tem memória entre chamadas:
        # o que parece memória é simplesmente esta lista sendo reenviada.
        response = openai.responses.create(
            model=model,
            instructions=instrucoes,
            input=conversa,
        )

        # Acumular a resposta do modelo também é obrigatório. Sem esta linha o
        # modelo esquece o que ele próprio disse — vale testar em sala.
        conversa.append({"role": "assistant", "content": response.output_text})

        print(f"IA: {response.output_text}\n")
        print(f"    [histórico: {len(conversa)} mensagens]\n")


# =============================================================================
# DEMO 2 — Zero-shot vs Few-shot
# =============================================================================
# A MESMA tarefa, o MESMO modelo, dois prompts diferentes.
#
#   Zero-shot: você descreve o que quer.
#   Few-shot:  você MOSTRA o que quer, com exemplos de pergunta e resposta.
#
# Few-shot não é um recurso da API — é uma técnica de prompt. Você monta uma
# conversa fictícia, na qual o assistente já respondeu no formato desejado, e
# entrega isso como se tivesse acontecido. O modelo continua o padrão.
def zero_shot_versus_few_shot(openai: OpenAI, model: str) -> None:
    # --- Zero-shot -----------------------------------------------------------
    # Só a instrução. O modelo entende a tarefa, mas escolhe o formato: às vezes
    # uma linha, às vezes três parágrafos. Correto e inutilizável ao mesmo
    # tempo, porque nenhum código consegue consumir isso de forma confiável.
    print("\n--- ZERO-SHOT (só a instrução) ---\n")

    for chamado in CHAMADOS:
        response = openai.responses.create(
            model=model,
            instructions="Classifique o chamado de suporte informado pelo usuário.",
            input=chamado,
        )
        print(f"Chamado: {chamado}")
        print(f"Modelo : {response.output_text}\n")

    # --- Few-shot ------------------------------------------------------------
    # Os exemplos entram como mensagens user/assistant alternadas, ANTES da
    # pergunta real. O assistente "já respondeu" no formato exato que queremos —
    # três exemplos bastam para fixar o padrão.
    exemplos = [
        {"role": "user", "content": "O sistema fechou sozinho quando cliquei em salvar."},
        {"role": "assistant", "content": "Erro | Alta | Aplicação encerra ao salvar."},
        {"role": "user", "content": "Como faço para trocar minha foto de perfil?"},
        {"role": "assistant", "content": "Dúvida | Baixa | Como alterar foto de perfil."},
        {"role": "user", "content": "Seria bom ter modo escuro na tela de lançamentos."},
        {"role": "assistant", "content": "Sugestão | Baixa | Modo escuro na tela de lançamentos."},
    ]

    print("--- FEW-SHOT (a instrução + 3 exemplos) ---\n")

    for chamado in CHAMADOS:
        # Copia os exemplos e acrescenta o chamado real ao final. Sem a cópia,
        # cada iteração poluiria a lista com os chamados anteriores.
        mensagens = [*exemplos, {"role": "user", "content": chamado}]

        response = openai.responses.create(
            model=model,
            instructions=(
                "Você classifica chamados de suporte. Responda SEMPRE em uma "
                "única linha, no formato: Categoria | Urgência | Resumo."
            ),
            input=mensagens,
        )
        print(f"Chamado: {chamado}")
        print(f"Modelo : {response.output_text}\n")

    print("Compare as duas saídas: mesma tarefa, mesmo modelo, formatos bem diferentes.")


# =============================================================================
# DEMO 3 — Structured output
# =============================================================================
# O few-shot deixou a saída consistente, mas ainda é texto: para usar em código
# você precisaria fazer split, strip e torcer para o modelo não variar.
#
# responses.parse resolve isso por outro caminho. O SDK:
#   1. gera um JSON Schema a partir da classe Pydantic;
#   2. envia esse schema junto com o prompt, pedindo resposta em JSON;
#   3. desserializa a resposta de volta para a classe.
#
# O modelo passa a ser obrigado pelo serviço a produzir JSON válido naquele
# formato. Não é o prompt pedindo com jeitinho — é uma restrição imposta na
# geração da resposta.
def structured_output(openai: OpenAI, model: str) -> None:
    print("\n--- STRUCTURED OUTPUT (classe Pydantic) ---\n")

    for chamado in CHAMADOS:
        # Repare que o prompt encolheu: não precisa mais descrever o formato,
        # listar campos nem dar exemplos. O schema da classe faz esse trabalho.
        response = openai.responses.parse(
            model=model,
            instructions="Você faz a triagem de chamados de suporte.",
            input=chamado,
            text_format=TriagemChamado,
        )

        # output_parsed é o objeto já desserializado e validado pelo Pydantic.
        triagem = response.output_parsed
        assert triagem is not None

        print(f"Chamado  : {chamado}")
        print(f"Categoria: {triagem.categoria}")
        print(f"Urgência : {triagem.urgencia}")
        print(f"Resumo   : {triagem.resumo}")
        print(f"Imediato : {triagem.requer_atencao_imediata}")
        print()

    print("A saída agora é um objeto Python: dá para gravar em banco, comparar, testar.")


# =============================================================================
# Menu
# =============================================================================
def main() -> None:
    load_dotenv()
    endpoint = os.getenv("FOUNDRY_ENDPOINT")
    model = os.getenv("FOUNDRY_MODEL")
    if not endpoint or not model:
        print("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL no .env antes de executar.")
        return

    # Setup idêntico ao da aula 1: Foundry SDK + Entra ID + cliente OpenAI.
    with (
        DefaultAzureCredential() as credential,
        AIProjectClient(endpoint=endpoint, credential=credential) as project,
    ):
        openai = project.get_openai_client()

        while True:
            print()
            print("=== Capacitação Microsoft AI — LLM e Prompts ===")
            print("1. Chat simples (histórico no cliente)")
            print("2. Zero-shot vs Few-shot (lista de mensagens)")
            print("3. Structured output (responses.parse com Pydantic)")
            print("4. Sair")

            match input("Opção: ").strip():
                case "1":
                    chat_simples(openai, model)
                case "2":
                    zero_shot_versus_few_shot(openai, model)
                case "3":
                    structured_output(openai, model)
                case "4" | "":
                    return
                case _:
                    print("Opção inválida.")


if __name__ == "__main__":
    main()
