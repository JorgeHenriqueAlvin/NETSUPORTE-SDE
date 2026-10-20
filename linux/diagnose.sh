#!/usr/bin/env bash
set -u
echo "Diagnostico SDE Suporte"
echo "Computador: $(hostname)"
echo "Arquitetura: $(uname -m)"
echo "Desktop: ${XDG_SESSION_TYPE:-ausente}; DISPLAY: ${DISPLAY:-ausente}; sessao: ${XDG_SESSION_ID:-ausente}"
ip -4 address show scope global
echo "Portas do agente:"
ss -ltnu | grep -E ':7443|:7446' || true
pgrep -a -u "$(id -u)" -f "SDE-Suporte-Cliente-Linux|sde-suporte-cliente" || true
cat "$HOME/.local/lib/sde-suporte/version.txt" 2>/dev/null || true
cat "${XDG_DATA_HOME:-$HOME/.local/share}/SdeSuporte/UpdateCache/status.json" 2>/dev/null || true
tail -n 25 "${XDG_DATA_HOME:-$HOME/.local/share}/SdeSuporte/startup.log" 2>/dev/null || true
