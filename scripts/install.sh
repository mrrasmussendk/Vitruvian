#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT_DIR/.env.Vitruvian"
DEFAULT_SQLITE_FILE_CONNECTION="Data Source=appdb/Vitruvian-memory.db"
HAS_SOURCE_LAYOUT="false"
if [[ -f "$ROOT_DIR/Vitruviansln" && -d "$ROOT_DIR/src/VitruvianCli" ]]; then
  HAS_SOURCE_LAYOUT="true"
fi

normalize_profile() {
  case "${1,,}" in
    1|dev) echo "dev" ;;
    2|personal) echo "personal" ;;
    3|team) echo "team" ;;
    4|prod) echo "prod" ;;
    *) return 1 ;;
  esac
}

read_cached_value() {
  local key="$1"
  local file_path="$2"
  [[ -f "$file_path" ]] || return 0

  while IFS= read -r line; do
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    local value=""
    if [[ "$line" =~ ^[[:space:]]*export[[:space:]]+$key=(.*)$ ]]; then
      value="${BASH_REMATCH[1]}"
    elif [[ "$line" =~ ^[[:space:]]*[$]env:${key}=(.*)$ ]]; then
      value="${BASH_REMATCH[1]}"
    elif [[ "$line" =~ ^[[:space:]]*${key}=(.*)$ ]]; then
      value="${BASH_REMATCH[1]}"
    else
      continue
    fi

    if [[ ${#value} -ge 2 ]]; then
      if [[ ( "${value:0:1}" == "'" && "${value: -1}" == "'" ) || ( "${value:0:1}" == "\"" && "${value: -1}" == "\"" ) ]]; then
        value="${value:1:${#value}-2}"
      fi
    fi

    printf '%s' "$value"
    return 0
  done < "$file_path"
}

write_active_profile() {
  local profile_name="$1"
  {
    echo "VITRUVIAN_PROFILE=${profile_name}"
  } > "$ENV_FILE"
}

if [[ $# -ge 1 ]]; then
  profile="$(normalize_profile "$1")" || { echo "Invalid profile '$1'. Use dev, personal, team, or prod."; exit 1; }
  profile_file="$ROOT_DIR/.env.Vitruvian.${profile}"
  if [[ ! -f "$profile_file" ]]; then
    echo "Profile '$profile' does not exist yet. Create it first by running the installer without arguments."
    exit 1
  fi

  write_active_profile "$profile"
  echo "Switched active profile to '$profile' in $ENV_FILE"
  exit 0
fi

echo "Vitruvian installer"
echo "Select onboarding action:"
echo "  1) Create/update profile configuration"
echo "  2) Switch active profile"
read -r -p "> " onboarding_action
echo "Select profile:"
echo "  1) dev"
echo "  2) personal"
echo "  3) team"
echo "  4) prod"
read -r -p "> " profile_choice
profile="$(normalize_profile "$profile_choice")" || { echo "Invalid profile choice"; exit 1; }
profile_env_file="$ROOT_DIR/.env.Vitruvian.${profile}"

if [[ "$onboarding_action" == "2" ]]; then
  if [[ ! -f "$profile_env_file" ]]; then
    echo "Profile '$profile' has not been configured yet. Choose create/update first."
    exit 1
  fi

  write_active_profile "$profile"
  echo "Active profile set to '$profile'."
  echo "Configuration saved to: $ENV_FILE"
  exit 0
fi

if [[ "$onboarding_action" != "1" ]]; then
  echo "Invalid onboarding action"
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK was not found in PATH. Install .NET 10 SDK before onboarding."
  exit 1
fi

echo "Select model provider:"
echo "  1) OpenAI"
echo "  2) Anthropic"
echo "  3) Gemini"
read -r -p "> " provider_choice

case "$provider_choice" in
  1) provider="openai"; key_name="OPENAI_API_KEY"; default_model="gpt-4o-mini" ;;
  2) provider="anthropic"; key_name="ANTHROPIC_API_KEY"; default_model="claude-3-5-haiku-latest" ;;
  3) provider="gemini"; key_name="GEMINI_API_KEY"; default_model="gemini-2.0-flash" ;;
  *) echo "Invalid provider choice"; exit 1 ;;
esac

read -r -p "Enter ${key_name}: " api_key
if [[ -z "${api_key}" ]]; then
  api_key="$(read_cached_value "$key_name" "$profile_env_file")"
fi
if [[ -z "${api_key}" ]]; then
  echo "${key_name} is required."
  exit 1
fi
read -r -p "Enter model name [${default_model}]: " selected_model
selected_model="${selected_model:-$default_model}"

echo
echo "Select deployment mode:"
echo "  1) Local console"
echo "  2) Discord channel"
echo "  3) WebSocket host"
read -r -p "> " deploy_choice
if [[ "$deploy_choice" != "1" && "$deploy_choice" != "2" && "$deploy_choice" != "3" ]]; then
  echo "Invalid deployment choice"
  exit 1
fi

echo
echo "Select memory storage:"
echo "  1) Local SQLite (recommended default)"
echo "  2) Third-party connection string"
read -r -p "> " storage_choice

case "$storage_choice" in
  1|"")
    memory_connection="$DEFAULT_SQLITE_FILE_CONNECTION"
    ;;
  2)
    read -r -p "Enter VITRUVIAN_MEMORY_CONNECTION_STRING: " memory_connection
    if [[ -z "${memory_connection}" ]]; then
      echo "A third-party connection string is required for this option."
      exit 1
    fi
    ;;
  *)
    echo "Invalid storage choice"
    exit 1
    ;;
