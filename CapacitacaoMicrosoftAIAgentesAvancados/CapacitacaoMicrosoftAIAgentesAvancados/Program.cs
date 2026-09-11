// =============================================================================
// Capacitação Microsoft AI — Aula 7: agentes avançados
// Tools, function calling, aprovação humana, MCP e workflow multi-agente.
//
// O que este exemplo demonstra:
//   1. Function calling completo: o agente da aula 6 ganha quatro tools e o
//      programa mostra, chamada a chamada, o ciclo "modelo pede → app executa
//      → modelo continua".
//   2. Human-in-the-loop na tool: o envio de e-mail exige aprovação. O agente
//      PARA e devolve um pedido; o programa pergunta ao humano e retoma.
//   3. Hosted MCP: o Microsoft Learn MCP Server como tool, sem escrever
//      integração nenhuma.
//   4. Workflow sequencial com três agentes (Pesquisador → Validador → Redator):
//      o padrão de pipeline editorial do slide, com streaming por executor.
//
// O setup (endpoint, identidade, AIProjectClient) é o mesmo das aulas 1 a 6. O
// que muda é a lista `tools:` do AsAIAgent — e é isso que transforma um agente
// que só conversa num agente que age.
// =============================================================================

using Azure.AI.Projects;              // chega transitivamente pelo MAF — NÃO está no .csproj
using Azure.Identity;
using CapacitacaoMicrosoftAIAgentesAvancados;
using Microsoft.Agents.AI;            // AIAgent, AgentSession, ApprovalRequiredAIFunction...
using Microsoft.Agents.AI.Workflows;  // AgentWorkflowBuilder, InProcessExecution, eventos
using Microsoft.Extensions.AI;        // AIFunctionFactory, HostedMcpServerTool, conteúdos

// -----------------------------------------------------------------------------
// 1. Configuração e autenticação — idênticas às aulas anteriores
// -----------------------------------------------------------------------------
var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");
var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");

if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
{
    Console.WriteLine("Defina FOUNDRY_ENDPOINT e FOUNDRY_MODEL antes de executar.");
    return;
}

AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

// -----------------------------------------------------------------------------
// 2. As tools
// -----------------------------------------------------------------------------
// AIFunctionFactory.Create lê a assinatura e os [Description] de cada método e
// gera o schema que o modelo recebe. O método continua sendo um método C# comum:
// nada aqui é específico de Foundry ou OpenAI.
AIFunction readKb = AIFunctionFactory.Create(Ferramentas.ReadKb);
AIFunction getWeather = AIFunctionFactory.Create(Ferramentas.GetWeather);
AIFunction calc = AIFunctionFactory.Create(Ferramentas.Calc);

// A tool sensível é EMBRULHADA: o framework passa a interromper a execução e a
// pedir aprovação antes de chamá-la. A função em si não muda.
AITool sendEmail = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(Ferramentas.SendEmailMock));

const string Persona = """
    Você é o Zé, assistente interno de uma agência de viagens brasileira.
    Fale em português do Brasil, de forma direta.
    Use as ferramentas disponíveis sempre que a pergunta pedir dados (políticas
    internas, clima, cálculos) — não invente valores que uma ferramenta pode dar.
    Ao enviar e-mails, confirme o destinatário e o assunto antes.
    Se uma ferramenta devolver erro, explique o erro ao usuário em vez de tentar
    contornar.
    """;

// -----------------------------------------------------------------------------
// 3. Menu
// -----------------------------------------------------------------------------
while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== Capacitação Microsoft AI — Aula 7: agentes avançados ===");
    Console.WriteLine("1. Function calling: 4 tools, com o ciclo visível");
    Console.WriteLine("2. Human-in-the-loop: aprovar o envio de e-mail");
    Console.WriteLine("3. Hosted MCP: Microsoft Learn como tool");
    Console.WriteLine("4. Workflow sequencial: Pesquisador → Validador → Redator");
    Console.WriteLine("0. Sair");
    Console.Write("Opção: ");

    switch (Console.ReadLine())
    {
        case "1": await FunctionCallingAsync(); break;
        case "2": await HumanInTheLoopAsync(); break;
        case "3": await HostedMcpAsync(); break;
        case "4": await WorkflowSequencialAsync(); break;
        case "0": case null: return;
        default: Console.WriteLine("Opção inválida."); break;
    }
}

