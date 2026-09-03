// =============================================================================
// Capacitação Microsoft AI — LLM e Prompts
// Console em .NET conversando com um modelo do Microsoft Foundry através da
// abstração IChatClient (Microsoft.Extensions.AI).
//
// O que este exemplo demonstra:
//   1. Como o histórico da conversa pode viver no CLIENTE, numa List<ChatMessage>.
//   2. Few-shot prompting: ensinar o formato da resposta por exemplos.
//   3. Structured output: receber um record C# tipado em vez de texto solto.
//
// O setup (seções 1 a 3) é IDÊNTICO ao da aula de Fundamentos: mesmo Foundry
// SDK, mesmo endpoint, mesma identidade, mesmo IChatClient. Esta aula não troca
// de camada — ela troca o que se faz com a camada. Veja a seção "O fio entre as
// aulas" no README da raiz.
// =============================================================================

// Azure.AI.Projects: o "Foundry SDK". Porta de entrada do projeto.
using Azure.AI.Projects;

// Azure.AI.Extensions.OpenAI: os clientes que falam "dialeto OpenAI" já
// apontados para o seu projeto Foundry.
using Azure.AI.Extensions.OpenAI;

// Azure.Identity: descobre "quem é você" a partir do ambiente
// (az login, Visual Studio, identidade gerenciada em produção...).
using Azure.Identity;

// Microsoft.Extensions.AI: a abstração. IChatClient, ChatMessage, ChatRole e os
// métodos de extensão GetResponseAsync / GetResponseAsync<T>.
// Nada aqui é específico de OpenAI, Foundry, Ollama ou Anthropic.
using Microsoft.Extensions.AI;

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
// Nada fica escrito no código: endpoint e modelo vêm de variáveis de ambiente.
// Em desenvolvimento elas são definidas em Properties/launchSettings.json.

// Endpoint DO PROJETO:
//   https://<recurso>.services.ai.azure.com/api/projects/<projeto>
// O mesmo das outras aulas. (Se você acompanhou uma versão anterior deste
// material, este endpoint mudou: antes esta aula usava /openai/v1, porque
// falava com o SDK da OpenAI direto. Agora ela entra pelo Foundry SDK como as
// outras, e um único endpoint serve as três.)
var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");

// Nome do DEPLOYMENT do modelo no Foundry — não é o nome comercial do modelo.
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
// aceite chave de API. Não há segredo para vazar em código, log ou repositório.
//
// Autenticar (provar quem você é) não é o mesmo que autorizar (ter permissão):
// sua conta precisa do papel "Foundry User" no recurso. Ser Owner da
// subscription NÃO basta.
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

// -----------------------------------------------------------------------------
// 3. Do Foundry SDK até IChatClient
// -----------------------------------------------------------------------------
// Três camadas, de baixo para cima:
//
//   AIProjectClient              → porta de entrada do projeto Foundry
//      └── ProjectResponsesClient → cliente da Responses API para UM deployment
//             └── IChatClient     → a abstração neutra que o resto do código usa
//
// Daqui para baixo é Azure; daqui para cima o código não menciona mais nem
// Azure nem OpenAI. Todas as demos abaixo usam só `chatClient` — é por isso que
// elas rodariam sem alteração contra um Ollama local ou outro provedor.
ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForModel(model);

IChatClient chatClient = responseClient.AsIChatClient(model);

// -----------------------------------------------------------------------------
// 4. Dados de apoio
// -----------------------------------------------------------------------------
// Os mesmos três chamados atravessam as demos 2 e 3. Usar sempre a mesma
// entrada é o que torna a comparação entre as técnicas honesta.
string[] chamados =
[
    "Não consigo acessar o sistema, minha senha foi recusada três vezes seguidas.",
    "O relatório mensal está somando o total errado desde a atualização de ontem.",
    "Gostaria de sugerir um botão de exportar para Excel na tela de clientes."
];

