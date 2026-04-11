FROM nvidia/cuda:12.2.2-devel-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive

# 1. Dependencias y Vulkan SDK
RUN apt-get update && apt-get install -y \
    wget gnupg2 software-properties-common tar unzip bzip2 libgomp1 build-essential git cmake mesa-vulkan-drivers && \
    wget -qO /etc/apt/trusted.gpg.d/lunarg.asc https://packages.lunarg.com/lunarg-signing-key-pub.asc && \
    wget -qO /etc/apt/sources.list.d/lunarg-vulkan-jammy.list http://packages.lunarg.com/vulkan/lunarg-vulkan-jammy.list && \
    apt-get update && apt-get install -y vulkan-sdk && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Configuración ICD para Vulkan (Solo si es necesario para tu entorno específico)
RUN mkdir -p /usr/share/vulkan/icd.d/ && \
    echo '{"file_format_version" : "1.0.0", "ICD": {"library_path": "libGLX_nvidia.so.0", "api_version" : "1.3.242"}}' > /usr/share/vulkan/icd.d/nvidia_icd.json

# 2. LLAMA.CPP (Compilación Híbrida una sola vez)
RUN git clone https://github.com/ggerganov/llama.cpp.git && \
    cd llama.cpp && \
    cmake -B build -DGGML_VULKAN=ON -DGGML_CUDA=ON -DLLAMA_CURL=OFF -DBUILD_SHARED_LIBS=OFF && \
    cmake --build build -j $(nproc) --config Release --target llama-server && \
    cp build/bin/llama-server /usr/local/bin/ && \
    cd .. && rm -rf llama.cpp

# 3. WHISPER.CPP (Con soporte CUDA)
RUN git clone -b v1.7.4 https://github.com/ggerganov/whisper.cpp.git && \
    cd whisper.cpp && \
    cmake -B build -DWHISPER_CUDA=ON -DWHISPER_BUILD_EXAMPLES=ON && \
    cmake --build build -j $(nproc) --config Release && \
    cp build/bin/main /usr/local/bin/whisper-cli && \
    cd .. && rm -rf whisper.cpp

# 4. SHERPA-ONNX
RUN wget https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.12.28/sherpa-onnx-v1.12.28-linux-x64-static.tar.bz2 && \
    tar -xjf sherpa-onnx-v1.12.28-linux-x64-static.tar.bz2 && \
    find . -name "sherpa-onnx-offline-tts" -exec cp {} /usr/local/bin/ \; && \
    rm -rf sherpa-onnx-v1.12.28-linux-x64-static*

RUN ldconfig
EXPOSE 8080