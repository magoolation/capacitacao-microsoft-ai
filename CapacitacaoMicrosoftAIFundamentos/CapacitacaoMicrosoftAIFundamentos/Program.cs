// =============================================================================
// Capacitação Microsoft AI — Fundamentos
// Chat de console conversando com um modelo hospedado no Microsoft Foundry.
//
// O que este exemplo demonstra:
//   1. Como autenticar no Foundry usando identidade do Entra ID (sem chave).
//   2. Como chamar um modelo pela Responses API.
//   3. Como manter o contexto da conversa SEM guardar histórico no cliente.
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

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
// Nada fica escrito no código: endpoint e modelo vêm de variáveis de ambiente.
// Em desenvolvimento elas são definidas em Properties/launchSettings.json.
// Em produção viriam do App Service, Container Apps, Key Vault etc.

// Endpoint DO PROJETO, no formato:
//   https://<recurso>.services.ai.azure.com/api/projects/<projeto>
// Atenção: não é o mesmo endpoint usado pelo SDK da OpenAI puro
// (aquele seria https://<recurso>.services.ai.azure.com/openai/v1).
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
// 3. Cliente do modelo
// -----------------------------------------------------------------------------
// AIProjectClient é a porta de entrada do projeto. A partir dele você alcança
// as várias capacidades; aqui queremos conversar com um modelo, então pedimos
// um cliente de Responses já vinculado ao deployment escolhido.
ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

// -----------------------------------------------------------------------------
// 4. Memória da conversa
// -----------------------------------------------------------------------------
// Este é o ponto mais interessante do exemplo.
//
// Na API clássica (Chat Completions) o CLIENTE guarda a conversa: você mantém
// uma List<ChatMessage> e reenvia tudo a cada pergunta.
//
// Na Responses API o SERVIÇO guarda. Cada resposta tem um Id; ao enviar a
// próxima pergunta você informa o Id anterior e o modelo recebe todo o
// contexto. Por isso a única coisa que precisamos lembrar é uma string.
//
// Começa null porque a primeira pergunta não tem nada antes dela.
string? previousResponseId = null;

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

    // A chamada de rede. Note que enviamos apenas a pergunta ATUAL mais o Id
    // da resposta anterior — o histórico não trafega.
    // Usamos a versão Async para não bloquear a thread enquanto o modelo pensa.
    var resposta = await responseClient.CreateResponseAsync(prompt, previousResponseId);

    // Guardamos o Id desta resposta: ele será o elo da próxima pergunta.
    // Remover esta linha faz o chat "esquecer" tudo a cada mensagem —
    // vale testar em sala para ver a diferença na prática.
    previousResponseId = resposta.Value.Id;

    // A resposta pode conter vários itens de saída (texto, chamadas de
    // ferramenta, etc.). GetOutputText() concatena só a parte textual.
    Console.WriteLine($"IA: {resposta.Value.GetOutputText()}\n");
}
