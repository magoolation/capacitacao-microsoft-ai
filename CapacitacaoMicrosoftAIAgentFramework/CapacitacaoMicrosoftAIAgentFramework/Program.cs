// =============================================================================
// Capacitação Microsoft AI — Aula 6: primeiro agente com o Microsoft Agent Framework
//
// O que este exemplo demonstra:
//   1. Um agente = modelo + identidade (nome) + regras (instructions), criado a
//      partir do mesmo AIProjectClient das aulas 1 a 3, em UMA chamada: AsAIAgent.
//   2. RunAsync: a resposta chega inteira. Sem sessão, cada chamada é independente.
//   3. AgentSession + RunStreamingAsync: multi-turn com a resposta token a token.
//
// Contraste com a aula 3: lá montávamos o IChatClient à mão (três linhas) e o
// programa guardava o ConversationId. Aqui o framework faz as duas coisas — o
// agente carrega a persona e a sessão carrega a memória da conversa. O modelo, o
// endpoint e a identidade são exatamente os mesmos; o que muda é a camada.
//
// Tools, MCP, memória de longo prazo e multi-agent ficam para a aula 7 — este
// agente ainda só conversa. É de propósito: o objetivo de hoje é que cada aluno
// saia da sala com um agente próprio rodando.
// =============================================================================

// Azure.AI.Projects: o "Foundry SDK". A mesma porta de entrada das aulas 1 a 3.
// Repare: NÃO está declarado no .csproj — chega transitivamente pelo MAF.
using Azure.AI.Projects;

// Azure.Identity: descobre "quem é você" a partir do ambiente. Sem chave de API.
using Azure.Identity;

// Microsoft.Agents.AI: o Agent Framework. Os tipos que usamos são AIAgent (o
// agente), AgentSession (a conversa) e AgentResponseUpdate (o pedaço de resposta
// no streaming). O método AsAIAgent, que liga o Foundry ao framework, vem do
// pacote Microsoft.Agents.AI.Foundry e vive neste mesmo namespace.
using Microsoft.Agents.AI;

// -----------------------------------------------------------------------------
// 1. Configuração
// -----------------------------------------------------------------------------
// Endpoint DO PROJETO, igual ao das aulas 1 a 3:
//   https://<recurso>.services.ai.azure.com/api/projects/<projeto>
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
//
// Esta linha é idêntica à das aulas 1 a 3. Vale projetar as duas lado a lado.
AIProjectClient projectClient = new(
    endpoint: new Uri(endpoint),
    tokenProvider: new DefaultAzureCredential());

// -----------------------------------------------------------------------------
// 3. O agente
// -----------------------------------------------------------------------------
// Na aula 3, daqui saíam três linhas até chegar num IChatClient. Aqui sai UMA:
// AsAIAgent embrulha o cliente de Responses do projeto num ChatClientAgent.
//
// Os três argumentos são a "anatomia do agente" do slide: o modelo (LLM), a
// identidade (name) e as regras (instructions — o system message). Tools e
// memória de longo prazo, os outros dois membros da anatomia, entram na aula 7.
//
// Nada é criado no serviço: o agente vive neste processo. É o que o slide
// "MAF × Foundry Agent Service" chama de code-first — você gerencia o ciclo de
// vida, o deploy e a observabilidade.
AIAgent agente = projectClient.AsAIAgent(
    model: model,
    name: "Zé",
    instructions: """
        Você é o Zé, assistente de viagens de uma agência brasileira.
        Fale em português do Brasil, de forma simpática e direta.
        Ajude com roteiros, destinos, melhor época para viajar e dicas práticas.
        Faça no máximo uma pergunta por vez quando faltar informação.
        Se o assunto não for viagem, diga que só cuida de viagens e ofereça ajuda
        com isso — sem inventar competência que não tem.
        Não invente preços nem disponibilidade: diga que precisam ser confirmados.
        """);

// -----------------------------------------------------------------------------
// 4. RunAsync — uma pergunta, uma resposta inteira, sem memória
// -----------------------------------------------------------------------------
// Sem sessão, o agente não lembra de nada entre chamadas. A segunda pergunta
// abaixo só faz sentido se ele lembrar da primeira — e ele NÃO vai lembrar.
// É o mesmo efeito da aula 2 sem a List<ChatMessage>: quem não guarda o
// contexto, perde o contexto.
Console.WriteLine("=== 1. RunAsync, sem sessão ===\n");

const string primeiraPergunta = "Quero passar 5 dias em Fortaleza em julho. Vale a pena?";
Console.WriteLine($"Você: {primeiraPergunta}");

AgentResponse resposta = await agente.RunAsync(primeiraPergunta);
Console.WriteLine($"{agente.Name}: {resposta}\n");

const string segundaPergunta = "E quantos dias você sugeriu mesmo?";
Console.WriteLine($"Você: {segundaPergunta}");

resposta = await agente.RunAsync(segundaPergunta);
Console.WriteLine($"{agente.Name}: {resposta}\n");

// -----------------------------------------------------------------------------
// 5. AgentSession + RunStreamingAsync — conversa com memória, token a token
// -----------------------------------------------------------------------------
// A sessão é o objeto que carrega o histórico. O programa não guarda lista de
// mensagens nem ConversationId: passa a MESMA sessão em todas as chamadas e o
// framework cuida do resto. Onde o histórico fica (no cliente ou no serviço) é
// decisão do provedor — para o código, a interface é a mesma.
Console.WriteLine("=== 2. RunStreamingAsync, com sessão ===");
Console.WriteLine("Converse com o Zé. Digite 'sair' para encerrar.\n");

AgentSession sessao = await agente.CreateSessionAsync();

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

    Console.Write($"{agente.Name}: ");

    // O mesmo laço da aula 3, com o tipo do framework no lugar do tipo da
    // abstração: AgentResponseUpdate em vez de ChatResponseUpdate. Cada
    // atualização carrega um pedaço da resposta; escrevemos na hora.
    await foreach (AgentResponseUpdate update in agente.RunStreamingAsync(prompt, sessao))
    {
        Console.Write(update.Text);
    }

    Console.WriteLine("\n");
}
