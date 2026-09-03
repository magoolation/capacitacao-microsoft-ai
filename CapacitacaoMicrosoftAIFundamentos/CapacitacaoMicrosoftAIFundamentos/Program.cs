// =============================================================================
// Capacitação Microsoft AI — Fundamentos
// Chat de console conversando com um modelo hospedado no Microsoft Foundry.
//
// O que este exemplo demonstra:
//   1. Como autenticar no Foundry usando identidade do Entra ID (sem chave).
//   2. Como montar um IChatClient (Microsoft.Extensions.AI) sobre o Foundry SDK.
//   3. Como manter o contexto da conversa SEM guardar histórico no cliente.
//
// As três aulas do repositório usam esta mesma montagem: o Foundry SDK para
// chegar ao projeto com a identidade do Entra ID, e Microsoft.Extensions.AI
// como camada de programação. O que muda de uma aula para outra é o que se faz
// com o IChatClient — não como ele é criado.
// =============================================================================

// Azure.AI.Projects: o "Foundry SDK". Dá acesso a tudo que é do projeto
// (modelos, agents, avaliações, conexões) por um único endpoint.
using Azure.AI.Projects;

// Azure.AI.Extensions.OpenAI: traz os clientes que falam "dialeto OpenAI"
// (Responses, Conversations, Files) já apontados para o seu projeto Foundry.
using Azure.AI.Extensions.OpenAI;

// Azure.Identity: descobre "quem é você" a partir do ambiente
// (az login, Visual Studio, identidade gerenciada em produção...).
using Azure.Identity;

// Microsoft.Extensions.AI: a abstração. IChatClient, ChatMessage, ChatOptions e
// os métodos de extensão GetResponseAsync / GetStreamingResponseAsync.
// Nada aqui é específico de OpenAI, Foundry, Ollama ou Anthropic.
using Microsoft.Extensions.AI;

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
// Nada fica escrito no código: endpoint e modelo vêm de variáveis de ambiente.
// Em desenvolvimento elas são definidas em Properties/launchSettings.json.
// Em produção viriam do App Service, Container Apps, Key Vault etc.

// Endpoint DO PROJETO, no formato:
//   https://<recurso>.services.ai.azure.com/api/projects/<projeto>
// É o mesmo nas três aulas, porque as três entram pelo Foundry SDK.
var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");

// Nome do DEPLOYMENT do modelo no Foundry — não é o nome comercial do modelo.
// Você escolhe esse nome ao implantar o modelo no portal.
var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");

// Falhar cedo e com mensagem clara é melhor do que estourar
// uma NullReferenceException lá na frente.
if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
{
    Console.WriteLine("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL antes de executar.");
    return;
}

// -----------------------------------------------------------------------------
// 2. Autenticação
// -----------------------------------------------------------------------------
// O Foundry SDK autentica SEMPRE por Entra ID — não existe construtor que
// aceite chave de API. Isso é uma decisão de segurança: não há segredo para
// vazar em código, log ou repositório.
//
// DefaultAzureCredential testa várias fontes de credencial em ordem e usa a
// primeira que responder (az login, Visual Studio, VS Code, identidade
// gerenciada...). É o que faz o MESMO código funcionar na sua máquina e no
// Azure sem alteração.
//
// IMPORTANTE: autenticar (provar quem você é) não é o mesmo que autorizar
// (ter permissão). Sua conta precisa do papel "Foundry User" no recurso.
// Ser Owner da subscription NÃO basta — veja a seção RBAC do README.
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

// -----------------------------------------------------------------------------
// 3. Do Foundry SDK até IChatClient
// -----------------------------------------------------------------------------
// Três camadas, de baixo para cima:
//
//   AIProjectClient            → porta de entrada do projeto Foundry
//      └── ProjectResponsesClient → cliente da Responses API para UM deployment
//             └── IChatClient      → a abstração neutra que o resto do código usa
//
// AsIChatClient() é o adaptador entre os dois mundos. Daqui para baixo é Azure;
// daqui para cima o código não menciona mais nem Azure nem OpenAI — trocar o
// modelo por um Ollama local é mexer nestas linhas, e em nada mais.
//
// (É este método que exige o NoWarn OPENAI001 no .csproj: ele ainda está
// marcado como experimental no pacote.)
ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);

// -----------------------------------------------------------------------------
// 4. Memória da conversa
// -----------------------------------------------------------------------------
// Este é o ponto mais interessante do exemplo.
//
// Por baixo do IChatClient está a Responses API, e nela quem guarda a conversa
// é o SERVIÇO. Cada resposta volta com um identificador; ao enviar a próxima
// pergunta você devolve esse identificador e o modelo recebe todo o contexto.
//
// Em Microsoft.Extensions.AI isso aparece como ChatOptions.ConversationId. Por
// isso a única coisa que precisamos lembrar entre uma pergunta e outra é uma
// string — não uma lista que cresce.
//
// Começa null porque a primeira pergunta não tem nada antes dela.
ChatOptions options = new();

// -----------------------------------------------------------------------------
// 5. Laço de conversa
// -----------------------------------------------------------------------------
Console.WriteLine("Chat iniciado. Digite sua pergunta (ou 'sair' para encerrar).\n");

while (true)
{
    Console.Write("Você: ");
    var prompt = Console.ReadLine();

    // Enter vazio: não faz sentido gastar uma chamada, apenas pergunta de novo.
    if (string.IsNullOrWhiteSpace(prompt))
    {
        continue;
    }

    // OrdinalIgnoreCase aceita "sair", "Sair", "SAIR".
    if (prompt.Equals("sair", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    // A chamada de rede. Note que enviamos apenas a pergunta ATUAL mais o
    // ConversationId da resposta anterior — o histórico não trafega.
    // Usamos a versão Async para não bloquear a thread enquanto o modelo pensa.
    var resposta = await chatClient.GetResponseAsync(prompt, options);

    // Guardamos o identificador desta resposta: ele será o elo da próxima
    // pergunta. Remover esta linha faz o chat "esquecer" tudo a cada mensagem —
    // vale testar em sala para ver a diferença na prática.
    options.ConversationId = resposta.ConversationId;

    // A resposta pode conter vários itens de saída (texto, chamadas de
    // ferramenta, etc.). .Text concatena só a parte textual.
    Console.WriteLine($"IA: {resposta.Text}\n");
}
