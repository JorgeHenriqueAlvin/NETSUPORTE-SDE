#!/usr/bin/env bash
set -euo pipefail
install_dir="$HOME/.local/lib/sde-suporte"
if [[ -f "$install_dir/supervisor.pid" ]]; then
 supervisor=$(cat "$install_dir/supervisor.pid")
 if [[ $supervisor =~ ^[0-9]+$ && -r /proc/$supervisor/cmdline ]] && tr '\0' '\n' < "/proc/$supervisor/cmdline" | grep -Fxq "$HOME/.local/bin/sde-suporte-cliente"; then
  kill "$supervisor"
  for _ in {1..20}; do kill -0 "$supervisor" 2>/dev/null || break; sleep 0.25; done
  if kill -0 "$supervisor" 2>/dev/null; then echo 'Supervisor nao encerrou; tente novamente.' >&2; exit 1; fi
 fi
fi
if [[ -f "$install_dir/agent.pid" ]]; then
  agent_pid=$(cat "$install_dir/agent.pid")
  if [[ $agent_pid =~ ^[0-9]+$ && -r /proc/$agent_pid/cmdline ]] && tr '\0' '\n' < "/proc/$agent_pid/cmdline" | head -n1 | grep -Fxq "$install_dir/SDE-Suporte-Cliente-Linux"; then
    kill "$agent_pid"
  fi
fi
rm -f "$HOME/.config/autostart/sde-suporte.desktop" "$HOME/.local/bin/sde-suporte-cliente"
rm -rf -- "$install_dir"
echo 'Cliente removido. Identidade e logs preservados em ~/.local/share/SdeSuporte.'
