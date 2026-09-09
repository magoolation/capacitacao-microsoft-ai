// =============================================================================
// Capacitação Microsoft AI — RAG com Azure AI Search e Microsoft Foundry
//
// O que este exemplo demonstra:
//   1. Indexação: chunking → embeddings → Azure AI Search.
//   2. Recuperação nos três modos: keyword, vetorial e híbrida com reranking.
//   3. A MESMA pergunta respondida SEM e COM RAG, lado a lado.
//   4. Citações: de qual documento veio cada afirmação.
//
// O ponto da aula não é "RAG é melhor". É: RAG resolve um problema específico —
// o modelo não conhece os seus documentos. Fora desse problema, ele não ajuda,
// e às vezes atrapalha. As três perguntas do item 5 do menu foram escolhidas
// para mostrar exatamente isso.
// =============================================================================

using System.ClientModel.Primitives;

using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

using CapacitacaoMicrosoftAIRag;

using Microsoft.Extensions.AI;

using OpenAI;

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
var foundryEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");
var modeloDeChat = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");
var modeloDeEmbedding = Environment.GetEnvironmentVariable("FOUNDRY_EMBEDDING_MODEL");
var searchEndpoint = Environment.GetEnvironmentVariable("SEARCH_ENDPOINT");
var nomeDoIndice = Environment.GetEnvironmentVariable("SEARCH_INDEX") ?? "politicas-internas";

if (string.IsNullOrWhiteSpace(foundryEndpoint) || string.IsNullOrWhiteSpace(modeloDeChat)
    || string.IsNullOrWhiteSpace(modeloDeEmbedding) || string.IsNullOrWhiteSpace(searchEndpoint))
{
    Console.WriteLine("""
        Faltam variáveis de ambiente. Defina, em Properties/launchSettings.json:
          FOUNDRY_ENDPOINT          https://<recurso>.services.ai.azure.com/openai/v1
          FOUNDRY_MODEL             deployment do modelo de chat
          FOUNDRY_EMBEDDING_MODEL   deployment do modelo de embeddings
          SEARCH_ENDPOINT           https://<servico>.search.windows.net
          SEARCH_INDEX              nome do índice (padrão: politicas-internas)
        """);
    return;
}

// -----------------------------------------------------------------------------
// 2. Clientes
// -----------------------------------------------------------------------------
// Uma credencial só para os dois serviços. Nenhuma chave de API em lugar nenhum:
// o Foundry e o AI Search autenticam por Entra ID, com papéis diferentes.
var credencial = new DefaultAzureCredential();

// Foundry, pelo protocolo da OpenAI. Diferente das aulas 1 a 3, que entram pelo
// AIProjectClient: sem o Azure.AI.Projects no .csproj (o comentario la explica por
// que), o cliente aqui e o OpenAIClient - e por isso o endpoint termina em /openai/v1.
var tokenPolicy = new BearerTokenPolicy(credencial, "https://ai.azure.com/.default");
OpenAIClient openAIClient = new(tokenPolicy, new OpenAIClientOptions
{
    Endpoint = new Uri(foundryEndpoint)
});

IChatClient chat = openAIClient.GetChatClient(modeloDeChat).AsIChatClient();
IEmbeddingGenerator<string, Embedding<float>> embeddings =
    openAIClient.GetEmbeddingClient(modeloDeEmbedding).AsIEmbeddingGenerator();

// Azure AI Search. Precisa de DOIS papéis distintos:
//   Search Service Contributor    → criar e apagar índices
//   Search Index Data Contributor → gravar e ler documentos
var uriDaBusca = new Uri(searchEndpoint);
var indexClient = new SearchIndexClient(uriDaBusca, credencial);
var searchClient = new SearchClient(uriDaBusca, nomeDoIndice, credencial);

var indice = new IndiceRag(indexClient, searchClient, embeddings, nomeDoIndice);

var pastaDoCorpus = Path.Combine(AppContext.BaseDirectory, "Corpus");

// -----------------------------------------------------------------------------
// 3. Menu
// -----------------------------------------------------------------------------
while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== Capacitação Microsoft AI — RAG ===");
    Console.WriteLine("1. Criar o índice e indexar o corpus");
    Console.WriteLine("2. Comparar recuperação: keyword × vetorial × híbrida");
    Console.WriteLine("3. Perguntar SEM RAG");
    Console.WriteLine("4. Perguntar COM RAG");
    Console.WriteLine("5. Roteiro de sala: as três perguntas, com e sem RAG");
    Console.WriteLine("6. Apagar o índice");
    Console.WriteLine("0. Sair");
    Console.Write("Opção: ");

    switch (Console.ReadLine())
    {
        case "1": await IndexarAsync(); break;
        case "2": await CompararRecuperacaoAsync(); break;
        case "3": await PerguntarAsync(Ler("Pergunta: "), comRag: false); break;
        case "4": await PerguntarAsync(Ler("Pergunta: "), comRag: true); break;
        case "5": await RoteiroDeSalaAsync(); break;
        case "6": await indice.ApagarIndiceAsync(); Console.WriteLine("Índice apagado."); break;
        case "0": return;

        // Console.ReadLine() devolve null quando a ENTRADA ACABA — entrada
        // redirecionada que chegou ao fim, Ctrl+Z, terminal encerrado. Sem este
        // caso, o null cai no `default` e o laço nunca termina: o programa fica
        // imprimindo "Opção inválida" para sempre, a 100% de um núcleo, e o
        // processo órfão ainda segura o .exe — o que faz o build seguinte falhar
        // com MSB3021 "arquivo em uso", um erro sem relação aparente com o menu.
        case null: return;

        default: Console.WriteLine("Opção inválida."); break;
    }
}

