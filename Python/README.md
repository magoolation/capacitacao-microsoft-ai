# Capacitação Microsoft AI — trilha Python

Exemplos em **Python 3.12+** das mesmas aulas da trilha .NET deste repositório. Cada aula é uma pasta independente, com o próprio `main.py`, `requirements.txt` (versões fixadas) e `.env.example`, e conversa com um modelo hospedado no **Microsoft Foundry** autenticando por **Entra ID** — sem chave de API em lugar nenhum.

| # | Aula | Tema central | Pasta |
|---|---|---|---|
| 1 | **Fundamentos** | Autenticação sem segredo, `AIProjectClient` → cliente OpenAI, contexto no serviço (`previous_response_id`) | [`aula01-fundamentos`](aula01-fundamentos/) |
| 2 | **LLM e Prompts** | Contexto no cliente (lista de mensagens), few-shot, structured output com Pydantic | [`aula02-llm-e-prompts`](aula02-llm-e-prompts/) |
| 3 | **Foundry SDK com streaming** | `stream=True`, resposta token a token, retry com `tenacity`, saída com `rich` | [`aula03-foundry-sdk`](aula03-foundry-sdk/) |
| 4 | **RAG com Azure AI Search** | Chunking, embeddings, keyword × vetorial × híbrida com reranking, resposta com citações | [`aula04-rag`](aula04-rag/) |
| 5 | **RAG end-to-end com avaliação** | Groundedness, relevance, retrieval, similarity; varredura de top-k; relatório em Markdown | [`aula05-rag-avancado`](aula05-rag-avancado/) |
| 6 | **Primeiro agente com o Agent Framework** | `Agent` + `FoundryChatClient`, `run` × `run(stream=True)`, `AgentSession` | [`aula06-agent-framework`](aula06-agent-framework/) |

As aulas 7 e 8 serão adicionadas junto com as versões .NET.

## O fio entre as aulas

As aulas 1, 2, 3, 4 e 5 entram pelo **mesmo** `AIProjectClient`, com a **mesma** identidade, pelo **mesmo** endpoint do projeto, e pedem ao Foundry SDK um cliente OpenAI já autenticado:

```python
project = AIProjectClient(endpoint=endpoint, credential=DefaultAzureCredential())
openai = project.get_openai_client()
```

O que muda é o que cada aula faz com `openai`:

| | Aula 1 | Aula 2 | Aula 3 | Aulas 4 e 5 |
|---|---|---|---|---|
| Quem guarda o histórico | O **serviço** (`previous_response_id`) | O **cliente** (`list[dict]`) | O **serviço** | O cliente monta o contexto a cada pergunta |
| Método | `responses.create` | `responses.create` / `responses.parse` | `responses.create(stream=True)` | `embeddings.create` + `responses.create` |
| Resposta | Inteira | Inteira / objeto Pydantic | Token a token | Com citações `[n]` |

A aula 6 troca o cliente OpenAI por um `Agent` do **Microsoft Agent Framework** sobre o mesmo endpoint do projeto.

### Uma diferença importante em relação ao .NET

Na trilha .NET, as aulas 4 e 5 precisam sair do `Azure.AI.Projects` e usar o `OpenAIClient` direto, com o endpoint `/openai/v1`, porque `Microsoft.Extensions.AI` e `Azure.AI.*` exigem versões diferentes do pacote `OpenAI` (a regra "um projeto = um pivô"). **Em Python esse problema não existe**: `azure-ai-projects`, `openai`, `azure-search-documents` e `azure-ai-evaluation` convivem no mesmo ambiente virtual, e **um único `FOUNDRY_ENDPOINT` serve as seis aulas**. O que continua valendo, nas duas trilhas, é a outra regra: **fixe as versões**. Sem pin, o aluno instala semanas depois e não reproduz a demo.

## Pré-requisitos comuns

| Item | Detalhe |
|---|---|
| **Python 3.12+** | `python --version` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de rodar |
| **Recurso Foundry** | Com um modelo de chat implantado |
| **Papel `Foundry User`** | No recurso Foundry, para cada aluno — ser `Owner` da subscription **não** basta |

As **aulas 4 e 5** pedem, além disso: um segundo deployment de **embeddings** no mesmo recurso; um serviço **Azure AI Search** em SKU `basic` ou superior (o `free` não tem ranqueamento semântico); e os papéis `Search Service Contributor` e `Search Index Data Contributor`. O script de provisionamento da aula 4 da trilha .NET (`CapacitacaoMicrosoftAIRag/scripts/`) serve para as duas trilhas.

## Como construir (qualquer aula)

```bash
cd Python/aula01-fundamentos
python -m venv .venv
# Windows: .venv\Scripts\activate    Linux/macOS: source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env      # e preencha FOUNDRY_ENDPOINT / FOUNDRY_MODEL
az login --tenant <tenant-id>
python main.py
```

Sempre dentro do venv: `pip list` mostra o que está realmente instalado.

## Variáveis de ambiente

| Variável | Aulas | Valor |
|---|---|---|
| `FOUNDRY_ENDPOINT` | todas | `https://<recurso>.services.ai.azure.com/api/projects/<projeto>` |
| `FOUNDRY_MODEL` | todas | nome do **deployment** do modelo de chat |
| `FOUNDRY_EMBEDDING_MODEL` | 4, 5 | nome do deployment de embeddings (1536 dimensões: `text-embedding-3-small`) |
| `FOUNDRY_JUDGE_MODEL` | 5 | deployment do modelo juiz (opcional; padrão: `FOUNDRY_MODEL`) |
| `SEARCH_ENDPOINT` | 4, 5 | `https://<servico>.search.windows.net` |
| `SEARCH_INDEX` | 4, 5 | nome do índice (padrão: `politicas-internas`) |
| `AZURE_TENANT_ID` | todas | GUID do tenant — evita o `DefaultAzureCredential` escolher o tenant errado |

## Solução de problemas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `403` ao chamar o modelo, mesmo sendo Owner | Falta o papel **Foundry User** (plano de dados) no recurso | Peça a atribuição; Owner só cobre o plano de gerenciamento |
| `404` no endpoint | `FOUNDRY_ENDPOINT` não é o endpoint do **projeto** | Confira o formato `.../api/projects/<projeto>` |
| Credencial escolhe o tenant errado | Vários tenants no `az login` | Defina `AZURE_TENANT_ID` no `.env` |
| A demo funcionava e parou de funcionar | Instalou sem pin ou fora do venv | `pip install -r requirements.txt` dentro do venv |
