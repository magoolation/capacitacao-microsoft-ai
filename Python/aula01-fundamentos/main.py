"""
Capacitação Microsoft AI — Aula 1: Fundamentos
Chat de console conversando com um modelo hospedado no Microsoft Foundry.

O que este exemplo demonstra:
  1. Como autenticar no Foundry usando identidade do Entra ID (sem chave).
  2. Como obter um cliente OpenAI já autenticado a partir do Foundry SDK.
  3. Como manter o contexto da conversa SEM guardar histórico no cliente.

As aulas 1 a 5 usam esta mesma montagem: o Foundry SDK (azure-ai-projects)
para chegar ao projeto com a identidade do Entra ID, e o cliente `openai`
como camada de programação. O que muda de uma aula para outra é o que se faz
com o cliente — não como ele é criado.
"""

import os

# azure-ai-projects: o "Foundry SDK". Dá acesso a tudo que é do projeto
# (modelos, agents, avaliações, conexões) por um único endpoint.
from azure.ai.projects import AIProjectClient

# azure-identity: descobre "quem é você" a partir do ambiente
# (az login, VS Code, identidade gerenciada em produção...).
from azure.identity import DefaultAzureCredential

# python-dotenv: carrega o .env para o ambiente. Em produção as variáveis
# viriam do App Service, Container Apps, Key Vault etc.
from dotenv import load_dotenv


def main() -> None:
    # -------------------------------------------------------------------------
    # 1. Configuração
    # -------------------------------------------------------------------------
    # Nada fica escrito no código: endpoint e modelo vêm de variáveis de ambiente.
    load_dotenv()

    # Endpoint DO PROJETO, no formato:
    #   https://<recurso>.services.ai.azure.com/api/projects/<projeto>
    endpoint = os.getenv("FOUNDRY_ENDPOINT")

    # Nome do DEPLOYMENT do modelo no Foundry — não é o nome comercial do modelo.
    model = os.getenv("FOUNDRY_MODEL")

    # Falhar cedo e com mensagem clara é melhor do que um KeyError lá na frente.
    if not endpoint or not model:
        print("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL no .env antes de executar.")
        return

    # -------------------------------------------------------------------------
    # 2. Autenticação
    # -------------------------------------------------------------------------
    # O Foundry SDK autentica SEMPRE por Entra ID — não existe construtor que
    # aceite chave de API. Não há segredo para vazar em código, log ou repositório.
    #
    # DefaultAzureCredential testa várias fontes de credencial em ordem e usa a
    # primeira que responder (variáveis de ambiente, identidade gerenciada,
    # az login, VS Code...). É o que faz o MESMO código funcionar na sua
    # máquina e no Azure sem alteração.
    #
    # IMPORTANTE: autenticar (provar quem você é) não é o mesmo que autorizar
    # (ter permissão). Sua conta precisa do papel "Foundry User" no recurso.
    # Ser Owner da subscription NÃO basta.
    with (
        DefaultAzureCredential() as credential,
        AIProjectClient(endpoint=endpoint, credential=credential) as project,
    ):
        # ---------------------------------------------------------------------
        # 3. Do Foundry SDK até o cliente OpenAI
        # ---------------------------------------------------------------------
        # get_openai_client() devolve um cliente do pacote `openai` já apontado
        # para o seu projeto e autenticado pelo Entra ID. Daqui para baixo é
        # Azure; daqui para cima o código é o mesmo que falaria com a OpenAI.
        openai = project.get_openai_client()

        # ---------------------------------------------------------------------
        # 4. Memória da conversa
        # ---------------------------------------------------------------------
        # Este é o ponto mais interessante do exemplo.
        #
        # Estamos na Responses API, e nela quem guarda a conversa é o SERVIÇO.
        # Cada resposta volta com um `id`; ao enviar a próxima pergunta você
        # devolve esse id em `previous_response_id` e o modelo recebe todo o
        # contexto. A única coisa que o programa lembra entre uma pergunta e
        # outra é uma string — não uma lista que cresce.
        #
        # Começa None porque a primeira pergunta não tem nada antes dela.
        previous_response_id: str | None = None

        # ---------------------------------------------------------------------
        # 5. Laço de conversa
        # ---------------------------------------------------------------------
        print("Chat iniciado. Digite sua pergunta (ou 'sair' para encerrar).\n")

        while True:
            prompt = input("Você: ").strip()

            # Enter vazio: não faz sentido gastar uma chamada.
            if not prompt:
                continue

            if prompt.lower() == "sair":
                break

            # A chamada de rede. Enviamos apenas a pergunta ATUAL mais o id da
            # resposta anterior — o histórico não trafega.
            response = openai.responses.create(
                model=model,
                input=prompt,
                previous_response_id=previous_response_id,
            )

            # Guardamos o id desta resposta: ele será o elo da próxima pergunta.
            # Remover esta linha faz o chat "esquecer" tudo a cada mensagem —
            # vale testar em sala para ver a diferença na prática.
            previous_response_id = response.id

            # A resposta pode conter vários itens de saída (texto, chamadas de
            # ferramenta...). output_text concatena só a parte textual.
            print(f"IA: {response.output_text}\n")


if __name__ == "__main__":
    main()