static string Ler(string rotulo)
{
    Console.Write(rotulo);
    return Console.ReadLine() ?? "";
}

// -----------------------------------------------------------------------------
// Indexação
// -----------------------------------------------------------------------------
async Task IndexarAsync()
{
    Console.WriteLine("\nCriando o índice...");
    await indice.CriarOuAtualizarIndiceAsync();

    Console.WriteLine("Fatiando e vetorizando o corpus:");
    var total = await indice.IndexarAsync(pastaDoCorpus, Console.WriteLine);

    Console.WriteLine($"\n{total} chunk(s) indexado(s) em '{nomeDoIndice}'.");
    Console.WriteLine("O índice leva alguns segundos para ficar consultável.");
}

// -----------------------------------------------------------------------------
// Comparação de modos de recuperação
// -----------------------------------------------------------------------------
// Rodar isto com uma pergunta que usa SINÔNIMOS do corpus (e não as palavras
// exatas) é o que torna a diferença visível: a keyword não acha, a vetorial acha.
async Task CompararRecuperacaoAsync()
{
    var pergunta = Ler("\nPergunta para buscar: ");

    foreach (var modo in new[] { IndiceRag.Modo.Keyword, IndiceRag.Modo.Vetorial, IndiceRag.Modo.Hibrida })
    {
        Console.WriteLine($"\n--- {modo} ---");
        var achados = await indice.BuscarAsync(pergunta, modo);

        if (achados.Count == 0)
        {
            Console.WriteLine("  (nada encontrado)");
            continue;
        }

        foreach (var (trecho, score) in achados)
        {
            var previa = trecho.Conteudo.ReplaceLineEndings(" ");
            if (previa.Length > 90)
            {
                previa = previa[..90] + "...";
            }

            Console.WriteLine($"  [{score,6:F3}] {trecho.Documento,-30} {previa}");
        }
    }
}

// -----------------------------------------------------------------------------
// A pergunta, com e sem RAG
// -----------------------------------------------------------------------------
async Task PerguntarAsync(string pergunta, bool comRag)
{
    if (string.IsNullOrWhiteSpace(pergunta))
    {
        return;
    }

    List<ChatMessage> mensagens;

    if (comRag)
    {
        var achados = await indice.BuscarAsync(pergunta, IndiceRag.Modo.Hibrida);

        // O contexto entra numerado. É isso que permite ao modelo citar [1], [2]
        // e ao usuário conferir. Sem numeração não há citação verificável.
        var contexto = string.Join("\n\n", achados.Select((r, i) =>
            $"[{i + 1}] ({r.Trecho.Documento}) {r.Trecho.Conteudo}"));

        // Duas instruções carregam todo o peso: responder SÓ pelo contexto e
        // admitir quando não sabe. Sem a segunda, o modelo preenche a lacuna
        // com o que ele acha que sabe — e a demo perde a graça.
        mensagens =
        [
            new(ChatRole.System, """
                Você responde perguntas sobre as políticas internas da empresa.
                Use EXCLUSIVAMENTE os trechos numerados fornecidos como contexto.
                Cite a fonte de cada afirmação no formato [n].
                Se a resposta não estiver nos trechos, responda exatamente:
                "Não encontrei essa informação nos documentos disponíveis."
                Não complete lacunas com conhecimento geral.
                """),
            new(ChatRole.User, $"Contexto:\n{contexto}\n\nPergunta: {pergunta}")
        ];

        Console.WriteLine($"\n[recuperados {achados.Count} trecho(s): " +
                          $"{string.Join(", ", achados.Select(a => a.Trecho.Documento).Distinct())}]");
    }
    else
    {
        mensagens =
        [
            new(ChatRole.System, "Você responde perguntas sobre políticas internas de empresas."),
            new(ChatRole.User, pergunta)
        ];
    }

    Console.Write(comRag ? "\nCOM RAG: " : "\nSEM RAG: ");

    await foreach (var pedaco in chat.GetStreamingResponseAsync(mensagens))
    {
        Console.Write(pedaco);
    }

    Console.WriteLine();
}

// -----------------------------------------------------------------------------
// Roteiro de sala
// -----------------------------------------------------------------------------
// Três perguntas, escolhidas para mostrar três comportamentos diferentes.
// Rodar nesta ordem: cada uma desmonta a conclusão apressada da anterior.
async Task RoteiroDeSalaAsync()
{
    (string Pergunta, string OQueObservar)[] roteiro =
    [
        ("Qual é o limite de reembolso para jantar em viagem nacional?",
         "Só o corpus responde. Sem RAG o modelo inventa um valor plausível; com RAG acerta e cita."),

        ("O que é uma VPN?",
         "Conhecimento geral. Os dois acertam — RAG não ajudou em nada aqui."),

        ("Qual é a política de reembolso para viagens internacionais?",
         "Parece estar no corpus, mas não está. Com RAG a resposta correta é admitir que não encontrou."),
    ];

    foreach (var (pergunta, observar) in roteiro)
    {
        Console.WriteLine($"\n{new string('=', 78)}");
        Console.WriteLine($"PERGUNTA: {pergunta}");
        Console.WriteLine($"O que observar: {observar}");
        Console.WriteLine(new string('=', 78));

        await PerguntarAsync(pergunta, comRag: false);
        await PerguntarAsync(pergunta, comRag: true);

        Console.WriteLine("\n(Enter para a próxima)");
        Console.ReadLine();
    }
}
