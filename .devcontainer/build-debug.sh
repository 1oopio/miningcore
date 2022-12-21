#!/bin/bash

(cd src/Miningcore && \
BUILDIR=${1:-../../build} && \
echo "Building into $BUILDIR" && \
dotnet publish -c Debug --framework net6.0 -o $BUILDIR)