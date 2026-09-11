# Capacitação Microsoft AI

Exemplos do treinamento em **.NET** e em **Python**. Cada aula é uma **solução .NET independente**, com seu próprio README, e tem a versão Python correspondente na pasta [`Python/`](Python/). Todas conversam com um modelo hospedado no **Microsoft Foundry** autenticando por **Entra ID**.

As aulas 1 a 3 usam a mesma montagem: o **Foundry SDK** (`Azure.AI.Projects`) para chegar ao projeto com a identidade do Entra ID, e **`Microsoft.Extensions.AI`** (`IChatClient`) como camada de programação. Mesmo endpoint, mesmos pacotes, mesmo pivô de `OpenAI` — o que muda de uma aula para outra é o que se faz com o `IChatClient`. As aulas **4 e 5** são o caso à parte: elas trocam o `Azure.AI.Projects` pelo `OpenAIClient` direto, e por isso pedem um endpoint e um pivô próprios — veja [Matriz de versões](#matriz-de-versões). A aula **6** volta ao `AIProjectClient` e ao endpoint do projeto, mas deixa o **Microsoft Agent Framework** escolher todas as versões: o `.csproj` declara só `Microsoft.Agents.AI.Foundry` e `Azure.Identity`.

> As soluções continuam separadas para que cada aluno abra uma aula por vez no Visual Studio, mas o grafo de pacotes hoje é idêntico nas aulas 1 a 3, e as aulas 4 e 5 compartilham o seu. Veja [Matriz de versões](#matriz-de-versões).

---

## Aulas

| # | Aula | Tema central | Projeto |
|---|---|---|---|
| 1 | **Fundamentos** | Autenticação sem segredo, do Foundry SDK ao `IChatClient`, contexto no serviço | [`CapacitacaoMicrosoftAIFundamentos`](CapacitacaoMicrosoftAIFundamentos/) |
| 2 | **LLM e Prompts** | Contexto no cliente, few-shot, structured output | [`CapacitacaoMicrosoftAILLMePrompts`](CapacitacaoMicrosoftAILLMePrompts/) |
| 3 | **Foundry SDK com streaming** | `GetStreamingResponseAsync`, resposta token a token | [`CapacitacaoMicrosoftAIFoundrySDK`](CapacitacaoMicrosoftAIFoundrySDK/) |
| 4 | **RAG com Azure AI Search** | Chunking, embeddings, busca híbrida, resposta com citações | [`CapacitacaoMicrosoftAIRag`](CapacitacaoMicrosoftAIRag/) |
| 5 | **RAG end-to-end com avaliação** | Groundedness, relevance, retrieval, varredura de top-k, relatório | [`CapacitacaoMicrosoftAIRagAvancado`](CapacitacaoMicrosoftAIRagAvancado/) |
| 6 | **Primeiro agente com o Agent Framework** | `AsAIAgent`, `AgentSession`, `RunAsync` × `RunStreamingAsync`, um só dono do grafo de pacotes | [`CapacitacaoMicrosoftAIAgentFramework`](CapacitacaoMicrosoftAIAgentFramework/) |

A versão Python de cada aula está em `Python/aula0N-<nome>/` — veja [Trilha Python](#trilha-python).

---

## Trilha Python

A pasta [`Python/`](Python/) traz as mesmas aulas em **Python 3.12+**, com a mesma pedagogia: uma pasta por aula, `main.py`, `requirements.txt` com versões fixadas e `.env.example`.

| # | Aula | Pasta |
|---|---|---|
| 1 | Fundamentos — `AIProjectClient`, cliente OpenAI, contexto no serviço (`previous_response_id`) | [`Python/aula01-fundamentos`](Python/aula01-fundamentos/) |
| 2 | LLM e Prompts — contexto no cliente, few-shot, structured output com Pydantic | [`Python/aula02-llm-e-prompts`](Python/aula02-llm-e-prompts/) |
| 3 | Foundry SDK com streaming — `stream=True`, retry com `tenacity`, `rich`, `tiktoken` | [`Python/aula03-foundry-sdk`](Python/aula03-foundry-sdk/) |
| 4 | RAG com Azure AI Search — chunking, embeddings, keyword × vetorial × híbrida | [`Python/aula04-rag`](Python/aula04-rag/) |
| 5 | RAG end-to-end com avaliação — `azure-ai-evaluation`, varredura de top-k, relatório | [`Python/aula05-rag-avancado`](Python/aula05-rag-avancado/) |
| 6 | Primeiro agente com o Agent Framework — `Agent` + `FoundryChatClient`, `AgentSession` | [`Python/aula06-agent-framework`](Python/aula06-agent-framework/) |

**A diferença que vale a aula:** em Python não existe o problema do pivô `OpenAI` descrito em [Matriz de versões](#matriz-de-versões). `azure-ai-projects`, `openai`, `azure-search-documents`, `azure-ai-evaluation` e `agent-framework` convivem no mesmo ambiente virtual, e **um único `FOUNDRY_ENDPOINT` (o do projeto) serve todas as aulas** — a versão Python das aulas 4 e 5 usa o mesmo `AIProjectClient` das anteriores. O que continua valendo nas duas trilhas é a outra regra: **fixe as versões**. Detalhes, variáveis e solução de problemas no [README da trilha](Python/README.md).

---

## Slides

As aulas desta trilha estão em PDF em [`Slides/`](Slides/) — incluindo o slide "Como construir o projeto da aula" e os cinco pares pergunta/resposta do quiz de cada aula.

---

## Índice dos exemplos

### Aula 1 — Fundamentos

📖 [README da aula](CapacitacaoMicrosoftAIFundamentos/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAIFundamentos/CapacitacaoMicrosoftAIFundamentos/Program.cs)

| Exemplo | O que demonstra |
|---|---|
| **Chat de console** | Autenticação por Entra ID sem chave de API, a ponte `AsIChatClient()` entre o Foundry SDK e `Microsoft.Extensions.AI`, e **contexto mantido pelo serviço** — o programa guarda apenas um `ChatOptions.ConversationId` |

Leitura obrigatória antes de rodar qualquer aula: [Permissões (RBAC)](CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar). Ser `Owner` da subscription **não** basta — é preciso o papel `Foundry User`.

### Aula 2 — LLM e Prompts

📖 [README da aula](CapacitacaoMicrosoftAILLMePrompts/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAILLMePrompts/CapacitacaoMicrosoftAILLMePrompts/Program.cs)

Três demos sobre **os mesmos três chamados de suporte** — a repetição da entrada é o que torna a comparação entre as técnicas honesta.

| # | Exemplo | O que demonstra |
|---|---|---|
| 1 | Chat simples | **Contexto mantido pelo cliente**, numa `List<ChatMessage>` que cresce — a estratégia oposta à da aula 1, com o mesmo cliente |
| 2 | Zero-shot vs Few-shot | A mesma tarefa com e sem exemplos. Zero-shot devolve markdown livre; few-shot devolve `Acesso \| Alta \| ...` |
| 3 | Structured output | `GetResponseAsync<T>()` com um `record` — a saída vira objeto C#, não texto |

### Aula 3 — Foundry SDK com streaming

📖 [README da aula](CapacitacaoMicrosoftAIFoundrySDK/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAIFoundrySDK/CapacitacaoMicrosoftAI.FoundrySDK/Program.cs)

| Exemplo | O que demonstra |
|---|---|
| **Chat com streaming** | O mesmo setup da aula 1, com a resposta chegando token a token em `ChatResponseUpdate` — a diferença cabe no laço final |

### Aula 4 — RAG com Azure AI Search

📖 [README da aula](CapacitacaoMicrosoftAIRag/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAIRag/CapacitacaoMicrosoftAIRag/Program.cs) · 🔧 [scripts de provisionamento](CapacitacaoMicrosoftAIRag/scripts/)

Um corpus de políticas internas de uma empresa fictícia, escrito de propósito para que algumas perguntas só ele responda — e para que **uma pergunta pareça estar nele e não esteja**.

| # | Exemplo | O que demonstra |
|---|---|---|
| 1 | Indexação | Chunking por parágrafo, embeddings em lote e upload para o índice |
| 2 | Três modos de busca | Keyword, vetorial e híbrida com reranking, lado a lado e com os scores à vista |
| 3 | Com × sem RAG | A mesma pergunta nos dois modos, no mesmo modelo, com citações verificáveis |

O [roteiro de sala](CapacitacaoMicrosoftAIRag/README.md#o-roteiro-de-sala) tem três perguntas em ordem deliberada: a que só o corpus responde, a de conhecimento geral (onde RAG não ajuda) e a que parece estar no corpus mas não está.

O script `provisionar-ai-search.ps1` (e o `.sh` equivalente) cria o serviço de busca e concede os dois papéis necessários.

### Aula 5 — RAG end-to-end com avaliação

📖 [README da aula](CapacitacaoMicrosoftAIRagAvancado/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAIRagAvancado/CapacitacaoMicrosoftAIRagAvancado/Program.cs)

O mesmo corpus e o mesmo índice da aula 4 — o que muda é que agora tudo é **medido**.

| # | Exemplo | O que demonstra |
|---|---|---|
| 1 | Pipeline avaliável | `ResponderAsync` devolve resposta **e** trechos, contexto, latência e tokens — sem isso não há o que medir |
| 2 | Quatro métricas | Groundedness, relevance, retrieval e correctness, cada uma comparando um par diferente de coisas |
| 3 | Varredura de top-k | 3 × 5 × 10 com qualidade e custo lado a lado |
| 4 | Com × sem reranking | Quanto o ranqueador semântico ganha **no seu corpus** |
| 5 | Relatório em Markdown | O entregável do desafio da aula |

A aula acrescenta dois pacotes ao grafo da aula 4 — `Microsoft.Extensions.AI.Evaluation` e `.Quality` — e **o pivô do `OpenAI` não muda**, porque os avaliadores falam com o modelo por `IChatClient` e não dependem de SDK de provedor nenhum.

### Aula 6 — Primeiro agente com o Microsoft Agent Framework

📖 [README da aula](CapacitacaoMicrosoftAIAgentFramework/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAIAgentFramework/CapacitacaoMicrosoftAIAgentFramework/Program.cs)

O mesmo `AIProjectClient` das aulas 1 a 3 — com um agente do outro lado, em vez de um `IChatClient`.

| # | Exemplo | O que demonstra |
|---|---|---|
| 1 | `AsAIAgent` | Modelo + nome + instructions viram um `AIAgent` em uma chamada; nada é criado no serviço |
| 2 | `RunAsync` sem sessão | Duas perguntas encadeadas — o agente **não** lembra da primeira |
| 3 | `AgentSession` + `RunStreamingAsync` | A mesma sessão em todas as chamadas dá memória; a resposta chega token a token |

A aula declara **dois pacotes** e deixa o Agent Framework ser o único dono do grafo — `Azure.AI.Projects` e `Microsoft.Extensions.AI*` chegam transitivamente, nas versões que o MAF testou. Declarar qualquer um deles à mão recria o `MissingMethodException`.

---

## O fio entre as aulas

As aulas 1 a 3 chamam o **mesmo modelo**, no **mesmo recurso**, com a **mesma identidade**, pelo **mesmo endpoint**, com os **mesmos pacotes**. Estas linhas são idênticas nos três `Program.cs`:

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);
```

O que muda é o que cada aula faz com `chatClient`:

| | Aula 1 | Aula 2 | Aula 3 |
|---|---|---|---|
| Setup (as linhas acima) | — | **idêntico** | **idêntico** |
| Endpoint | `.../api/projects/<projeto>` | **idêntico** | **idêntico** |
| Pivô do pacote `OpenAI` | 2.9.1 | **idêntico** | **idêntico** |
| Quem guarda o histórico | O **serviço** | O **cliente** | O **serviço** |
| O que se envia | Pergunta + `ConversationId` | `List<ChatMessage>` inteira | Pergunta + `ConversationId` |
| Método chamado | `GetResponseAsync` | `GetResponseAsync` / `<T>` | `GetStreamingResponseAsync` |
| Resposta | Inteira, de uma vez | Inteira, de uma vez | Token a token |

A **aula 4** sai desse fio de propósito: para usar o `Microsoft.Extensions.AI` mais novo ela não pode carregar o `Azure.AI.Projects` no mesmo `.csproj`, e monta o `OpenAIClient` direto sobre o mesmo recurso Foundry.

> **Um único `FOUNDRY_ENDPOINT` serve as aulas 1, 2 e 3**, porque as três entram pelo `AIProjectClient` e pedem o endpoint do projeto: `.../api/projects/<projeto>`. **A aula 4 pede outro caminho no mesmo recurso**, `.../openai/v1`, que é o que o `OpenAIClient` entende. Não são dois recursos — é o mesmo, por duas portas. Reaproveitar o `launchSettings.json` de uma aula na outra quebra: o endpoint do projeto na aula 4 dá `400 Missing required query parameter: api-version`.

---

## Matriz de versões

Verificada nos `.nuspec` do NuGet. **Todo o problema de compatibilidade deste stack cabe em uma frase:** `Microsoft.Extensions.AI` e os pacotes `Azure.AI.*` consomem o mesmo pacote `OpenAI`, que **quebra compatibilidade binária a cada minor do 2.x mantendo a mesma assembly version**. O NuGet unifica para a maior versão, e o assembly compilado contra a menor estoura em runtime — sem erro de compilação.

Como as aulas 1 a 3 misturam os dois stacks no mesmo `.csproj`, **as três dependem de escolher versões que apontem para o mesmo pivô**. Este é o conjunto, idêntico nas três soluções:

| Pacote declarado | Versão | Pivô `OpenAI` |
|---|---|---|
| `Azure.AI.Projects` | 2.0.1 | **2.9.1** |
| `Azure.AI.Extensions.OpenAI` | 2.0.0 | 2.9.1 |
| `Microsoft.Extensions.AI` | 10.4.1 | — |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | 2.9.1 |
| `Azure.Identity` | 1.21.0 | — |

A **aula 4** resolve o mesmo problema pelo outro lado: em vez de prender o `Microsoft.Extensions.AI` na 10.4.1 para acompanhar o Foundry SDK, ela **tira o `Azure.AI.Projects` do `.csproj`** e sobe o `Microsoft.Extensions.AI` para a 10.9.0. O `Azure.Search.Documents` é neutro — depende só do `Azure.Core` — então convive com qualquer pivô:

| Pacote declarado | Versão | Pivô `OpenAI` |
|---|---|---|
| `Microsoft.Extensions.AI` | 10.9.0 | — |
| `Microsoft.Extensions.AI.OpenAI` | 10.9.0 | **[2.12.0, 2.13.0)** |
| `Azure.Search.Documents` | 12.0.0 | — (não depende de `OpenAI`) |
| `Azure.Identity` | 1.21.0 | — |

É por isso que a aula 4 usa o `OpenAIClient` e o endpoint `/openai/v1`: sem o `Azure.AI.Projects` não há `AIProjectClient`. Somar os dois no mesmo `.csproj` faz o NuGet unificar em 2.12.0, e o Foundry SDK, compilado contra a 2.9.1, quebra em runtime.

A **aula 5** herda esse grafo e acrescenta dois pacotes — **sem mexer no pivô**:

| Pacote declarado | Versão | Pivô `OpenAI` |
|---|---|---|
| `Microsoft.Extensions.AI.Evaluation` | 10.9.0 | — (só `Microsoft.Extensions.AI.Abstractions`) |
| `Microsoft.Extensions.AI.Evaluation.Quality` | 10.9.0 | — (só `Evaluation`, na versão exata) |

Vale reparar no motivo, porque ele é o argumento prático a favor da abstração: **os avaliadores falam com o modelo por `IChatClient`**, e por isso não dependem de SDK de provedor nenhum. Um pacote que só conhece a abstração não tem como brigar pelo pivô.

A **aula 6** resolve o problema de um terceiro jeito: **delega a escolha**. `Microsoft.Agents.AI.Foundry` declara `Azure.AI.Projects`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI` e `OpenAI` nas versões com que foi compilado; o `.csproj` só declara ele e o `Azure.Identity`. O pacote ainda é prerelease, por isso o `.csproj` usa `1.*-*` — fixe a versão resolvida (`dotnet list package`) antes de distribuir para a turma.

| Pacote declarado | Versão | Pivô `OpenAI` |
|---|---|---|
| `Microsoft.Agents.AI.Foundry` | prerelease (`1.*-*`) | o que o MAF trouxer — **não declare outro** |
| `Azure.Identity` | 1.21.0 | — |

**A regra:** um `.csproj` = um pivô de `OpenAI`. Misturar `Azure.AI.*` com `Microsoft.Extensions.AI.OpenAI` é possível — é o que fazemos nas aulas 1 a 3 — mas só conferindo os dois pinos antes. Quando um framework já fez essa conferência, como na aula 6, não a refaça por cima dele.

O pino de `OpenAI` por versão do `Microsoft.Extensions.AI.OpenAI` é o que amarra a escolha:

| Versão | Exige `OpenAI` |
|---|---|
| 10.3.0 | 2.8.0 |
| **10.4.1** | **2.9.1** — a única compatível com o Foundry SDK GA |
| 10.5.0 … 10.6.0 | 2.10.0 |
| 10.9.0 | [2.12.0, 2.13.0) |

Por isso os `dotnet add package` deste repositório levam sempre `--version` explícito. Sem ele, o comando pega a versão mais nova e o projeto passa a compilar e quebrar ao executar.

---

## Como construir

**1. Confirme o SDK:**

```powershell
dotnet --version   # deve responder 10.x
```

**2. Compile as seis soluções antes da aula:**

```powershell
dotnet build CapacitacaoMicrosoftAIFundamentos\CapacitacaoMicrosoftAIFundamentos.slnx
dotnet build CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts.slnx
dotnet build CapacitacaoMicrosoftAIFoundrySDK\CapacitacaoMicrosoftAI.FoundrySDK.slnx
dotnet build CapacitacaoMicrosoftAIRag\CapacitacaoMicrosoftAIRag.slnx
dotnet build CapacitacaoMicrosoftAIRagAvancado\CapacitacaoMicrosoftAIRagAvancado.slnx
dotnet build CapacitacaoMicrosoftAIAgentFramework\CapacitacaoMicrosoftAIAgentFramework.slnx
```

**3. Configure o `launchSettings.json` de cada aula** a partir do `launchSettings.template.json` correspondente — ele não é versionado. Nas aulas 1, 2 e 3 o endpoint é o mesmo e só o nome do perfil muda; a aula 4 pede o caminho `/openai/v1` do mesmo recurso e mais três variáveis, para o modelo de embeddings e o Azure AI Search.

**4. Diagnóstico de dependência**, quando algo compilar e quebrar ao executar:

```powershell
dotnet nuget why <caminho-do-csproj> OpenAI
dotnet list package --include-transitive
```

O [`Directory.Build.props`](Directory.Build.props) da raiz promove `NU1605`, `NU1608` e `NU1109` a **erro** em todas as soluções, para que divergência de versão apareça no `restore` — e não em runtime, na frente da turma.

---

## Pré-requisitos comuns

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de rodar |
| **Recurso Foundry** | Com um modelo implantado |
| **Papel `Foundry User`** | No recurso Foundry, para cada aluno |

A **aula 4** pede, além disso: um segundo deployment no mesmo recurso, de **embeddings**; um serviço **Azure AI Search** em SKU `basic` ou superior (o `free` não tem ranqueamento semântico); e os papéis `Search Service Contributor` e `Search Index Data Contributor`. O script [`provisionar-ai-search.ps1`](CapacitacaoMicrosoftAIRag/scripts/) cria o serviço e concede os dois papéis. Os detalhes estão no [README da aula 4](CapacitacaoMicrosoftAIRag/README.md#pré-requisitos).

A **aula 5** não pede nada de novo: mesmo recurso, mesmos dois deployments, **mesmo índice**. Se a aula 4 rodou, a aula 5 roda.

A **aula 6** volta aos pré-requisitos das aulas 1 a 3: o mesmo endpoint do projeto e o papel `Foundry User`. O `launchSettings.json` da aula 3 serve, trocando o nome do perfil.

Cada projeto lê a configuração de `Properties/launchSettings.json`, que **não é versionado**. Crie o seu a partir do `launchSettings.template.json` da aula correspondente — as aulas 1, 2, 3 e 6 pedem as mesmas quatro variáveis, com os mesmos valores.

```powershell
cd CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts
copy Properties\launchSettings.template.json Properties\launchSettings.json
dotnet run
```
