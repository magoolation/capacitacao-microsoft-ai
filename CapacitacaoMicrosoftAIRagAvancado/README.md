# Capacitação Microsoft AI — RAG end-to-end com avaliação

A quinta aula pega o RAG da aula 4, que já funcionava, e responde a pergunta que a aula 4 não conseguia responder: **funcionava bem?**

O projeto acrescenta três coisas ao anterior: um pipeline que devolve **objeto em vez de texto**, quatro **métricas de qualidade** medidas por um modelo juiz, e os dois **experimentos** que transformam "acho que melhorou" em uma tabela — a varredura de top-k e a comparação com e sem ranqueamento semântico.

---

## Sumário

- [O que este projeto demonstra](#o-que-este-projeto-demonstra)
- [Pré-requisitos](#pré-requisitos)
- [Como construir](#como-construir)
- [Configuração](#configuração)
- [Como executar](#como-executar)
- [As quatro métricas](#as-quatro-métricas)
- [O conjunto de avaliação](#o-conjunto-de-avaliação)
- [Os dois experimentos](#os-dois-experimentos)
- [Como o código funciona](#como-o-código-funciona)
- [Solução de problemas](#solução-de-problemas)
- [Notas para quem ministra o treinamento](#notas-para-quem-ministra-o-treinamento)

---

## O que este projeto demonstra

1. **Um pipeline avaliável.** `PipelineRag.ResponderAsync` devolve a resposta **junto com** os trechos recuperados, o contexto exato enviado, a latência e os tokens. Sem isso não há o que medir.
2. **Quatro métricas** do `Microsoft.Extensions.AI.Evaluation.Quality`: groundedness, relevance, retrieval e correctness — cada uma comparando um par diferente de coisas.
3. **Duas conferências determinísticas** que não custam chamada de modelo: recuperou os documentos esperados? recusou exatamente quando devia?
4. **A varredura de top-k** (3 × 5 × 10) com qualidade e custo lado a lado.
5. **O relatório em Markdown**, que é o entregável do desafio da aula.

O ponto da aula não é "avaliar é bom". É que **groundedness e retrieval são métricas separadas de propósito**: uma aponta para a geração, a outra para a recuperação. Sem separá-las, quando a qualidade cai o diagnóstico vira adivinhação.

---

## Pré-requisitos

Os mesmos da aula 4 — nada de novo:

| Item | Detalhe |
|---|---|
| **.NET SDK 10.0** | `dotnet --version` deve responder `10.x` |
| **Azure CLI** | `az login --tenant <tenant-id>` antes de tudo |
| **Recurso Foundry** | Com **dois** deployments: um de chat e um de **embeddings** |
| **Azure AI Search** | SKU `basic` ou superior. O `free` **não** tem ranqueamento semântico |
| **Papéis no Search** | `Search Service Contributor` e `Search Index Data Contributor` |

O script [`provisionar-ai-search.ps1`](../CapacitacaoMicrosoftAIRag/scripts/) da aula 4 serve esta aula sem alteração. O índice também: se a aula 4 já rodou, **o índice desta aula já existe**.

> **O modelo juiz é o mesmo deployment de chat, por padrão.** Não é preciso implantar nada a mais. A variável `FOUNDRY_JUDGE_MODEL` existe para quem quiser usar um modelo diferente — o que reduz o viés de um modelo se avaliar bem.

---

## Como construir

```powershell
cd CapacitacaoMicrosoftAIRagAvancado
dotnet restore
dotnet build
```

**Não mexa nas versões de pacote.** Este projeto converge para **`OpenAI 2.12.0`**, o mesmo pivô da aula 4:

| Pacote declarado | Versão | Traz `OpenAI` |
|---|---|---|
| `Microsoft.Extensions.AI` | 10.9.0 | — |
| `Microsoft.Extensions.AI.OpenAI` | 10.9.0 | [2.12.0, 2.13.0) |
| `Microsoft.Extensions.AI.Evaluation` | 10.9.0 | **não** |
| `Microsoft.Extensions.AI.Evaluation.Quality` | 10.9.0 | **não** |
| `Azure.Search.Documents` | 12.0.0 | **não** |
| `Azure.Identity` | 1.21.0 | — |

**A aula 5 acrescenta dois pacotes ao grafo da aula 4 e o pivô não muda.** Isso não é sorte: os pacotes de avaliação falam com o modelo pela abstração `IChatClient`, e por isso não dependem de SDK de provedor nenhum. É o argumento prático a favor da abstração — e vale dizer isso em sala.

As quatro versões dos pacotes `Microsoft.Extensions.AI*` precisam ser **iguais**: `Evaluation.Quality` 10.9.0 exige `Evaluation` 10.9.0 exato.

---

## Configuração

```powershell
copy CapacitacaoMicrosoftAIRagAvancado\Properties\launchSettings.template.json `
     CapacitacaoMicrosoftAIRagAvancado\Properties\launchSettings.json
```

| Variável | Obrigatória | Descrição |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Sim | `https://<recurso>.services.ai.azure.com/openai/v1` |
| `FOUNDRY_MODEL` | Sim | Deployment do modelo de **chat** |
| `FOUNDRY_EMBEDDING_MODEL` | Sim | Deployment do modelo de **embeddings** |
| `FOUNDRY_JUDGE_MODEL` | Não | Deployment do **juiz**. Padrão: `FOUNDRY_MODEL` |
| `SEARCH_ENDPOINT` | Sim | `https://<servico>.search.windows.net` |
| `SEARCH_INDEX` | Não | Padrão: `politicas-internas` — o mesmo da aula 4 |
| `RAG_CHUNK_ALVO` | Não | Tamanho-alvo do chunk em caracteres. Padrão: `800` |
| `AZURE_TENANT_ID` | Recomendada | GUID do tenant |
| `AZURE_TOKEN_CREDENTIALS` | Recomendada | Ex.: `AzureCliCredential` |

> O endpoint é o `/openai/v1`, como na aula 4 — **não** o `/api/projects/<projeto>` das aulas 1 a 3.

---

## Como executar

```powershell
az login --tenant <tenant-id>
cd CapacitacaoMicrosoftAIRagAvancado\CapacitacaoMicrosoftAIRagAvancado
dotnet run
```

> **Repare no caminho duplicado.** A pasta de fora é a da **solução** (só tem o `.slnx`); a de dentro é a do **projeto**. Rodar `dotnet run` na de fora falha com *"Não foi possível localizar um projeto para executar"*. Se preferir não navegar, rode de qualquer lugar com o caminho explícito:
>
> ```powershell
> dotnet run --project CapacitacaoMicrosoftAIRagAvancado\CapacitacaoMicrosoftAIRagAvancado
> ```

```
=== Aula 5 — RAG end-to-end com avaliação ===
1. Criar o índice e indexar o corpus
2. Perguntar (híbrida + reranking, com citações e custo)
3. Avaliar UMA pergunta do conjunto — as quatro métricas
4. Avaliar o conjunto inteiro
5. Varredura de top-k: 3 × 5 × 10
6. O que o ranqueador semântico ganha (com × sem reranking)
7. Gravar o relatório em Markdown
8. Apagar o índice
0. Sair
```

Se a aula 4 já indexou, pule a opção 1. Comece pela **3**, com a pergunta **6**.

---

## As quatro métricas

O erro conceitual mais comum é achar que elas medem a mesma coisa com nomes diferentes. Cada uma compara um **par** distinto:

| Métrica | Compara | A pergunta que faz | Aponta para |
|---|---|---|---|
| **Groundedness** | resposta × **contexto recuperado** | "O que foi afirmado está no material?" | A **geração** |
| **Relevance** | resposta × **pergunta** | "Respondeu o que foi perguntado?" | A **geração** |
| **Retrieval** | **trechos** × pergunta | "Os trechos trazidos serviam?" | A **recuperação** |
| **Correctness** | resposta × **gabarito** | "Está certa?" | O sistema todo |

Todas devolvem **1 a 5**, com uma justificativa em texto — e a justificativa vale tanto quanto a nota: é ela que transforma "caiu de 4,2 para 3,1" numa tarefa.

**O caso perigoso:** relevance alta com groundedness baixa. A resposta parece ótima e é inventada.

**A separação que importa:** as três primeiras olham para a resposta; **retrieval olha para o que veio antes dela**. Se a resposta piorou e o retrieval continua bom, o problema é o prompt ou a geração. Se os dois caíram, é chunking, embedding, filtro ou índice.

> **O juiz é um LLM.** Ele erra, é sensível ao próprio prompt, custa uma chamada por métrica e **responde em inglês** — os prompts dos avaliadores são internos ao pacote. Ele não é a verdade; é um instrumento barato o bastante para rodar a cada mudança, o que nenhum painel humano é.

### As duas conferências que não custam nada

Rodam sempre, são determinísticas e, quando discordam do juiz, costumam estar certas:

- **Recuperou os documentos esperados?** Compara os arquivos recuperados com `DocumentosEsperados`.
- **Recusou exatamente quando devia?** Compara a recusa observada com `DeveRecusar`.

Comece por elas ao investigar uma queda. Metade do diagnóstico é um `Contains`.

---

## O conjunto de avaliação

Seis perguntas em [`ConjuntoDeAvaliacao.cs`](CapacitacaoMicrosoftAIRagAvancado/ConjuntoDeAvaliacao.cs), **escolhidas e não sorteadas**:

| # | Tipo | O que ela testa |
|---|---|---|
| 1 | Fácil, um chunk | Se falhar, o problema é de indexação, não de ajuste fino |
| 2 | Termo literal (`severidade 1`) | Onde BM25 brilha e a vetorial não agrega |
| 3 | Fácil com ressalva no mesmo chunk | Se a resposta é **completa**, não só correta |
| 4 | **Exige dois documentos** | A que mais falha — e a que justifica agentic retrieval |
| 5 | Resposta é uma **negativa presente** no documento | Diferente de recusar por ausência; modelos confundem |
| 6 | **Não tem resposta no corpus** | O caso que vale a aula |

A pergunta 6 é a mais rica: o assunto **está** no corpus (o documento de reembolso diz que viagens ao exterior seguem processo próprio, não publicado), mas a resposta **não está**. A recuperação acerta; a geração é que precisa se conter.

Em produção o conjunto começa com as perguntas que os usuários realmente fizeram, e cresce toda vez que alguém reclama de uma resposta.

---

## Os dois experimentos

### Varredura de top-k (opção 5)

Roda o conjunto em 3, 5 e 10, medindo só groundedness e retrieval — as duas que dizem **qual etapa mudou** — com tokens e latência ao lado.

Como ler a tabela:

| O que você vê | O que significa |
|---|---|
| Retrieval subiu, groundedness igual | Mais contexto ajudou a achar |
| Retrieval igual, tokens subiram | **Você está pagando por nada** |
| Groundedness caiu com top-k maior | Entrou ruído; o modelo se apoiou no trecho errado |

> **Com o corpus padrão a curva sai plana**, e essa é a resposta certa: 5 documentos viram 12 chunks, então top-k 10 traz quase o corpus inteiro e não há o que a busca decida. O que muda é só o custo. Para ver a curva aparecer, reindexe com chunks menores (`RAG_CHUNK_ALVO=300`) — mais chunks, cada um mais estreito, e o top-k volta a decidir o que entra no prompt.

### Com × sem ranqueamento semântico (opção 6)

O slide da aula promete "+5 a +15 pontos de relevance". Esse número vem de corpora grandes e heterogêneos: **num corpus de cinco documentos o ganho é pequeno, às vezes nulo**. Meça em vez de acreditar — e diga isso em sala. Ensinar a turma a desconfiar de número de slide, inclusive dos seus, vale mais do que um resultado bonito.

---

## Como o código funciona

**`PipelineRag`** — recuperar → montar contexto numerado → gerar → devolver `RespostaRag`, que carrega resposta, trechos, contexto, latência e tokens. O tipo de retorno **é** a mudança de arquitetura da aula: um pipeline que só imprime texto é um pipeline que não pode ser avaliado.

**`AvaliadorRag`** — envolve os quatro avaliadores. As chamadas ao juiz são independentes e rodam **em paralelo** (`.AsTask()` + `Task.WhenAll`); o conjunto roda com no máximo **três perguntas simultâneas**, porque acima disso o deployment começa a devolver `429`.

**`ConjuntoDeAvaliacao`** — as perguntas com gabarito. É o artefato mais subestimado de um projeto de RAG: sem ele, "melhorou" é opinião.

**`IndiceRag`** — vem da aula 4 quase intacto. As duas diferenças: `topK` virou parâmetro obrigatório de `BuscarAsync`, e `Modo` ganhou `HibridaComReranking` separado de `Hibrida`, para que a opção 6 possa medir a diferença entre os dois.

**O que o juiz recebe como "a conversa"** é só a pergunta do usuário. O prompt de sistema com os trechos entra como **contexto de avaliação**, não como mensagem. Misturar os dois faz o juiz avaliar o seu prompt em vez da sua resposta.

---

## Solução de problemas

### As notas vêm em inglês

Esperado. Os prompts dos avaliadores são internos ao `Microsoft.Extensions.AI.Evaluation.Quality` e estão em inglês. As notas são numéricas; só a justificativa é texto.

### `429 Too Many Requests` durante a opção 4 ou 5

Cota do deployment. O código já limita a três perguntas simultâneas — reduza para 1 em `AvaliadorRag.AvaliarConjuntoAsync` ou aumente a capacidade do deployment.

### A varredura de top-k não mostra diferença nenhuma

É o resultado esperado com o corpus padrão — veja [Os dois experimentos](#os-dois-experimentos). Reindexe com `RAG_CHUNK_ALVO=300`.

### `NotSupportedException` ou erro de `temperature` no modelo juiz

Modelos de raciocínio não aceitam alguns parâmetros de amostragem. Os avaliadores do pacote não os enviam por padrão; se você customizou o `ChatConfiguration`, remova a temperatura.

### O modo híbrido falha com erro de configuração semântica

O SKU `free` não tem ranqueamento semântico. Use `basic` ou superior.

### `MissingMethodException` em runtime

Algum pacote mudou o pivô do `OpenAI`. Veja [Como construir](#como-construir) e rode `dotnet nuget why <csproj> OpenAI`.

### O relatório foi parar num diretório estranho

Ele é gravado no **diretório de trabalho atual**, não na pasta do projeto. Rode `dotnet run` de dentro da pasta da solução.

---

## Notas para quem ministra o treinamento

**Rode a opção 4 uma vez antes da turma chegar.** As opções 4 e 5 fazem dezenas de chamadas ao juiz, e a primeira execução do dia é sensivelmente mais lenta.

**Comece pela opção 3, pergunta 6.** O resultado — groundedness 5, correctness 5, **retrieval 2** — é o melhor material da aula. O sistema fez a coisa certa (recusou) e mesmo assim o retrieval pontua baixo, porque os trechos de fato não respondem à pergunta. Métrica não é nota de prova: é instrumento, e quem interpreta é você.

**Peça um palpite antes da varredura.** Quase todo mundo aposta que top-k 10 é melhor. A tabela discorda, e a discordância é a aula.

**Deixe a pergunta 4 falhar.** A de dois documentos costuma sair pela metade — o modelo responde uma parte e diz que não achou a outra, mesmo tendo recuperado os dois arquivos. "Recuperar não é o mesmo que usar" é uma frase que a turma leva para casa.

**Troque `FOUNDRY_JUDGE_MODEL` ao vivo** se alguém desconfiar de uma nota. Ver as notas mudarem com o juiz é a demonstração mais curta de que a avaliação é um instrumento, não uma medida absoluta.
