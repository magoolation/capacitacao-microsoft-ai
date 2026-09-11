// =============================================================================
// As quatro tools do agente da aula 7.
// =============================================================================
// Uma tool e uma funcao C# comum. O que a transforma em "ferramenta" e o
// AIFunctionFactory.Create (no Program.cs), que gera o schema a partir da
// assinatura: o nome do metodo vira o nome da tool, os parametros viram o schema
// de argumentos e os atributos [Description] viram a documentacao que o modelo
// le para decidir QUANDO chamar cada uma.
//
// Tres regras que valem para toda tool, e que este arquivo demonstra:
//
//   1. O MODELO NAO EXECUTA NADA. Ele emite uma intencao ("chame get_weather
//      com city=Fortaleza"); quem executa e o seu codigo. Logo, validar os
//      argumentos e obrigacao sua - o modelo pode alucinar valores invalidos.
//   2. Trate os argumentos como entrada hostil. Uma instrucao maliciosa num
//      documento lido pelo agente pode virar uma chamada de tool. Escopo
//      minimo, sem segredos no retorno, e erro claro em vez de excecao solta.
//   3. Acao irreversivel pede aprovacao humana. Aqui, o envio de e-mail e
//      embrulhado em ApprovalRequiredAIFunction no Program.cs - a tool em si nao
//      sabe disso, e e assim que deve ser.
//
// Nenhuma delas fala com servico real: sao mocks deterministas, para que a
// demo funcione sem depender de mais nada alem do Foundry. O ponto da aula e o
// CICLO do function calling, nao o clima de verdade.
// =============================================================================

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace CapacitacaoMicrosoftAIAgentesAvancados;

public static class Ferramentas
{
    // Uma "base de conhecimento" minima. Na aula 4 isto era o Azure AI Search;
    // aqui e um dicionario, para a tool ser trivialmente testavel.
    private static readonly Dictionary<string, string> BaseDeConhecimento = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reembolso"] = "Teto de jantar em viagem nacional: R$ 90,00 por pessoa. Lancar em ate 15 dias corridos.",
        ["ferias"] = "Solicitar com 30 dias de antecedencia. Periodos de 10, 15 ou 30 dias.",
        ["remoto"] = "Times de engenharia: 2 dias por semana no escritorio, medidos por trimestre.",
        ["sla"] = "Severidade 1: primeira resposta em 30 minutos, 24x7.",
    };

    [Description("Consulta a base de conhecimento interna da empresa por um assunto (ex.: reembolso, ferias, remoto, sla).")]
    public static string ReadKb(
        [Description("Assunto a consultar, em uma ou duas palavras.")] string topic)
    {
        // Validacao: argumento vazio ou absurdo vira erro legivel para o modelo,
        // nao excecao. O modelo le a mensagem e corrige a chamada.
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 40)
        {
            return "Erro: informe um assunto curto (ex.: 'reembolso').";
        }

        var chave = topic.Trim().ToLowerInvariant();
        return BaseDeConhecimento.TryGetValue(chave, out var texto)
            ? texto
            : $"Nao ha entrada para '{topic}'. Assuntos disponiveis: {string.Join(", ", BaseDeConhecimento.Keys)}.";
    }

    [Description("Retorna a previsao do tempo atual para uma cidade brasileira.")]
    public static string GetWeather(
        [Description("Nome da cidade, ex.: Fortaleza.")] string city)
    {
        if (string.IsNullOrWhiteSpace(city) || city.Length is < 2 or > 60)
        {
            return "Erro: nome de cidade invalido.";
        }

        // Determinista de proposito: a mesma cidade sempre devolve o mesmo
        // resultado, o que torna a demo (e um teste) reproduzivel.
        var temperatura = 22 + Math.Abs(city.ToLowerInvariant().GetHashCode(StringComparison.Ordinal)) % 12;
        return $"Em {city.Trim()}: {temperatura}°C, parcialmente nublado.";
    }

    [Description("Envia um e-mail em nome do usuario. ACAO IRREVERSIVEL: exige confirmacao humana.")]
    public static string SendEmailMock(
        [Description("Endereco do destinatario.")] string to,
        [Description("Assunto do e-mail.")] string subject,
        [Description("Corpo do e-mail.")] string body)
    {
        if (!to.Contains('@') || to.Length > 120)
        {
            return "Erro: destinatario invalido.";
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return "Erro: o assunto e obrigatorio.";
        }

        // Mock: nada e enviado. Em producao, aqui entraria o Graph API ou o
        // servico de e-mail - com a identidade do agente, nao a de um humano.
        return $"[mock] E-mail enviado para {to} com assunto '{subject}' ({body.Length} caracteres).";
    }

    [Description("Calcula uma expressao aritmetica simples com dois operandos (ex.: 90 * 5).")]
    public static string Calc(
        [Description("Primeiro operando.")] double a,
        [Description("Operador: +, -, * ou /.")] string op,
        [Description("Segundo operando.")] double b)
    {
        // Sem eval, sem parser generico: a superficie de ataque de uma tool e
        // proporcional ao que ela aceita. Quatro operadores bastam para a aula.
        double resultado = op switch
        {
            "+" => a + b,
            "-" => a - b,
            "*" => a * b,
            "/" when b != 0 => a / b,
            "/" => double.NaN,
            _ => double.NaN,
        };

        return double.IsNaN(resultado)
            ? "Erro: operador invalido ou divisao por zero."
            : resultado.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Serializa os argumentos de uma chamada para o log. Util para mostrar em
    /// sala, em tempo real, o que o modelo pediu - e para auditoria depois.
    /// </summary>
    public static string Descrever(IDictionary<string, object?>? args)
        => args is null ? "{}" : JsonSerializer.Serialize(args);
}