// -----------------------------------------------------------------------------
// 5. Menu
// -----------------------------------------------------------------------------
while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== Capacitação Microsoft AI — LLM e Prompts ===");
    Console.WriteLine("1. Chat simples (histórico no cliente)");
    Console.WriteLine("2. Zero-shot vs Few-shot (List<ChatMessage>)");
    Console.WriteLine("3. Structured output (GetResponseAsync<T> com record)");
    Console.WriteLine("4. Sair");
    Console.Write("Opção: ");

    switch (Console.ReadLine())
    {
        case "1": await ChatSimplesAsync(); break;
        case "2": await ZeroShotVersusFewShotAsync(); break;
        case "3": await StructuredOutputAsync(); break;
        case "4": return;
        default: Console.WriteLine("Opção inválida."); break;
    }
}

// =============================================================================
// DEMO 1 — Chat simples: quem guarda o histórico?
// =============================================================================
// Contraste direto com a aula de Fundamentos — e repare que o cliente é o
// MESMO. Quem muda é a estratégia:
//
//   Fundamentos: o SERVIÇO guarda a conversa. O código mantinha apenas um
//   ChatOptions com o ConversationId da resposta anterior.
//
//   Aqui: o CLIENTE guarda a conversa. O código mantém uma List<ChatMessage>
//   que cresce, e ela inteira trafega a cada pergunta. Nenhum ConversationId é
//   enviado — por isso o serviço trata cada chamada como independente.
//
// Nenhum dos dois é "melhor". O cliente guardando dá controle total sobre o que
// entra no contexto — e é exatamente esse controle que torna possível o
// few-shot da demo 2, onde inventamos um histórico que nunca aconteceu.
async Task ChatSimplesAsync()
{
    // A primeira mensagem é a de sistema (system prompt): define papel, tom e
    // regras. Ela fica no topo da lista e é reenviada em todas as chamadas, por
    // isso influencia a conversa inteira, não só a primeira resposta.
    List<ChatMessage> conversa =
    [
        new(ChatRole.System, "Você é um assistente de suporte técnico. Responda em português do Brasil, de forma direta e objetiva.")
    ];

    Console.WriteLine("\nChat iniciado. Digite sua pergunta (ou 'sair' para voltar ao menu).\n");

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
            return;
        }

        // Acumula a pergunta do usuário no histórico...
        conversa.Add(new ChatMessage(ChatRole.User, prompt));

        // ...e envia a lista INTEIRA. O modelo não tem memória entre chamadas:
        // o que parece memória é simplesmente esta lista sendo reenviada.
        var resposta = await chatClient.GetResponseAsync(conversa);

        // Acumular a resposta do modelo também é obrigatório. Sem esta linha o
        // modelo esquece o que ele próprio disse — vale testar em sala.
        conversa.AddMessages(resposta);

        Console.WriteLine($"IA: {resposta.Text}\n");
        Console.WriteLine($"    [histórico: {conversa.Count} mensagens]\n");
    }
}

// =============================================================================
// DEMO 2 — Zero-shot vs Few-shot
// =============================================================================
// A MESMA tarefa, o MESMO modelo, dois prompts diferentes.
//
//   Zero-shot: você descreve o que quer.
//   Few-shot:  você MOSTRA o que quer, com exemplos de pergunta e resposta.
//
// Few-shot não é um recurso da API — é uma técnica de prompt. Você monta uma
// conversa fictícia, na qual o assistente já respondeu no formato desejado, e
// entrega isso como se tivesse acontecido. O modelo continua o padrão.
async Task ZeroShotVersusFewShotAsync()
{
    // --- Zero-shot ---------------------------------------------------------
    // Só a instrução. O modelo entende a tarefa, mas escolhe o formato: às
    // vezes responde em uma linha, às vezes em três parágrafos, às vezes com
    // categorias que ninguém pediu. Correto e inutilizável ao mesmo tempo,
    // porque nenhum código consegue consumir isso de forma confiável.
    Console.WriteLine("\n--- ZERO-SHOT (só a instrução) ---\n");

    foreach (var chamado in chamados)
    {
        var resposta = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "Classifique o chamado de suporte informado pelo usuário."),
            new ChatMessage(ChatRole.User, chamado)
        ]);

        Console.WriteLine($"Chamado: {chamado}");
        Console.WriteLine($"Modelo : {resposta.Text}\n");
    }

    // --- Few-shot ----------------------------------------------------------
    // Os exemplos entram como mensagens User/Assistant alternadas, ANTES da
    // pergunta real. Repare que o assistente "já respondeu" no formato exato
    // que queremos — três exemplos bastam para fixar o padrão.
    List<ChatMessage> exemplos =
    [
        new(ChatRole.System, "Você classifica chamados de suporte. Responda SEMPRE em uma única linha, no formato: Categoria | Urgência | Resumo."),

        new(ChatRole.User, "O sistema fechou sozinho quando cliquei em salvar."),
        new(ChatRole.Assistant, "Erro | Alta | Aplicação encerra ao salvar."),

        new(ChatRole.User, "Como faço para trocar minha foto de perfil?"),
        new(ChatRole.Assistant, "Dúvida | Baixa | Como alterar foto de perfil."),

        new(ChatRole.User, "Seria bom ter modo escuro na tela de lançamentos."),
        new(ChatRole.Assistant, "Sugestão | Baixa | Modo escuro na tela de lançamentos.")
    ];

    Console.WriteLine("--- FEW-SHOT (a instrução + 3 exemplos) ---\n");

    foreach (var chamado in chamados)
    {
        // Copia os exemplos e acrescenta o chamado real ao final. Sem a cópia,
        // cada iteração poluiria a lista com os chamados anteriores.
        List<ChatMessage> mensagens = [.. exemplos, new ChatMessage(ChatRole.User, chamado)];

        var resposta = await chatClient.GetResponseAsync(mensagens);

        Console.WriteLine($"Chamado: {chamado}");
        Console.WriteLine($"Modelo : {resposta.Text}\n");
    }

    Console.WriteLine("Compare as duas saídas: mesma tarefa, mesmo modelo, formatos bem diferentes.");
}

