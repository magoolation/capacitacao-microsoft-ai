// =============================================================================
// O conjunto de avaliação: as perguntas contra as quais o sistema é medido.
// =============================================================================
// Este arquivo é o artefato mais subestimado de um projeto de RAG. Sem ele,
// "melhorou" é opinião: alguém mexe no chunking, faz três perguntas na mão, acha
// que ficou melhor e sobe. Com ele, "melhorou" é um número que se compara com o
// da semana passada.
//
// Seis perguntas bastam para uma aula. Em produção o conjunto começa com as
// perguntas que os usuários realmente fizeram — e cresce toda vez que alguém
// reclama de uma resposta.
//
// A composição foi escolhida, não sorteada. Há três perguntas fáceis (um
// documento, um chunk), uma que exige DOIS documentos ao mesmo tempo, uma que
// NÃO tem resposta no corpus e uma cuja palavra-chave não aparece no texto.
// Cada uma quebra o sistema de um jeito diferente, e é isso que faz a média
// significar alguma coisa.
// =============================================================================

namespace CapacitacaoMicrosoftAIRagAvancado;

/// <summary>Uma pergunta com gabarito.</summary>
/// <param name="Pergunta">O que se pergunta ao sistema.</param>
/// <param name="RespostaEsperada">
/// O gabarito, em uma frase. É o "gold" do slide de métricas: sem ele dá para
/// medir groundedness e relevance, mas não dá para medir se a resposta está
/// CERTA. Uma resposta pode ser perfeitamente aterrada no trecho errado.
/// </param>
/// <param name="DocumentosEsperados">
/// Quais arquivos deveriam ter sido recuperados. Permite conferir a recuperação
/// sem chamar modelo nenhum — a checagem mais barata do conjunto.
/// </param>
/// <param name="DeveRecusar">
/// Verdadeiro quando a resposta CERTA é admitir que não sabe. Sem casos assim, a
/// avaliação premia um sistema que inventa com confiança.
/// </param>
/// <param name="OQueEnsina">Por que esta pergunta está no conjunto.</param>
public sealed record PerguntaAvaliada(
    string Pergunta,
    string RespostaEsperada,
    string[] DocumentosEsperados,
    bool DeveRecusar,
    string OQueEnsina);

public static class ConjuntoDeAvaliacao
{
    public static readonly IReadOnlyList<PerguntaAvaliada> Perguntas =
    [
        new(
            Pergunta: "Qual é o teto de reembolso para jantar em viagem nacional?",
            RespostaEsperada: "R$ 90,00 por pessoa, por refeição.",
            DocumentosEsperados: ["politica-de-reembolso.md"],
            DeveRecusar: false,
            OQueEnsina: "Caso fácil: um documento, um chunk, número explícito. " +
                        "Se esta falhar, o problema não é de ajuste fino — é de indexação."),

        new(
            Pergunta: "Qual é o prazo de primeira resposta para um chamado de severidade 1?",
            RespostaEsperada: "30 minutos, em regime 24x7.",
            DocumentosEsperados: ["sla-de-suporte.md"],
            DeveRecusar: false,
            OQueEnsina: "Caso fácil com termo técnico literal ('severidade 1'). " +
                        "É onde a busca por palavra-chave brilha e a vetorial não agrega."),

        new(
            Pergunta: "Quantos dias por semana os times de engenharia precisam ir ao escritório?",
            RespostaEsperada: "Dois dias por semana, definidos pelo próprio time, " +
                              "com a presença medida por trimestre.",
            DocumentosEsperados: ["politica-de-trabalho-remoto.md"],
            DeveRecusar: false,
            OQueEnsina: "Fácil, mas com uma ressalva importante no mesmo chunk " +
                        "(medição trimestral). Mede se a resposta é completa, não só correta."),

        new(
            Pergunta: "Vou trabalhar de outra cidade por vinte dias, a serviço. " +
                      "Preciso de aprovação e qual o teto de diária de hotel?",
            RespostaEsperada: "Até trinta dias basta comunicar ao gestor, sem aprovação de RH. " +
                              "O teto de hospedagem é R$ 420,00 em capitais e R$ 310,00 nas demais cidades.",
            DocumentosEsperados: ["politica-de-trabalho-remoto.md", "politica-de-reembolso.md"],
            DeveRecusar: false,
            OQueEnsina: "A pergunta que EXIGE dois documentos. É a que mais falha, e falha de um " +
                        "jeito instrutivo: os dois arquivos SÃO recuperados e mesmo assim a resposta " +
                        "sai pela metade — o modelo responde uma parte e diz que não achou a outra. " +
                        "Recuperar não é o mesmo que usar."),

        new(
            Pergunta: "O SMS ainda é aceito como segundo fator de autenticação?",
            RespostaEsperada: "Não. O SMS foi descontinuado como segundo fator em janeiro de 2025; " +
                              "valem aplicativo autenticador ou chave física.",
            DocumentosEsperados: ["seguranca-e-acessos.md"],
            DeveRecusar: false,
            OQueEnsina: "A resposta correta é uma NEGATIVA que está no documento. " +
                        "Diferente de recusar por ausência — e modelos confundem as duas."),

        new(
            Pergunta: "Qual é a política de reembolso para viagens internacionais?",
            RespostaEsperada: RespostaRag.Recusa,
            DocumentosEsperados: ["politica-de-reembolso.md"],
            DeveRecusar: true,
            OQueEnsina: "O caso que vale a aula. O assunto ESTÁ no corpus (o documento diz que " +
                        "viagens ao exterior seguem processo próprio, não publicado), mas a resposta " +
                        "NÃO está. A recuperação acerta e a geração é que precisa se conter."),
    ];

    /// <summary>
    /// Os valores de top-k varridos pela opção 5. Três pontos bastam para a
    /// curva aparecer: um apertado demais, um razoável e um exagerado.
    /// </summary>
    public static readonly int[] TopKsDaVarredura = [3, 5, 10];
}
