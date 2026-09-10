# Capacitação Microsoft AI — Primeiro agente com o Microsoft Agent Framework

O **mesmo modelo**, o **mesmo recurso**, a **mesma identidade** e o **mesmo `AIProjectClient`** das aulas 1 a 3 — só que agora quem está do outro lado é um **agente**, com nome, persona e memória de conversa, e não um `IChatClient` cru.

A diferença cabe em uma chamada: `AsAIAgent(model, name, instructions)`. Tudo o que vem antes é idêntico às aulas anteriores.

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

1. **Do Foundry SDK ao agente em uma linha.** `AIProjectClient.AsAIAgent(...)` devolve um `AIAgent` — modelo + identidade + regras. Nada é criado no serviço: o agente vive no seu processo (o lado *code-first* do slide "MAF × Foundry Agent Service").
2. **`RunAsync` sem sessão**: cada chamada é independente. A demo faz duas perguntas encadeadas para mostrar que o agente **não** lembra da primeira.
3. **`AgentSession` + `RunStreamingAsync`**: a mesma sessão em todas as chamadas dá memória à conversa, e a resposta chega token a token em `AgentResponseUpdate`.

### As quatro aulas, lado a lado

| | Aula 1 — Fundamentos | Aula 3 — Streaming | Aula 6 — este projeto |
|---|---|---|---|
| Pacotes declarados | `Azure.AI.Projects` + `Microsoft.Extensions.AI*` (versões escolhidas à mão) | idem | **`Microsoft.Agents.AI.Foundry`** + `Azure.Identity` |
| Endpoint | `.../api/projects/<projeto>` | idem | **idem** |
| Autenticação | `DefaultAzureCredential` | idem | **idem** |
| Tipo central | `IChatClient` | `IChatClient` | **`AIAgent`** |
| Quem guarda o histórico | O serviço, via `ConversationId` que o programa carrega | idem | A **`AgentSession`** — o programa só a passa adiante |
| Resposta | inteira | token a token | as duas: `RunAsync` e `RunStreamingAsync` |

O ponto pedagógico: **a aula 6 não troca o modelo, o endpoint nem a identidade.** Troca a camada. O agente é o `IChatClient` das aulas anteriores com persona e sessão embutidas — e, na aula 7, com tools.

---

## Pré-requisitos

Os mesmos das aulas 1 a 3 — nada de novo:

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de rodar |
| **Recurso Foundry** | Com um modelo implantado |
| **Papel `Foundry User`** | No recurso Foundry — ser `Owner` da subscription **não** basta. Procedimento completo na [aula de Fundamentos](../CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar) |

---

## Como construir

```powershell
cd CapacitacaoMicrosoftAIAgentFramework
dotnet restore
dotnet build
```

Pelo Visual Studio, abra `CapacitacaoMicrosoftAIAgentFramework.slnx`.

**Este projeto declara dois pacotes, e só dois:**

