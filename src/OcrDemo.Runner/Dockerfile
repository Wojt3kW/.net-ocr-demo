# Runner .NET 10 – kontekst budowania: katalog główny repozytorium.
#
# Etap "base" musi być pierwszy: Visual Studio (docker-compose.dcproj, tryb Fast / Debug) buduje tylko ten
# etap i montuje do /app wynik kompilacji z hosta. Pozostałe etapy służą do pełnego budowania obrazu.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
# DejaVu Sans – czcionka z polskimi znakami dla niewidocznej warstwy tekstowej PDF.
RUN apt-get update \
 && apt-get install -y --no-install-recommends fonts-dejavu-core libgomp1 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/OcrDemo.Core/OcrDemo.Core.csproj src/OcrDemo.Core/
COPY src/OcrDemo.Engines/OcrDemo.Engines.csproj src/OcrDemo.Engines/
COPY src/OcrDemo.Runner/OcrDemo.Runner.csproj src/OcrDemo.Runner/
RUN dotnet restore src/OcrDemo.Runner/OcrDemo.Runner.csproj -r linux-x64
COPY src/ src/
RUN dotnet publish src/OcrDemo.Runner/OcrDemo.Runner.csproj -c "$BUILD_CONFIGURATION" -r linux-x64 --self-contained false --no-restore -o /app/publish

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OcrDemo.Runner.dll"]
