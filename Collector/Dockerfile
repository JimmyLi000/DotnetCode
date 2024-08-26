# 使用官方 .NET 8.0 运行时作为基础镜像
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
# EXPOSE 5064

# 安装 tzdata 并设置时区，安装 net-snmp-tools 包括 snmpwalk
RUN apk add --no-cache tzdata net-snmp-tools \
    && cp /usr/share/zoneinfo/Asia/Taipei /etc/localtime \
    && echo "Asia/Taipei" > /etc/timezone \
    && apk del tzdata

# 构建环境
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY ["Collector.csproj", "./"]
RUN dotnet restore "Collector.csproj"
COPY . .
RUN dotnet build "Collector.csproj" -c Release -o /app/build

# 发布应用
FROM build AS publish
RUN dotnet publish "Collector.csproj" -c Release -o /app/publish

# 最终镜像
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# 複製設定檔
COPY appsettings*.json ./ 
ENTRYPOINT ["dotnet", "Collector.dll"]
