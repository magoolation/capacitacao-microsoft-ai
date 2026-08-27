# Capacitação Microsoft AI — LLM e Prompts

Console em .NET que conversa com um modelo do **Microsoft Foundry** através da abstração **`IChatClient`** (`Microsoft.Extensions.AI`), demonstrando três técnicas de prompt: system prompt, **few-shot** e **structured output**.

Continuação da aula [Fundamentos](../CapacitacaoMicrosoftAIFundamentos), que usava o Foundry SDK e a Responses API. Aqui trocamos de camada — e a troca em si é parte da aula.

---

## Sumário

- [O que este projeto demonstra](#o-que-este-projeto-demonstra)
- [Duas camadas: Foundry SDK vs Microsoft.Extensions.AI](#duas-camadas-foundry-sdk-vs-microsoftextensionsai)
- [Pré-requisitos](#pré-requisitos)
- [Hands-on guiado](#hands-on-guiado)
- [Configuração](#configuração)
- [Como executar](#como-executar)
- [As três demos](#as-três-demos)
- [Solução de problemas](#solução-de-problemas)
- [Notas para quem ministra o treinamento](#notas-para-quem-ministra-o-treinamento)

---

## O que este projeto demonstra

1. **`IChatClient` apontando para o Foundry** — a abstração da Microsoft sobre o SDK da OpenAI, com autenticação por Entra ID.
2. **Histórico no cliente**, numa `List<ChatMessage>` — o oposto da aula anterior.
3. **Few-shot prompting** — ensinar o formato da resposta por exemplos, sem descrevê-lo.
4. **Structured output** — `GetResponseAsync<T>()` devolvendo um `record` C# tipado em vez de texto solto.

---

## Duas camadas: Foundry SDK vs Microsoft.Extensions.AI

As duas aulas chamam o **mesmo modelo**, no **mesmo recurso**, com a **mesma identidade**. O que muda é a camada — e, junto com ela, o endpoint.

| | Aula anterior (Fundamentos) | Esta aula (LLM e Prompts) |
|---|---|---|
| Pacote | `Azure.AI.Projects` | `Microsoft.Extensions.AI` + `.OpenAI` |
| Tipo central | `ProjectResponsesClient` | `IChatClient` |
| API do serviço | Responses API | Chat Completions |
| Endpoint | `.../api/projects/<projeto>` | `.../openai/v1` |
| Quem guarda o histórico | O **serviço** (`previousResponseId`) | O **cliente** (`List<ChatMessage>`) |
| Acoplamento | Preso ao Foundry | Neutro — troca-se o provedor sem tocar no resto |

> **O endpoint é diferente.** Copiar o `FOUNDRY_ENDPOINT` da aula anterior é o tropeço mais comum: o endpoint do projeto não atende requisições no formato OpenAI.

### Por que `IChatClient`

`IChatClient` é uma interface do .NET, não da Azure. O mesmo código roda contra Foundry, OpenAI, Ollama local, Anthropic ou qualquer provedor com adaptador — só mudam as ~10 linhas de configuração. Em troca, você perde acesso ao que é exclusivo do Foundry (agents, avaliações, tracing do projeto).

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
| `Microsoft.Extensions.AI` | 10.9.0 | A abstração: `IChatClient`, `ChatMessage`, `GetResponseAsync<T>` |
| `Microsoft.Extensions.AI.OpenAI` | 10.9.0 | O adaptador `AsIChatClient()` sobre o SDK da OpenAI |
| `Azure.Identity` | 1.21.0 | Resolução da credencial do Entra ID |

O pacote `OpenAI` (2.13.0) e o `System.ClientModel` (1.15.0) chegam como dependências transitivas, nas versões corretas. **Não os adicione explicitamente.**

---

## Hands-on guiado

Os cinco passos executados em sala, e onde cada um aparece no código.

**1 e 2 — os pacotes**

```powershell
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.AI.OpenAI
```

O primeiro traz só contratos: `IChatClient`, `ChatMessage`, `ChatRole`, `ChatOptions`. O segundo traz a implementação que fala com endpoints no formato OpenAI — e arrasta o SDK `OpenAI` junto.

**3 — `IChatClient` apontando para o Foundry** (`Program.cs`, seções 2 e 3)

```csharp
BearerTokenPolicy tokenPolicy = new(
    new DefaultAzureCredential(),
    "https://cognitiveservices.azure.com/.default");

OpenAIClient openAIClient = new(tokenPolicy, new OpenAIClientOptions
{
    Endpoint = new Uri(endpoint)   // https://<recurso>.services.ai.azure.com/openai/v1
});

IChatClient chatClient = openAIClient.GetChatClient(model).AsIChatClient();
```

Três pontos que merecem pausa na explicação:

- **`BearerTokenPolicy`** é a ponte entre dois mundos. O SDK da OpenAI espera uma chave de API; o Foundry quer Entra ID. A policy pede o token e o injeta no cabeçalho `Authorization` de cada chamada — o SDK da OpenAI não fica sabendo.
- **O escopo `https://cognitiveservices.azure.com/.default` é fixo.** Não é o seu endpoint; é o identificador do serviço no Entra ID, igual para qualquer recurso.
- **`Endpoint`** é o que redireciona o SDK para o Foundry. Sem essa propriedade, o cliente chamaria `api.openai.com`.

> Esse construtor do `OpenAIClient` está marcado como experimental, e o SDK trata o aviso como **erro** de compilação. Por isso o `.csproj` traz `<NoWarn>$(NoWarn);OPENAI001</NoWarn>`.

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
| `FOUNDRY_ENDPOINT` | Sim | `https://<recurso>.services.ai.azure.com/openai/v1` — **note o `/openai/v1`** |
| `FOUNDRY_MODEL` | Sim | Nome do **deployment** do modelo (não o nome comercial) |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant onde vive o recurso Foundry |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Fixa qual credencial usar, ex.: `AzureCliCredential` |

As duas últimas tornam determinística a escolha do `DefaultAzureCredential`, que testa Azure CLI, Visual Studio, VS Code e identidade gerenciada em ordem. Em máquinas com contas Microsoft diferentes em ferramentas diferentes, sem elas o programa pode autenticar com uma identidade e falhar por falta de permissão, com um erro que parece do código.

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

O `.csproj` está sem `<NoWarn>$(NoWarn);OPENAI001</NoWarn>`. O construtor `OpenAIClient(AuthenticationPolicy, ...)` é experimental e o SDK promove o aviso a erro.

### `404 (Resource not found)`

Quase sempre o endpoint da aula anterior. Confira que `FOUNDRY_ENDPOINT` termina em **`/openai/v1`** e não em `/api/projects/<projeto>`.

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

O pacote `OpenAI` foi adicionado explicitamente numa versão incompatível com o `Microsoft.Extensions.AI.OpenAI`.

```powershell
dotnet remove package OpenAI
```

O sintoma característico: **compila sem erro e quebra ao executar**.

---

## Notas para quem ministra o treinamento

**Comece pelo endpoint.** A diferença entre `/api/projects/<projeto>` e `/openai/v1` é a primeira coisa que os alunos vão errar ao reaproveitar o `launchSettings.json` da aula anterior. Errar de propósito, na frente da turma, ensina mais rápido que avisar.

**A demo 2 depende de rodar ao vivo.** A saída zero-shot muda a cada execução — é justamente essa variação que sustenta o argumento. Rodar duas vezes seguidas e comparar as duas saídas zero-shot vale mais que qualquer slide.

**Sequência sugerida:** demo 1 (contraste com a aula anterior) → demo 2 zero-shot (problema) → demo 2 few-shot (solução por prompt) → demo 3 (solução por contrato). Cada passo resolve o incômodo deixado pelo anterior.

**RBAC continua igual.** Cada aluno precisa da própria atribuição de `Foundry User` — a mesma da aula anterior serve, é o mesmo recurso e o mesmo papel. Não há nada novo a configurar.

---

## Para onde isso vai

O que `IChatClient` abre e que este projeto não usa:

| Recurso | Como entra |
|---|---|
| **Streaming** | `GetStreamingResponseAsync()` — resposta token a token |
| **Function calling** | `ChatOptions.Tools` + `.UseFunctionInvocation()` |
| **Cache** | `.UseDistributedCache()` na `ChatClientBuilder` |
| **Telemetria** | `.UseOpenTelemetry()` — rastreio de prompts, tokens e latência |
| **Injeção de dependência** | `builder.Services.AddChatClient(...)` |

Todos são decoradores sobre o mesmo `IChatClient`: entram sem alterar o código que consome o cliente.
