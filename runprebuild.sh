#!/bin/sh

mono Prebuild.exe /target nant
# needed until we break up OpenSim.exe
perl -pi -e 's{OpenSim.dll}{OpenSim.exe}' OpenSim/ApplicationPlugins/LoadRegions/OpenSim.ApplicationPlugins.LoadRegions.dll.build
mono Prebuild.exe /target monodev
mono Prebuild.exe /target vs2008
