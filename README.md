# Capacitação Microsoft AI

Exemplos em .NET do treinamento. Cada aula é uma solução independente, com seu próprio README, e conversa com um modelo hospedado no **Microsoft Foundry** autenticando por **Entra ID**.

---

## Aulas

| # | Aula | Tema central | Projeto |
|---|---|---|---|
| 1 | **Fundamentos** | Foundry SDK, autenticação sem segredo, Responses API | [`CapacitacaoMicrosoftAIFundamentos`](CapacitacaoMicrosoftAIFundamentos/) |
| 2 | **LLM e Prompts** | `Microsoft.Extensions.AI`, few-shot, structured output | [`CapacitacaoMicrosoftAILLMePrompts`](CapacitacaoMicrosoftAILLMePrompts/) |

---

## Índice dos exemplos

### Aula 1 — Fundamentos

📖 [README da aula](CapacitacaoMicrosoftAIFundamentos/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAIFundamentos/CapacitacaoMicrosoftAIFundamentos/Program.cs)

| Exemplo | O que demonstra |
|---|---|
| **Chat de console** | Autenticação por Entra ID sem chave de API, chamada ao modelo pela Responses API e **contexto mantido pelo serviço** — o programa guarda apenas uma `string` com o `previousResponseId` |

Leitura obrigatória antes de rodar qualquer aula: [Permissões (RBAC)](CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar). Ser `Owner` da subscription **não** basta — é preciso o papel `Foundry User`.

### Aula 2 — LLM e Prompts

📖 [README da aula](CapacitacaoMicrosoftAILLMePrompts/README.md) · 💻 [`Program.cs`](CapacitacaoMicrosoftAILLMePrompts/CapacitacaoMicrosoftAILLMePrompts/Program.cs)

Três demos sobre **os mesmos três chamados de suporte** — a repetição da entrada é o que torna a comparação entre as técnicas honesta.

| # | Exemplo | O que demonstra |
|---|---|---|
| 1 | [Chat simples](CapacitacaoMicrosoftAILLMePrompts/CapacitacaoMicrosoftAILLMePrompts/Program.cs) | **Contexto mantido pelo cliente**, numa `List<ChatMessage>` que cresce — o oposto da aula 1 |
| 2 | [Zero-shot vs Few-shot](CapacitacaoMicrosoftAILLMePrompts/CapacitacaoMicrosoftAILLMePrompts/Program.cs) | A mesma tarefa com e sem exemplos. Zero-shot devolve markdown livre; few-shot devolve `Acesso \| Alta \| ...` |
| 3 | [Structured output](CapacitacaoMicrosoftAILLMePrompts/CapacitacaoMicrosoftAILLMePrompts/Program.cs) | `GetResponseAsync<T>()` com um `record` — a saída vira objeto C#, não texto |

---

## O fio entre as duas aulas

As duas chamam o **mesmo modelo**, no **mesmo recurso**, com a **mesma identidade**. O que muda é a camada — e, junto com ela, o endpoint.

| | Aula 1 — Fundamentos | Aula 2 — LLM e Prompts |
|---|---|---|
| Pacote | `Azure.AI.Projects` | `Microsoft.Extensions.AI` + `.OpenAI` |
| Tipo central | `ProjectResponsesClient` | `IChatClient` |
| Endpoint | `.../api/projects/<projeto>` | `.../openai/v1` |
| Quem guarda o histórico | O **serviço** | O **cliente** |
| Acoplamento | Preso ao Foundry | Neutro — troca-se o provedor sem tocar no resto |

> **O endpoint é diferente entre as aulas.** Reaproveitar o `launchSettings.json` da aula 1 na aula 2 dá `404`. É o tropeço mais comum da turma.

---

## Pré-requisitos comuns

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de rodar |
| **Recurso Foundry** | Com um modelo implantado |
| **Papel `Foundry User`** | No recurso Foundry, para cada aluno |

Cada projeto lê a configuração de `Properties/launchSettings.json`, que **não é versionado**. Crie o seu a partir do `launchSettings.template.json` da aula correspondente.

```powershell
cd CapacitacaoMicrosoftAILLMePrompts\CapacitacaoMicrosoftAILLMePrompts
copy Properties\launchSettings.template.json Properties\launchSettings.json
dotnet run
```
