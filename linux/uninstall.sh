#!/usr/bin/env bash
set -euo pipefail
install_dir="$HOME/.local/lib/sde-suporte"
if [[ -f "$install_dir/agent.pid" ]]; then
  agent_pid=$(cat "$install_dir/agent.pid")
  if [[ $agent_pid =~ ^[0-9]+$ && -r /proc/$agent_pid/cmdline ]] && tr '\0' '\n' < "/proc/$agent_pid/cmdline" | head -n1 | grep -Fxq "$install_dir/SDE-Suporte-Cliente-Linux"; then
    kill "$agent_pid"
  fi
fi
rm -f "$HOME/.config/autostart/sde-suporte.desktop" "$HOME/.local/bin/sde-suporte-cliente"
rm -rf -- "$install_dir"
echo 'Cliente removido. Identidade e logs preservados em ~/.local/share/SdeSuporte.'