| Pacote declarado | Versão | O que traz |
|---|---|---|
| `Microsoft.Agents.AI.Foundry` | prerelease (`1.*-*`) | `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`, `Azure.AI.Projects`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI` e o `OpenAI` — nas versões que o framework testou |
| `Azure.Identity` | 1.21.0 | `DefaultAzureCredential` (depende só de `Azure.Core`) |

**A regra desta aula inverte a das aulas 1 a 3.** Lá, *nós* escolhíamos as versões de `Azure.AI.*` e `Microsoft.Extensions.AI*` para que convergissem no mesmo pivô de `OpenAI` (2.9.1). Aqui, quem escolhe é o Agent Framework: ele declara os quatro pacotes nas versões com que foi compilado. **Deixe-o ser o único dono do grafo.**

Por isso o `.csproj` **não** declara `Azure.AI.Projects`, `Azure.AI.Extensions.OpenAI`, `Microsoft.Extensions.AI` nem `Microsoft.Extensions.AI.OpenAI` — mesmo que o `Program.cs` use tipos deles. Os três comandos abaixo quebram o projeto:

```powershell
dotnet add package Azure.AI.Projects --version 2.0.1            # pivô 2.9.1 — o do MAF é outro
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.4.1  # idem
dotnet add package OpenAI                                       # sobe o pivô à revelia do MAF
```

Qualquer um deles **compila sem aviso e quebra ao executar** com `MissingMethodException`: o `OpenAI` rompe compatibilidade binária a cada minor do 2.x mantendo a mesma *assembly version*, e o NuGet unifica para a maior. O [`Directory.Build.props`](../Directory.Build.props) da raiz promove `NU1608` a erro para que o conflito apareça no `restore`.

> **Sobre a versão flutuante.** O pacote `Microsoft.Agents.AI.Foundry` ainda é prerelease e recebe builds com frequência; o `1.*-*` pega o mais novo. Antes de distribuir para a turma, fixe a versão resolvida para que todos compilem o mesmo grafo:
>
> ```powershell
> dotnet list package                       # mostra a versão resolvida
> dotnet list package --include-transitive  # e o pivô de OpenAI que ela trouxe
> ```
>
> Depois troque `1.*-*` pelo número exibido.

O `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` continua necessário: a ponte entre o cliente do Foundry e a abstração segue marcada como experimental.

Conferindo o grafo:

```powershell
dotnet nuget why CapacitacaoMicrosoftAIAgentFramework\CapacitacaoMicrosoftAIAgentFramework.csproj OpenAI
```

---

## Configuração

O programa lê variáveis de ambiente. Em desenvolvimento elas ficam em `Properties/launchSettings.json`, que **não é versionado**. Crie o seu a partir do template:

```powershell
copy CapacitacaoMicrosoftAIAgentFramework\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAIAgentFramework\Properties\launchSettings.json
```

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | `https://<recurso>.services.ai.azure.com/api/projects/<projeto>` |
| `FOUNDRY_MODEL` | Sim | Nome do **deployment** do modelo (não o nome comercial) |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant onde vive o recurso Foundry |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Fixa qual credencial usar, ex.: `AzureCliCredential` |

> O endpoint é o **do projeto**, igual ao das aulas 1 a 3. O `launchSettings.json` da aula 3 serve aqui — basta trocar o nome do perfil. O da aula 4/5 **não** serve: aquele termina em `/openai/v1`.

---

## Como executar

```powershell
az login --tenant <tenant-id>
cd CapacitacaoMicrosoftAIAgentFramework
dotnet run
```

```
=== 1. RunAsync, sem sessão ===

Você: Quero passar 5 dias em Fortaleza em julho. Vale a pena?
Zé: Vale sim! Julho em Fortaleza é época de ...

Você: E quantos dias você sugeriu mesmo?
Zé: Ainda não sugeri nenhum roteiro — me conta quantos dias você tem ...

=== 2. RunStreamingAsync, com sessão ===
Converse com o Zé. Digite 'sair' para encerrar.

Você: Quero passar 5 dias em Fortaleza em julho.
Zé: Ótima escolha! ...
Você: E quantos dias eu disse mesmo?
Zé: Você disse 5 dias ...
```

A segunda pergunta da parte 1 é a demonstração: sem sessão, o agente não sabe do que se trata. Na parte 2, a mesma pergunta funciona — e a resposta aparece progressivamente.

---

## Como o código funciona

O setup é o das aulas anteriores, sem uma linha de diferença:

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());
```

Daqui, na aula 3, saíam três linhas até um `IChatClient`. Aqui sai **uma**, e ela já carrega a "anatomia do agente" do slide — modelo, identidade e regras:

```csharp
AIAgent agente = projectClient.AsAIAgent(
    model: model,
    name: "Zé",
    instructions: "Você é o Zé, assistente de viagens ...");