esac

{
  echo "VITRUVIAN_MODEL_PROVIDER=${provider}"
  echo "${key_name}=${api_key}"
  echo "VITRUVIAN_MODEL_NAME=${selected_model}"
  echo "VITRUVIAN_MEMORY_CONNECTION_STRING=${memory_connection}"
} > "$profile_env_file"

if [[ "$deploy_choice" == "2" ]]; then
  read -r -p "Enter DISCORD_BOT_TOKEN: " discord_token
  read -r -p "Enter DISCORD_CHANNEL_ID: " discord_channel
  if [[ -z "${discord_token}" || -z "${discord_channel}" ]]; then
    echo "DISCORD_BOT_TOKEN and DISCORD_CHANNEL_ID are required for Discord mode."
    exit 1
  fi
  {
    echo "DISCORD_BOT_TOKEN=${discord_token}"
    echo "DISCORD_CHANNEL_ID=${discord_channel}"
  } >> "$profile_env_file"
elif [[ "$deploy_choice" == "3" ]]; then
  read -r -p "Enter VITRUVIAN_WEBSOCKET_URL [ws://0.0.0.0:5005/Vitruvian/]: " websocket_url
  websocket_url="${websocket_url:-ws://0.0.0.0:5005/Vitruvian/}"
  read -r -p "Enter VITRUVIAN_WEBSOCKET_PUBLIC_URL [${websocket_url}]: " websocket_public_url
  websocket_public_url="${websocket_public_url:-$websocket_url}"
  read -r -p "Enter VITRUVIAN_WEBSOCKET_DOMAIN [dev]: " websocket_domain
  websocket_domain="${websocket_domain:-dev}"
  {
    echo "VITRUVIAN_WEBSOCKET_URL=${websocket_url}"
    echo "VITRUVIAN_WEBSOCKET_PUBLIC_URL=${websocket_public_url}"
    echo "VITRUVIAN_WEBSOCKET_DOMAIN=${websocket_domain}"
  } >> "$profile_env_file"
fi

write_active_profile "$profile"

cat <<EOF

Configuration saved to: $ENV_FILE
Profile configuration saved to: $profile_env_file

Next steps:
EOF

if [[ "$HAS_SOURCE_LAYOUT" == "true" ]]; then
  cat <<EOF
  1. dotnet build "$ROOT_DIR/Vitruviansln"
  2. dotnet run --framework net10.0 --project "$ROOT_DIR/src/VitruvianCli"
EOF
else
  cat <<EOF
  1. Run: Vitruvian
  2. Use /help to view available commands.
EOF
fi

cat <<EOF

The host loads .env.Vitruvian automatically — no need to source the file.
To switch profiles quickly, run: ./scripts/install.sh <dev|personal|team|prod>
If Discord variables are configured, the host will start in Discord mode automatically.
If VITRUVIAN_WEBSOCKET_URL is configured, the host will start in WebSocket mode before Discord mode.
EOF
