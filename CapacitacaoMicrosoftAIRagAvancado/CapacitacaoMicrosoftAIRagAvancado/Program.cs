// =============================================================================
// Capacitação Microsoft AI — Aula 5
// RAG end-to-end no Microsoft Foundry, com avaliação automática
//
// O que esta aula acrescenta à anterior:
//   1. O pipeline devolve um OBJETO (resposta + trechos + contexto + custo),
//      não texto solto — sem isso não há o que avaliar.
//   2. Quatro métricas de qualidade com modelo juiz: groundedness, relevance,
//      retrieval e correctness.
//   3. A varredura de top-k (3 × 5 × 10) medida, e não chutada.
//   4. Quanto o ranqueador semântico realmente ganha, no seu corpus.
//   5. Um relatório em Markdown para comparar com o da semana que vem.
//
// A frase que a aula inteira serve para sustentar: RAG não termina quando a
// resposta aparece na tela. Termina quando você consegue dizer, com número, se
// ela ficou melhor ou pior do que ontem — e QUAL etapa mudou.
// =============================================================================

using System.ClientModel.Primitives;
using System.Globalization;
using System.Text;

using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

using CapacitacaoMicrosoftAIRagAvancado;

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

// O juiz pode ser outro deployment. Trocar esta variável ao vivo e ver as notas
// mudarem é a demonstração mais curta de que a avaliação é um INSTRUMENTO, com
// as suas próprias características, e não uma medida absoluta.
var modeloJuiz = Environment.GetEnvironmentVariable("FOUNDRY_JUDGE_MODEL") ?? modeloDeChat;

if (string.IsNullOrWhiteSpace(foundryEndpoint) || string.IsNullOrWhiteSpace(modeloDeChat)
    || string.IsNullOrWhiteSpace(modeloDeEmbedding) || string.IsNullOrWhiteSpace(searchEndpoint))
{
    Console.WriteLine("""
        Faltam variáveis de ambiente. Defina, em Properties/launchSettings.json:
          FOUNDRY_ENDPOINT          https://<recurso>.services.ai.azure.com/openai/v1
          FOUNDRY_MODEL             deployment do modelo de chat
          FOUNDRY_EMBEDDING_MODEL   deployment do modelo de embeddings
          FOUNDRY_JUDGE_MODEL       deployment do juiz (opcional; padrão: FOUNDRY_MODEL)
          SEARCH_ENDPOINT           https://<servico>.search.windows.net
          SEARCH_INDEX              nome do índice (padrão: politicas-internas)
        """);
    return;
}

// -----------------------------------------------------------------------------
// 2. Clientes
// -----------------------------------------------------------------------------
// Idêntico à aula 4: uma credencial só, nenhuma chave de API, Entra ID nos dois
// serviços. O que muda é que agora há TRÊS clientes de modelo — chat, embeddings
// e juiz — saindo do mesmo OpenAIClient.
var credencial = new DefaultAzureCredential();

var tokenPolicy = new BearerTokenPolicy(credencial, "https://ai.azure.com/.default");
OpenAIClient openAIClient = new(tokenPolicy, new OpenAIClientOptions
{
    Endpoint = new Uri(foundryEndpoint)
});

IChatClient chat = openAIClient.GetChatClient(modeloDeChat).AsIChatClient();
IChatClient juiz = openAIClient.GetChatClient(modeloJuiz).AsIChatClient();

IEmbeddingGenerator<string, Embedding<float>> embeddings =
    openAIClient.GetEmbeddingClient(modeloDeEmbedding).AsIEmbeddingGenerator();

var uriDaBusca = new Uri(searchEndpoint);
var indexClient = new SearchIndexClient(uriDaBusca, credencial);
var searchClient = new SearchClient(uriDaBusca, nomeDoIndice, credencial);

var indice = new IndiceRag(indexClient, searchClient, embeddings, nomeDoIndice);
var pipeline = new PipelineRag(indice, chat);
var avaliador = new AvaliadorRag(juiz);

