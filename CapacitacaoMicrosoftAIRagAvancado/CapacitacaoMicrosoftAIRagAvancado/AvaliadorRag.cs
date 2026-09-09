// =============================================================================
// A metade nova da aula: medir a qualidade da resposta com um modelo juiz.
// =============================================================================
// Os quatro avaliadores do Microsoft.Extensions.AI.Evaluation.Quality olham para
// pares diferentes de coisas. Confundi-los é o erro conceitual mais comum, e a
// tabela abaixo é o slide de métricas em forma de código:
//
//   Groundedness  resposta × contexto recuperado   "isto que foi afirmado está no material?"
//   Relevance     resposta × pergunta              "respondeu o que foi perguntado?"
//   Retrieval     trechos  × pergunta              "os trechos trazidos serviam?"
//   Equivalence   resposta × gabarito              "está CERTA?" (só com gold)
//
// Todos devolvem de 1 a 5, com uma justificativa em texto. A justificativa vale
// tanto quanto a nota: é ela que transforma "caiu de 4.2 para 3.1" em uma tarefa.
//
// E repare no que separa as duas primeiras linhas da terceira. Se a resposta está
// ruim e o RETRIEVAL está bom, o defeito é da geração ou do prompt. Se o retrieval
// caiu junto, o defeito é do chunking, do embedding ou do índice. Medir as duas
// coisas separadas é o que troca adivinhação por diagnóstico.
//
// -----------------------------------------------------------------------------
// Uma ressalva honesta, para dizer em voz alta na aula:
// o juiz é um LLM. Ele erra, é sensível ao próprio prompt e custa uma chamada por
// métrica. Ele não é a verdade — é um instrumento barato o bastante para rodar em
// toda mudança, o que nenhum painel humano é. As duas conferências determinísticas
// no fim desta classe (recuperou os documentos certos? recusou quando devia?) não
// dependem de juiz nenhum, e são as primeiras a olhar quando a nota surpreende.
// =============================================================================

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace CapacitacaoMicrosoftAIRagAvancado;

/// <summary>Quais métricas rodar. Cada uma custa uma chamada ao modelo juiz.</summary>
public enum ConjuntoDeMetricas
{
    /// <summary>
    /// Groundedness + Retrieval. As duas que respondem "qual etapa quebrou?".
    /// É o conjunto da varredura de top-k: metade do custo, todo o diagnóstico.
    /// </summary>
    Diagnostico,

    /// <summary>As quatro. Para o relatório final e para a análise de uma pergunta.</summary>
    Completo
}

/// <summary>As notas de uma resposta. Nulo significa "esta métrica não foi rodada".</summary>
public sealed record Notas(
    double? Groundedness,
    string? RazaoGroundedness,
    double? Relevance,
    double? Retrieval,
    string? RazaoRetrieval,
    double? Correctness,
    bool RecuperouOsDocumentosEsperados,
    bool RecusouComoEsperado);

/// <summary>Uma linha do relatório: o caso, o que o sistema respondeu e as notas.</summary>
public sealed record LinhaDoRelatorio(PerguntaAvaliada Caso, RespostaRag Resposta, Notas Notas);

