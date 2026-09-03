<#
.SYNOPSIS
    Cria o serviço Azure AI Search da Aula 4 e concede os papéis necessários.

.DESCRIPTION
    O script é idempotente: rodar duas vezes não quebra nada. Ele
      1. garante o resource group;
      2. cria o serviço de busca com autenticação por Entra ID habilitada;
      3. concede a você os dois papéis que a demo precisa;
      4. imprime o bloco pronto para colar no launchSettings.json.

    Os dois papéis são distintos de propósito, e vale explicar isso em sala:
      Search Service Contributor    → criar, alterar e apagar ÍNDICES
      Search Index Data Contributor → gravar e ler DOCUMENTOS dentro do índice
    Ser Owner da subscription não substitui o segundo: Owner concede Actions,
    e mexer em documento é DataActions. É o mesmo mal-entendido da Aula 1.

.EXAMPLE
    # Só para o console:
    ./provisionar-ai-search.ps1 -ResourceGroup rg-capacitacao -SearchName srch-capacitacao-01

.EXAMPLE
    # Console + playground do Foundry (concede também à identidade do recurso):
    ./provisionar-ai-search.ps1 -ResourceGroup rg-capacitacao -SearchName srch-capacitacao-01 `
        -FoundryName capacitacao-msai-foundry -FoundryResourceGroup rg-capacitacao
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,

    # Precisa ser único no mundo: só minúsculas, dígitos e hífen.
    [Parameter(Mandatory)] [string] $SearchName,

    [string] $Location = 'brazilsouth',

    # 'free' permite 1 serviço por subscription e NÃO suporta ranqueamento
    # semântico. Para a Aula 4 completa, use 'basic'.
    [ValidateSet('free', 'basic', 'standard')]
    [string] $Sku = 'basic',

    # Recurso Foundry cuja IDENTIDADE GERENCIADA vai consultar o índice a partir
    # do playground. Sem estes dois, o script concede os papéis só para VOCÊ — o
    # que basta para o console, mas não para o portal.
    [string] $FoundryName,
    [string] $FoundryResourceGroup
)

$ErrorActionPreference = 'Stop'

function Escrever($mensagem) { Write-Host "  $mensagem" -ForegroundColor Cyan }

# --- 0. Pré-condições ---------------------------------------------------------
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI não encontrado. Instale o 'az' antes de rodar este script."
}

$conta = az account show --output json 2>$null | ConvertFrom-Json
if (-not $conta) {
    throw "Você não está autenticado. Rode: az login --tenant <tenant-id>"
}

Escrever "Subscription: $($conta.name)"
Escrever "Identidade:   $($conta.user.name)"

# O provider precisa estar registrado na subscription — em assinaturas novas,
# muitas vezes não está, e o erro que aparece depois é pouco informativo.
Escrever "Registrando o provider Microsoft.Search (idempotente)..."
az provider register --namespace Microsoft.Search --wait --only-show-errors | Out-Null

# --- 1. Resource group --------------------------------------------------------
Escrever "Garantindo o resource group '$ResourceGroup' em '$Location'..."
az group create --name $ResourceGroup --location $Location --only-show-errors | Out-Null

# --- 2. Serviço de busca ------------------------------------------------------
$existente = az search service show `
    --name $SearchName `
    --resource-group $ResourceGroup `
    --output json 2>$null | ConvertFrom-Json

if ($existente) {
    Escrever "Serviço '$SearchName' já existe — pulando a criação."
}
else {
    Escrever "Criando o serviço '$SearchName' (SKU $Sku)... isso leva alguns minutos."

    # --auth-options aadOrApiKey habilita o Entra ID mantendo as chaves como
    # alternativa. Para exigir SOMENTE Entra ID, veja o passo opcional no fim.
    az search service create `
        --name $SearchName `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku $Sku `
        --partition-count 1 `
        --replica-count 1 `
        --auth-options aadOrApiKey `
        --aad-auth-failure-mode http401WithBearerChallenge `
        --only-show-errors | Out-Null
}

$escopo = az search service show `
    --name $SearchName `
    --resource-group $ResourceGroup `
    --query id --output tsv

# --- 3. Papéis ----------------------------------------------------------------
$objectId = az ad signed-in-user show --query id --output tsv 2>$null

if (-not $objectId) {
    Write-Warning "Não consegui obter o seu objectId (comum em contas convidadas)."
    Write-Warning "Atribua manualmente, no portal, os papéis 'Search Service Contributor'"
    Write-Warning "e 'Search Index Data Contributor' no escopo do serviço de busca."
}
else {
    foreach ($papel in @('Search Service Contributor', 'Search Index Data Contributor')) {
        Escrever "Concedendo '$papel'..."

        # `az role assignment create` é idempotente: se já existe, avisa e segue.
        az role assignment create `
            --assignee-object-id $objectId `
            --assignee-principal-type User `
            --role $papel `
            --scope $escopo `
            --only-show-errors | Out-Null
    }

    Escrever "Papéis concedidos. A propagação leva de 1 a 5 minutos."
}

# --- 3b. Identidade gerenciada do projeto Foundry -----------------------------
# A ferramenta Azure AI Search do playground NÃO usa a sua conta: ela usa a
# identidade gerenciada do recurso Foundry. Se você só conceder os papéis para
# si mesmo, o console funciona e o playground devolve 403 — e o erro no portal
# não diz qual identidade foi negada, o que torna o diagnóstico penoso.
if ($FoundryName -and $FoundryResourceGroup) {
    Escrever "Procurando a identidade gerenciada do recurso Foundry '$FoundryName'..."

    $principalId = az cognitiveservices account show `
        --name $FoundryName `
        --resource-group $FoundryResourceGroup `
        --query identity.principalId --output tsv 2>$null

    if (-not $principalId) {
        Write-Warning "O recurso Foundry não tem identidade gerenciada habilitada."
        Write-Warning "Habilite em: portal do Azure > recurso Foundry > Identity > System assigned > On."
    }
    else {
        foreach ($papel in @('Search Service Contributor', 'Search Index Data Contributor')) {
            Escrever "Concedendo '$papel' à identidade do Foundry..."

            az role assignment create `
                --assignee-object-id $principalId `
                --assignee-principal-type ServicePrincipal `
                --role $papel `
                --scope $escopo `
                --only-show-errors | Out-Null
        }
    }
}
else {
    Write-Warning "Parâmetros -FoundryName e -FoundryResourceGroup não informados."
    Write-Warning "Os papéis foram concedidos apenas à SUA conta. Isso basta para rodar o"
    Write-Warning "console (dotnet run), mas o playground do Foundry vai falhar com 403."
}

# --- 4. Saída ----------------------------------------------------------------
$endpoint = "https://$SearchName.search.windows.net"

Write-Host ""
Write-Host "Pronto. Cole em Properties/launchSettings.json:" -ForegroundColor Green
Write-Host ""
Write-Host "  `"SEARCH_ENDPOINT`": `"$endpoint`","
Write-Host "  `"SEARCH_INDEX`": `"politicas-internas`""
Write-Host ""
Write-Host "Opcional — exigir Entra ID e desligar as chaves de API:" -ForegroundColor DarkGray
Write-Host "  az search service update --name $SearchName --resource-group $ResourceGroup --disable-local-auth true" -ForegroundColor DarkGray