var pastaDoCorpus = Path.Combine(AppContext.BaseDirectory, "Corpus");

// O que já foi medido nesta sessão. É daqui que sai o relatório da opção 7.
var resumos = new List<Resumo>();
List<LinhaDoRelatorio> ultimaCorrida = [];

// -----------------------------------------------------------------------------
// 3. Menu
// -----------------------------------------------------------------------------
while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== Aula 5 — RAG end-to-end com avaliação ===");
    Console.WriteLine($"    modelo: {modeloDeChat}   juiz: {modeloJuiz}   índice: {nomeDoIndice}");
    Console.WriteLine();
    Console.WriteLine("1. Criar o índice e indexar o corpus");
    Console.WriteLine("2. Perguntar (híbrida + reranking, com citações e custo)");
    Console.WriteLine("3. Avaliar UMA pergunta do conjunto — as quatro métricas");
    Console.WriteLine("4. Avaliar o conjunto inteiro");
    Console.WriteLine("5. Varredura de top-k: 3 × 5 × 10");
    Console.WriteLine("6. O que o ranqueador semântico ganha (com × sem reranking)");
    Console.WriteLine("7. Gravar o relatório em Markdown");
    Console.WriteLine("8. Apagar o índice");
    Console.WriteLine("0. Sair");
    Console.Write("Opção: ");

    switch (Console.ReadLine())
    {
        case "1": await IndexarAsync(); break;
        case "2": await PerguntarAsync(); break;
        case "3": await AvaliarUmaAsync(); break;
        case "4": await AvaliarConjuntoAsync(); break;
        case "5": await VarrerTopKAsync(); break;
        case "6": await CompararRerankingAsync(); break;
        case "7": GravarRelatorio(); break;
        case "8": await indice.ApagarIndiceAsync(); Console.WriteLine("Índice apagado."); break;
        case "0": return;

        // Console.ReadLine() devolve null quando a ENTRADA ACABA — entrada
        // redirecionada que chegou ao fim, Ctrl+Z, terminal encerrado. Sem este
        // caso, o null caía no `default`, o laço nunca terminava e o programa
        // ficava imprimindo "Opção inválida" para sempre, a 100% de um núcleo.
        //
        // O sintoma que aparece depois é pior que a causa: o processo órfão
        // segura o .exe e o build seguinte falha com MSB3021 "arquivo em uso",
        // que não tem nenhuma relação aparente com o menu.
        case null: return;

        default: Console.WriteLine("Opção inválida."); break;
    }
}

// =============================================================================
// Opção 1 — Indexação
// =============================================================================
// O tamanho do chunk entra por variável de ambiente porque ele é a outra
// metade do experimento desta aula. Com 800 caracteres o corpus vira 12 chunks,
// e aí top-k 10 traz quase o corpus inteiro — a varredura da opção 5 sai PLANA,
// e a conclusão certa é "estou pagando por nada". Reindexe com 300 e a curva
// aparece: mais chunks, cada um mais estreito, e o top-k volta a decidir o que
// entra no prompt. Trocar este número e reindexar ao vivo é o experimento mais
// barato e mais instrutivo do dia.
async Task IndexarAsync()
{
    var alvo = int.TryParse(Environment.GetEnvironmentVariable("RAG_CHUNK_ALVO"), out var lido) && lido > 0
        ? lido
        : 800;

    Console.WriteLine($"\nCriando o índice (chunk alvo: {alvo} caracteres)...");
    await indice.CriarOuAtualizarIndiceAsync();

    Console.WriteLine("Fatiando e vetorizando o corpus:");
    var total = await indice.IndexarAsync(pastaDoCorpus, alvo, Console.WriteLine);

    Console.WriteLine($"\n{total} chunk(s) indexado(s) em '{nomeDoIndice}'.");
    Console.WriteLine("O índice leva alguns segundos para ficar consultável.");
}

