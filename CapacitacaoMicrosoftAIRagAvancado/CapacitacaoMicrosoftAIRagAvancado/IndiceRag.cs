// =============================================================================
// A metade "R" do RAG: criar o índice, indexar o corpus e recuperar trechos.
// =============================================================================
// Vem da aula 4 praticamente intacta. A única diferença que importa está em
// BuscarAsync: aqui o `topK` deixou de ser um detalhe com valor padrão e virou
// o parâmetro que a aula inteira gira em torno (opção 5 do menu).
// =============================================================================

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

using Microsoft.Extensions.AI;

namespace CapacitacaoMicrosoftAIRagAvancado;

public sealed class IndiceRag
{
    // Nomes usados na definição do índice. Ficam como const porque o atributo
    // [VectorSearchField] do Chunk precisa deles em tempo de compilação.
    public const string PerfilVetorial = "perfil-vetorial";
    public const string AlgoritmoVetorial = "hnsw-padrao";
    public const string ConfiguracaoSemantica = "semantica-padrao";

    /// <summary>
    /// Dimensão do vetor. text-embedding-3-small e text-embedding-ada-002 usam
    /// 1536; text-embedding-3-large usa 3072. Trocar o modelo exige recriar o
    /// índice — o serviço rejeita vetores de tamanho diferente do declarado.
    /// </summary>
    public const int DimensoesDoEmbedding = 1536;

    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly string _nomeDoIndice;

    public IndiceRag(
        SearchIndexClient indexClient,
        SearchClient searchClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        string nomeDoIndice)
    {
        _indexClient = indexClient;
        _searchClient = searchClient;
        _embeddings = embeddings;
        _nomeDoIndice = nomeDoIndice;
    }

