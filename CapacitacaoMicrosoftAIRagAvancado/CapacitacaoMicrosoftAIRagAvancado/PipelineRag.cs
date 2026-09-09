// =============================================================================
// O pipeline RAG de ponta a ponta, numa chamada só: recuperar → montar contexto
// → gerar → devolver a resposta JUNTO COM tudo que a produziu.
// =============================================================================
// A diferença desta aula para a anterior está no tipo de retorno. Na aula 4 a
// resposta era escrita direto no console e desaparecia. Aqui ela volta como um
// objeto que carrega também os trechos recuperados, o contexto exato que foi
// enviado, a latência e os tokens.
//
// Isso não é zelo de arquitetura: é o pré-requisito da avaliação. Não dá para
// medir groundedness sem ter em mãos, ao mesmo tempo, a resposta e o contexto
// que deveria sustentá-la. Um pipeline que só imprime texto é um pipeline que
// não pode ser avaliado — e esse é o ponto que abre a segunda metade da aula.
// =============================================================================

using System.Diagnostics;

using Microsoft.Extensions.AI;

namespace CapacitacaoMicrosoftAIRagAvancado;

/// <summary>Um trecho recuperado, com o número que ele recebe na citação.</summary>
public sealed record TrechoCitado(int Numero, Chunk Trecho, double? Score);

/// <summary>
/// Tudo que uma pergunta produziu. É o insumo dos avaliadores e a linha do
/// relatório final.
/// </summary>
public sealed record RespostaRag(
    string Pergunta,
    string Texto,
    ChatResponse Bruta,
    IReadOnlyList<TrechoCitado> Trechos,
    string Contexto,
    IndiceRag.Modo Modo,
    int TopK,
    TimeSpan Latencia)
{
    /// <summary>Os documentos distintos que sustentaram a resposta.</summary>
    public IEnumerable<string> Documentos => Trechos.Select(t => t.Trecho.Documento).Distinct();

    /// <summary>
    /// Tokens de entrada. Cresce com o top-k, e é a metade "custo" do trade-off
    /// do slide de custo × qualidade: cada chunk extra é pago em toda pergunta.
    /// </summary>
    public long? TokensDeEntrada => Bruta.Usage?.InputTokenCount;

    public long? TokensDeSaida => Bruta.Usage?.OutputTokenCount;

    /// <summary>
    /// A frase exata que o prompt manda usar quando a resposta não está no
    /// contexto. Compará-la com o texto devolvido é a checagem mais barata de
    /// groundedness que existe — e roda sem chamar modelo nenhum.
    /// </summary>
    public const string Recusa = "Não encontrei essa informação nos documentos disponíveis.";

    public bool Recusou => Texto.Contains("Não encontrei essa informação", StringComparison.OrdinalIgnoreCase);
}

public sealed class PipelineRag
{
    private readonly IndiceRag _indice;
    private readonly IChatClient _chat;

    public PipelineRag(IndiceRag indice, IChatClient chat)
    {
        _indice = indice;
        _chat = chat;
    }

    // As duas instruções que carregam todo o peso: responder SÓ pelo contexto e
    // admitir quando não sabe. A segunda é a que a avaliação de groundedness
    // testa — e a que, sozinha, nem sempre basta.
    private const string Instrucao = $"""
        Você responde perguntas sobre as políticas internas da empresa Aurora Log.
        Use EXCLUSIVAMENTE os trechos numerados fornecidos como contexto.
        Cite a fonte de cada afirmação no formato [n].
        Se a resposta não estiver nos trechos, responda exatamente:
        "{RespostaRag.Recusa}"
        Não complete lacunas com conhecimento geral.
        """;

    public async Task<RespostaRag> ResponderAsync(
        string pergunta,
        int topK = 5,
        IndiceRag.Modo modo = IndiceRag.Modo.HibridaComReranking,
        CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();

        var achados = await _indice.BuscarAsync(pergunta, modo, topK, ct);

        var trechos = achados
            .Select((a, i) => new TrechoCitado(i + 1, a.Trecho, a.Score))
            .ToList();

        // O contexto entra numerado. É isso que permite ao modelo citar [1], [2]
        // e ao usuário conferir. Sem numeração não há citação verificável — e
        // sem citação verificável não há como um humano auditar a resposta.
        var contexto = string.Join("\n\n", trechos.Select(t =>
            $"[{t.Numero}] ({t.Trecho.Documento}) {t.Trecho.Conteudo}"));

        List<ChatMessage> mensagens =
        [
            new(ChatRole.System, Instrucao),
            new(ChatRole.User, $"Contexto:\n{contexto}\n\nPergunta: {pergunta}")
        ];

        var resposta = await _chat.GetResponseAsync(mensagens, cancellationToken: ct);

        cronometro.Stop();

        return new RespostaRag(
            Pergunta: pergunta,
            Texto: resposta.Text,
            Bruta: resposta,
            Trechos: trechos,
            Contexto: contexto,
            Modo: modo,
            TopK: topK,
            Latencia: cronometro.Elapsed);
    }

    /// <summary>
    /// A mesma pergunta SEM recuperação nenhuma. Serve de linha de base: é
    /// contra ela que se mede se o RAG ajudou — e, na pergunta de conhecimento
    /// geral, se ele só encareceu.
    /// </summary>
    public async Task<RespostaRag> ResponderSemRagAsync(string pergunta, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();

        List<ChatMessage> mensagens =
        [
            new(ChatRole.System, "Você responde perguntas sobre políticas internas de empresas."),
            new(ChatRole.User, pergunta)
        ];

        var resposta = await _chat.GetResponseAsync(mensagens, cancellationToken: ct);
        cronometro.Stop();

        return new RespostaRag(
            Pergunta: pergunta,
            Texto: resposta.Text,
            Bruta: resposta,
            Trechos: [],
            Contexto: "",
            Modo: IndiceRag.Modo.Keyword,
            TopK: 0,
            Latencia: cronometro.Elapsed);
    }
}