// =============================================================================
// Opção 2 — Uma pergunta, com o custo à vista
// =============================================================================
async Task PerguntarAsync()
{
    Console.Write("\nPergunta: ");
    var pergunta = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(pergunta))
    {
        return;
    }

    var topK = LerInteiro("top-k", 5);
    var resposta = await pipeline.ResponderAsync(pergunta, topK);

    ImprimirResposta(resposta);
}

void ImprimirResposta(RespostaRag r)
{
    Console.WriteLine("\n--- trechos recuperados ---");
    foreach (var t in r.Trechos)
    {
        Console.WriteLine($"  [{t.Numero}] {t.Score,6:F3}  {t.Trecho.Documento,-32} " +
                          $"{AvaliadorRag.Encurtar(t.Trecho.Conteudo, 60)}");
    }

    Console.WriteLine("\n--- resposta ---");
    Console.WriteLine(r.Texto);

    // O custo, sempre visível. É o outro eixo do trade-off: a nota sobe, e
    // esta linha sobe junto.
    Console.WriteLine($"\n[top-k {r.TopK} · {r.Trechos.Count} trecho(s) · " +
                      $"{r.TokensDeEntrada} tokens de entrada · " +
                      $"{r.TokensDeSaida} de saída · {r.Latencia.TotalSeconds:F1}s]");
}

// =============================================================================
// Opção 3 — Uma pergunta do conjunto, com as quatro métricas e as justificativas
// =============================================================================
// É aqui que a turma vê a avaliação de perto, antes de virar média numa tabela.
// Escolha a pergunta 6 (a que não tem resposta no corpus) para a discussão mais
// rica: se o modelo inventou, groundedness cai e retrieval NÃO cai — e o
// diagnóstico fica evidente na tela.
async Task AvaliarUmaAsync()
{
    Console.WriteLine();
    for (var i = 0; i < ConjuntoDeAvaliacao.Perguntas.Count; i++)
    {
        var p = ConjuntoDeAvaliacao.Perguntas[i];
        Console.WriteLine($"  {i + 1}. {AvaliadorRag.Encurtar(p.Pergunta, 70)}");
    }

    var escolha = LerInteiro("\nQual pergunta", 1);
    if (escolha < 1 || escolha > ConjuntoDeAvaliacao.Perguntas.Count)
    {
        Console.WriteLine("Fora do intervalo.");
        return;
    }

    var caso = ConjuntoDeAvaliacao.Perguntas[escolha - 1];
    var topK = LerInteiro("top-k", 5);

    Console.WriteLine($"\nPor que esta pergunta está no conjunto:\n  {caso.OQueEnsina}");

    Console.WriteLine("\nRespondendo...");
    var resposta = await pipeline.ResponderAsync(caso.Pergunta, topK);
    ImprimirResposta(resposta);

    Console.WriteLine($"\nGabarito: {caso.RespostaEsperada}");
    Console.WriteLine("\nAvaliando (quatro chamadas ao modelo juiz, em paralelo)...");

    var notas = await avaliador.AvaliarAsync(caso, resposta);

    Console.WriteLine("\n--- notas (escala 1 a 5) ---");
    Console.WriteLine($"  Groundedness  {Nota(notas.Groundedness)}   resposta × contexto recuperado");
    Console.WriteLine($"  Relevance     {Nota(notas.Relevance)}   resposta × pergunta");
    Console.WriteLine($"  Retrieval     {Nota(notas.Retrieval)}   trechos  × pergunta");
    Console.WriteLine($"  Correctness   {Nota(notas.Correctness)}   resposta × gabarito");

    Console.WriteLine("\n--- conferências determinísticas (sem modelo juiz) ---");
    Console.WriteLine($"  Recuperou os documentos esperados?  " +
                      $"{Sim(notas.RecuperouOsDocumentosEsperados)}  " +
                      $"(esperados: {string.Join(", ", caso.DocumentosEsperados)})");
    Console.WriteLine($"  Recusou exatamente quando devia?    " +
                      $"{Sim(notas.RecusouComoEsperado)}  " +
                      $"(devia recusar: {(caso.DeveRecusar ? "sim" : "não")})");

    // A justificativa é o que se leva para a próxima iteração. A nota diz que
    // caiu; a justificativa diz o que consertar.
    if (!string.IsNullOrWhiteSpace(notas.RazaoGroundedness))
    {
        Console.WriteLine($"\n  Por que essa nota de groundedness:\n    " +
                          $"{AvaliadorRag.Encurtar(notas.RazaoGroundedness, 400)}");
    }

    if (!string.IsNullOrWhiteSpace(notas.RazaoRetrieval))
    {
        Console.WriteLine($"\n  Por que essa nota de retrieval:\n    " +
                          $"{AvaliadorRag.Encurtar(notas.RazaoRetrieval, 400)}");
    }
}

