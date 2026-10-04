#!/bin/bash
D=$(cd "$(dirname "$0")" && pwd); cd "$D"
b(){ n=$1; shift; ./run.sh ProjA Spike.BuildAndroid A_and_$n -buildTarget Android -out ../out/A_$n.aab -report ../out/A_$n.txt "$@"; head -5 out/A_$n.txt; ls -l out/A_$n.aab; ./bt.sh out/A_$n.aab; }
b master -splash 0 -cfg Master
b best -splash 0 -cfg Master -entry activity -gfx gles
b bestvk -splash 0 -cfg Master -entry activity -gfx vulkan
./run.sh ProjA SpikeWhy.Why A_why