// =============================================================================
// DEMO 3 — Structured output
// =============================================================================
// O few-shot deixou a saída consistente, mas ainda é texto: para usar em código
// você precisaria fazer Split, Trim e torcer para o modelo não variar.
//
// GetResponseAsync<T> resolve isso por outro caminho. A biblioteca:
//   1. gera um JSON Schema a partir do tipo T;
//   2. envia esse schema junto com o prompt, pedindo resposta em JSON;
//   3. desserializa a resposta de volta para T.
//
// O modelo passa a ser obrigado pelo serviço a produzir JSON válido naquele
// formato. Não é o prompt pedindo com jeitinho — é uma restrição imposta na
// geração da resposta.
async Task StructuredOutputAsync()
{
    Console.WriteLine("\n--- STRUCTURED OUTPUT (record tipado) ---\n");

    foreach (var chamado in chamados)
    {
        // Repare que o prompt encolheu: não precisa mais descrever o formato,
        // listar campos nem dar exemplos. O schema do record faz esse trabalho.
        // Os nomes das propriedades do record viram documentação para o modelo —
        // por isso vale nomeá-las bem.
        var resposta = await chatClient.GetResponseAsync<TriagemChamado>(
        [
            new ChatMessage(ChatRole.System, "Você faz a triagem de chamados de suporte."),
            new ChatMessage(ChatRole.User, chamado)
        ]);

        // .Result é o objeto já desserializado. Se o modelo devolvesse algo que
        // não encaixa no tipo, .Result lançaria exceção; TryGetResult(out var t)
        // permite tratar essa falha sem try/catch.
        TriagemChamado triagem = resposta.Result;

        Console.WriteLine($"Chamado  : {chamado}");
        Console.WriteLine($"Categoria: {triagem.Categoria}");
        Console.WriteLine($"Urgência : {triagem.Urgencia}");
        Console.WriteLine($"Resumo   : {triagem.Resumo}");
        Console.WriteLine($"Imediato : {triagem.RequerAtencaoImediata}");
        Console.WriteLine();
    }

    Console.WriteLine("A saída agora é um objeto C#: dá para gravar em banco, comparar, testar.");
}

// -----------------------------------------------------------------------------
// O contrato da resposta
// -----------------------------------------------------------------------------
// Este record é, ao mesmo tempo, o tipo C# e a especificação enviada ao modelo.
// Um record posicional basta — não precisa de atributos nem de configuração.
//
// Um enum daria uma restrição ainda mais forte no schema (o modelo só poderia
// escolher entre os valores declarados); usamos string aqui para manter o
// exemplo mínimo.
record TriagemChamado(
    string Categoria,
    string Urgencia,
    string Resumo,
    bool RequerAtencaoImediata);
