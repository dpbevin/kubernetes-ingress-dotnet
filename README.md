# Kubernetes Ingress .NET

This project takes a .NET Core-based reverse proxy and combines it with a Kubernetes client to form a highly scalable Ingress Controller.

It is based on the following technology:
- ASP.NET 5.0
- Microsoft's Reverse Proxy - https://microsoft.github.io/reverse-proxy/articles/getting_started.html
- Kubernetes API Client for .NET - https://github.com/kubernetes-client/csharp

## Development

## Prerequisties

- .NET Core SDK 5.0
- An editor (VS Code, or Visual Studio 2019+)
- Docker + Kubernetes

### Dev Cycle

1. Make changes
1. Run `dotnet publish ./src/Bevo.KubernetesIngressDotNet/Bevo.KubernetesIngressDotNet.csproj -c release`
1. Run `skaffold run`