// =============================================================================
// Opção 4 — O conjunto inteiro numa configuração
// =============================================================================
async Task AvaliarConjuntoAsync()
{
    var topK = LerInteiro("top-k", 5);

    Console.WriteLine($"\nRodando {ConjuntoDeAvaliacao.Perguntas.Count} pergunta(s) com top-k {topK}...");

    var linhas = await avaliador.AvaliarConjuntoAsync(
        pipeline,
        ConjuntoDeAvaliacao.Perguntas,
        topK,
        IndiceRag.Modo.HibridaComReranking,
        ConjuntoDeMetricas.Completo,
        Console.WriteLine);

    ultimaCorrida = [.. linhas];

    Console.WriteLine();
    Console.WriteLine("  #  Ground  Relev  Retriev  Correct  Docs  Recusa  Pergunta");
    Console.WriteLine("  " + new string('-', 74));

    for (var i = 0; i < linhas.Count; i++)
    {
        var l = linhas[i];
        Console.WriteLine($"  {i + 1}  {Nota(l.Notas.Groundedness)}   {Nota(l.Notas.Relevance)}  " +
                          $"{Nota(l.Notas.Retrieval)}    {Nota(l.Notas.Correctness)}    " +
                          $"{Sim(l.Notas.RecuperouOsDocumentosEsperados)}   " +
                          $"{Sim(l.Notas.RecusouComoEsperado)}    " +
                          $"{AvaliadorRag.Encurtar(l.Caso.Pergunta, 32)}");
    }

    var resumo = Resumo.De(topK, IndiceRag.Modo.HibridaComReranking, linhas);
    resumos.Add(resumo);

    ImprimirResumo(resumo);
}

