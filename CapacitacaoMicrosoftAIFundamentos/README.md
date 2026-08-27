# Capacitação Microsoft AI — Fundamentos

Chat de console em .NET que conversa com um modelo hospedado no **Microsoft Foundry**, usando o **Foundry SDK** e autenticação por **Entra ID**.

O objetivo é mostrar o caminho mínimo — e correto — entre uma aplicação .NET e um modelo no Foundry.

---

## Sumário

- [O que este projeto demonstra](#o-que-este-projeto-demonstra)
- [Pré-requisitos](#pré-requisitos)
- [Permissões (RBAC) — leia antes de rodar](#permissões-rbac--leia-antes-de-rodar)
- [Configuração](#configuração)
- [Como executar](#como-executar)
- [Como o código funciona](#como-o-código-funciona)
- [Solução de problemas](#solução-de-problemas)
- [Notas para quem ministra o treinamento](#notas-para-quem-ministra-o-treinamento)
- [Quando usar outro SDK](#quando-usar-outro-sdk)
- [Próxima aula](#próxima-aula)

---

## O que este projeto demonstra

1. **Autenticação sem segredo.** Nenhuma chave de API no código — a identidade vem do Entra ID.
2. **Chamada a um modelo pela Responses API**, através do endpoint do projeto Foundry.
3. **Contexto de conversa mantido pelo serviço**, não pelo cliente. O programa guarda apenas uma `string`.

---

## Pré-requisitos

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az --version`. Usada para autenticar |
| **Acesso a um recurso Foundry** | Com um modelo implantado |
| **Papel `Foundry User`** | No recurso Foundry — veja a seção abaixo |

Pacotes NuGet (restaurados automaticamente no build):

| Pacote | Versão | Para quê |
|---|---|---|
| `Azure.AI.Projects` | 2.0.1 | Foundry SDK — acesso ao projeto |
| `Azure.AI.Extensions.OpenAI` | 2.0.0 | Clientes no formato OpenAI apontados ao projeto |
| `Azure.Identity` | 1.21.0 | Resolução da credencial do Entra ID |

> **Não adicione o pacote `OpenAI` explicitamente.** Ele já vem como dependência transitiva na versão correta (2.9.1). Fixar uma versão mais nova causa `MissingMethodException` em tempo de execução — o projeto compila e quebra ao rodar. Detalhes em [Solução de problemas](#solução-de-problemas).

---

## Permissões (RBAC) — leia antes de rodar

Esta é a etapa que mais causa tropeço, e por um motivo pouco intuitivo:

> **Ser `Owner` da subscription NÃO é suficiente.**

O Azure separa dois tipos de permissão:

| Tipo | O que controla | `Owner` concede? |
|---|---|---|
| **Actions** (plano de gerenciamento) | Criar, ler, alterar e apagar recursos | Sim (`*`) |
| **DataActions** (plano de dados) | Usar o conteúdo do recurso — conversar com o modelo | **Não** |

Chamar o modelo exige a *data action* `Microsoft.CognitiveServices/accounts/AIServices/agents/write`. O papel `Owner` tem a lista de `DataActions` **vazia**. Resultado: você é dono do recurso, autentica com sucesso e mesmo assim recebe `403 Forbidden`.

O papel que resolve é o **`Foundry User`** (ID `53ca6127-db72-4b80-b1b0-d745d6d5456d`), cujas `DataActions` incluem `Microsoft.CognitiveServices/*`.

### Como atribuir

**Pelo portal** (mais simples): recurso Foundry → **Access control (IAM)** → **Add role assignment** → **Foundry User** → selecione o usuário → **Review + assign**.

**Pela CLI:**

```powershell
az role assignment create `
  --role "53ca6127-db72-4b80-b1b0-d745d6d5456d" `
  --assignee "<seu-email>" `
  --scope "/subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<recurso>"
```

Se esse comando falhar com `MissingSubscription` — algo que acontece com contas convidadas (`#EXT#`) —, use a API REST diretamente:

```powershell
az rest --method put `
  --url "https://management.azure.com/subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<recurso>/providers/Microsoft.Authorization/roleAssignments/$([guid]::NewGuid())?api-version=2022-04-01" `
  --body '{"properties":{"roleDefinitionId":"/subscriptions/<sub-id>/providers/Microsoft.Authorization/roleDefinitions/53ca6127-db72-4b80-b1b0-d745d6d5456d","principalId":"<object-id>","principalType":"User"}}'
```

Para descobrir o `object-id` do usuário:

```powershell
az ad signed-in-user show --query id -o tsv
```

A propagação leva cerca de um minuto.

### Conferindo o que já existe

```powershell
az rest --method get `
  --url "https://management.azure.com/subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<recurso>/providers/Microsoft.Authorization/roleAssignments?api-version=2022-04-01" `
  --query "value[].{principal:properties.principalId, role:properties.roleDefinitionId}"
```

---

## Configuração

O programa lê variáveis de ambiente. Em desenvolvimento, elas ficam em `Properties/launchSettings.json`.

Esse arquivo **não é versionado** (contém identificadores do seu ambiente). Crie o seu a partir do template:

```powershell
copy CapacitacaoMicrosoftAIFundamentos\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAIFundamentos\Properties\launchSettings.json
```

Depois preencha os valores conforme a tabela abaixo.

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | Endpoint **do projeto**: `https://<recurso>.services.ai.azure.com/api/projects/<projeto>` |
| `FOUNDRY_MODEL` | Sim | Nome do **deployment** do modelo (não o nome comercial) |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant onde vive o recurso Foundry |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Fixa qual credencial usar, ex.: `AzureCliCredential` |

### Por que fixar tenant e credencial

`DefaultAzureCredential` testa várias fontes em ordem e usa a primeira que responder: Azure CLI, Visual Studio, VS Code, identidade gerenciada. Se a máquina tem contas Microsoft diferentes em ferramentas diferentes — cenário comum —, o programa pode autenticar com uma identidade e falhar por falta de permissão, com um erro que parece do código.

Fixar as duas variáveis torna o comportamento determinístico. O custo é que `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` **exige** um `az login` válido; as outras fontes deixam de ser tentadas.

---

## Como executar

**1. Autentique no tenant correto:**

```powershell
az login --tenant <tenant-id>
```

**2. Rode:**

```powershell
cd CapacitacaoMicrosoftAIFundamentos
dotnet run
```

**3. Converse:**

```
Chat iniciado. Digite sua pergunta (ou 'sair' para encerrar).

Você: Meu nome é Alexandre. Responda só OK.
IA: OK

Você: Qual é o meu nome?
IA: Alexandre

Você: sair
```

A segunda resposta é a demonstração central: o modelo lembrou do nome **sem que o programa guardasse a conversa**.

---

## Como o código funciona

```
AIProjectClient                        ← porta de entrada (endpoint + credencial)
   └── ProjectOpenAIClient
          └── ProjectResponsesClient   ← cliente do modelo escolhido
                 └── CreateResponseAsync(pergunta, previousResponseId)
```

### Memória da conversa

| | Chat Completions (clássico) | Responses API (aqui) |
|---|---|---|
| Quem guarda o histórico | O cliente | O serviço |
| O que o código mantém | `List<ChatMessage>` crescente | Uma `string` com o Id |
| O que trafega por chamada | A conversa inteira | Só a pergunta nova |

Cada resposta traz um `Id`. Ao enviar a próxima pergunta junto com esse `Id`, o serviço reconstrói o contexto.

> **Experimento sugerido em sala:** comente a linha `previousResponseId = resposta.Value.Id;` e rode de novo. O chat passa a esquecer tudo a cada mensagem — a forma mais rápida de entender o que essa linha faz.

---

## Solução de problemas

### `403 (Forbidden)` ao chamar o modelo

Falta o papel **`Foundry User`**. Ser `Owner` não resolve — veja [Permissões (RBAC)](#permissões-rbac--leia-antes-de-rodar).

Se o erro trouxer a mensagem detalhada `Identity(object id: ...) does not have permissions for ... agents/write`, é confirmação direta disso.

### `403` com corpo de resposta vazio

Mesma causa. A ausência de mensagem costuma indicar que o token está correto (tenant certo, identidade certa) e a negação é limpa, só de autorização.

### `MissingMethodException: Method not found: 'Void OpenAI.Responses.ResponsesClient..ctor(...)'`

O pacote `OpenAI` foi adicionado explicitamente numa versão incompatível. O `Azure.AI.Extensions.OpenAI` 2.0.0 foi compilado contra `OpenAI 2.9.1`.

```powershell
dotnet remove package OpenAI
```

O sintoma característico: **compila sem erro e quebra ao executar**.

### `The access token is from the wrong issuer '...'`

A CLI está autenticada em outro tenant.

```powershell
az login --tenant <tenant-do-foundry>
az account set --subscription <subscription-id>
```

### `MissingSubscription` ao criar atribuição de papel

Defeito da Azure CLI com contas convidadas (`#EXT#`). Use a variante `az rest --method put` mostrada acima, ou o portal.

### Funciona no terminal, falha na IDE

`DefaultAzureCredential` escolheu credenciais diferentes nos dois contextos — tipicamente Azure CLI no terminal e a conta do Visual Studio na IDE. Defina `AZURE_TENANT_ID` e `AZURE_TOKEN_CREDENTIALS` no `launchSettings.json`.

---

## Notas para quem ministra o treinamento

**Cada aluno precisa da própria atribuição de `Foundry User`.** Não há chave compartilhada — o Foundry SDK só autentica por Entra ID. E, como visto, promover o aluno a `Owner` não substitui o papel.

Para turmas, atribua a um **grupo do Entra ID** uma única vez e inclua os alunos nele:

```powershell
az role assignment create `
  --role "53ca6127-db72-4b80-b1b0-d745d6d5456d" `
  --assignee-object-id "<object-id-do-grupo>" `
  --assignee-principal-type Group `
  --scope "/subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<recurso>"
```

**Verifique antes da aula** que cada máquina fez `az login --tenant <tenant-id>` — com `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, é o único caminho de autenticação aceito.

**Se precisar de acesso apenas de consumo**, existe o papel `Foundry Agent Consumer`, mais restrito que `Foundry User`.

---

## Quando usar outro SDK

O Foundry SDK não é a única opção. A escolha depende do cenário:

| SDK | Use quando | Endpoint |
|---|---|---|
| **Foundry SDK** (este projeto) | Agents, avaliações, tracing, ferramentas do Foundry | `.../api/projects/<projeto>` |
| **OpenAI SDK** | Compatibilidade máxima com OpenAI, menor latência, **embeddings** | `.../openai/v1` |
| **Agent Framework** | Orquestração multi-agente | Responses API via `FoundryChatClient` |

> O endpoint do projeto **não roteia requisições de embeddings**. Se o treinamento avançar para RAG ou busca vetorial, essa parte precisará do SDK da OpenAI apontando para `.../openai/v1`.

---

## Próxima aula

**[LLM e Prompts](../CapacitacaoMicrosoftAILLMePrompts/)** — troca o Foundry SDK pela abstração `IChatClient` (`Microsoft.Extensions.AI`), e é onde o endpoint `.../openai/v1` da tabela acima entra em uso.

| Exemplo | O que demonstra |
|---|---|
| Chat simples | Contexto mantido pelo **cliente**, numa `List<ChatMessage>` — o oposto do que esta aula faz |
| Zero-shot vs Few-shot | A mesma tarefa com e sem exemplos, lado a lado |
| Structured output | `GetResponseAsync<T>()` devolvendo um `record` C# em vez de texto |

O índice completo das aulas está no [README do repositório](../README.md).
