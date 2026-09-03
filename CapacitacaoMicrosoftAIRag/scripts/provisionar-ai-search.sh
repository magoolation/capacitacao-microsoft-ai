#!/usr/bin/env bash
# =============================================================================
# Cria o serviço Azure AI Search da Aula 4 e concede os papéis necessários.
# Equivalente ao provisionar-ai-search.ps1, para quem está em Linux ou macOS.
#
# Uso:
#   ./provisionar-ai-search.sh -g rg-capacitacao -n srch-capacitacao-01 [-l brazilsouth] [-s basic]
#
# Os dois papéis concedidos são distintos de propósito:
#   Search Service Contributor    -> criar, alterar e apagar ÍNDICES
#   Search Index Data Contributor -> gravar e ler DOCUMENTOS dentro do índice
# Ser Owner da subscription não substitui o segundo: Owner concede Actions,
# e mexer em documento é DataActions.
# =============================================================================

set -euo pipefail

LOCATION="brazilsouth"
SKU="basic"     # 'free' não suporta ranqueamento semântico; a Aula 4 usa 'basic'.

FOUNDRY_NAME=""
FOUNDRY_RG=""

while getopts "g:n:l:s:f:r:" opt; do
  case $opt in
    g) RESOURCE_GROUP="$OPTARG" ;;
    n) SEARCH_NAME="$OPTARG" ;;
    l) LOCATION="$OPTARG" ;;
    s) SKU="$OPTARG" ;;
    f) FOUNDRY_NAME="$OPTARG" ;;   # recurso Foundry, para o playground
    r) FOUNDRY_RG="$OPTARG" ;;     # resource group do recurso Foundry
    *) echo "Uso: $0 -g <rg> -n <servico> [-l <regiao>] [-s <sku>] [-f <foundry> -r <rg-do-foundry>]" >&2; exit 1 ;;
  esac
done

: "${RESOURCE_GROUP:?Informe o resource group com -g}"
: "${SEARCH_NAME:?Informe o nome do serviço com -n}"

log() { printf '  \033[36m%s\033[0m\n' "$1"; }

# --- 0. Pré-condições --------------------------------------------------------
command -v az >/dev/null 2>&1 || { echo "Azure CLI não encontrado. Instale o 'az'." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Você não está autenticado. Rode: az login --tenant <tenant-id>" >&2; exit 1; }

log "Subscription: $(az account show --query name -o tsv)"
log "Identidade:   $(az account show --query user.name -o tsv)"

log "Registrando o provider Microsoft.Search (idempotente)..."
az provider register --namespace Microsoft.Search --wait --only-show-errors >/dev/null

# --- 1. Resource group -------------------------------------------------------
log "Garantindo o resource group '$RESOURCE_GROUP' em '$LOCATION'..."
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --only-show-errors >/dev/null

# --- 2. Serviço de busca -----------------------------------------------------
if az search service show --name "$SEARCH_NAME" --resource-group "$RESOURCE_GROUP" >/dev/null 2>&1; then
  log "Serviço '$SEARCH_NAME' já existe — pulando a criação."
else
  log "Criando o serviço '$SEARCH_NAME' (SKU $SKU)... isso leva alguns minutos."
  az search service create \
    --name "$SEARCH_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --sku "$SKU" \
    --partition-count 1 \
    --replica-count 1 \
    --auth-options aadOrApiKey \
    --aad-auth-failure-mode http401WithBearerChallenge \
    --only-show-errors >/dev/null
fi

SCOPE=$(az search service show --name "$SEARCH_NAME" --resource-group "$RESOURCE_GROUP" --query id -o tsv)

# --- 3. Papéis ---------------------------------------------------------------
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv 2>/dev/null || true)

if [[ -z "$OBJECT_ID" ]]; then
  echo "AVISO: não consegui obter o seu objectId (comum em contas convidadas)." >&2
  echo "AVISO: atribua manualmente 'Search Service Contributor' e" >&2
  echo "AVISO: 'Search Index Data Contributor' no escopo do serviço." >&2
else
  for PAPEL in "Search Service Contributor" "Search Index Data Contributor"; do
    log "Concedendo '$PAPEL'..."
    az role assignment create \
      --assignee-object-id "$OBJECT_ID" \
      --assignee-principal-type User \
      --role "$PAPEL" \
      --scope "$SCOPE" \
      --only-show-errors >/dev/null
  done
  log "Papéis concedidos. A propagação leva de 1 a 5 minutos."
fi

# --- 3b. Identidade gerenciada do projeto Foundry ----------------------------
# A ferramenta Azure AI Search do playground NÃO usa a sua conta: usa a
# identidade gerenciada do recurso Foundry. Conceder os papéis só para você faz
# o console funcionar e o playground devolver 403.
if [[ -n "$FOUNDRY_NAME" && -n "$FOUNDRY_RG" ]]; then
  log "Procurando a identidade gerenciada do recurso Foundry '$FOUNDRY_NAME'..."
  PRINCIPAL_ID=$(az cognitiveservices account show \
    --name "$FOUNDRY_NAME" --resource-group "$FOUNDRY_RG" \
    --query identity.principalId -o tsv 2>/dev/null || true)

  if [[ -z "$PRINCIPAL_ID" ]]; then
    echo "AVISO: o recurso Foundry não tem identidade gerenciada habilitada." >&2
    echo "AVISO: habilite em portal > recurso Foundry > Identity > System assigned > On." >&2
  else
    for PAPEL in "Search Service Contributor" "Search Index Data Contributor"; do
      log "Concedendo '$PAPEL' à identidade do Foundry..."
      az role assignment create \
        --assignee-object-id "$PRINCIPAL_ID" \
        --assignee-principal-type ServicePrincipal \
        --role "$PAPEL" \
        --scope "$SCOPE" \
        --only-show-errors >/dev/null
    done
  fi
else
  echo "AVISO: -f/-r não informados. Os papéis foram concedidos apenas à SUA conta." >&2
  echo "AVISO: isso basta para o console, mas o playground do Foundry falhará com 403." >&2
fi

# --- 4. Saída ----------------------------------------------------------------
cat <<SAIDA

Pronto. Cole em Properties/launchSettings.json:

  "SEARCH_ENDPOINT": "https://${SEARCH_NAME}.search.windows.net",
  "SEARCH_INDEX": "politicas-internas"

Opcional — exigir Entra ID e desligar as chaves de API:
  az search service update --name ${SEARCH_NAME} --resource-group ${RESOURCE_GROUP} --disable-local-auth true
SAIDA
