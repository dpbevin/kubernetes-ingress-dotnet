# .NET Kubernetes Ingress Controller

This project takes a .NET 5 (formerly .NET Core)-based reverse proxy and combines it with a Kubernetes client to form a highly scalable Ingress Controller.

It is based on the following technology:

- ASP.NET 5.0
- Microsoft's Reverse Proxy - https://microsoft.github.io/reverse-proxy/articles/getting_started.html
- Kubernetes API Client for .NET - https://github.com/kubernetes-client/csharp

This is still very much a new project... still lots of work ahead!

## Why .NET?

There are plenty of [Ingress Controllers](https://kubernetes.io/docs/concepts/services-networking/ingress-controllers/) out there but I'm yet to find one that uses .NET. Even the Azure AKS Gateway is written in Go.

So why did I write this in C#?

- Why not, .NET?
- It's super fast!
- It's extensible using a framework many developers know.

## Getting Started

Currently, this project is only available in source code form, so you will need to compile/build yourself.

## Prerequisties

- .NET Core SDK 5.0
- An editor (VS Code, or Visual Studio 2019+)
- Docker + Kubernetes

### Dev Cycle

1. Make changes
1. Run `dotnet publish ./src/Bevo.KubernetesIngressDotNet/Bevo.KubernetesIngressDotNet.csproj -c release`
1. Run `skaffold run`

Note that you can simply debug on your host machine, and the Kubernetes client will automatically connect to your local cluster and generate the ingress routes... not that they will work (because the requests will be sent to the cluster addresses).
