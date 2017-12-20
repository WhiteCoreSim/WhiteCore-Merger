#!/bin/sh

case "$1" in

  'clean')

    mono Prebuild.exe /clean

  ;;


  'autoclean')

    echo y|mono Prebuild.exe /clean

  ;;


  'vs2010')
  
    mono Prebuild.exe /target vs2010
  
  ;;

  *)

    mono Prebuild.exe /target nant
    mono Prebuild.exe /target vs2010

  ;;

esac