```

`RunAsync` devolve um `AgentResponse` com a resposta inteira. Sem sessão, cada chamada começa do zero:

```csharp
AgentResponse resposta = await agente.RunAsync("Quero passar 5 dias em Fortaleza em julho.");
Console.WriteLine(resposta);          // ToString() devolve o texto
```

A sessão é o objeto que carrega o histórico. O programa não guarda lista de mensagens nem `ConversationId` — cria a sessão uma vez e passa a **mesma** em todas as chamadas:

```csharp
AgentSession sessao = await agente.CreateSessionAsync();

await foreach (AgentResponseUpdate update in agente.RunStreamingAsync(prompt, sessao))
{
    Console.Write(update.Text);
}
```

Compare com a aula 3: o laço é o mesmo, com `AgentResponseUpdate` no lugar de `ChatResponseUpdate`, e sem o `if (update.ConversationId is not null)` — a sessão faz esse trabalho. Onde o histórico fica (no cliente ou no serviço) é decisão do provedor; para o código, a interface é a mesma. É o que o slide "Provedores suportados" chama de plug-and-play: trocar o Foundry por outro provedor muda a linha do `AsAIAgent`, não o laço.

---

## Solução de problemas

### `403 (Forbidden)`

Falta o papel **`Foundry User`** no recurso. `Owner` da subscription não resolve — `Owner` concede *Actions*, chamar o modelo exige *DataActions*.

### `404 (Resource not found)`

Endpoint errado. Esta aula exige `.../api/projects/<projeto>`, o mesmo das aulas 1 a 3. Um `FOUNDRY_ENDPOINT` terminando em `/openai/v1` é o das aulas 4 e 5.

### `NU1608` / `NU1605` no restore

Algum pacote entrou no `.csproj` além dos dois esperados e pediu outra versão de `Azure.AI.*`, `Microsoft.Extensions.AI*` ou `OpenAI`. Remova-o: o MAF já traz todos. Veja [Como construir](#como-construir).

### `MissingMethodException` em tempo de execução

O mesmo problema, quando o `restore` não pegou (por exemplo, se o `Directory.Build.props` da raiz não foi encontrado porque a solução foi copiada para fora do repositório). Rode `dotnet nuget why <csproj> OpenAI` e confira que só o MAF aponta para ele.

### `error OPENAI001`

Falta `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` no `.csproj`.

### Versão prerelease não encontrada

`dotnet restore` sem acesso ao NuGet.org, ou uma fonte de pacotes interna que não espelha prereleases. Confira `dotnet nuget list source`.

---

## Notas para quem ministra o treinamento

**Projete o `Program.cs` da aula 3 e o desta aula lado a lado.** As seções 1 e 2 são idênticas. Na 3, três linhas viram uma. É a forma mais rápida de mostrar que o Agent Framework não é "outro SDK" — é uma camada sobre o mesmo SDK.

**Deixe a parte 1 falhar de propósito.** A segunda pergunta ("quantos dias você sugeriu mesmo?") sem sessão é o gancho para a `AgentSession`. Não explique antes de rodar — a turma vê o agente se perder e entende o que a sessão resolve.

**O grafo de pacotes é a lição escondida.** Nas aulas 1 a 3 gastamos tempo escolhendo versões para que `Azure.AI.*` e `Microsoft.Extensions.AI*` convergissem. Aqui é o contrário: dois pacotes, e o MAF escolhe o resto. Vale mostrar `dotnet list package --include-transitive` e apontar que `Azure.AI.Projects` está lá, sem ninguém tê-lo declarado. E vale mostrar o erro ao vivo: `dotnet add package Azure.AI.Projects --version 2.0.1`, `restore` falhando com `NU1608`, e desfazer.

**Persona divertida engaja.** O Zé é sugestão; peça que cada aluno troque as `instructions` pelo domínio dele — é o primeiro passo do desafio da aula (agente especialista em .NET 10) e o que a aula 7 vai estender com tools.
