FROM node:22-bookworm-slim AS web
WORKDIR /web
COPY src/TeachWell.Web/package*.json ./
RUN npm ci --no-audit --no-fund
COPY src/TeachWell.Web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY Directory.Build.props TeachWell.sln ./
COPY src/ ./src/
RUN dotnet publish src/TeachWell.Api -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out ./
COPY --from=web /web/dist ./wwwroot
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID:$APP_UID /home/app/.aspnet
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TeachWell.Api.dll"]
