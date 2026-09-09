# Slides

Versão em PDF dos decks da trilha **DotNet 10 LTS** — a que corresponde ao código deste repositório.

| Aula | Páginas | Projeto correspondente |
|---|---|---|
| 1 — Fundamentos de IA, IA Generativa e o Ecossistema Microsoft | 30 | [`CapacitacaoMicrosoftAIFundamentos`](../CapacitacaoMicrosoftAIFundamentos/) |
| 2 — Large Language Models e Engenharia de Prompts | 28 | [`CapacitacaoMicrosoftAILLMePrompts`](../CapacitacaoMicrosoftAILLMePrompts/) |
| 3 — Microsoft Foundry: Recurso, Projeto, Deploy e SDK | 28 | [`CapacitacaoMicrosoftAIFoundrySDK`](../CapacitacaoMicrosoftAIFoundrySDK/) |
| 4 — Conceitos de RAG, Embeddings e Azure AI Search | 27 | [`CapacitacaoMicrosoftAIRag`](../CapacitacaoMicrosoftAIRag/) |
| 5 — Construindo soluções RAG end-to-end no Microsoft Foundry | 26 | [`CapacitacaoMicrosoftAIRagAvancado`](../CapacitacaoMicrosoftAIRagAvancado/) |

Cada aula traz, além do conteúdo, um slide **"Como construir o projeto da aula"** logo após o hands-on e **cinco pares pergunta/resposta** de quiz — a pergunta com as quatro alternativas em um slide, a alternativa correta e a justificativa no seguinte. O gabarito também está nas notas do apresentador do arquivo `.pptx`.

## Origem e regeneração

Estes PDFs são **gerados**, não editados. A fonte são os `.pptx` da trilha DotNet 10 LTS, que vivem fora deste repositório. Ao alterar um deck, regere o PDF:

```powershell
# No PowerPoint: Arquivo > Exportar > Criar PDF/XPS
# Ou, com o LibreOffice instalado:
soffice --headless --convert-to pdf "Aula 03 - ....pptx"
```

> Os PDFs deste diretório foram convertidos com LibreOffice. A substituição de fontes pode deslocar levemente algumas quebras de linha em relação ao que o PowerPoint renderiza — para a versão de referência exata, exporte pelo próprio PowerPoint.

As notas do apresentador **não** saem na exportação padrão. Para incluí-las, exporte no PowerPoint escolhendo o layout "Anotações".