// =============================================================================
// Opção 5 — A varredura de top-k
// =============================================================================
// O experimento central da aula. A pergunta que ele responde não é "qual top-k é
// o melhor", e sim "o que EU ganho e o que EU pago quando aumento o top-k NESTE
// corpus" — e a resposta muda de corpus para corpus.
//
// O que costuma aparecer: de 3 para 5, o retrieval sobe (a pergunta que precisa
// de dois documentos passa a funcionar). De 5 para 10, o retrieval quase não
// muda, os tokens quase dobram e o groundedness às vezes CAI — porque entra
// ruído junto, e o modelo se apoia no trecho errado.
async Task VarrerTopKAsync()
{
    Console.WriteLine($"\nVarrendo top-k {string.Join(", ", ConjuntoDeAvaliacao.TopKsDaVarredura)} " +
                      $"sobre {ConjuntoDeAvaliacao.Perguntas.Count} pergunta(s).");
    Console.WriteLine("Só groundedness e retrieval: são as duas que dizem qual etapa mudou.\n");

    var daVarredura = new List<Resumo>();

    foreach (var topK in ConjuntoDeAvaliacao.TopKsDaVarredura)
    {
        Console.WriteLine($"  top-k {topK}:");

        var linhas = await avaliador.AvaliarConjuntoAsync(
            pipeline,
            ConjuntoDeAvaliacao.Perguntas,
            topK,
            IndiceRag.Modo.HibridaComReranking,
            ConjuntoDeMetricas.Diagnostico,
            Console.WriteLine);

        var resumo = Resumo.De(topK, IndiceRag.Modo.HibridaComReranking, linhas);
        daVarredura.Add(resumo);
        resumos.Add(resumo);
    }

    Console.WriteLine();
    Console.WriteLine("  top-k  Groundedness  Retrieval  Docs certos  Tokens in  Latência");
    Console.WriteLine("  " + new string('-', 66));

    foreach (var r in daVarredura)
    {
        Console.WriteLine($"  {r.TopK,5}  {Nota(r.Groundedness),12}  {Nota(r.Retrieval),9}  " +
                          $"{r.AcertoDeRecuperacao,10:P0}  {r.TokensDeEntradaMedios,9:F0}  " +
                          $"{r.LatenciaMediaEmSegundos,7:F1}s");
    }

    Console.WriteLine("""

          Como ler esta tabela:
            Retrieval subiu e groundedness ficou igual  → mais contexto ajudou a achar.
            Retrieval igual e tokens subiram            → você está pagando por nada.
            Groundedness CAIU com top-k maior           → entrou ruído; o modelo se
                                                          apoiou no trecho errado.
        """);
}

// =============================================================================
// Opção 6 — Quanto o ranqueador semântico ganha
// =============================================================================
// O slide diz "+5 a +15 pontos de relevance". Este menu existe para você não
// precisar acreditar no slide: mede no seu corpus, com o seu conjunto.
// Em um corpus de cinco documentos o ganho costuma ser pequeno — e dizer isso em
// voz alta vale mais do que fingir um resultado bonito.
async Task CompararRerankingAsync()
{
    var topK = LerInteiro("top-k", 5);

    foreach (var modo in new[] { IndiceRag.Modo.Hibrida, IndiceRag.Modo.HibridaComReranking })
    {
        Console.WriteLine($"\n  {modo}:");

        var linhas = await avaliador.AvaliarConjuntoAsync(
            pipeline,
            ConjuntoDeAvaliacao.Perguntas,
            topK,
            modo,
            ConjuntoDeMetricas.Diagnostico,
            Console.WriteLine);

        var resumo = Resumo.De(topK, modo, linhas);
        resumos.Add(resumo);

        Console.WriteLine($"    groundedness {Nota(resumo.Groundedness)} · " +
                          $"retrieval {Nota(resumo.Retrieval)} · " +
                          $"docs certos {resumo.AcertoDeRecuperacao:P0} · " +
                          $"{resumo.LatenciaMediaEmSegundos:F1}s");
    }
}

