# ===== build =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copia csproj e restaura separado (cache eficiente)
COPY BibleFTS.Api/*.csproj ./BibleFTS.Api/
RUN dotnet restore ./BibleFTS.Api/BibleFTS.Api.csproj

# copia o restante e publica
COPY . .
RUN dotnet publish ./BibleFTS.Api/BibleFTS.Api.csproj -c Release -o /app/publish

# ===== runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "BibleFTS.Api.dll"]
