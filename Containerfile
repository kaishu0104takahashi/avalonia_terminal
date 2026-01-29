# 1. ビルド用ステージ
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# プロジェクトファイルをコピーして復元
COPY *.csproj ./
RUN dotnet restore

# ソースコードをすべてコピーしてビルド (Releaseモード)
COPY . ./
RUN dotnet publish -c Release -o /app/out

# 2. 実行用ステージ
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app

# GUI動作に必要なライブラリ + 日本語フォント(fonts-noto-cjk)をインストール
RUN apt-get update && apt-get install -y \
    libx11-6 \
    libx11-data \
    libx11-xcb1 \
    libxcursor1 \
    libxrandr2 \
    libxi6 \
    libice6 \
    libsm6 \
    libfontconfig1 \
    libgl1-mesa-glx \
    fonts-noto-cjk \
    && rm -rf /var/lib/apt/lists/*

# ビルド成果物をコピー
COPY --from=build /app/out .

# 実行コマンド
ENTRYPOINT ["dotnet", "avalonia_terminal.dll"]