// =============================================================================
// Opção 7 — O relatório
// =============================================================================
// O entregável do desafio da aula. Um arquivo que se compara com o da semana
// passada é o que transforma "acho que melhorou" em engenharia.
void GravarRelatorio()
{
    if (resumos.Count == 0)
    {
        Console.WriteLine("\nNada medido ainda nesta sessão. Rode a opção 4, 5 ou 6 antes.");
        return;
    }

    var texto = new StringBuilder();
    var agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    texto.AppendLine("# Relatório de avaliação RAG — Aula 5");
    texto.AppendLine();
    texto.AppendLine($"- Gerado em: {agora}");
    texto.AppendLine($"- Modelo de geração: `{modeloDeChat}`");
    texto.AppendLine($"- Modelo juiz: `{modeloJuiz}`");
    texto.AppendLine($"- Índice: `{nomeDoIndice}`");
    texto.AppendLine($"- Perguntas no conjunto: {ConjuntoDeAvaliacao.Perguntas.Count}");
    texto.AppendLine();

    texto.AppendLine("## Configurações medidas");
    texto.AppendLine();
    texto.AppendLine("| Modo | top-k | Groundedness | Relevance | Retrieval | Correctness | Docs certos | Recusa certa | Tokens in | Latência |");
    texto.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

    foreach (var r in resumos)
    {
        texto.AppendLine(
            $"| {r.Modo} | {r.TopK} | {Nota(r.Groundedness)} | {Nota(r.Relevance)} | " +
            $"{Nota(r.Retrieval)} | {Nota(r.Correctness)} | {r.AcertoDeRecuperacao:P0} | " +
            $"{r.AcertoDeRecusa:P0} | {r.TokensDeEntradaMedios:F0} | {r.LatenciaMediaEmSegundos:F1}s |");
    }

    if (ultimaCorrida.Count > 0)
    {
        texto.AppendLine();
        texto.AppendLine("## Última corrida completa, pergunta a pergunta");
        texto.AppendLine();

        foreach (var l in ultimaCorrida)
        {
            texto.AppendLine($"### {l.Caso.Pergunta}");
            texto.AppendLine();
            texto.AppendLine($"- **Gabarito:** {l.Caso.RespostaEsperada}");
            texto.AppendLine($"- **Resposta:** {l.Resposta.Texto.ReplaceLineEndings(" ")}");
            texto.AppendLine($"- **Trechos:** {string.Join(", ", l.Resposta.Documentos)}");
            texto.AppendLine($"- **Notas:** groundedness {Nota(l.Notas.Groundedness)} · " +
                             $"relevance {Nota(l.Notas.Relevance)} · " +
                             $"retrieval {Nota(l.Notas.Retrieval)} · " +
                             $"correctness {Nota(l.Notas.Correctness)}");
            texto.AppendLine($"- **Por que esta pergunta está no conjunto:** {l.Caso.OQueEnsina}");

            if (!string.IsNullOrWhiteSpace(l.Notas.RazaoGroundedness))
            {
                texto.AppendLine($"- **Justificativa do juiz (groundedness):** " +
                                 $"{l.Notas.RazaoGroundedness.ReplaceLineEndings(" ")}");
            }

            texto.AppendLine();
        }
    }

    var caminho = Path.Combine(
        Directory.GetCurrentDirectory(),
        $"relatorio-rag-{DateTime.Now:yyyyMMdd-HHmm}.md");

    File.WriteAllText(caminho, texto.ToString(), Encoding.UTF8);
    Console.WriteLine($"\nRelatório gravado em:\n  {caminho}");
}

// =============================================================================
// Utilitários de console
// =============================================================================
void ImprimirResumo(Resumo r)
{
    Console.WriteLine();
    Console.WriteLine($"  Médias (top-k {r.TopK}, {r.Modo}):");
    Console.WriteLine($"    groundedness {Nota(r.Groundedness)} · relevance {Nota(r.Relevance)} · " +
                      $"retrieval {Nota(r.Retrieval)} · correctness {Nota(r.Correctness)}");
    Console.WriteLine($"    documentos certos {r.AcertoDeRecuperacao:P0} · " +
                      $"recusa certa {r.AcertoDeRecusa:P0}");
    Console.WriteLine($"    custo médio {r.TokensDeEntradaMedios:F0} tokens de entrada · " +
                      $"{r.LatenciaMediaEmSegundos:F1}s por pergunta");
}

static string Nota(double? valor) =>
    valor.HasValue ? valor.Value.ToString("F2", CultureInfo.InvariantCulture) : "  — ";

static string Sim(bool valor) => valor ? " ok " : "FALHA";

static int LerInteiro(string rotulo, int padrao)
{
    Console.Write($"{rotulo} [{padrao}]: ");
    var lido = Console.ReadLine();
    return int.TryParse(lido, out var valor) && valor > 0 ? valor : padrao;
}
