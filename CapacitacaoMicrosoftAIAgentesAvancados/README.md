# Capacitação Microsoft AI — Aula 7: agentes avançados

O agente da aula 6 ganha **mãos**: quatro tools com function calling completo, aprovação humana antes da ação irreversível, uma tool **MCP hospedada** (o Microsoft Learn MCP Server) e um **workflow sequencial** com três agentes. O setup — endpoint, identidade, `AIProjectClient` — continua o mesmo das aulas 1 a 6.

> **Estado:** projeto novo, escrito a partir dos slides da aula 7 e dos quickstarts oficiais do Microsoft Agent Framework. Ainda não foi executado contra o Foundry — rode as quatro demos antes da aula e ajuste o que a versão resolvida do MAF pedir (ver "Solução de problemas").

---

## O que este projeto demonstra

| # | Demo | O que mostra |
|---|---|---|
| 1 | **Function calling** | Quatro tools (`read_kb`, `get_weather`, `calc`, `send_email_mock`) criadas com `AIFunctionFactory.Create` + `[Description]`. O programa imprime cada chamada e cada retorno — o ciclo "modelo pede → app executa → modelo continua" acontece na tela |
| 2 | **Human-in-the-loop** | `send_email_mock` embrulhada em `ApprovalRequiredAIFunction`: o agente devolve um `ToolApprovalRequestContent` e **para**; o programa pergunta ao humano e retoma com `CreateResponse(aprovado)` na mesma sessão |
| 3 | **Hosted MCP** | `HostedMcpServerTool` apontando para `https://learn.microsoft.com/api/mcp`. O agente responde com a documentação oficial sem uma linha de integração |
| 4 | **Workflow sequencial** | `AgentWorkflowBuilder.BuildSequential([pesquisador, validador, redator])`, executado com `InProcessExecution.RunStreamingAsync` — cada executor imprime em streaming |

As três regras de segurança do slide estão em [`Ferramentas.cs`](CapacitacaoMicrosoftAIAgentesAvancados/Ferramentas.cs): **o modelo não executa nada** (validar argumentos é obrigação sua); **argumentos são entrada hostil** (escopo mínimo, sem segredos no retorno, erro legível em vez de exceção); **ação irreversível pede aprovação** (a tool não sabe disso — quem sabe é o embrulho).

---

## Pré-requisitos

Os mesmos da aula 6: .NET SDK 10, Azure CLI, recurso Foundry com um modelo implantado e o papel **`Foundry User`**. A demo 3 precisa de saída para a internet a partir do serviço (o MCP do Microsoft Learn é público).

---

## Como construir

```powershell
cd CapacitacaoMicrosoftAIAgentesAvancados
dotnet restore
dotnet build
```

**Este projeto declara três pacotes:**

| Pacote | Versão | O que traz |
|---|---|---|
| `Microsoft.Agents.AI.Foundry` | prerelease (`1.*-*`) | O framework, `Azure.AI.Projects`, `Microsoft.Extensions.AI*` e o `OpenAI` — nas versões que o MAF testou |
| `Microsoft.Agents.AI.Workflows` | prerelease (`1.*-*`) | `AgentWorkflowBuilder`, `InProcessExecution`, eventos. Mesma linha de versão do framework; não traz `OpenAI` |
| `Azure.Identity` | 1.21.0 | `DefaultAzureCredential` |

A regra da aula 6 continua: **deixe o Agent Framework ser o único dono do grafo.** Não declare `Azure.AI.*`, `Microsoft.Extensions.AI*` nem `OpenAI` à mão. Depois do primeiro restore, fixe as versões resolvidas (`dotnet list package`) para que a turma toda compile o mesmo grafo.

---

## Configuração

O `launchSettings.json` da aula 6 serve — só o nome do perfil muda:

```powershell
copy CapacitacaoMicrosoftAIAgentesAvancados\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAIAgentesAvancados\Properties\launchSettings.json
```

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | `https://<recurso>.services.ai.azure.com/api/projects/<projeto>` |
| `FOUNDRY_MODEL` | Sim | Nome do **deployment** do modelo — use um que suporte function calling |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | `AzureCliCredential` |

---

## Como executar

```powershell
az login --tenant <tenant-id>
cd CapacitacaoMicrosoftAIAgentesAvancados
dotnet run
```

Roteiro de sala sugerido:

1. **Demo 1** com as três perguntas do roteiro. Mostre que a primeira dispara duas tools (`read_kb` e depois `calc`) e que o modelo encadeia sozinho.
2. **Demo 2**: responda **n** na primeira aprovação e mostre que o e-mail não sai; repita respondendo **s**.
3. **Demo 3**: pergunte "o que é o Foundry Agent Service?" e repare nos links citados.
4. **Demo 4** com o pedido padrão; depois peça algo fora da base de conhecimento e veja o Validador apontar a lacuna.

---

## Como o código funciona

**Tools.** `AIFunctionFactory.Create(Ferramentas.GetWeather)` lê a assinatura e os `[Description]` e gera o schema. A lista `tools:` do `AsAIAgent` é a única diferença em relação ao agente da aula 6:

```csharp
AIAgent agente = projectClient.AsAIAgent(
    model: model, name: "Zé", instructions: Persona,
    tools: [readKb, getWeather, calc]);
```

**Aprovação.** O embrulho muda o comportamento sem tocar na função:

```csharp
AITool sendEmail = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(Ferramentas.SendEmailMock));

AgentResponse resposta = await agente.RunAsync(pedido, sessao);
var pendentes = resposta.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
// ... pergunta ao humano ...
resposta = await agente.RunAsync([new ChatMessage(ChatRole.User, [pedido.CreateResponse(aprovado)])], sessao);
```

**Workflow.** Agentes viram executors automaticamente; eles só processam quando recebem o `TurnToken`:

```csharp
Workflow workflow = AgentWorkflowBuilder.BuildSequential([pesquisador, validador, redator]);
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, mensagens);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
await foreach (WorkflowEvent evento in run.WatchStreamAsync()) { /* AgentResponseUpdateEvent, WorkflowOutputEvent */ }
```

---

## Solução de problemas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `403` | Falta o papel `Foundry User` | Ver a aula de Fundamentos |
| `NU1608` / `NU1605` no restore | O pacote Workflows resolveu uma versão diferente do Foundry | Fixe os dois na **mesma** versão exibida por `dotnet list package` |
| `HostedMcpServerTool` não encontrado | A versão de `Microsoft.Extensions.AI` trazida pelo MAF não expõe o tipo | Confira o namespace com `dotnet list package --include-transitive`; a alternativa é declarar a tool MCP via `ResponseTool.CreateMcpTool` do `Azure.AI.Projects` |
| O modelo nunca chama tool | Deployment sem suporte a function calling | Use um modelo da família GPT-5.x |
| A demo 2 executa o e-mail sem perguntar | O embrulho `ApprovalRequiredAIFunction` foi removido | Ele é o que gera o `ToolApprovalRequestContent` |
