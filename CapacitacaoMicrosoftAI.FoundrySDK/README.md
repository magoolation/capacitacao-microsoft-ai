# Capacitação Microsoft AI — Foundry SDK com streaming

O **mesmo modelo**, a **mesma identidade**, o **mesmo SDK** e o **mesmo `IChatClient`** da aula de Fundamentos — com a resposta chegando token a token em vez de de uma vez só.

A troca cabe em uma linha: `GetResponseAsync` vira `GetStreamingResponseAsync`. Tudo o que vem antes é idêntico, linha por linha.

---

## Sumário

- [O que este projeto demonstra](#o-que-este-projeto-demonstra)
- [Pré-requisitos](#pré-requisitos)
- [Como construir](#como-construir)
- [Configuração](#configuração)
- [Como executar](#como-executar)
- [Como o código funciona](#como-o-código-funciona)
- [Solução de problemas](#solução-de-problemas)
- [Notas para quem ministra o treinamento](#notas-para-quem-ministra-o-treinamento)

---

## O que este projeto demonstra

1. O **Foundry SDK + `Microsoft.Extensions.AI`** — a mesma montagem das outras duas aulas.
2. **Streaming** token a token com `GetStreamingResponseAsync`, num tipo neutro (`ChatResponseUpdate`).
3. Contexto mantido pelo **serviço**, via `ChatOptions.ConversationId` capturado ao longo do fluxo.

### As três aulas, lado a lado

| | Aula 1 — Fundamentos | Aula 2 — LLM e Prompts | Aula 3 — este projeto |
|---|---|---|---|
| Pacotes | `Azure.AI.Projects` + `Microsoft.Extensions.AI` | idem | idem |
| Tipo central | `IChatClient` | `IChatClient` | `IChatClient` |
| Endpoint | `.../api/projects/<projeto>` | idem | idem |
| Pivô do pacote `OpenAI` | 2.9.1 | 2.9.1 | 2.9.1 |
| Quem guarda o histórico | O **serviço** | O **cliente** | O **serviço** |
| Resposta | inteira, de uma vez | inteira, de uma vez | **token a token** |
| O método chamado | `GetResponseAsync` | `GetResponseAsync` / `<T>` | `GetStreamingResponseAsync` |

O ponto pedagógico: as três linhas de baixo são as **únicas** que diferem. SDK, autenticação, endpoint, grafo de pacotes e tipo de cliente são os mesmos nas três aulas — o que muda é a decisão de quem guarda o contexto e de como a resposta é consumida.

---

## Pré-requisitos

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de rodar |
| **Recurso Foundry** | Com um modelo implantado |
| **Papel `Foundry User`** | No recurso Foundry — ser `Owner` da subscription **não** basta. Procedimento completo na [aula de Fundamentos](../CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar) |

---

## Como construir

```powershell
cd CapacitacaoMicrosoftAI.FoundrySDK
dotnet restore
dotnet build
```

Pelo Visual Studio, abra `CapacitacaoMicrosoftAI.FoundrySDK.slnx`.

**Não mexa nas versões de pacote.** Este projeto converge para um pivô único, **`OpenAI 2.9.1`**:

| Pacote declarado | Versão | Traz `OpenAI` |
|---|---|---|
| `Azure.AI.Projects` | 2.0.1 | 2.9.1 (via `Azure.AI.Projects.Agents` 2.0.0) |
| `Azure.AI.Extensions.OpenAI` | 2.0.0 | 2.9.1 |
| `Azure.Identity` | 1.21.0 | — |
| `Microsoft.Extensions.AI` | 10.4.1 | — (só `Microsoft.Extensions.AI.Abstractions`) |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | 2.9.1 |

O pacote `OpenAI` continua **não** declarado: ele chega transitivamente, pelos dois lados, na mesma versão. É por isso que os dois stacks convivem — e a peça que torna isso possível é a versão `10.4.1`, a única de `Microsoft.Extensions.AI.OpenAI` cujo pino é `OpenAI 2.9.1`:

| Versão de `Microsoft.Extensions.AI.OpenAI` | Exige `OpenAI` |
|---|---|
| 10.3.0 | 2.8.0 |
| **10.4.1** | **2.9.1** (a que usamos) |
| 10.5.0 … 10.6.0 | 2.10.0 |
| 10.9.0 | [2.12.0, 2.13.0) |

Três comandos quebram o projeto a partir daqui:

```powershell
dotnet add package OpenAI                                   # sobe para 2.12.0
dotnet add package Azure.AI.Extensions.OpenAI --prerelease  # 3.0.0-beta.1 exige OpenAI 2.12.0
dotnet add package Microsoft.Extensions.AI.OpenAI           # sem --version, pega a mais nova
```

Qualquer um dos três **compila sem aviso e quebra ao executar**: o `OpenAI` rompe compatibilidade binária a cada minor do 2.x mantendo a mesma *assembly version*, o NuGet unifica para a maior e os assemblies Azure, compilados contra 2.9.1, estouram com `MissingMethodException`.

O `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` não é opcional: `AsIChatClient(ResponsesClient, string)` está marcado como experimental, e o SDK promove o aviso a erro.

Conferindo o grafo:

```powershell
dotnet nuget why CapacitacaoMicrosoftAI.FoundrySDK\CapacitacaoMicrosoftAI.FoundrySDK.csproj OpenAI
dotnet list package --include-transitive
```

---

## Configuração

O programa lê variáveis de ambiente. Em desenvolvimento elas ficam em `Properties/launchSettings.json`, que **não é versionado**. Crie o seu a partir do template:

```powershell
copy CapacitacaoMicrosoftAI.FoundrySDK\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAI.FoundrySDK\Properties\launchSettings.json
```

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | `https://<recurso>.services.ai.azure.com/api/projects/<projeto>` |
| `FOUNDRY_MODEL` | Sim | Nome do **deployment** do modelo (não o nome comercial) |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant onde vive o recurso Foundry |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Fixa qual credencial usar, ex.: `AzureCliCredential` |

> O endpoint é o **do projeto**, igual ao das outras duas aulas. Um único `launchSettings.json` serve as três — basta trocar o nome do perfil.

---

## Como executar

```powershell
az login --tenant <tenant-id>
cd CapacitacaoMicrosoftAI.FoundrySDK
dotnet run
```

```
Chat iniciado. Digite sua pergunta (ou 'sair' para encerrar).

Você: Explique o que é o Microsoft Foundry em duas frases.
IA: O Microsoft Foundry é a plataforma da Microsoft para...
```

O texto aparece progressivamente. Rodar esta demo e a de Fundamentos lado a lado, com a mesma pergunta, mostra a diferença de percepção de latência sem mudar nada além do método chamado.

---

## Como o código funciona

O setup é o das outras aulas, sem uma linha de diferença:

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);
```

`GetStreamingResponseAsync` devolve um `IAsyncEnumerable<ChatResponseUpdate>`: um fluxo de pedaços da resposta. Não é preciso testar tipo nenhum — cada atualização já traz o texto em `.Text`:

```csharp
ChatOptions options = new();

await foreach (ChatResponseUpdate update
    in chatClient.GetStreamingResponseAsync(prompt, options))
{
    Console.Write(update.Text);              // pedaço de texto

    if (update.ConversationId is not null)
    {
        options.ConversationId = update.ConversationId;   // elo da próxima pergunta
    }
}
```

Vale comparar com a versão que fala direto com o SDK do Foundry, sem a abstração: lá o laço recebe `StreamingResponseUpdate`, um tipo-base do pacote `OpenAI`, e precisa de `is StreamingResponseOutputTextDeltaUpdate` / `is StreamingResponseCompletedUpdate` para separar texto de metadados. `ChatResponseUpdate` já entrega os dois campos prontos — e o mesmo laço funcionaria contra qualquer outro provedor.

O `ConversationId` chega junto com as atualizações e só é definitivo no fim; por isso guardamos o último valor não nulo em vez de assumir que ele veio na primeira. É a mesma memória do lado do serviço da aula de Fundamentos: o cliente guarda uma `string`, não a conversa.

---

## Solução de problemas

### `403 (Forbidden)`

Falta o papel **`Foundry User`** no recurso. `Owner` da subscription não resolve — `Owner` concede *Actions*, chamar o modelo exige *DataActions*.

### `404 (Resource not found)`

Endpoint errado. As três aulas exigem `.../api/projects/<projeto>`. Um `FOUNDRY_ENDPOINT` terminando em `/openai/v1` vem de uma versão anterior deste material.

### `MissingMethodException` em tempo de execução

Algum pacote entrou no `.csproj` e puxou outra versão do `OpenAI`. Veja [Como construir](#como-construir).

### `error OPENAI001`

Falta `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` no `.csproj`. O método marcado como experimental é o `AsIChatClient(ResponsesClient, string)`.

---

## Notas para quem ministra o treinamento

**Rode esta demo logo depois da de Fundamentos, com o mesmo modelo e a mesma pergunta.** O SDK é o mesmo, o endpoint é o mesmo, a identidade é a mesma, o cliente é o mesmo — só o método muda. É a forma mais rápida de separar "o que é o SDK" de "como eu consumo a resposta".

**Um `diff` entre os dois `Program.cs` fecha o argumento.** As seções 1 a 4 são idênticas; a diferença cabe no laço final. Vale projetar isso na tela.

**O erro vale ser mostrado ao vivo.** Rode `dotnet add package OpenAI` na frente da turma, compile com sucesso e veja a explosão em runtime. Depois desfaça. Ensina mais sobre grafo de dependências do que qualquer slide — e o `Directory.Build.props` da raiz já promove `NU1608` a erro, então o próprio `restore` avisa antes.