    // -------------------------------------------------------------------------
    // 1. Criação do índice
    // -------------------------------------------------------------------------
    // O índice declara três coisas independentes: os CAMPOS (gerados a partir da
    // classe Chunk pelo FieldBuilder), como fazer busca VETORIAL e como fazer
    // reordenação SEMÂNTICA. Busca keyword não precisa de configuração: sai de
    // graça de todo campo marcado como pesquisável.
    public async Task CriarOuAtualizarIndiceAsync()
    {
        var campos = new FieldBuilder().Build(typeof(Chunk));

        var indice = new SearchIndex(_nomeDoIndice, campos)
        {
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration(AlgoritmoVetorial) },
                Profiles = { new VectorSearchProfile(PerfilVetorial, AlgoritmoVetorial) }
            },
            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(
                        ConfiguracaoSemantica,
                        new SemanticPrioritizedFields
                        {
                            TitleField = new SemanticField("titulo"),
                            ContentFields = { new SemanticField("conteudo") }
                        })
                }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(indice);
    }

    public async Task ApagarIndiceAsync()
    {
        try
        {
            await _indexClient.DeleteIndexAsync(_nomeDoIndice, CancellationToken.None);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // Índice já não existe: apagar de novo não é erro.
        }
    }

    /// <summary>Quantos documentos há no índice. Zero significa "indexe antes".</summary>
    public async Task<long?> ContarDocumentosAsync()
    {
        try
        {
            var contagem = await _searchClient.GetDocumentCountAsync();
            return contagem.Value;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // 2. Chunking
    // -------------------------------------------------------------------------
    // Quebrar por parágrafo e juntar parágrafos vizinhos até chegar perto de um
    // tamanho-alvo. Chunk grande demais dilui o sinal; pequeno demais perde o
    // contexto que dá sentido à frase.
    public static IEnumerable<string> Fatiar(string texto, int alvoDeCaracteres = 800)
    {
        var paragrafos = texto
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var atual = new System.Text.StringBuilder();

        foreach (var paragrafo in paragrafos)
        {
            // Fecha o chunk atual antes de estourar o alvo.
            if (atual.Length > 0 && atual.Length + paragrafo.Length > alvoDeCaracteres)
            {
                yield return atual.ToString();
                atual.Clear();
            }

            if (atual.Length > 0)
            {
                atual.Append("\n\n");
            }

            atual.Append(paragrafo);
        }

        if (atual.Length > 0)
        {
            yield return atual.ToString();
        }
    }

    // -------------------------------------------------------------------------
    // 3. Indexação
    // -------------------------------------------------------------------------
    // O embedding é gerado UMA vez por chunk, na indexação. Na hora da pergunta,
    // gera-se só o embedding da pergunta. É essa assimetria que torna RAG barato
    // em tempo de consulta.
    public async Task<int> IndexarAsync(
        string pastaDoCorpus,
        int alvoDeCaracteres = 800,
        Action<string>? log = null)
    {
        var chunks = new List<Chunk>();

        foreach (var caminho in Directory.EnumerateFiles(pastaDoCorpus, "*.md").Order())
        {
            var nomeDoArquivo = Path.GetFileName(caminho);
            var texto = await File.ReadAllTextAsync(caminho);

            // Primeira linha "# Título" vira o título do documento.
            var primeiraLinha = texto.Split('\n', 2)[0].TrimStart('#', ' ').Trim();

            var pedacos = Fatiar(texto, alvoDeCaracteres).ToList();
            log?.Invoke($"  {nomeDoArquivo,-34} {pedacos.Count} chunk(s)");

            for (var i = 0; i < pedacos.Count; i++)
            {
                chunks.Add(new Chunk
                {
                    // A chave do índice não aceita ponto nem acento.
                    Id = $"{Path.GetFileNameWithoutExtension(caminho)}-{i:D3}",
                    Documento = nomeDoArquivo,
                    Titulo = primeiraLinha,
                    Url = $"https://intranet.auroralog.example/politicas/{nomeDoArquivo}",
                    Conteudo = pedacos[i]
                });
            }
        }

        // Um lote só de embeddings: bem mais rápido que uma chamada por chunk.
        var vetores = await _embeddings.GenerateAsync(chunks.Select(c => c.Conteudo));

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Vetor = vetores[i].Vector.ToArray();
        }

        await _searchClient.UploadDocumentsAsync(chunks);
        return chunks.Count;
    }

    // -------------------------------------------------------------------------
    // 4. Recuperação
    // -------------------------------------------------------------------------
    public enum Modo
    {
        /// <summary>Só palavras-chave (BM25). Acerta o literal, erra o sinônimo.</summary>
        Keyword,

        /// <summary>Só vetorial. Acerta o sinônimo, erra o código exato.</summary>
        Vetorial,

        /// <summary>Vetorial + BM25 em paralelo, sem reordenação semântica.</summary>
        Hibrida,

        /// <summary>
        /// Híbrida com o ranqueador semântico por cima. É o modo padrão desta
        /// aula, e a opção 6 do menu existe para medir quanto ele muda.
        /// </summary>
        HibridaComReranking
    }

    public async Task<IReadOnlyList<(Chunk Trecho, double? Score)>> BuscarAsync(
        string pergunta,
        Modo modo,
        int topK,
        CancellationToken ct = default)
    {
        var opcoes = new SearchOptions
        {
            Size = topK,
            // Sem isto, o campo `vetor` viria de volta em todo resultado.
            Select = { "id", "documento", "titulo", "conteudo", "url" }
        };

        // A metade vetorial: precisa do embedding DA PERGUNTA.
        if (modo is not Modo.Keyword)
        {
            ReadOnlyMemory<float> vetorDaPergunta =
                await _embeddings.GenerateVectorAsync(pergunta, cancellationToken: ct);

            opcoes.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vetorDaPergunta)
                    {
                        KNearestNeighborsCount = topK,
                        Fields = { "vetor" }
                    }
                }
            };
        }

        // A reordenação semântica é o que separa Hibrida de HibridaComReranking.
        // Só ela precisa de SKU basic ou superior: no free, esta linha dá erro.
        if (modo is Modo.HibridaComReranking)
        {
            opcoes.QueryType = SearchQueryType.Semantic;
            opcoes.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = ConfiguracaoSemantica
            };
        }

        // No modo puramente vetorial não se envia texto de busca: o `null` é
        // que diz ao serviço "ignore BM25, use só o vetor".
        var textoDeBusca = modo is Modo.Vetorial ? null : pergunta;

        var resposta = await _searchClient.SearchAsync<Chunk>(textoDeBusca, opcoes, ct);

        var resultados = new List<(Chunk, double?)>();
        await foreach (var item in resposta.Value.GetResultsAsync())
        {
            // Quando há reranking, o score que interessa é o do ranqueador
            // semântico (escala 0-4), e não o do BM25/vetorial (escala aberta).
            // Comparar os dois números entre si não significa nada — e é um erro
            // comum de quem olha esta saída pela primeira vez.
            resultados.Add((item.Document, item.SemanticSearch?.RerankerScore ?? item.Score));
        }

        return resultados;
    }
}
