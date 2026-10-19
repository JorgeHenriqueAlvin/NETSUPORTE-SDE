"""Protocol-only test double; never shipped in the installation package."""
import json
import sys
args=sys.argv
command=args[2]
if command=='ui':
    for line in sys.stdin:
        if json.loads(line).get('kind')=='quit':break
elif command=='consent': print('yes')
elif command=='monitors':print('[{"id":"test","name":"test","x":0,"y":0,"width":100,"height":100,"primary":true}]')
elif command=='layout':print('W10=')
elif command=='frame':sys.stdout.buffer.write(b'frame-test')
elif command=='input':json.load(sys.stdin)
