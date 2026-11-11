FROM node:26-bookworm-slim@sha256:add5c17c14752c54d2f3d519ec46a3815684d41c33f198bcb09fb998f7dcbf68 AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/GrowLog.Api/GrowLog.Api.csproj --configuration Release --output /app

# Makes label PDFs. Update the version and checksum together.
FROM alpine:3.24.4@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf AS typst
ADD --checksum=sha256:a6d077d0a95eed5a2eba715b2dae06be954f624ccbf85758a03f389ded33118c https://github.com/typst/typst/releases/download/v0.15.1/typst-x86_64-unknown-linux-musl.tar.xz /tmp/typst.tar.xz
RUN tar -xJf /tmp/typst.tar.xz -C /tmp --strip-components=1

# Chiseled: no shell or package manager. "extra" keeps ICU and time zones for UK dates.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/web/dist ./wwwroot
COPY --from=typst /tmp/typst /usr/local/bin/typst
COPY --from=typst /tmp/LICENSE /usr/share/licenses/typst/LICENSE
ENV Typst__Path=/usr/local/bin/typst
ENV ASPNETCORE_HTTP_PORTS=8080
# Shown under Settings, About GrowLog, with a link to this exact source. Set by the publish workflow.
ARG GROWLOG_VERSION=development
ARG GROWLOG_COMMIT=
ARG GROWLOG_BUILT=
ENV Build__Version=$GROWLOG_VERSION Build__Commit=$GROWLOG_COMMIT Build__Date=$GROWLOG_BUILT
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "GrowLog.Api.dll"]