// =============================================================================
// DEMO 1 — Function calling com o ciclo visível
// =============================================================================
// O agente recebe as três tools "seguras". A cada resposta, o programa varre as
// mensagens e imprime as chamadas de função e os resultados — é o slide
// "Function calling — anatomia" acontecendo na tela.
async Task FunctionCallingAsync()
{
    AIAgent agente = projectClient.AsAIAgent(
        model: model,
        name: "Zé",
        instructions: Persona,
        tools: [readKb, getWeather, calc]);

    AgentSession sessao = await agente.CreateSessionAsync();

    string[] roteiro =
    [
        "Qual é o teto de jantar em viagem nacional? E quanto dá para 5 jantares?",
        "Como está o tempo em Fortaleza agora?",
        "Qual é a política de home office para engenharia?",
    ];

    Console.WriteLine("\nRoteiro de três perguntas — repare nas chamadas de tool entre elas.\n");

    foreach (var pergunta in roteiro)
    {
        Console.WriteLine($"Você: {pergunta}");
        AgentResponse resposta = await agente.RunAsync(pergunta, sessao);
        ImprimirChamadasDeTool(resposta);
        Console.WriteLine($"{agente.Name}: {resposta}\n");
    }

    Console.WriteLine("Agora é sua vez. Digite perguntas (ou 'sair').\n");
    while (true)
    {
        Console.Write("Você: ");
        var prompt = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(prompt)) continue;
        if (prompt.Equals("sair", StringComparison.OrdinalIgnoreCase)) return;

        AgentResponse resposta = await agente.RunAsync(prompt, sessao);
        ImprimirChamadasDeTool(resposta);
        Console.WriteLine($"{agente.Name}: {resposta}\n");
    }
}

// =============================================================================
// DEMO 2 — Human-in-the-loop
// =============================================================================
// Com a tool embrulhada em ApprovalRequiredAIFunction, o agente não a executa:
// devolve um ToolApprovalRequestContent e PARA. O programa mostra a chamada
// pretendida, pergunta ao humano e devolve a resposta na mesma sessão. Só então
// a tool roda (ou não) e o agente conclui.
//
// É o slide "Human-in-the-loop": o checkpoint entra exatamente antes da ação
// irreversível — e vira, de brinde, o melhor log de auditoria do sistema.
async Task HumanInTheLoopAsync()
{
    AIAgent agente = projectClient.AsAIAgent(
        model: model,
        name: "Zé",
        instructions: Persona,
        tools: [readKb, sendEmail]);

    AgentSession sessao = await agente.CreateSessionAsync();

    const string pedido = "Mande um e-mail para financeiro@auroralog.example com o assunto 'Teto de jantar' " +
                          "informando o valor do teto de jantar em viagem nacional.";
    Console.WriteLine($"\nVocê: {pedido}");

    AgentResponse resposta = await agente.RunAsync(pedido, sessao);

    // Enquanto houver pedidos de aprovação pendentes, o agente está pausado.
    List<ToolApprovalRequestContent> pendentes = ExtrairPedidosDeAprovacao(resposta);

    while (pendentes.Count > 0)
    {
        List<ChatMessage> decisoes = pendentes.ConvertAll(pedidoDeAprovacao =>
        {
            var chamada = (FunctionCallContent)pedidoDeAprovacao.ToolCall;
            Console.WriteLine();
            Console.WriteLine($"  [HITL] O agente quer chamar {chamada.Name} com {Ferramentas.Descrever(chamada.Arguments)}");
            Console.Write("  Aprovar? (s/n): ");
            bool aprovado = Console.ReadLine()?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) ?? false;

            // A decisão volta como conteúdo de uma mensagem do usuário.
            return new ChatMessage(ChatRole.User, [pedidoDeAprovacao.CreateResponse(aprovado)]);
        });

        resposta = await agente.RunAsync(decisoes, sessao);
        pendentes = ExtrairPedidosDeAprovacao(resposta);
    }

    ImprimirChamadasDeTool(resposta);
    Console.WriteLine($"\n{agente.Name}: {resposta}");
}

// =============================================================================
// DEMO 3 — Hosted MCP
// =============================================================================
// Uma tool MCP hospedada é declarada, não implementada: o servidor expõe as
// ferramentas e o serviço as chama. Aqui usamos o Microsoft Learn MCP Server —
// o agente passa a responder com a documentação oficial, sem uma linha de
// integração.
//
// MCP é o "USB dos agentes" do slide: o mesmo servidor serve a qualquer
// framework compatível. Compare com as tools customizadas da demo 1, que são
// código seu, rodando no seu processo.
async Task HostedMcpAsync()
{
    var mcpLearn = new HostedMcpServerTool(
        serverName: "microsoft_learn",
        serverAddress: "https://learn.microsoft.com/api/mcp");

    AIAgent agente = projectClient.AsAIAgent(
        model: model,
        name: "Zé Docs",
        instructions: "Você responde perguntas técnicas sobre Azure e Microsoft Foundry usando a " +
                      "documentação oficial via MCP. Cite os links que encontrar. Responda em português.",
        tools: [mcpLearn]);

    Console.WriteLine("\nPergunte algo sobre Azure/Foundry (ou 'sair'). O agente consulta o Microsoft Learn via MCP.\n");

    AgentSession sessao = await agente.CreateSessionAsync();

    while (true)
    {
        Console.Write("Você: ");
        var prompt = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(prompt)) continue;
        if (prompt.Equals("sair", StringComparison.OrdinalIgnoreCase)) return;

        Console.Write($"{agente.Name}: ");
        await foreach (AgentResponseUpdate update in agente.RunStreamingAsync(prompt, sessao))
        {
            Console.Write(update.Text);
        }
        Console.WriteLine("\n");
    }
}

