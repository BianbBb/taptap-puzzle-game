#!/bin/bash

cd "$(dirname "$0")"

# 公共配置
export WORKSPACE="$(realpath ../../)"
export LUBAN_DLL="../Tools/LubanTools/Luban/Luban.dll"
export CONF_ROOT="$(pwd)/.."

# 客户端配置
export CLIENT_BIN_DATA_OUTPATH="${WORKSPACE}/GameUnity/Assets/BundleAssets/Configs/Binary/"
export CLIENT_JSON_DATA_OUTPATH="${WORKSPACE}/GameUnity/Configs/Json/"
export CLIENT_CODE_OUTPATH="${WORKSPACE}/GameUnity/Assets/Scripts/HotFix/GameProto/LubanConfig/"

# 服务器配置
export SERVER_BINARY_DATA_OUTPATH="${WORKSPACE}/GameServer/Configs/Binary/"
export SERVER_JSON_DATA_OUTPATH="${WORKSPACE}/GameServer/Configs/Json/"
export SERVER_CODE_OUTPATH="${WORKSPACE}/GameServer/Server/Entity/Generate/Config/"

echo "环境变量已设置："
echo "WORKSPACE=${WORKSPACE}"
echo "LUBAN_DLL=${LUBAN_DLL}"
echo "CONF_ROOT=${CONF_ROOT}"
echo "CLIENT_BIN_DATA_OUTPATH=${CLIENT_BIN_DATA_OUTPATH}"
echo "CLIENT_JSON_DATA_OUTPATH=${CLIENT_JSON_DATA_OUTPATH}"
echo "CLIENT_CODE_OUTPATH=${CLIENT_CODE_OUTPATH}"
echo "SERVER_BINARY_DATA_OUTPATH=${SERVER_BINARY_DATA_OUTPATH}"
echo "SERVER_JSON_DATA_OUTPATH=${SERVER_JSON_DATA_OUTPATH}"
echo "SERVER_CODE_OUTPATH=${SERVER_CODE_OUTPATH}"