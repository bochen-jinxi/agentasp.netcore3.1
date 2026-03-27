# 使用 .NET Core 3.1 SDK 镜像进行构建
FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build
WORKDIR /src

# 复制项目文件并还原依赖
COPY ["AspNetCoreWebApi.csproj", "./"]
RUN dotnet restore "AspNetCoreWebApi.csproj"

# 复制所有文件并构建
COPY . .
RUN dotnet publish "AspNetCoreWebApi.csproj" -c Release -o /app/publish

# 使用 .NET Core 3.1 Runtime 镜像运行
FROM mcr.microsoft.com/dotnet/aspnet:3.1 AS final
WORKDIR /app
EXPOSE 5000

# 复制发布文件
COPY --from=build /app/publish .

# 设置环境变量
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

# 启动应用
ENTRYPOINT ["dotnet", "AspNetCoreWebApi.dll"]
