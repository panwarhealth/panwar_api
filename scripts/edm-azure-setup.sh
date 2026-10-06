#!/usr/bin/env bash
# eDM Mailer Azure setup. Run the phases in order from Git Bash after `az login`:
#
#   bash scripts/edm-azure-setup.sh create     # email service, 3 domains, ACS resource; prints DNS records
#   (add the printed DNS records in Cloudflare, wait for them to resolve)
#   bash scripts/edm-azure-setup.sh verify     # verifies domains, links them, adds sender addresses
#   bash scripts/edm-azure-setup.sh configure  # mailer app role + app settings on panwar-api / pharmachat / lms
#   (deploy panwar-api with the eDM Mailer so /api/edm/events exists)
#   bash scripts/edm-azure-setup.sh events     # Event Grid delivery reports -> /api/edm/events
#
# Every phase is safe to re-run.
set -euo pipefail
# Git Bash would otherwise rewrite Azure resource ids (/subscriptions/...) into Windows paths.
export MSYS_NO_PATHCONV=1

RG=panwarhealth
EMAIL_SERVICE=panwar-email
ACS=panwar-comms
API_APP=panwar-api
API_BASE=https://api.panwarhealth.com.au
ENTRA_APP_ID=479359b6-04f9-4471-9dd5-924e4365da7c

# domain|sender username|display name. Each one is also seeded into edm_sender by a migration
# (see Migrations/*_SeedEdmSenders.cs); a new brand needs a line here, a new migration, and DNS.
SENDERS=(
  "pharmachat.com.au|updates|PharmaChat"
  "clinicalstudio.com.au|insights|Clinical Studio"
  "panwarhealth.com.au|newsletter|Panwar Health"
)

domain_of() { echo "${1%%|*}"; }

create() {
  az extension add --name communication --upgrade --only-show-errors
  az communication email create -n "$EMAIL_SERVICE" -g "$RG" --location global --data-location Australia -o none
  for s in "${SENDERS[@]}"; do
    d=$(domain_of "$s")
    # Our own pixel tracks opens; ACS engagement tracking would rewrite every link and break the UTMs.
    az communication email domain create --domain-name "$d" --email-service-name "$EMAIL_SERVICE" -g "$RG" \
      --location global --domain-management CustomerManaged --user-engmnt-tracking Disabled -o none
  done
  az communication create -n "$ACS" -g "$RG" --location global --data-location Australia -o none

  echo
  echo "Add these DNS records (Cloudflare: DNS only, not proxied):"
  for s in "${SENDERS[@]}"; do
    d=$(domain_of "$s")
    echo "--- $d"
    az communication email domain show --domain-name "$d" --email-service-name "$EMAIL_SERVICE" -g "$RG" \
      --query "verificationRecords.[Domain, SPF, DKIM, DKIM2][].{type:type, name:name, value:value}" -o table
  done
  echo
  echo "If a domain already has an SPF record, merge include:spf.protection.outlook.com into it rather than adding a second one."
}

verify() {
  ids=()
  for s in "${SENDERS[@]}"; do
    IFS='|' read -r d user display <<<"$s"
    for t in Domain SPF DKIM DKIM2; do
      az communication email domain initiate-verification --domain-name "$d" --email-service-name "$EMAIL_SERVICE" \
        -g "$RG" --verification-type "$t" -o none || true
    done
    az communication email domain sender-username create --sender-username "$user" --username "$user" \
      --display-name "$display" --domain-name "$d" --email-service-name "$EMAIL_SERVICE" -g "$RG" -o none
    ids+=("$(az communication email domain show --domain-name "$d" --email-service-name "$EMAIL_SERVICE" -g "$RG" --query id -o tsv)")
    az communication email domain show --domain-name "$d" --email-service-name "$EMAIL_SERVICE" -g "$RG" \
      --query "{domain:name, domainCheck:verificationStates.Domain.status, spf:verificationStates.SPF.status, dkim:verificationStates.DKIM.status, dkim2:verificationStates.DKIM2.status}" -o table
  done
  # Linking fails until every domain shows Verified; re-run this phase once they all are.
  az communication update -n "$ACS" -g "$RG" --linked-domains "${ids[@]}" -o none \
    && echo "Domains linked to $ACS." \
    || echo "Not all domains are verified yet. Re-run 'verify' in a few minutes."
}

