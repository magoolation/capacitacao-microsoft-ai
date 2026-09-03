# Capacitação Microsoft AI

Exemplos em .NET do treinamento. Cada aula é uma **solução independente**, com seu próprio README, e conversa com um modelo hospedado no **Microsoft Foundry** autenticando por **Entra ID**.

As três usam a mesma montagem: o **Foundry SDK** (`Azure.AI.Projects`) para chegar ao projeto com a identidade do Entra ID, e **`Microsoft.Extensions.AI`** (`IChatClient`) como camada de programação. Mesmo endpoint, mesmos pacotes, mesmo pivô de `OpenAI` — o que muda de uma aula para outra é o que se faz com o `IChatClient`.

> As soluções continuam separadas para que cada aluno abra uma aula por vez no Visual Studio, mas o grafo de pacotes hoje é idêntico nas três. Veja [Matriz de versões](#matriz-de-versões).

---

## Aulas

| # | Aula | Tema central | Projeto |
|---|---|---|---|
| 1 | **Fundamentos** | Autenticação sem segredo, do Foundry SDK ao `IChatClient`, contexto no serviço | [`CapacitacaoMicrosoftAIFundamentos`](CapacitacaoMicrosoftAIFundamentos/) |
| 2 | **LLM e Prompts** | Contexto no cliente, few-shot, structured output | [`CapacitacaoMicrosoftAILLMePrompts`](CapacitacaoMicrosoftAILLMePrompts/) |
| 3 | **Foundry SDK com streaming** | `GetStreamingResponseAsync`, resposta token a token | [`CapacitacaoMicrosoftAI.FoundrySDK`](CapacitacaoMicrosoftAI.FoundrySDK/) |

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

📖 [README da aula](CapacitacaoMicrosoftAI.FoundrySDK/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAI.FoundrySDK/CapacitacaoMicrosoftAI.FoundrySDK/Program.cs)

| Exemplo | O que demonstra |
|---|---|
| **Chat com streaming** | O mesmo setup da aula 1, com a resposta chegando token a token em `ChatResponseUpdate` — a diferença cabe no laço final |

---

## O fio entre as aulas

As três chamam o **mesmo modelo**, no **mesmo recurso**, com a **mesma identidade**, pelo **mesmo endpoint**, com os **mesmos pacotes**. Estas linhas são idênticas nos três `Program.cs`:

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

> **Um único `FOUNDRY_ENDPOINT` serve as três aulas.** Numa versão anterior deste material a aula 2 usava `.../openai/v1`, e reaproveitar o `launchSettings.json` entre aulas dava `404`. Isso deixou de ser um problema — mas se o seu arquivo ainda termina em `/openai/v1`, ele é dessa versão antiga.

---

## Matriz de versões

Verificada nos `.nuspec` do NuGet. **Todo o problema de compatibilidade deste stack cabe em uma frase:** `Microsoft.Extensions.AI` e os pacotes `Azure.AI.*` consomem o mesmo pacote `OpenAI`, que **quebra compatibilidade binária a cada minor do 2.x mantendo a mesma assembly version**. O NuGet unifica para a maior versão, e o assembly compilado contra a menor estoura em runtime — sem erro de compilação.

Como as três aulas misturam os dois stacks no mesmo `.csproj`, **as três dependem de escolher versões que apontem para o mesmo pivô**. Este é o conjunto, idêntico nas três soluções:

| Pacote declarado | Versão | Pivô `OpenAI` |
|---|---|---|
| `Azure.AI.Projects` | 2.0.1 | **2.9.1** |
| `Azure.AI.Extensions.OpenAI` | 2.0.0 | 2.9.1 |
| `Microsoft.Extensions.AI` | 10.4.1 | — |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | 2.9.1 |
| `Azure.Identity` | 1.21.0 | — |

**A regra:** um `.csproj` = um pivô de `OpenAI`. Misturar `Azure.AI.*` com `Microsoft.Extensions.AI.OpenAI` é possível — é o que fazemos — mas só conferindo os dois pinos antes.

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

**2. Compile as três soluções antes da aula:**

```powershell
dotnet build CapacitacaoMicrosoftAIFundamentos\CapacitacaoMicrosoftAIFundamentos.slnx
dotnet build CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts.slnx
dotnet build CapacitacaoMicrosoftAI.FoundrySDK\CapacitacaoMicrosoftAI.FoundrySDK.slnx
```

**3. Configure o `launchSettings.json` de cada aula** a partir do `launchSettings.template.json` correspondente — ele não é versionado. O endpoint é o mesmo nas três; só o nome do perfil muda.

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

Cada projeto lê a configuração de `Properties/launchSettings.json`, que **não é versionado**. Crie o seu a partir do `launchSettings.template.json` da aula correspondente — as três pedem as mesmas quatro variáveis, com os mesmos valores.

```powershell
cd CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts
copy Properties\launchSettings.template.json Properties\launchSettings.json
dotnet run
```
