#!/bin/bash
# The whole pilot polish for one pilot, from the FBX the game ships to the new FBX and textures:
#   pilot_prep.py (FBX -> blend) -> pilot_paws.py (thumb + three fingers, finger bones) -> pilot_face.py (eyelids, ear
#   bones) -> pilot_tail.py (tail bones, if the spec has a "tail") -> pilot_anims2.py (the re-authored clips, FBX out).
# Run: Tools~/ArtPipeline/pilot_polish.sh <Name> <work dir> [python with bpy]    e.g.  pilot_polish.sh Fox /tmp/w
#   reads Assets/OODA/Art/<Name>Pilot/ and specs/<name>_pilot.json; writes <work dir>/<Name>/ (copy the FBX and PNGs back).
#   It starts from the unpolished pilot: run it on a checkout from before the polish, never on its own output.
set -e
N=$1; W=$2; PY="${3:-python} -I"; n=$(echo $N | tr A-Z a-z)
T=$(cd "$(dirname "$0")" && pwd); A=$T/../../Assets/OODA/Art/${N}Pilot; O=$W/$N; SPEC=$T/specs/${n}_pilot.json
mkdir -p $O
$PY $T/pilot_prep.py -- $A/${N}Pilot.fbx $A/${N}_BaseColor.png $O/0_prep.blend > $O/log_prep.txt 2>&1
$PY $T/pilot_paws.py -- $O/0_prep.blend $A/${N}_BaseColor.png $A/${N}_MetallicSmoothness.png $O/1_paws.blend $O $SPEC > $O/log_paws.txt 2>&1
$PY $T/pilot_face.py -- $O/1_paws.blend $O/${N}_BaseColor.png $O/2_face.blend $SPEC > $O/log_face.txt 2>&1
if grep -q '"tail"' $SPEC; then $PY $T/pilot_tail.py -- $O/2_face.blend $O/3_tail.blend $SPEC > $O/log_tail.txt 2>&1; else cp $O/2_face.blend $O/3_tail.blend; fi
$PY $T/pilot_anims2.py -- $O/3_tail.blend ${N}Rig $O/4_anims.blend $O/${N}Pilot.fbx > $O/log_anims.txt 2>&1
echo "$N: $O/${N}Pilot.fbx"
