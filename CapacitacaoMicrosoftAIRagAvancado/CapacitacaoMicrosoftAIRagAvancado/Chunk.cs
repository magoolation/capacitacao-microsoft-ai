// =============================================================================
// O documento como ele vive no índice. Idêntico ao da aula 4, de propósito.
// =============================================================================
// A aula 5 não mexe no formato do índice: o que ela acrescenta acontece DEPOIS
// da recuperação. Manter esta classe igual é o que permite dizer em sala que
// nada aqui embaixo mudou — mudou só o que passamos a medir.
//
// Atenção ao nome dos campos NO ÍNDICE: o serializador padrão do
// Azure.Search.Documents NÃO converte para camelCase — ele usa o nome da
// propriedade C# como está. Como este projeto se refere aos campos em minúsculo
// (em SearchOptions.Select, no VectorizedQuery.Fields e na configuração
// semântica), cada propriedade declara o nome explicitamente com
// [JsonPropertyName].
// =============================================================================

using System.Text.Json.Serialization;

using Azure.Search.Documents.Indexes;

namespace CapacitacaoMicrosoftAIRagAvancado;

/// <summary>Um trecho de documento, pronto para indexação e recuperação.</summary>
public sealed class Chunk
{
    /// <summary>Chave do índice. Só aceita letras, dígitos, _, - e =.</summary>
    [SimpleField(IsKey = true, IsFilterable = true)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Nome do arquivo de origem — é o que aparece na citação.</summary>
    [SearchableField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("documento")]
    public string Documento { get; set; } = "";

    /// <summary>Título do documento, para dar contexto ao trecho.</summary>
    [SearchableField(AnalyzerName = "pt-BR.microsoft")]
    [JsonPropertyName("titulo")]
    public string Titulo { get; set; } = "";

    /// <summary>
    /// URL de origem do documento. O console não usa — existe para a ferramenta
    /// Azure AI Search do playground do Foundry, que precisa de um campo
    /// recuperável com a URL para transformar a citação em link clicável.
    /// </summary>
    [SimpleField]
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// O texto do trecho. É isto que vai para o contexto do modelo — e é isto
    /// que o avaliador de groundedness recebe para julgar se a resposta se
    /// sustenta ou se o modelo completou a lacuna sozinho.
    /// </summary>
    [SearchableField(AnalyzerName = "pt-BR.microsoft")]
    [JsonPropertyName("conteudo")]
    public string Conteudo { get; set; } = "";

    /// <summary>
    /// O embedding do <see cref="Conteudo"/>. IsHidden = true porque a aplicação
    /// nunca lê este campo de volta: ele existe só para o serviço calcular
    /// similaridade.
    ///
    /// O tipo PRECISA ser float[] (ou IReadOnlyList&lt;float&gt;). Com
    /// ReadOnlyMemory&lt;float&gt; o FieldBuilder não reconhece um vetor e a
    /// criação do índice falha.
    /// </summary>
    [VectorSearchField(
        VectorSearchDimensions = IndiceRag.DimensoesDoEmbedding,
        VectorSearchProfileName = IndiceRag.PerfilVetorial,
        IsHidden = true)]
    [JsonPropertyName("vetor")]
    public float[] Vetor { get; set; } = [];
}
