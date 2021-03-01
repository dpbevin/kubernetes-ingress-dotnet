# Compile/Test/Package .NET
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS builder
WORKDIR /source

COPY ./Bevo.KubernetesIngressDotNet.sln .
COPY ./src/Bevo.KubernetesIngressDotNet/Bevo.KubernetesIngressDotNet.csproj ./src/Bevo.KubernetesIngressDotNet/
COPY ./src/Bevo.ReverseProxy.Kubernetes/Bevo.ReverseProxy.Kubernetes.csproj ./src/Bevo.ReverseProxy.Kubernetes/

RUN dotnet restore

COPY ./src/Bevo.KubernetesIngressDotNet ./src/Bevo.KubernetesIngressDotNet/
COPY ./src/Bevo.ReverseProxy.Kubernetes ./src/Bevo.ReverseProxy.Kubernetes/

RUN dotnet publish "./src/Bevo.KubernetesIngressDotNet/Bevo.KubernetesIngressDotNet.csproj" --output "../dist" --configuration Release --no-restore

# Build Docker image
FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app
COPY --from=builder /dist .
ENTRYPOINT ["dotnet", "Bevo.KubernetesIngressDotNet.dll"]