/// <summary>O agregado de uma configuração inteira (um top-k, um modo).</summary>
public sealed record Resumo(
    int TopK,
    IndiceRag.Modo Modo,
    double? Groundedness,
    double? Relevance,
    double? Retrieval,
    double? Correctness,
    double AcertoDeRecuperacao,
    double AcertoDeRecusa,
    double TokensDeEntradaMedios,
    double LatenciaMediaEmSegundos)
{
    public static Resumo De(int topK, IndiceRag.Modo modo, IReadOnlyList<LinhaDoRelatorio> linhas)
    {
        static double? Media(IEnumerable<double?> valores)
        {
            var presentes = valores.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return presentes.Count == 0 ? null : presentes.Average();
        }

        return new Resumo(
            TopK: topK,
            Modo: modo,
            Groundedness: Media(linhas.Select(l => l.Notas.Groundedness)),
            Relevance: Media(linhas.Select(l => l.Notas.Relevance)),
            Retrieval: Media(linhas.Select(l => l.Notas.Retrieval)),
            Correctness: Media(linhas.Select(l => l.Notas.Correctness)),
            AcertoDeRecuperacao: linhas.Count(l => l.Notas.RecuperouOsDocumentosEsperados) / (double)linhas.Count,
            AcertoDeRecusa: linhas.Count(l => l.Notas.RecusouComoEsperado) / (double)linhas.Count,
            TokensDeEntradaMedios: linhas.Average(l => (double)(l.Resposta.TokensDeEntrada ?? 0)),
            LatenciaMediaEmSegundos: linhas.Average(l => l.Resposta.Latencia.TotalSeconds));
    }
}

public sealed class AvaliadorRag
{
    // O juiz é um IChatClient como qualquer outro. Ele PODE ser o mesmo modelo
    // que gerou a resposta — é o que fazemos aqui, por simplicidade — mas usar um
    // modelo diferente reduz o viés de um modelo se achar ótimo. A variável
    // FOUNDRY_JUDGE_MODEL existe justamente para trocar isso ao vivo.
    private readonly ChatConfiguration _juiz;

    private readonly GroundednessEvaluator _groundedness = new();
    private readonly RelevanceEvaluator _relevance = new();
    private readonly RetrievalEvaluator _retrieval = new();
    private readonly EquivalenceEvaluator _equivalence = new();

    public AvaliadorRag(IChatClient juiz)
    {
        _juiz = new ChatConfiguration(juiz);
    }

    public async Task<Notas> AvaliarAsync(
        PerguntaAvaliada caso,
        RespostaRag resposta,
        ConjuntoDeMetricas metricas = ConjuntoDeMetricas.Completo,
        CancellationToken ct = default)
    {
        // O que o juiz recebe como "a conversa": só a pergunta do usuário. O
        // prompt de sistema com os trechos NÃO entra aqui — ele entra como
        // contexto de avaliação, abaixo. Misturar os dois faz o juiz avaliar o
        // seu prompt em vez da sua resposta.
        ChatMessage[] conversa = [new(ChatRole.User, caso.Pergunta)];

        var completo = metricas is ConjuntoDeMetricas.Completo;

        // O contexto recuperado, do jeito que cada avaliador espera recebê-lo:
        // o de groundedness quer o bloco inteiro, o de retrieval quer os chunks
        // separados (ele julga cada um).
        var paraGroundedness = new GroundednessEvaluatorContext(resposta.Contexto);
        var paraRetrieval = new RetrievalEvaluatorContext(
            resposta.Trechos.Select(t => t.Trecho.Conteudo));
        var paraEquivalence = new EquivalenceEvaluatorContext(caso.RespostaEsperada);

        // Os avaliadores devolvem ValueTask; o .AsTask() existe para podermos
        // guardá-las e esperar por todas de uma vez. As chamadas são
        // independentes, e rodar as quatro em paralelo é a diferença, numa
        // varredura de top-k, entre a turma esperar um minuto e esperar quatro.
        Task<EvaluationResult> tarefaGroundedness =
            _groundedness.EvaluateAsync(conversa, resposta.Bruta, _juiz, [paraGroundedness], ct).AsTask();

        Task<EvaluationResult> tarefaRetrieval =
            _retrieval.EvaluateAsync(conversa, resposta.Bruta, _juiz, [paraRetrieval], ct).AsTask();

        Task<EvaluationResult>? tarefaRelevance = completo
            ? _relevance.EvaluateAsync(conversa, resposta.Bruta, _juiz, null, ct).AsTask()
            : null;

        Task<EvaluationResult>? tarefaEquivalence = completo
            ? _equivalence.EvaluateAsync(conversa, resposta.Bruta, _juiz, [paraEquivalence], ct).AsTask()
            : null;

        await Task.WhenAll(
            new[] { tarefaGroundedness, tarefaRetrieval, tarefaRelevance, tarefaEquivalence }
                .OfType<Task<EvaluationResult>>());

        var (notaGround, razaoGround) =
            Extrair(await tarefaGroundedness, GroundednessEvaluator.GroundednessMetricName);

        var (notaRetrieval, razaoRetrieval) =
            Extrair(await tarefaRetrieval, RetrievalEvaluator.RetrievalMetricName);

        double? notaRelevance = tarefaRelevance is null
            ? null
            : Extrair(await tarefaRelevance, RelevanceEvaluator.RelevanceMetricName).Valor;

        double? notaCorrectness = tarefaEquivalence is null
            ? null
            : Extrair(await tarefaEquivalence, EquivalenceEvaluator.EquivalenceMetricName).Valor;

        return new Notas(
            Groundedness: notaGround,
            RazaoGroundedness: razaoGround,
            Relevance: notaRelevance,
            Retrieval: notaRetrieval,
            RazaoRetrieval: razaoRetrieval,
            Correctness: notaCorrectness,

            // --- As duas conferências que NÃO custam uma chamada de modelo ----
            // Rodam sempre, são determinísticas e, quando discordam do juiz,
            // costumam estar certas. Comece por elas ao investigar uma queda.
            RecuperouOsDocumentosEsperados:
                caso.DocumentosEsperados.All(esperado =>
                    resposta.Documentos.Contains(esperado, StringComparer.OrdinalIgnoreCase)),

            RecusouComoEsperado: resposta.Recusou == caso.DeveRecusar);
    }

