// =============================================================================
// Capacitação Microsoft AI — LLM e Prompts
// Console em .NET conversando com um modelo do Microsoft Foundry através da
// abstração IChatClient (Microsoft.Extensions.AI).
//
// O que este exemplo demonstra:
//   1. Como configurar um IChatClient apontando para o Foundry.
//   2. Como o histórico da conversa vive no CLIENTE, numa List<ChatMessage>.
//   3. Few-shot prompting: ensinar o formato da resposta por exemplos.
//   4. Structured output: receber um record C# tipado em vez de texto solto.
//
// Continuação da aula anterior (Fundamentos), que usava o Foundry SDK e a
// Responses API. Aqui trocamos de camada — veja a seção "Duas camadas" no
// README para entender quando usar cada uma.
// =============================================================================

// System.ClientModel.Primitives: camada de transporte comum aos SDKs novos da
// Microsoft e da OpenAI. É de lá que vem BearerTokenPolicy, a peça que injeta
// o token do Entra ID em cada requisição HTTP.
using System.ClientModel.Primitives;

// Azure.Identity: descobre "quem é você" a partir do ambiente
// (az login, Visual Studio, identidade gerenciada em produção...).
using Azure.Identity;

// Microsoft.Extensions.AI: a abstração. IChatClient, ChatMessage, ChatRole e os
// métodos de extensão GetResponseAsync / GetResponseAsync<T>.
// Nada aqui é específico de OpenAI, Foundry, Ollama ou Anthropic.
using Microsoft.Extensions.AI;

// OpenAI: a implementação concreta. OpenAIClient fala o protocolo da OpenAI —
// que é o mesmo protocolo exposto pelo endpoint /openai/v1 do Foundry.
using OpenAI;

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
// Nada fica escrito no código: endpoint e modelo vêm de variáveis de ambiente.
// Em desenvolvimento elas são definidas em Properties/launchSettings.json.

// ATENÇÃO: este endpoint é DIFERENTE do usado na aula anterior.
//   Aula anterior (Foundry SDK): https://<recurso>.services.ai.azure.com/api/projects/<projeto>
//   Esta aula    (OpenAI SDK):   https://<recurso>.services.ai.azure.com/openai/v1
// Trocar de SDK significa trocar de endpoint. Confundir os dois é o tropeço
// mais comum ao migrar código entre as duas aulas.
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
// O SDK da OpenAI foi feito para o serviço da OpenAI, onde autenticação é uma
// chave de API. O Foundry aceita chave, mas o caminho correto é Entra ID.
//
// A ponte entre os dois mundos é BearerTokenPolicy: ela recebe uma credencial
// do Azure, pede um token para o escopo informado e o coloca no cabeçalho
// Authorization de cada chamada. O SDK da OpenAI nem sabe que isso aconteceu.
//
// O escopo é sempre este, fixo, para qualquer recurso de Cognitive Services ou
// Foundry. Não é o seu endpoint — é o identificador do serviço no Entra ID.
BearerTokenPolicy tokenPolicy = new(
    new DefaultAzureCredential(),
    "https://cognitiveservices.azure.com/.default");

// -----------------------------------------------------------------------------
// 3. IChatClient apontando para o Foundry
// -----------------------------------------------------------------------------
// Três camadas, de baixo para cima:
//
//   OpenAIClient             → cliente do serviço inteiro (chat, embeddings...)
//      └── ChatClient        → cliente de UM deployment de modelo
//             └── IChatClient → a abstração neutra que o resto do código usa
//
// A propriedade Endpoint é o que redireciona o SDK da OpenAI para o Foundry.
// Sem ela, o cliente chamaria api.openai.com.
OpenAIClient openAIClient = new(tokenPolicy, new OpenAIClientOptions
{
    Endpoint = new Uri(endpoint)
});

// AsIChatClient() é o adaptador. A partir daqui o código não menciona mais
// OpenAI nem Azure: trocar o modelo por um Ollama local ou por outro provedor
// é mexer nas linhas acima, e em nada mais.
IChatClient chatClient = openAIClient
    .GetChatClient(model)
    .AsIChatClient();

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
// Contraste direto com a aula anterior.
//
//   Responses API (aula anterior): o SERVIÇO guarda a conversa. O código
//   mantinha apenas uma string com o Id da resposta anterior.
//
//   IChatClient (esta aula): o CLIENTE guarda a conversa. O código mantém uma
//   List<ChatMessage> que cresce, e ela inteira trafega a cada pergunta.
//
// Nenhum dos dois é "melhor". O cliente guardando dá controle total sobre o que
// entra no contexto — e é exatamente esse controle que torna possível o
// few-shot da demo 2.
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