configure() {
  # 1. The mailer app role (Users & Roles assigns it; people sign out and in to pick it up).
  roles=$(az ad app show --id "$ENTRA_APP_ID" --query appRoles -o json)
  if ! grep -q '"value": "mailer"' <<<"$roles"; then
    new=$(py -c "import json,sys,uuid; r=json.loads(sys.argv[1]); r.append({'allowedMemberTypes':['User'],'description':'Send eDMs with the eDM Mailer','displayName':'Mailer','id':str(uuid.uuid4()),'isEnabled':True,'value':'mailer'}); print(json.dumps(r))" "$roles")
    tmp=$(mktemp); echo "$new" >"$tmp"
    az ad app update --id "$ENTRA_APP_ID" --app-roles @"$(cygpath -w "$tmp")"
    rm "$tmp"
    echo "Added the mailer app role. Restart $API_APP so Users & Roles sees it."
  fi

  # 2. Secrets shared between the apps. Generated once; reuse what's already set.
  existing() { az functionapp config appsettings list -n "$API_APP" -g "$RG" --query "[?name=='$1'].value | [0]" -o tsv; }
  eventgrid_key=$(existing EDM_EVENTGRID_KEY); [ -n "$eventgrid_key" ] || eventgrid_key=$(openssl rand -hex 24)
  pc_key=$(existing EDM_SYNC_PHARMACHAT_KEY); [ -n "$pc_key" ] || pc_key=$(openssl rand -hex 32)
  lms_key=$(existing EDM_SYNC_CLINICALSTUDIO_KEY); [ -n "$lms_key" ] || lms_key=$(openssl rand -hex 32)
  acs_conn=$(az communication list-key -n "$ACS" -g "$RG" --query primaryConnectionString -o tsv)
  pc_host=$(az functionapp show -n pharmachat-api-flex -g pharmachat --query defaultHostName -o tsv)
  lms_host=$(az functionapp show -n lms-api -g LMS --query defaultHostName -o tsv)

  az functionapp config appsettings set -n "$API_APP" -g "$RG" -o none --settings \
    "ACS_EMAIL_CONNECTION_STRING=$acs_conn" \
    "API_BASE_URL=$API_BASE" \
    "EDM_R2_BUCKET=edm-assets" \
    "EDM_R2_PUBLIC_BASE_URL=https://cdn.panwarhealth.com.au" \
    "EDM_SEND_PER_MINUTE=30" \
    "EDM_EVENTGRID_KEY=$eventgrid_key" \
    "EDM_SYNC_PHARMACHAT_URL=https://$pc_host" \
    "EDM_SYNC_PHARMACHAT_KEY=$pc_key" \
    "EDM_SYNC_CLINICALSTUDIO_URL=https://$lms_host" \
    "EDM_SYNC_CLINICALSTUDIO_KEY=$lms_key"
  az functionapp config appsettings set -n pharmachat-api-flex -g pharmachat -o none --settings "EDM_SYNC_KEY=$pc_key"
  az functionapp config appsettings set -n lms-api -g LMS -o none --settings \
    "EDM_SYNC_KEY=$lms_key" "EDM_SYNC_SIGNUP_ENVIRONMENT=production"
  echo "App settings set on $API_APP, pharmachat-api-flex and lms-api."
  echo "If the CLOUDFLARE_R2_* token on $API_APP can't write to edm-assets, add EDM_R2_ACCESS_KEY / EDM_R2_SECRET_KEY for one that can."
}

events() {
  key=$(az functionapp config appsettings list -n "$API_APP" -g "$RG" --query "[?name=='EDM_EVENTGRID_KEY'].value | [0]" -o tsv)
  [ -n "$key" ] || { echo "Run 'configure' first."; exit 1; }
  acs_id=$(az communication show -n "$ACS" -g "$RG" --query id -o tsv)
  az eventgrid event-subscription create --name edm-delivery-reports --source-resource-id "$acs_id" \
    --endpoint "$API_BASE/api/edm/events?key=$key" \
    --included-event-types Microsoft.Communication.EmailDeliveryReportReceived -o none
  echo "Event Grid is sending delivery reports to $API_BASE/api/edm/events."
}

case "${1:-}" in
  create | verify | configure | events) "$1" ;;
  *) sed -n '2,11p' "$0"; exit 1 ;;
esac
