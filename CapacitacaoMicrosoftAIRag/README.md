# Capacitação Microsoft AI — RAG com Azure AI Search

A quarta aula junta as três anteriores: o modelo do Foundry, a abstração do `Microsoft.Extensions.AI` e um índice vetorial no Azure AI Search.

O corpus é um conjunto de políticas internas de uma empresa fictícia — a Aurora Log. Ele foi escrito de propósito para que **algumas perguntas só ele responda**, e para que **uma pergunta pareça estar nele e não esteja**. É essa segunda que sustenta a discussão sobre groundedness.

---

## Sumário

- [O que este projeto demonstra](#o-que-este-projeto-demonstra)
- [Pré-requisitos](#pré-requisitos)
- [Provisionar o Azure AI Search](#provisionar-o-azure-ai-search)
- [Como construir](#como-construir)
- [Configuração](#configuração)
- [Como executar](#como-executar)
- [O roteiro de sala](#o-roteiro-de-sala)
- [Demonstrando com × sem RAG no playground](#demonstrando-com--sem-rag-no-playground)
- [Como o código funciona](#como-o-código-funciona)
- [Solução de problemas](#solução-de-problemas)
- [Notas para quem ministra o treinamento](#notas-para-quem-ministra-o-treinamento)

---

## O que este projeto demonstra

1. **Indexação**: chunking por parágrafo → embeddings em lote → upload para o índice.
2. **Recuperação nos três modos**: keyword (BM25), vetorial (HNSW) e híbrida com reranking semântico — lado a lado, com os scores visíveis.
3. **A mesma pergunta com e sem RAG**, no mesmo modelo, com a mesma temperatura.
4. **Citações**: cada afirmação da resposta aponta para o trecho de onde veio.

O ponto da aula não é "RAG é melhor". É que RAG resolve **um** problema — o modelo não conhece os seus documentos — e fora desse problema não ajuda.

---

## Pré-requisitos

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de tudo |
| **Recurso Foundry** | Com **dois** deployments: um de chat e um de **embeddings** (`text-embedding-3-small`) |
| **Papel `Foundry User`** | No recurso Foundry — veja a [aula 1](../CapacitacaoMicrosoftAIFundamentos/README.md#permissões-rbac--leia-antes-de-rodar) |
| **Azure AI Search** | SKU `basic` ou superior. O `free` **não** tem ranqueamento semântico |
| **Papéis no Search** | `Search Service Contributor` e `Search Index Data Contributor`, para a sua conta **e** para a identidade do Foundry — o script concede |
| **Identidade gerenciada** | Habilitada no recurso Foundry, se você for fazer a demo no playground |

> O deployment de embeddings é o pré-requisito mais esquecido. Sem ele o projeto falha na primeira indexação, com um erro de deployment inexistente.

---

## Provisionar o Azure AI Search

```powershell
cd scripts
./provisionar-ai-search.ps1 `
    -ResourceGroup rg-capacitacao -SearchName srch-capacitacao-01 `
    -FoundryName capacitacao-msai-foundry -FoundryResourceGroup rg-capacitacao
```

Em Linux ou macOS:

```bash
cd scripts
./provisionar-ai-search.sh -g rg-capacitacao -n srch-capacitacao-01 \
    -f capacitacao-msai-foundry -r rg-capacitacao
```

Os dois últimos parâmetros são **opcionais para o console e obrigatórios para o playground**: eles concedem os papéis também à identidade gerenciada do recurso Foundry. Sem eles, `dotnet run` funciona e o portal devolve `403`. Detalhes em [Demonstrando com × sem RAG no playground](#demonstrando-com--sem-rag-no-playground).

O script é idempotente — rodar de novo não quebra nada. Ele registra o provider `Microsoft.Search`, garante o resource group, cria o serviço com autenticação por Entra ID e concede os dois papéis à sua identidade. No fim, imprime o bloco pronto para o `launchSettings.json`.

**Os dois papéis são diferentes de propósito**, e vale dizer isso em voz alta:

| Papel | Para quê |
|---|---|
| `Search Service Contributor` | Criar, alterar e apagar **índices** |
| `Search Index Data Contributor` | Gravar e ler **documentos** dentro do índice |

E são concedidos a **duas identidades diferentes**:

| Identidade | Para quê |
|---|---|
| A sua conta | Rodar o console (`dotnet run`) |
| A identidade gerenciada do recurso Foundry | A ferramenta de busca do playground |

Ser `Owner` da subscription não substitui o segundo — é o mesmo mal-entendido entre *Actions* e *DataActions* da aula 1, agora em outro serviço. Se a turma já apanhou disso uma vez, aqui a ficha cai sozinha.

A propagação do RBAC leva de 1 a 5 minutos. Se a primeira execução der `403`, espere e tente de novo antes de investigar qualquer outra coisa.

---

## Como construir

```powershell
cd CapacitacaoMicrosoftAIRag
dotnet restore
dotnet build
```

**Não mexa nas versões de pacote.** Este projeto converge para **`OpenAI 2.12.0`**:

| Pacote declarado | Versão | Traz `OpenAI` |
|---|---|---|
| `Microsoft.Extensions.AI` | 10.9.0 | — |
| `Microsoft.Extensions.AI.OpenAI` | 10.9.0 | [2.12.0, 2.13.0) |
| `Azure.Search.Documents` | 12.0.0 | **não depende de `OpenAI`** |
| `Azure.Identity` | 1.21.0 | — |

O `Azure.Search.Documents` é **neutro**: depende só de `Azure.Core`, então convive com qualquer pivô. Quem define o pivô aqui é o `Microsoft.Extensions.AI`.

Adicionar `Azure.AI.Projects` a este `.csproj` recria o conflito das aulas anteriores — ele exige `OpenAI 2.9.1`, o NuGet unifica para 2.12.0 e o projeto quebra em runtime. Se precisar dos dois juntos, a única versão compatível do `Microsoft.Extensions.AI.OpenAI` é a **10.4.1**.

```powershell
dotnet nuget why CapacitacaoMicrosoftAIRag\CapacitacaoMicrosoftAIRag.csproj OpenAI
dotnet list package --include-transitive
```

---

## Configuração

```powershell
copy CapacitacaoMicrosoftAIRag\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAIRag\Properties\launchSettings.json
```

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | `https://<recurso>.services.ai.azure.com/openai/v1` |
| `FOUNDRY_MODEL` | Sim | Deployment do modelo de **chat** |
| `FOUNDRY_EMBEDDING_MODEL` | Sim | Deployment do modelo de **embeddings** |
| `SEARCH_ENDPOINT` | Sim | `https://<servico>.search.windows.net` |
| `SEARCH_INDEX` | Não | Padrão: `politicas-internas` |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Ex.: `AzureCliCredential` |

> O endpoint do Foundry aqui é o `/openai/v1` — **não** o `/api/projects/<projeto>` das aulas 1, 2 e 3. É o mesmo recurso por outra porta: o `AIProjectClient` daquelas aulas entra pelo projeto, o `OpenAIClient` desta entra pela API compatível com a OpenAI. Reaproveitar o `launchSettings.json` de outra aula aqui dá `404`.

---

## Como executar

```powershell
az login --tenant <tenant-id>
cd CapacitacaoMicrosoftAIRag
dotnet run
```

```
=== Capacitação Microsoft AI — RAG ===
1. Criar o índice e indexar o corpus
2. Comparar recuperação: keyword × vetorial × híbrida
3. Perguntar SEM RAG
4. Perguntar COM RAG
5. Roteiro de sala: as três perguntas, com e sem RAG
6. Apagar o índice
0. Sair
```

Comece pela opção **1**. O índice leva alguns segundos para ficar consultável depois do upload — se a primeira busca vier vazia, tente de novo.

---

## O roteiro de sala

A opção **5** roda três perguntas, nesta ordem, cada uma sem e com RAG. A ordem importa: cada pergunta desmonta a conclusão apressada da anterior.

| # | Pergunta | Sem RAG | Com RAG | O que a turma aprende |
|---|---|---|---|---|
| 1 | *Qual é o limite de reembolso para jantar em viagem nacional?* | Inventa um valor plausível, com toda a confiança | **R$ 90,00**, com citação | RAG resolve o problema do conhecimento que o modelo não tem |
| 2 | *O que é uma VPN?* | Responde certo | Responde certo, citando um trecho que não vinha ao caso | RAG **não** ajudou aqui — e ainda gastou tokens e latência |
| 3 | *Qual é a política de reembolso para viagens internacionais?* | Inventa uma política inteira | *"Não encontrei essa informação nos documentos disponíveis."* | O comportamento certo é admitir a ausência. Este é o slide de groundedness |

A pergunta 1 é a que todo mundo espera. A 2 é a que evita que a turma saia achando que RAG é sempre a resposta. **A 3 é a que vale a aula**: o documento de reembolso menciona explicitamente que viagens internacionais seguem processo próprio "ainda não publicado nesta base" — ou seja, o assunto está no corpus, mas a resposta não está. É o caso mais difícil e o mais comum em produção.

Se na pergunta 3 o modelo inventar mesmo com RAG, não conserte: **é a melhor coisa que pode acontecer na aula**. Mostre o prompt de sistema, aponte a instrução que manda admitir a ausência, e discuta por que ela nem sempre basta. É daí que sai a necessidade de medir groundedness, tema da aula 5.

---

## Demonstrando com × sem RAG no playground

### Antes de tudo: o "chat playground com seus dados" não existe mais

Se você procurar por *Add your data* no playground de chat, não vai achar. Aquele fluxo é do **Foundry (classic)**. No Foundry atual os playgrounds são **Model**, **Agents**, **Images** e **Video**, e aterrar respostas em dados próprios acontece no **playground de Agents** — dando ao agente uma *knowledge source* ou uma ferramenta.

Ou seja: a comparação com × sem RAG é feita entre **dois agentes**, não entre duas configurações de um chat.

### Três caminhos, e o que cada um custa

| Caminho | Precisa de AI Search? | Precisa de código? | Mostra o mecanismo? |
|---|---|---|---|
| **A. File search** no playground de Model | Não | Não | Não — o serviço faz chunking e embeddings escondido |
| **B. Ferramenta Azure AI Search** no playground de Agents | Sim, com índice pronto | Sim (a opção 1 deste projeto) | **Sim** — é o mesmo índice do console |
| **C. Knowledge base (Foundry IQ)** | Sim, gerenciado | Não | Não, e é assunto da aula 5 |

**Para a aula 4, use o caminho B.** O objetivo da aula é justamente o mecanismo — chunking, embeddings, busca — e o caminho B usa exatamente o índice que a turma acabou de ver ser criado. Console e portal falando com o mesmo índice é o que torna a comparação honesta.

O caminho A é o plano B de cinco minutos: subir os cinco `.md` de `Corpus/` na ferramenta *File search* e pronto. Sacrifica o mecanismo, mas salva a aula se o RBAC não propagar. O caminho C é o gancho para a aula 5, não para esta.

### Caminho B, passo a passo

**1. Crie e popule o índice** — opção **1** do console. Ao fim, o índice `politicas-internas` existe no seu serviço de busca.

**2. Ligue o AI Search ao projeto Foundry.** Isto é feito **uma vez por projeto**:

> Portal do Foundry → seu projeto → **Manage** → **Project details** → aba **Connected resources** → **Add connection** → **Azure AI Search** → escolha o serviço → escolha a autenticação (**Microsoft Entra ID**, sem chave) → **Add connection**.

**3. Dê os papéis à identidade do projeto — não só à sua.** Este é o passo que derruba a maioria das demos. A ferramenta do playground **não usa a sua conta**: ela usa a **identidade gerenciada do recurso Foundry**. Conceder os papéis apenas para você faz o `dotnet run` funcionar e o playground devolver `403`, sem dizer qual identidade foi negada.

```powershell
./scripts/provisionar-ai-search.ps1 `
    -ResourceGroup rg-capacitacao -SearchName srch-capacitacao-01 `
    -FoundryName capacitacao-msai-foundry -FoundryResourceGroup rg-capacitacao
```

Os parâmetros `-FoundryName` e `-FoundryResourceGroup` fazem o script conceder `Search Service Contributor` e `Search Index Data Contributor` também à identidade gerenciada do recurso. Se o recurso Foundry não tiver identidade habilitada, o script avisa — habilite em *portal do Azure → recurso Foundry → Identity → System assigned → On*.

**4. Crie os dois agentes**, no playground de Agents, com o **mesmo deployment**, a **mesma temperatura** e o **mesmo prompt de sistema**:

- **Agente A — "Sem RAG"**: nenhuma knowledge source, nenhuma ferramenta.
- **Agente B — "Com RAG"**: adicione a ferramenta **Azure AI Search**, apontando para a conexão do passo 2 e para o índice `politicas-internas`.

**5. Rode as três perguntas** do [roteiro de sala](#o-roteiro-de-sala), sempre o agente A primeiro.

### O que o índice precisa ter para a ferramenta funcionar

A ferramenta do Foundry exige um formato mínimo, e o índice deste projeto já o atende:

| Requisito | Campo neste projeto |
|---|---|
| Campo `Edm.String` pesquisável e recuperável | `conteudo`, `titulo`, `documento` |
| Campo vetorial `Collection(Edm.Single)` pesquisável | `vetor` |
| Campo recuperável com o texto da citação | `conteudo` |
| Campo recuperável com a URL de origem | `url` |

O campo `url` existe **só por causa do playground**: é ele que transforma a citação em link clicável. O console não usa. Como o corpus são arquivos locais, ele é preenchido com uma URL fictícia e estável (`https://intranet.auroralog.example/politicas/<arquivo>`) — num caso real seria o endereço na intranet ou no SharePoint.

### Onde isso costuma travar

| Sintoma | Causa quase certa |
|---|---|
| `403` no playground, mas o console funciona | Faltam os papéis para a **identidade gerenciada do Foundry** (passo 3) |
| A conexão não aparece na lista de ferramentas | Ela foi criada em outro projeto, ou ainda não propagou — recarregue a página |
| O agente responde sem citar nada | A ferramenta não foi adicionada ao agente, ou o índice está vazio |
| Citação aparece sem link | Falta o campo de URL recuperável no índice |
| Tudo certo e mesmo assim `403` | RBAC leva até 5 minutos para propagar. Espere antes de investigar |

### O fecho da demo

Depois do playground, volte ao console (`dotnet run`, opção 4) e mostre a linha `[recuperados N trecho(s): ...]`. O playground esconde o prompt montado; o console mostra. Ver o contexto sendo injetado desfaz a impressão de que "o modelo aprendeu" o documento — que é a confusão mais comum ao fim desta aula.

---

## Como o código funciona

**Chunking** (`IndiceRag.Fatiar`): quebra por parágrafo e junta vizinhos até ~800 caracteres. Deliberadamente simples, e deliberadamente exposto como parâmetro — mudar de 800 para 200 e reindexar, ao vivo, é o experimento mais barato desta aula.

**Embeddings**: gerados em **um lote** na indexação e **um por pergunta** na consulta. Essa assimetria é o que torna RAG barato em tempo de consulta.

**Os três modos de busca**:

| Modo | O que envia | Acerta | Erra |
|---|---|---|---|
| Keyword | só o texto | códigos, siglas, nomes próprios | sinônimos |
| Vetorial | só o vetor (`searchText` é `null`) | sentido, paráfrase | identificadores literais |
| Híbrida | os dois + reranking semântico | ambos | custa mais latência |

**O prompt de RAG** carrega o peso em duas instruções: responder *exclusivamente* pelos trechos e admitir quando não sabe. Os trechos entram numerados — é isso que torna a citação `[n]` verificável.

**`IsHidden` no campo `vetor`**: o índice guarda o vetor, mas a busca não o devolve. São 1536 floats por resultado que a aplicação nunca lê.

---

## Solução de problemas

### `403 (Forbidden)` no Azure AI Search — no console

Papel faltando ou RBAC ainda propagando. Confirme os dois papéis e espere até 5 minutos:

```powershell
az role assignment list --assignee $(az ad signed-in-user show --query id -o tsv) --scope <id-do-servico> --output table
```

### `403` no playground, mas o console funciona

São identidades diferentes. O console usa a **sua** conta; o playground usa a **identidade gerenciada do recurso Foundry**. Rode o script de provisionamento com `-FoundryName` e `-FoundryResourceGroup`.

### `403` no Foundry

Falta o papel `Foundry User` no recurso Foundry. É outro serviço e outro papel — não confunda com os do Search.

### A busca não retorna nada logo após indexar

Normal. O índice leva alguns segundos para ficar consultável. Tente de novo.

### `The vector field dimension does not match`

O `DimensoesDoEmbedding` (1536) não bate com o modelo de embeddings implantado. `text-embedding-3-large` usa 3072. Ajuste a constante em `IndiceRag.cs`, apague o índice (opção 6) e reindexe — o serviço não altera a dimensão de um campo existente.

### O modo híbrido falha com erro de configuração semântica

O SKU `free` não tem ranqueamento semântico. Use `basic` ou superior.

### `MissingMethodException` em tempo de execução

Algum pacote entrou no `.csproj` e mudou o pivô do `OpenAI`. Veja [Como construir](#como-construir).

---

## Notas para quem ministra o treinamento

**Indexe antes da aula.** A indexação é a parte chata e a que mais falha por RBAC. Chegue com o índice pronto e reserve o tempo de aula para as perguntas.

**Mostre a recuperação antes da geração.** A opção 2 com uma pergunta que usa sinônimos — *"quanto posso gastar com comida à noite viajando?"* — deixa a diferença entre keyword e vetorial evidente em uma tela. Sem isso, RAG vira mágica.

**Deixe o modelo errar.** A pergunta 1 sem RAG produz um valor inventado com aparência de verdade. É o argumento mais forte da aula inteira, e ele só funciona se você deixar acontecer.

**O corpus é curto de propósito.** Cinco documentos cabem na cabeça da turma; um corpus grande esconde o mecanismo. Se alguém perguntar "e com dez mil documentos?", essa é exatamente a ponte para a aula 5.

**Prepare o plano B.** Portal ao vivo falha. O console faz a mesma demonstração e não depende de propagação de conexão.
