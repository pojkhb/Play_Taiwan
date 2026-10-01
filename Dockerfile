# 後端 Docker 映像檔
# main 每次通過測試後，GitHub Actions 會自動建置並推到 GHCR：ghcr.io/<owner>/play-taiwan-backend（見 .github/workflows/ci.yml）
# 本機建置：docker build -t play-taiwan-backend .
# 執行方式與要帶的環境變數見 README「部署（Docker）」

# ── 建置 ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先只複製專案檔還原套件，程式碼有改但套件沒改時可以沿用快取
COPY TrafficSystem.csproj ./
RUN dotnet restore TrafficSystem.csproj

COPY . ./
RUN dotnet publish TrafficSystem.csproj -c Release -o /app --no-restore

# ── 執行 ──
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    TZ=Asia/Taipei

COPY --from=build /app ./

# Program.cs 固定聽 5501
EXPOSE 5501

# 執行時產生的檔案（上傳的照片、迷霧圖、旁白語音），要保存的話掛 volume
VOLUME ["/app/wwwroot/uploads", "/app/wwwroot/images/fog/generated", "/app/wwwroot/audio"]

ENTRYPOINT ["dotnet", "TrafficSystem.dll"]