    /// <summary>
    /// Roda o conjunto inteiro numa configuração. O <paramref name="progresso"/>
    /// existe porque, em sala, uma tela parada por um minuto parece uma tela
    /// travada.
    /// </summary>
    public async Task<IReadOnlyList<LinhaDoRelatorio>> AvaliarConjuntoAsync(
        PipelineRag pipeline,
        IReadOnlyList<PerguntaAvaliada> casos,
        int topK,
        IndiceRag.Modo modo,
        ConjuntoDeMetricas metricas,
        Action<string>? progresso = null,
        CancellationToken ct = default)
    {
        // Três perguntas de cada vez. Mais que isso e o deployment começa a
        // devolver 429 — que é, por sinal, uma ótima deixa para falar de cota.
        using var limite = new SemaphoreSlim(3);

        var concluidas = 0;

        var tarefas = casos.Select(async caso =>
        {
            await limite.WaitAsync(ct);
            try
            {
                var resposta = await pipeline.ResponderAsync(caso.Pergunta, topK, modo, ct);
                var notas = await AvaliarAsync(caso, resposta, metricas, ct);

                var feitas = Interlocked.Increment(ref concluidas);
                progresso?.Invoke($"    {feitas}/{casos.Count}  {Encurtar(caso.Pergunta, 58)}");

                return new LinhaDoRelatorio(caso, resposta, notas);
            }
            finally
            {
                limite.Release();
            }
        });

        var linhas = await Task.WhenAll(tarefas);

        // A ordem do conjunto é deliberada (fácil → difícil); Task.WhenAll não a
        // garante em tempo de execução, mas devolve na ordem original.
        return linhas;
    }

    private static (double? Valor, string? Razao) Extrair(EvaluationResult resultado, string metrica)
    {
        return resultado.TryGet<NumericMetric>(metrica, out var m) ? (m.Value, m.Reason) : (null, null);
    }

    public static string Encurtar(string texto, int limite)
    {
        var linha = texto.ReplaceLineEndings(" ").Trim();
        return linha.Length <= limite ? linha : linha[..(limite - 3)] + "...";
    }
}
