#!/usr/bin/env bash
set -euo pipefail
if [[ $(id -u) == 0 ]]; then
  echo 'Execute como o usuário do desktop, sem sudo. A instalação pedirá sudo apenas para dependências.' >&2
  exit 1
fi
if [[ $(uname -m) != aarch64 ]]; then
  echo 'Este pacote requer Linux ARM64 (aarch64).' >&2; exit 1
fi
if [[ ${XDG_SESSION_TYPE:-} != x11 || -z ${DISPLAY:-} ]]; then
  echo 'Entre no desktop X11 antes de instalar. Consulte README.md.' >&2; exit 1
fi
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
[[ -x "$source_dir/SDE-Suporte-Cliente-Linux" ]] || { echo 'Use o pacote linux-arm64 compilado, não a pasta de fontes.' >&2; exit 1; }
sudo apt-get update
sudo apt-get install -y python3 python3-tk python3-pil python3-pil.imagetk xdotool x11-xserver-utils libicu-dev libssl-dev zlib1g util-linux
install_dir="$HOME/.local/lib/sde-suporte"
mkdir -p "$install_dir" "$HOME/.local/bin" "$HOME/.config/autostart"
if [[ -f "$install_dir/supervisor.pid" ]]; then
 supervisor=$(cat "$install_dir/supervisor.pid")
 if [[ $supervisor =~ ^[0-9]+$ && -r /proc/$supervisor/cmdline ]] && tr '\0' '\n' < "/proc/$supervisor/cmdline" | grep -Fxq "$HOME/.local/bin/sde-suporte-cliente"; then
  kill "$supervisor"
  for _ in {1..20}; do kill -0 "$supervisor" 2>/dev/null || break; sleep 0.25; done
  if kill -0 "$supervisor" 2>/dev/null; then echo 'Supervisor nao encerrou; tente novamente.' >&2; exit 1; fi
 fi
fi

# Stop only the installed agent for this user before replacing its files.
if [[ -f "$install_dir/agent.pid" ]]; then
  agent_pid=$(cat "$install_dir/agent.pid")
  if [[ $agent_pid =~ ^[0-9]+$ && -r /proc/$agent_pid/cmdline ]] && tr '\0' '\n' < "/proc/$agent_pid/cmdline" | head -n1 | grep -Fxq "$install_dir/SDE-Suporte-Cliente-Linux"; then
    kill "$agent_pid"
    for _ in {1..20}; do kill -0 "$agent_pid" 2>/dev/null || break; sleep 0.25; done
    if kill -0 "$agent_pid" 2>/dev/null; then echo 'Feche o Cliente antes de atualizar.' >&2; exit 1; fi
  fi
fi
cp -a "$source_dir/." "$install_dir/"
chmod 700 "$install_dir" "$install_dir/SDE-Suporte-Cliente-Linux"
cp "$source_dir/launch.sh" "$HOME/.local/bin/sde-suporte-cliente"
chmod 700 "$HOME/.local/bin/sde-suporte-cliente"
# Desktop Exec field codes and quoting are escaped, including home paths with spaces.
python3 - "$HOME/.local/bin/sde-suporte-cliente" "$HOME/.config/autostart/sde-suporte.desktop" <<'PY'
import sys
exe=sys.argv[1].replace('\\','\\\\\\\\').replace('"','\\\\"').replace('`','\\\\`').replace('$','\\\\$').replace('%','%%')
with open(sys.argv[2],'w') as f:
    f.write('[Desktop Entry]\nType=Application\nName=SDE Suporte Cliente\nExec="'+exe+'"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n')
PY
"$HOME/.local/bin/sde-suporte-cliente" &
echo 'Cliente instalado com reinicio apos falha e atualizacao verificada quando ocioso. Inicia ao entrar no desktop X11.'
echo 'Se houver firewall, permita TCP 7443 e UDP 7446 somente a partir da rede da TI.'
