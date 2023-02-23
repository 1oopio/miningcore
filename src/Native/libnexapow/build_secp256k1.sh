#/usr/bin/env bash

cd ./secp256k1

./autogen.sh
./configure --enable-module-schnorr
make -j