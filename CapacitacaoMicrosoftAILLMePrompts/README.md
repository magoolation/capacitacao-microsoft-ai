# Capacitação Microsoft AI — LLM e Prompts

Console em .NET que conversa com um modelo do **Microsoft Foundry** através da abstração **`IChatClient`** (`Microsoft.Extensions.AI`), demonstrando três técnicas de prompt: system prompt, **few-shot** e **structured output**.

Continuação da aula [Fundamentos](../CapacitacaoMicrosoftAIFundamentos). O setup é **o mesmo**: mesmo Foundry SDK, mesmo endpoint, mesma identidade, mesmo `IChatClient`. Esta aula não troca de camada — ela troca o que se faz com a camada.

---

## Sumário

- [O que este projeto demonstra](#o-que-este-projeto-demonstra)
- [A mesma montagem, outro uso](#a-mesma-montagem-outro-uso)
- [Pré-requisitos](#pré-requisitos)
- [Hands-on guiado](#hands-on-guiado)
- [Configuração](#configuração)
- [Como construir](#como-construir)
- [Como executar](#como-executar)
- [As três demos](#as-três-demos)
- [Solução de problemas](#solução-de-problemas)
- [Notas para quem ministra o treinamento](#notas-para-quem-ministra-o-treinamento)

---

## O que este projeto demonstra

1. **`IChatClient` sobre o Foundry SDK** — a abstração da Microsoft montada em cima do cliente de Responses do projeto, com autenticação por Entra ID.
2. **Histórico no cliente**, numa `List<ChatMessage>` — a estratégia oposta à da aula anterior, com o **mesmo** cliente.
3. **Few-shot prompting** — ensinar o formato da resposta por exemplos, sem descrevê-lo.
4. **Structured output** — `GetResponseAsync<T>()` devolvendo um `record` C# tipado em vez de texto solto.

---

## A mesma montagem, outro uso

As duas aulas chamam o **mesmo modelo**, no **mesmo recurso**, com a **mesma identidade**, pelo **mesmo endpoint** e com o **mesmo tipo de cliente**. As linhas que montam o `IChatClient` são idênticas nos dois projetos:

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);
```

O que muda é o que se faz com `chatClient`:

| | Aula anterior (Fundamentos) | Esta aula (LLM e Prompts) |
|---|---|---|
| Setup | `AIProjectClient` → `AsIChatClient()` | **idêntico** |
| Endpoint | `.../api/projects/<projeto>` | **idêntico** |
| O que se envia | Uma pergunta + `ChatOptions.ConversationId` | Uma `List<ChatMessage>` inteira |
| Quem guarda o histórico | O **serviço** | O **cliente** |
| Foco | Chegar ao modelo | O que se coloca no contexto |

> Se você acompanhou uma versão anterior deste material, **o endpoint desta aula mudou**: antes era `.../openai/v1`, porque o projeto falava com o SDK da OpenAI direto. Hoje as três aulas entram pelo Foundry SDK, e um único endpoint serve todas.

### Por que `IChatClient`

`IChatClient` é uma interface do .NET, não da Azure. Todas as demos abaixo tocam apenas `chatClient` — rodariam sem alteração contra OpenAI, Ollama local, Anthropic ou qualquer provedor com adaptador, mudando só as linhas de configuração acima.

E, ao contrário do que a versão anterior desta aula sugeria, adotar a abstração **não custa** o acesso ao que é exclusivo do Foundry: `projectClient` continua ali, ao lado, para agents, avaliações e tracing do projeto.

O ganho prático imediato é a `ChatClientBuilder`: cache, logging, telemetria OpenTelemetry e chamada de funções entram como decoradores, sem alterar o código que consome o cliente.

---

## Pré-requisitos

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az --version`. Usada para autenticar |
| **Acesso a um recurso Foundry** | Com um modelo implantado |
| **Papel `Foundry User`** | No recurso Foundry — [veja a aula anterior](../CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar) |

Pacotes NuGet:

| Pacote | Versão | Para quê |
|---|---|---|
| `Azure.AI.Projects` | 2.0.1 | Foundry SDK — acesso ao projeto |
| `Azure.AI.Extensions.OpenAI` | 2.0.0 | Clientes no formato OpenAI apontados ao projeto |
| `Azure.Identity` | 1.21.0 | Resolução da credencial do Entra ID |
| `Microsoft.Extensions.AI` | 10.4.1 | A abstração: `IChatClient`, `ChatMessage`, `GetResponseAsync<T>` |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | O adaptador `AsIChatClient()` |

É a mesma lista das outras duas aulas. O pacote `OpenAI` (2.9.1) e o `System.ClientModel` chegam como dependências transitivas, nas versões corretas. **Não os adicione explicitamente** — e não suba as duas versões `10.4.1`, veja [Como construir](#como-construir).

---

## Hands-on guiado

Os cinco passos executados em sala, e onde cada um aparece no código.

**1 e 2 — os pacotes**

```powershell
dotnet add package Azure.AI.Projects --version 2.0.1
dotnet add package Microsoft.Extensions.AI --version 10.4.1
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.4.1
```

O primeiro é o Foundry SDK. O segundo traz só contratos: `IChatClient`, `ChatMessage`, `ChatRole`, `ChatOptions`. O terceiro traz o adaptador entre os dois.

**As versões explícitas não são preciosismo.** `10.4.1` é a única versão de `Microsoft.Extensions.AI.OpenAI` que aceita o mesmo `OpenAI 2.9.1` exigido pelos pacotes Azure — sem `--version`, o `dotnet add` pega a mais nova e o projeto passa a compilar e quebrar em runtime.

**3 — do Foundry SDK até o `IChatClient`** (`Program.cs`, seções 2 e 3)

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),   // https://<recurso>.services.ai.azure.com/api/projects/<projeto>
    tokenProvider: new DefaultAzureCredential());

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);
```

Três pontos que merecem pausa na explicação:

- **Não há chave de API em lugar nenhum.** O Foundry SDK só autentica por Entra ID; `DefaultAzureCredential` resolve a identidade a partir do ambiente. Compare com o custo de guardar e rotacionar uma chave.
- **`ProjectResponsesClient` herda de `OpenAI.Responses.ResponsesClient`.** É isso que permite ao `AsIChatClient()`, que vem de `Microsoft.Extensions.AI.OpenAI`, aceitá-lo — um cliente Azure entrando num adaptador que não sabe nada sobre Azure.
- **`AsIChatClient()` é a fronteira.** Abaixo dela é Azure; acima, nenhuma das três demos menciona Azure ou OpenAI.

> Esse método está marcado como experimental, e o SDK trata o aviso como **erro** de compilação. Por isso o `.csproj` traz `<NoWarn>$(NoWarn);OPENAI001</NoWarn>`.

**4 — Few-shot via `List<ChatMessage>`** (demo 2)

```csharp
List<ChatMessage> exemplos =
[
    new(ChatRole.System, "Você classifica chamados de suporte. Responda SEMPRE em uma única linha, no formato: Categoria | Urgência | Resumo."),

    new(ChatRole.User, "O sistema fechou sozinho quando cliquei em salvar."),
    new(ChatRole.Assistant, "Erro | Alta | Aplicação encerra ao salvar."),
    // ... mais dois pares
];

List<ChatMessage> mensagens = [.. exemplos, new ChatMessage(ChatRole.User, chamado)];
var resposta = await chatClient.GetResponseAsync(mensagens);
```

Few-shot **não é um recurso da API** — é uma técnica de prompt. Você monta uma conversa fictícia em que o assistente já respondeu no formato desejado e entrega isso como se tivesse acontecido. O modelo continua o padrão.

**5 — Structured output com `record`** (demo 3)

```csharp
record TriagemChamado(string Categoria, string Urgencia, string Resumo, bool RequerAtencaoImediata);

var resposta = await chatClient.GetResponseAsync<TriagemChamado>(mensagens);
TriagemChamado triagem = resposta.Result;
```

A biblioteca gera um JSON Schema a partir do `record`, envia junto com o prompt e desserializa a resposta. O modelo fica **obrigado pelo serviço** a produzir JSON válido naquele formato — não é o prompt pedindo com jeitinho.

---

## Configuração

O programa lê variáveis de ambiente. Em desenvolvimento, elas ficam em `Properties/launchSettings.json`, que **não é versionado**. Crie o seu a partir do template:

```powershell
copy CapacitacaoMicrosoftAILLMePrompts\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAILLMePrompts\Properties\launchSettings.json
```

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | Endpoint **do projeto**: `https://<recurso>.services.ai.azure.com/api/projects/<projeto>` — o mesmo das outras aulas |
| `FOUNDRY_MODEL` | Sim | Nome do **deployment** do modelo (não o nome comercial) |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant onde vive o recurso Foundry |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Fixa qual credencial usar, ex.: `AzureCliCredential` |

As duas últimas tornam determinística a escolha do `DefaultAzureCredential`, que testa Azure CLI, Visual Studio, VS Code e identidade gerenciada em ordem. Em máquinas com contas Microsoft diferentes em ferramentas diferentes, sem elas o programa pode autenticar com uma identidade e falhar por falta de permissão, com um erro que parece do código.

---

## Como construir

**1. Confirme o SDK.** O repositório inteiro tem como alvo `net10.0`.

```powershell
dotnet --version   # deve responder 10.x
```

**2. Restaure e compile a partir da pasta da solução:**

```powershell
cd CapacitacaoMicrosoftAILLMePrompts
dotnet restore
dotnet build
```

Abrindo pelo Visual Studio, use `CapacitacaoMicrosoftAILLMePrompts.slnx`.

**3. Não mexa nas versões de pacote.** Este projeto converge para um pivô único, **`OpenAI 2.9.1`**:

| Pacote declarado | Versão | Traz `OpenAI` |
|---|---|---|
| `Azure.AI.Projects` | 2.0.1 | 2.9.1 (via `Azure.AI.Projects.Agents` 2.0.0) |
| `Azure.AI.Extensions.OpenAI` | 2.0.0 | 2.9.1 |
| `Azure.Identity` | 1.21.0 | — |
| `Microsoft.Extensions.AI` | 10.4.1 | — (só `Microsoft.Extensions.AI.Abstractions`) |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | 2.9.1 |

Este `.csproj` mistura os dois stacks — `Azure.AI.*` e `Microsoft.Extensions.AI` — e essa é exatamente a combinação que costuma quebrar. Ela funciona aqui por um motivo só: **`10.4.1` é a única versão de `Microsoft.Extensions.AI.OpenAI` cujo pino é `OpenAI 2.9.1`**, o mesmo contra o qual os assemblies Azure foram compilados.

| Versão de `Microsoft.Extensions.AI.OpenAI` | Exige `OpenAI` |
|---|---|
| 10.3.0 | 2.8.0 |
| **10.4.1** | **2.9.1** (a que usamos) |
| 10.5.0 … 10.6.0 | 2.10.0 |
| 10.9.0 | [2.12.0, 2.13.0) |

Subir qualquer um dos dois pacotes `10.4.1` arrasta o `OpenAI` para cima; o NuGet unifica para a maior versão e os assemblies Azure estouram com `MissingMethodException` **em runtime, sem erro de compilação**. Atualize sempre com `--version` explícito, conferindo o pino antes.

O `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` no `.csproj` **não é opcional**: sem ele o projeto não compila, porque `AsIChatClient(ResponsesClient, string)` é experimental e o SDK promove o aviso a erro.

**4. Conferindo o grafo quando algo cheirar mal:**

```powershell
dotnet nuget why CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts.csproj OpenAI
dotnet list package --include-transitive
```

O `Directory.Build.props` na raiz do repositório promove `NU1605`, `NU1608` e `NU1109` a **erro**, para que esse tipo de divergência apareça no `restore` em vez de na frente da turma.

---

## Como executar

```powershell
az login --tenant <tenant-id>
cd CapacitacaoMicrosoftAILLMePrompts
dotnet run
```

```
=== Capacitação Microsoft AI — LLM e Prompts ===
1. Chat simples (histórico no cliente)
2. Zero-shot vs Few-shot (List<ChatMessage>)
3. Structured output (GetResponseAsync<T> com record)
4. Sair
Opção:
```

---

## As três demos

### 1. Chat simples — quem guarda o histórico?

Contraste direto com a aula anterior. O programa mantém uma `List<ChatMessage>` e a envia **inteira** a cada pergunta; o contador de mensagens na tela cresce a cada rodada.

```
Você: Meu nome é Alexandre. Responda só OK.
IA: OK
    [histórico: 3 mensagens]

Você: Qual é o meu nome?
IA: Alexandre
    [histórico: 5 mensagens]
```

O modelo não tem memória entre chamadas. O que parece memória é essa lista sendo reenviada.

> **Experimento em sala:** comente a linha `conversa.AddMessages(resposta);`. O modelo passa a esquecer o que ele próprio disse — só as perguntas do usuário sobrevivem.

### 2. Zero-shot vs Few-shot

A mesma tarefa, o mesmo modelo, dois prompts. Os três chamados de suporte são classificados duas vezes, primeiro só com a instrução, depois com a instrução mais três exemplos.

**Zero-shot** — o modelo entende a tarefa e escolhe o formato:

```
Modelo : **Classificação do chamado:** **Sugestão de melhoria / nova funcionalidade**

**Resumo:**
O usuário solicita a inclusão de um **botão de exportação para Excel** na **tela de clientes**.

**Categoria sugerida:**
- **Tipo:** Melhoria
- **Subtipo:** Exportação de dados / Relatórios
...
```

**Few-shot** — os mesmos três chamados, mesma chamada de API:

```
Modelo : Acesso | Alta | Senha recusada três vezes e acesso ao sistema bloqueado.
Modelo : Erro | Alta | Relatório mensal com total incorreto após atualização de ontem.
Modelo : Sugestão | Baixa | Botão para exportar clientes para Excel.
```

A saída zero-shot está **correta e inutilizável ao mesmo tempo**: nenhum código consegue consumi-la de forma confiável. É o ponto que justifica a demo seguinte.

### 3. Structured output

O few-shot deixou a saída consistente, mas ainda é texto — para usar em código você faria `Split('|')`, `Trim()` e torceria para o modelo não variar.

`GetResponseAsync<TriagemChamado>()` devolve o objeto pronto:

```
Chamado  : Não consigo acessar o sistema, minha senha foi recusada três vezes seguidas.
Categoria: Acesso/Login
Urgência : Alta
Resumo   : Usuário não consegue acessar o sistema após ter a senha recusada três vezes...
Imediato : True
```

Repare que **o prompt encolheu**: não descreve formato, não lista campos, não dá exemplos. Os nomes das propriedades do `record` fazem esse trabalho — por isso vale nomeá-las bem.

> **Experimento em sala:** rode a demo 3 e compare `Categoria` e `Urgencia` entre os três chamados. O formato é garantido, o **vocabulário** não: aparecem `Alta` e `alta`, `Acesso/Login` e `Sugestão de melhoria`. Trocar as propriedades por `enum` fecha essa porta — o schema passa a listar os valores aceitos e o modelo não pode inventar outros.

---

## Solução de problemas

### `error OPENAI001: ... is for evaluation purposes only`

O `.csproj` está sem `<NoWarn>$(NoWarn);OPENAI001</NoWarn>`. O método `AsIChatClient(ResponsesClient, string)` é experimental e o SDK promove o aviso a erro.

### `404 (Resource not found)`

Quase sempre um `FOUNDRY_ENDPOINT` no formato errado. As três aulas usam o endpoint **do projeto**, terminando em `/api/projects/<projeto>`. Se o seu termina em `/openai/v1`, é um `launchSettings.json` de uma versão anterior deste material.

### `403 (Forbidden)`

Falta o papel **`Foundry User`** no recurso. Ser `Owner` da subscription **não** resolve — `Owner` concede *Actions*, e chamar o modelo exige *DataActions*. O procedimento completo está na [aula anterior](../CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar).

### `The access token is from the wrong issuer`

A CLI está autenticada em outro tenant.

```powershell
az login --tenant <tenant-do-foundry>
az account set --subscription <subscription-id>
```

### Funciona no terminal, falha na IDE

`DefaultAzureCredential` escolheu credenciais diferentes nos dois contextos. Defina `AZURE_TENANT_ID` e `AZURE_TOKEN_CREDENTIALS` no `launchSettings.json`.

### `MissingMethodException` em tempo de execução

O pacote `OpenAI` foi adicionado explicitamente numa versão incompatível, ou um dos pacotes `Microsoft.Extensions.AI` subiu acima de `10.4.1` e arrastou o `OpenAI` junto.

```powershell
dotnet remove package OpenAI
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.4.1
dotnet nuget why CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts.csproj OpenAI
```

O sintoma característico: **compila sem erro e quebra ao executar**.

---

## Notas para quem ministra o treinamento

**Comece abrindo os dois `Program.cs` lado a lado.** As seções 1 a 3 são idênticas às da aula anterior, linha por linha — e é esse fato que sustenta a aula inteira: o que separa "histórico no serviço" de "histórico no cliente" não é o SDK nem o endpoint, é o que se decide enviar.

**A demo 2 depende de rodar ao vivo.** A saída zero-shot muda a cada execução — é justamente essa variação que sustenta o argumento. Rodar duas vezes seguidas e comparar as duas saídas zero-shot vale mais que qualquer slide.

**Sequência sugerida:** demo 1 (contraste com a aula anterior) → demo 2 zero-shot (problema) → demo 2 few-shot (solução por prompt) → demo 3 (solução por contrato). Cada passo resolve o incômodo deixado pelo anterior.

**RBAC continua igual.** Cada aluno precisa da própria atribuição de `Foundry User` — a mesma da aula anterior serve, é o mesmo recurso e o mesmo papel. Não há nada novo a configurar.

---

## Para onde isso vai

O que `IChatClient` abre e que este projeto não usa:

| Recurso | Como entra |
|---|---|
| **Streaming** | `GetStreamingResponseAsync()` — resposta token a token, é a [aula 3](../CapacitacaoMicrosoftAI.FoundrySDK/) |
| **Function calling** | `ChatOptions.Tools` + `.UseFunctionInvocation()` |
| **Cache** | `.UseDistributedCache()` na `ChatClientBuilder` |
| **Telemetria** | `.UseOpenTelemetry()` — rastreio de prompts, tokens e latência |
| **Injeção de dependência** | `builder.Services.AddChatClient(...)` |

Todos são decoradores sobre o mesmo `IChatClient`: entram sem alterar o código que consome o cliente.

---

## Próxima aula

**[Foundry SDK com streaming](../CapacitacaoMicrosoftAI.FoundrySDK/)** — o mesmo setup destas três seções iniciais, com a resposta chegando token a token e o histórico de volta ao serviço.

O índice completo das aulas está no [README do repositório](../README.md).
