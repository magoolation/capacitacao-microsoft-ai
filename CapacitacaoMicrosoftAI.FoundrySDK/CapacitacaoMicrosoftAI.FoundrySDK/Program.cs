// =============================================================================
// Capacitação Microsoft AI — Foundry SDK: chat multi-turn com streaming
//
// O que este exemplo demonstra:
//   1. A mesma montagem das outras aulas: Foundry SDK + Microsoft.Extensions.AI.
//   2. Streaming token-a-token com GetStreamingResponseAsync.
//   3. Contexto mantido pelo SERVIÇO, via ChatOptions.ConversationId.
//
// Contraste com a aula de Fundamentos: lá a resposta chega inteira, de uma vez.
// Aqui ela chega em pedaços, e a diferença aparece na percepção de latência —
// é o mesmo modelo, o mesmo endpoint, a mesma identidade e o MESMO IChatClient.
// A única troca é GetResponseAsync por GetStreamingResponseAsync.
// =============================================================================

// Azure.AI.Projects: o "Foundry SDK". Porta de entrada do projeto.
using Azure.AI.Projects;

// Azure.AI.Extensions.OpenAI: os clientes que falam "dialeto OpenAI"
// (Responses, Files, Vector Stores) já apontados para o seu projeto Foundry.
using Azure.AI.Extensions.OpenAI;

// Azure.Identity: descobre "quem é você" a partir do ambiente.
using Azure.Identity;

// Microsoft.Extensions.AI: a abstração. Note que o tipo das atualizações de
// streaming é ChatResponseUpdate — um tipo neutro, não os tipos de streaming do
// pacote OpenAI. É essa a diferença de usar a abstração: o laço abaixo funciona
// igual contra qualquer provedor.
using Microsoft.Extensions.AI;

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
// Endpoint DO PROJETO:
//   https://<recurso>.services.ai.azure.com/api/projects/<projeto>
// É o mesmo das outras duas aulas — as três entram pelo Foundry SDK.
var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");

// Nome do DEPLOYMENT do modelo no Foundry — não é o nome comercial do modelo.
var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");

if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
{
    Console.WriteLine("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL antes de executar.");
    return;
}

// -----------------------------------------------------------------------------
// 2. Autenticação
// -----------------------------------------------------------------------------
// O Foundry SDK autentica SEMPRE por Entra ID — não existe construtor que aceite
// chave de API. Autenticar não é autorizar: sua conta precisa do papel
// "Foundry User" no recurso. Ser Owner da subscription NÃO basta.
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

// -----------------------------------------------------------------------------
// 3. Do Foundry SDK até IChatClient
// -----------------------------------------------------------------------------
// ProjectResponsesClient herda de OpenAI.Responses.ResponsesClient: é a mesma
// API da OpenAI, já apontada para o seu projeto e autenticada pelo Entra ID.
// AsIChatClient() a embrulha na abstração neutra.
//
// Estas três linhas são idênticas nas três aulas. Vale reparar: elas são todo o
// "setup" que separa uma credencial do Azure de um IChatClient pronto para uso.
ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);

// -----------------------------------------------------------------------------
// 4. Memória da conversa
// -----------------------------------------------------------------------------
// Por baixo está a Responses API, onde quem guarda o histórico é o SERVIÇO. O
// cliente só precisa lembrar do ConversationId da última resposta — uma string,
// não uma lista que cresce (compare com a demo 1 da aula de LLM e Prompts).
ChatOptions options = new();

// -----------------------------------------------------------------------------
// 5. Laço de conversa, com streaming
// -----------------------------------------------------------------------------
Console.WriteLine("Chat iniciado. Digite sua pergunta (ou 'sair' para encerrar).\n");

while (true)
{
    Console.Write("Você: ");
    var prompt = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(prompt))
    {
        continue;
    }

    if (prompt.Equals("sair", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    Console.Write("IA: ");

    // A chamada devolve um fluxo assíncrono de atualizações. Cada uma carrega
    // um pedaço da resposta; escrevemos na hora, sem esperar o resto.
    await foreach (ChatResponseUpdate update
        in chatClient.GetStreamingResponseAsync(prompt, options))
    {
        Console.Write(update.Text);

        // O ConversationId chega junto com as atualizações. Guardamos o último
        // valor não nulo: é ele que liga esta resposta à próxima pergunta.
        if (update.ConversationId is not null)
        {
            options.ConversationId = update.ConversationId;
        }
    }

    Console.WriteLine("\n");
}
