// =============================================================================
// O documento como ele vive no índice.
// =============================================================================
// Um "chunk" é um pedaço de um documento, não o documento inteiro. É essa a
// unidade que é vetorizada, recuperada e citada — por isso ele carrega, além do
// texto, de onde veio (Documento, Titulo) e a própria representação vetorial.
//
// Os atributos abaixo são lidos pelo FieldBuilder, que gera a definição dos
// campos do índice a partir desta classe. Uma classe, uma verdade: não há um
// schema escrito à mão que possa divergir do modelo.
//
// Atenção ao nome dos campos NO ÍNDICE: o serializador padrão do
// Azure.Search.Documents NÃO converte para camelCase — ele usa o nome da
// propriedade C# como está, então `Conteudo` viraria o campo `Conteudo`. Como
// este projeto se refere aos campos em minúsculo (em SearchOptions.Select, no
// VectorizedQuery.Fields e na configuração semântica), cada propriedade declara
// o nome explicitamente com [JsonPropertyName].
//
// O mesmo atributo vale para os dois lados: o FieldBuilder o usa para nomear o
// campo na criação do índice, e o serializador do SearchClient o usa ao gravar
// e ao ler documentos. Um nome só, em um lugar só.
// =============================================================================

using System.Text.Json.Serialization;

using Azure.Search.Documents.Indexes;

namespace CapacitacaoMicrosoftAIRag;

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
    /// URL de origem do documento. Não é usada pelo console — existe para a
    /// ferramenta Azure AI Search do playground do Foundry, que precisa de um
    /// campo recuperável com a URL para transformar a citação em link clicável.
    /// Sem ele a citação aparece, mas sem destino.
    ///
    /// Aqui é uma URL fictícia e estável, montada a partir do nome do arquivo.
    /// Num caso real seria o endereço do documento na intranet ou no SharePoint.
    /// </summary>
    [SimpleField]
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// O texto do trecho. É isto que vai para o contexto do modelo.
    /// O analisador em português importa para a metade keyword da busca híbrida:
    /// é ele que faz "reembolsos" casar com "reembolso".
    /// </summary>
    [SearchableField(AnalyzerName = "pt-BR.microsoft")]
    [JsonPropertyName("conteudo")]
    public string Conteudo { get; set; } = "";

    /// <summary>
    /// O embedding do <see cref="Conteudo"/>. IsHidden = true porque a aplicação
    /// nunca lê este campo de volta: ele existe só para o serviço calcular
    /// similaridade. Trazer 1536 floats por resultado seria desperdício puro.
    ///
    /// O tipo PRECISA ser float[] (ou IReadOnlyList&lt;float&gt;). Com
    /// ReadOnlyMemory&lt;float&gt; o FieldBuilder não reconhece um vetor: ele
    /// ignora este atributo e gera um campo Edm.ComplexType com as propriedades
    /// da struct (Length, IsEmpty, Span) — e aí a criação do índice falha.
    ///
    /// VectorSearchDimensions PRECISA bater com a dimensão do modelo de
    /// embeddings usado. Se você trocar o modelo, o índice tem de ser recriado.
    /// </summary>
    [VectorSearchField(
        VectorSearchDimensions = IndiceRag.DimensoesDoEmbedding,
        VectorSearchProfileName = IndiceRag.PerfilVetorial,
        IsHidden = true)]
    [JsonPropertyName("vetor")]
    public float[] Vetor { get; set; } = [];
}