// =============================================================================
// DEMO 4 — Workflow sequencial: Pesquisador → Validador → Redator
// =============================================================================
// Três agentes, cada um com uma função estreita, ligados num grafo. A saída de
// um é a entrada do próximo. Comparado a um agente único com muitas tools, o
// workflow troca flexibilidade por PREVISIBILIDADE: a ordem é conhecida, cada
// etapa é testável e o custo é estimável.
//
// Os agentes são embrulhados em executors automaticamente pelo
// AgentWorkflowBuilder. Eles só começam a processar quando recebem o TurnToken.
async Task WorkflowSequencialAsync()
{
    AIAgent pesquisador = projectClient.AsAIAgent(
        model: model,
        name: "Pesquisador",
        instructions: "Levante, em tópicos curtos, os fatos relevantes para o pedido do usuário usando a " +
                      "base de conhecimento. Não escreva o texto final — só os fatos, com a fonte.",
        tools: [readKb, getWeather]);

    AIAgent validador = projectClient.AsAIAgent(
        model: model,
        name: "Validador",
        instructions: "Revise os fatos recebidos: marque o que está inconsistente, faltando ou fora do " +
                      "escopo do pedido. Devolva a lista corrigida, sem redigir o texto final.");

    AIAgent redator = projectClient.AsAIAgent(
        model: model,
        name: "Redator",
        instructions: "Com os fatos validados, escreva a resposta final ao usuário em português, " +
                      "em no máximo 8 linhas, citando os fatos usados.");

    Workflow workflow = AgentWorkflowBuilder.BuildSequential([pesquisador, validador, redator]);

    Console.Write("\nPedido para o pipeline (Enter para o exemplo padrão): ");
    var pedido = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(pedido))
    {
        pedido = "Quero uma nota curta para a equipe sobre o teto de reembolso de jantar e a regra de " +
                 "dias no escritório, e diga como está o tempo em Fortaleza hoje.";
    }

    var mensagens = new List<ChatMessage> { new(ChatRole.User, pedido) };

    await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, mensagens);
    await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

    string? executorAtual = null;
    List<ChatMessage> resultado = [];

    await foreach (WorkflowEvent evento in run.WatchStreamAsync())
    {
        if (evento is AgentResponseUpdateEvent atualizacao)
        {
            // Cada executor imprime em streaming, com um cabeçalho quando muda.
            if (atualizacao.ExecutorId != executorAtual)
            {
                executorAtual = atualizacao.ExecutorId;
                Console.WriteLine($"\n\n--- {executorAtual} ---");
            }
            Console.Write(atualizacao.Update.Text);
        }
        else if (evento is WorkflowOutputEvent saida)
        {
            resultado = saida.As<List<ChatMessage>>() ?? [];
            break;
        }
    }

    Console.WriteLine("\n\n=== Saída final do workflow ===");
    var final = resultado.LastOrDefault(m => m.Role == ChatRole.Assistant);
    Console.WriteLine(final?.Text ?? "(sem saída)");
}

// -----------------------------------------------------------------------------
// Utilitários
// -----------------------------------------------------------------------------
// Varre as mensagens da resposta e imprime cada chamada de função e cada
// resultado. É o "log por etapa" do hands-on — e a base do tracing da aula 8.
static void ImprimirChamadasDeTool(AgentResponse resposta)
{
    foreach (ChatMessage mensagem in resposta.Messages)
    {
        foreach (AIContent conteudo in mensagem.Contents)
        {
            switch (conteudo)
            {
                case FunctionCallContent chamada:
                    Console.WriteLine($"  → tool {chamada.Name}({Ferramentas.Descrever(chamada.Arguments)})");
                    break;
                case FunctionResultContent retorno:
                    Console.WriteLine($"  ← {retorno.Result}");
                    break;
            }
        }
    }
}

static List<ToolApprovalRequestContent> ExtrairPedidosDeAprovacao(AgentResponse resposta)
    => resposta.Messages
        .SelectMany(m => m.Contents)
        .OfType<ToolApprovalRequestContent>()
        .ToList();
