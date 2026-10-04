#!/bin/bash
D=$(cd "$(dirname "$0")" && pwd); cd "$D"
b(){ n=$1; shift; ./run.sh ProjA Spike.BuildAndroid A_and_$n -buildTarget Android -out ../out/A_$n.aab -report ../out/A_$n.txt "$@"; head -5 out/A_$n.txt; ls -l out/A_$n.aab; ./bt.sh out/A_$n.aab; }
b nosplash -splash 0
b gles -splash 0 -gfx gles
b vulkan -splash 0 -gfx vulkan
b activity -splash 0 -entry activity
b stripMin -splash 0 -strip Minimal
b speed -splash 0 -codegen speed
b noStripEngine -splash 0 -stripEngine 0
