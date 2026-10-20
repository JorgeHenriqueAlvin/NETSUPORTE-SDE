#!/usr/bin/env bash
set -euo pipefail
install_dir="$HOME/.local/lib/sde-suporte"
state_dir="${XDG_DATA_HOME:-$HOME/.local/share}/SdeSuporte"
mkdir -p "$state_dir"; chmod 700 "$state_dir"
exec 9>"$state_dir/agent.lock"
flock -n 9 || exit 0
[[ ${XDG_SESSION_TYPE:-} == x11 && -n ${DISPLAY:-} && $(id -u) != 0 ]] || {
 echo "Entre no desktop X11 como usuario normal." >> "$state_dir/startup.log"; exit 1;
}
worker=""
agent=""
cleanup() {
 [[ -z $worker ]] || kill "$worker" 2>/dev/null || true
 [[ -z $agent ]] || kill "$agent" 2>/dev/null || true
}
trap 'cleanup; exit 0' TERM INT HUP
(
 while true; do
  python3 "$install_dir/update.py" --check >> "$state_dir/startup.log" 2>&1 || true
  sleep 3600
 done
) &
worker=$!
delay=3
while true; do
 started=$SECONDS
 "$install_dir/SDE-Suporte-Cliente-Linux" >> "$state_dir/startup.log" 2>&1 &
 agent=$!
 printf '%s\n' "$agent" > "$install_dir/agent.pid"
 printf '%s\n' "$$" > "$install_dir/supervisor.pid"
 code=0
 wait "$agent" || code=$?
 agent=""
 if [[ $code == 75 ]]; then
  python3 "$install_dir/update.py" --apply >> "$state_dir/startup.log" 2>&1 || true
  sleep 2
  continue
 fi
 if (( SECONDS - started > 120 )); then delay=3; fi
 printf '%s Agente encerrou (%s); reinicio em %ss\n' "$(date -Is)" "$code" "$delay" >> "$state_dir/startup.log"
 sleep "$delay"
 delay=$(( delay < 30 ? delay * 2 : 60 ))
done
